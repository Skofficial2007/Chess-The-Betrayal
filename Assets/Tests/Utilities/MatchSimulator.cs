using System;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>How a simulated game ended, from White's point of view.</summary>
    public enum MatchOutcome
    {
        WhiteWon,
        BlackWon,
        Draw
    }

    /// <summary>One completed simulated game.</summary>
    public readonly struct MatchResult
    {
        public readonly MatchOutcome Outcome;
        public readonly int PlyCount;
        public readonly bool ReachedPlyCap;

        public MatchResult(MatchOutcome outcome, int plyCount, bool reachedPlyCap)
        {
            Outcome = outcome;
            PlyCount = plyCount;
            ReachedPlyCap = reachedPlyCap;
        }
    }

    /// <summary>
    /// Search telemetry summed across every move one side made in a game — a benchmark run cares
    /// about a tier's typical cost per move, not any single move's numbers, so this divides the
    /// interesting counters back out to a per-move mean rather than reporting the raw totals.
    /// </summary>
    public readonly struct MatchSideStats
    {
        public readonly int MoveCount;
        public readonly long TotalNodesVisited;
        public readonly long TotalQNodesVisited;
        public readonly int DeepestCompletedDepth;
        public readonly double TotalElapsedMs;

        /// <summary>How many of this side's moves rolled a real blunder (see
        /// MoveSelectionPolicy.SelectFinalMove's blunderRollFired out param) versus how many moves
        /// had a nonzero chance to (BlunderRate &gt; 0). A tier whose observed rate drifts far from
        /// its configured BlunderRate means the roll isn't expressing at the probability the preset
        /// table claims.</summary>
        public readonly int BlunderRollOffered;
        public readonly int BlunderRollFired;

        public MatchSideStats(int moveCount, long totalNodesVisited, long totalQNodesVisited,
            int deepestCompletedDepth, double totalElapsedMs, int blunderRollOffered, int blunderRollFired)
        {
            MoveCount = moveCount;
            TotalNodesVisited = totalNodesVisited;
            TotalQNodesVisited = totalQNodesVisited;
            DeepestCompletedDepth = deepestCompletedDepth;
            TotalElapsedMs = totalElapsedMs;
            BlunderRollOffered = blunderRollOffered;
            BlunderRollFired = blunderRollFired;
        }

        public double MeanNodesPerMove => MoveCount == 0 ? 0 : (double)(TotalNodesVisited + TotalQNodesVisited) / MoveCount;
        public double MeanMsPerMove => MoveCount == 0 ? 0 : TotalElapsedMs / MoveCount;
        public float ObservedBlunderActuationRate => BlunderRollOffered == 0 ? 0f : (float)BlunderRollFired / BlunderRollOffered;
    }

    /// <summary>A completed game plus each side's aggregated search telemetry for it.</summary>
    public readonly struct MatchStatsResult
    {
        public readonly MatchResult Result;
        public readonly MatchSideStats WhiteStats;
        public readonly MatchSideStats BlackStats;

        public MatchStatsResult(MatchResult result, MatchSideStats whiteStats, MatchSideStats blackStats)
        {
            Result = result;
            WhiteStats = whiteStats;
            BlackStats = blackStats;
        }
    }

    /// <summary>
    /// Plays one full game between two AIProfile-driven sides, synchronously on the calling
    /// thread. One instance is NOT safe to share across threads — parallel tournament runs give
    /// each worker thread its own simulator instead, which also keeps every game's searches,
    /// tables, and RNG streams fully independent of scheduling order.
    ///
    /// Composes the same stack a real match uses: AlphaBetaSearch with a full-size transposition
    /// table, MoveSelectionPolicy, BetrayalAwareEvaluator, and moves applied through MatchDriver so
    /// Betrayal sub-sequences and checkmate/stalemate detection run through the exact seam a live
    /// game uses. Under MatchTimeControl.ProductionBudget (the default) each move's search is also
    /// time-bounded exactly the way the live agent bounds it — hard-budget cancellation plus the
    /// settle-early logic — so tournament results measure the engine as it ships, not an
    /// unbounded-depth variant of it that no player ever faces.
    ///
    /// The two transposition tables (one per side, like a real match where each agent owns its own)
    /// are allocated once and reused across every game this simulator plays, wiped between games.
    /// Reusing ~16 MB tables matters when a tournament plays hundreds of games; wiping them keeps
    /// each game's result independent of whatever was played before it.
    ///
    /// A game with no result by the ply cap is adjudicated by static evaluation margin. Before the
    /// cap, MatchAdjudicator can also end a game early on threefold repetition, the fifty-move
    /// rule, or a large/small score sustained over several plies — see AdjudicationRules.
    /// </summary>
    public sealed class MatchSimulator
    {
        public const int DefaultPlyCap = 120;
        public const int AdjudicationMarginCp = 300;

        /// <summary>Same table size the live agent allocates — an undersized table degrades move
        /// ordering more and more as a long game fills it, inflating node counts in a way that has
        /// nothing to do with the search being measured.</summary>
        public const int ProductionTranspositionTableLog2Size = 20;

        private readonly IChessEngine _engine = new ChessEngineAdapter();
        private readonly IPositionEvaluator _adjudicationEvaluator = new BetrayalAwareEvaluator();
        private readonly MatchTimeControl _timeControl;
        private readonly AdjudicationRules _adjudicationRules;
        private readonly int _moveBudgetCapMs;
        private readonly TranspositionTable _whiteTable;
        private readonly TranspositionTable _blackTable;

        /// <param name="moveBudgetCapMs">When positive (and time control is ProductionBudget),
        /// every move's hard budget is clamped to at most this many milliseconds, and its soft
        /// budget scaled down proportionally. This runs the exact same search a real game does —
        /// same cancellation, same settle-early logic — just on a compressed clock, so a strength
        /// tournament can play many games fast without switching to a different, less-faithful code
        /// path. A profile whose real budget is already under the cap is unaffected. 0 (the
        /// default) leaves each profile's own budget in force.</param>
        public MatchSimulator(
            MatchTimeControl timeControl = MatchTimeControl.ProductionBudget,
            int transpositionTableLog2Size = ProductionTranspositionTableLog2Size,
            AdjudicationRules? adjudicationRules = null,
            int moveBudgetCapMs = 0)
        {
            _timeControl = timeControl;
            _adjudicationRules = adjudicationRules ?? AdjudicationRules.Standard;
            _moveBudgetCapMs = moveBudgetCapMs;
            _whiteTable = new TranspositionTable(transpositionTableLog2Size);
            _blackTable = new TranspositionTable(transpositionTableLog2Size);
        }

        /// <summary>
        /// Plays one game from <paramref name="startingPosition"/> between <paramref name="whiteProfile"/>
        /// and <paramref name="blackProfile"/>. The starting position is cloned internally, so the
        /// caller's instance is never mutated and the same curated position can be reused across many
        /// simulated games. rngSeedWhite/rngSeedBlack are independent streams (see the harness's
        /// per-side seeding scheme) so perturbing one side's roll count can never affect the other's.
        /// </summary>
        public MatchResult PlayGame(
            BoardState startingPosition, AIProfile whiteProfile, AIProfile blackProfile,
            int rngSeedWhite, int rngSeedBlack, int plyCap = DefaultPlyCap)
        {
            return PlayGameCore(startingPosition, whiteProfile, blackProfile, rngSeedWhite, rngSeedBlack, plyCap,
                out _, out _);
        }

        /// <summary>
        /// Same game as <see cref="PlayGame"/>, but also returns each side's search telemetry
        /// summed across every move it made — the benchmark suite's whole reason for existing is
        /// to capture strength drift and performance drift from the SAME tournament pass, since a
        /// move-ordering or TT change usually shifts both node counts and game outcomes together.
        /// </summary>
        public MatchStatsResult PlayGameWithStats(
            BoardState startingPosition, AIProfile whiteProfile, AIProfile blackProfile,
            int rngSeedWhite, int rngSeedBlack, int plyCap = DefaultPlyCap)
        {
            MatchResult result = PlayGameCore(startingPosition, whiteProfile, blackProfile, rngSeedWhite, rngSeedBlack, plyCap,
                out MatchSideStats whiteStats, out MatchSideStats blackStats);

            return new MatchStatsResult(result, whiteStats, blackStats);
        }

        private MatchResult PlayGameCore(
            BoardState startingPosition, AIProfile whiteProfile, AIProfile blackProfile,
            int rngSeedWhite, int rngSeedBlack, int plyCap,
            out MatchSideStats whiteStats, out MatchSideStats blackStats)
        {
            BoardState board = startingPosition.CloneForSnapshot();

            var matchDriver = new MatchDriver(_engine, board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            matchDriver.TransitionToPhase(TurnPhase.Normal);

            // The searches themselves are rebuilt per game because each side's evaluator bakes in
            // that profile's weights, but they borrow this simulator's long-lived tables — wiped
            // here so nothing from an earlier game can influence this one.
            _whiteTable.Clear();
            _blackTable.Clear();
            var whiteSearch = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(whiteProfile)),
                transpositionTable: _whiteTable);
            var blackSearch = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(blackProfile)),
                transpositionTable: _blackTable);
            var whitePolicy = new MoveSelectionPolicy();
            var blackPolicy = new MoveSelectionPolicy();
            IRandomSource whiteRng = new SystemRandomSource(rngSeedWhite);
            IRandomSource blackRng = new SystemRandomSource(rngSeedBlack);

            var whiteAccumulator = new SideStatsAccumulator();
            var blackAccumulator = new SideStatsAccumulator();

            var adjudicator = new MatchAdjudicator(_adjudicationRules);
            adjudicator.RecordStartingPosition(board);

            int ply = 0;
            for (; ply < plyCap && !board.IsGameOver; ply++)
            {
                Team mover = board.CurrentTurn;
                bool isWhite = mover == Team.White;

                AIProfile profile = isWhite ? whiteProfile : blackProfile;
                AlphaBetaSearch search = isWhite ? whiteSearch : blackSearch;
                MoveSelectionPolicy policy = isWhite ? whitePolicy : blackPolicy;
                IRandomSource rng = isWhite ? whiteRng : blackRng;
                SideStatsAccumulator accumulator = isWhite ? whiteAccumulator : blackAccumulator;

                var settings = ApplyMoveBudgetCap(AISearchSettings.FromProfile(BetrayalUsage.Full, profile));
                int rescoreMargin = Math.Max(profile.BlunderMarginCp, profile.TieBreakWindowCp);

                var moveStopwatch = System.Diagnostics.Stopwatch.StartNew();
                MoveCommand move = FindMoveUnderTimeControl(search, board, settings, rescoreMargin);

                // RootScoresExactForSelection mirrors the live agent's own guard: a search that
                // spent its whole time budget on the depth loop never rescored its candidate
                // scores to exact values, and applying blunder/tie-break windows to the leftover
                // alpha-beta bounds selects near-random moves — the exact failure that once made
                // every time-capped tier lose to shallower ones in this very harness.
                bool blunderRollFired = false;
                if ((profile.BlunderRate > 0f || profile.TieBreakWindowCp > 0)
                    && search.RootScoresExactForSelection)
                {
                    move = policy.SelectFinalMove(
                        search.RootMoves, search.RootScores, search.RootMoveCount, search.BestRootIndex,
                        profile, rng, out blunderRollFired);
                }
                moveStopwatch.Stop();

                // The mover's own root score is already exact-enough at BestRootIndex (see
                // AlphaBetaSearch.RootScores' doc comment) and free — no extra evaluator call
                // needed to feed the adjudicator. Flipped to White's perspective since scores come
                // back from the mover's own point of view (positive == good for whoever just moved).
                int scoreFromMoverPerspective = search.RootMoveCount > 0 ? search.RootScores[search.BestRootIndex] : 0;
                int scoreForWhiteCp = isWhite ? scoreFromMoverPerspective : -scoreFromMoverPerspective;

                accumulator.Record(search.Stats, moveStopwatch.Elapsed.TotalMilliseconds, profile.BlunderRate > 0f, blunderRollFired);

                matchDriver.PlayMove(move);

                // Betrayal sub-sequence moves (Act/Retribution/DefensiveOverride) don't end a turn
                // and can leave the board in a state a repetition/fifty-move count shouldn't
                // sample mid-sequence — only adjudicate once the turn is actually settled, i.e.
                // back in Normal phase (or the game just ended, which the loop condition below
                // already handles).
                if (matchDriver.CurrentPhase != TurnPhase.Normal) continue;

                MatchOutcome? adjudicated = adjudicator.RecordPly(board, move, ply, scoreForWhiteCp);
                if (adjudicated.HasValue)
                {
                    whiteStats = whiteAccumulator.ToStats();
                    blackStats = blackAccumulator.ToStats();
                    return new MatchResult(adjudicated.Value, ply + 1, reachedPlyCap: false);
                }
            }

            whiteStats = whiteAccumulator.ToStats();
            blackStats = blackAccumulator.ToStats();

            if (board.IsGameOver)
            {
                MatchOutcome outcome = board.Winner switch
                {
                    Team.White => MatchOutcome.WhiteWon,
                    Team.Black => MatchOutcome.BlackWon,
                    _ => MatchOutcome.Draw
                };
                return new MatchResult(outcome, ply, reachedPlyCap: false);
            }

            return new MatchResult(AdjudicateByMargin(board), ply, reachedPlyCap: true);
        }

        /// <summary>
        /// Clamps a profile's time budget to the simulator's move-budget cap when one is set,
        /// scaling the soft budget by the same ratio so the settle-early threshold stays in the
        /// same relative position within the shortened budget. A no-op when no cap is set, when the
        /// profile is already under the cap, or under Uncapped time control (which ignores the
        /// budget entirely). Keeps the search on its exact production code path — only the numbers
        /// on the clock change, not which logic reads them.
        /// </summary>
        private AISearchSettings ApplyMoveBudgetCap(AISearchSettings settings)
        {
            if (_moveBudgetCapMs <= 0 || settings.TimeBudget.HardMs <= _moveBudgetCapMs)
                return settings;

            int cappedHard = _moveBudgetCapMs;
            // Preserve the soft/hard ratio so a tier that settles early still gets the same
            // proportional window before it's allowed to stop.
            long scaledSoft = (long)settings.TimeBudget.SoftMs * cappedHard / settings.TimeBudget.HardMs;
            int cappedSoft = (int)Math.Max(1, Math.Min(scaledSoft, cappedHard));
            return new AISearchSettings(settings.MaxDepth, new AITimeBudget(cappedSoft, cappedHard), settings.BetrayalUsage);
        }

        /// <summary>
        /// Runs one move's search bounded the way this simulator's time control dictates. Under
        /// ProductionBudget this is the live agent's exact contract: a cancellation timer armed at
        /// the profile's hard budget (the unconditional wall-clock ceiling) plus the search's own
        /// settle-early/panic-extend logic reading the soft budget. The per-move token source is a
        /// small allocation, accepted deliberately: this harness code never runs in a shipped game,
        /// and the search hot path itself stays allocation-free.
        /// </summary>
        private MoveCommand FindMoveUnderTimeControl(
            AlphaBetaSearch search, BoardState board, AISearchSettings settings, int rescoreMargin)
        {
            if (_timeControl == MatchTimeControl.Uncapped)
            {
                return search.FindBestMove(board, settings, System.Threading.CancellationToken.None, rescoreMargin);
            }

            using (var cts = new System.Threading.CancellationTokenSource())
            {
                cts.CancelAfter(settings.TimeBudget.HardMs);
                return search.FindBestMove(board, settings, cts.Token, rescoreMargin,
                    enableInstabilityTimeManagement: true);
            }
        }

        /// <summary>Sums one side's SearchStats across every move it made this game — a plain
        /// mutable accumulator kept private since MatchSideStats itself stays an immutable result.</summary>
        private sealed class SideStatsAccumulator
        {
            private int _moveCount;
            private long _totalNodesVisited;
            private long _totalQNodesVisited;
            private int _deepestCompletedDepth;
            private double _totalElapsedMs;
            private int _blunderRollOffered;
            private int _blunderRollFired;

            public void Record(SearchStats stats, double elapsedMs, bool blunderRollOffered, bool blunderRollFired)
            {
                _moveCount++;
                _totalNodesVisited += stats.NodesVisited;
                _totalQNodesVisited += stats.QNodesVisited;
                if (stats.LastCompletedDepth > _deepestCompletedDepth)
                    _deepestCompletedDepth = stats.LastCompletedDepth;
                _totalElapsedMs += elapsedMs;
                if (blunderRollOffered) _blunderRollOffered++;
                if (blunderRollFired) _blunderRollFired++;
            }

            public MatchSideStats ToStats() => new MatchSideStats(
                _moveCount, _totalNodesVisited, _totalQNodesVisited, _deepestCompletedDepth,
                _totalElapsedMs, _blunderRollOffered, _blunderRollFired);
        }

        /// <summary>
        /// A game that hit the ply cap without a decisive result is scored by static evaluation
        /// margin rather than left unresolved — a clear material/positional edge (beyond
        /// AdjudicationMarginCp) counts as a win, anything closer counts as a draw.
        /// </summary>
        private MatchOutcome AdjudicateByMargin(BoardState board)
        {
            int whiteScore = _adjudicationEvaluator.Evaluate(board, Team.White);

            if (whiteScore > AdjudicationMarginCp) return MatchOutcome.WhiteWon;
            if (whiteScore < -AdjudicationMarginCp) return MatchOutcome.BlackWon;
            return MatchOutcome.Draw;
        }
    }
}
