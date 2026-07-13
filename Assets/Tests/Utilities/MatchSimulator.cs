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
    /// Plays one full game between two AIProfile-driven sides, entirely synchronously and off the
    /// worker-thread path AsyncAIAgent normally uses — there is no benefit to threading here and it
    /// would only add nondeterministic scheduling to what needs to be a bit-reproducible tournament.
    ///
    /// Composes the same stack a real match uses (AlphaBetaSearch + MoveSelectionPolicy +
    /// BetrayalAwareEvaluator, moves applied through MatchDriver so Betrayal sub-sequences,
    /// checkmate/stalemate detection, and move logging all run through the exact seam a live game
    /// uses) but calls FindBestMove directly instead of going through AsyncAIAgent, since the
    /// threading/cancellation contract there is already covered by its own tests and would only
    /// slow this down.
    ///
    /// A game with no result by the ply cap is adjudicated by static evaluation margin rather than
    /// played out indefinitely — there is no threefold-repetition detection yet, so the cap is what
    /// actually terminates a repeating line.
    /// </summary>
    public sealed class MatchSimulator
    {
        public const int DefaultPlyCap = 120;
        public const int AdjudicationMarginCp = 300;

        private readonly IChessEngine _engine = new ChessEngineAdapter();
        private readonly IPositionEvaluator _adjudicationEvaluator = new BetrayalAwareEvaluator();

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

            var whiteSearch = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(whiteProfile)));
            var blackSearch = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(blackProfile)));
            var whitePolicy = new MoveSelectionPolicy();
            var blackPolicy = new MoveSelectionPolicy();
            IRandomSource whiteRng = new SystemRandomSource(rngSeedWhite);
            IRandomSource blackRng = new SystemRandomSource(rngSeedBlack);

            var whiteAccumulator = new SideStatsAccumulator();
            var blackAccumulator = new SideStatsAccumulator();

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

                var settings = AISearchSettings.FromProfile(BetrayalUsage.Full, profile);
                int rescoreMargin = Math.Max(profile.BlunderMarginCp, profile.TieBreakWindowCp);

                var moveStopwatch = System.Diagnostics.Stopwatch.StartNew();
                MoveCommand move = search.FindBestMove(board, settings, System.Threading.CancellationToken.None, rescoreMargin);

                bool blunderRollFired = false;
                if (profile.BlunderRate > 0f || profile.TieBreakWindowCp > 0)
                {
                    move = policy.SelectFinalMove(
                        search.RootMoves, search.RootScores, search.RootMoveCount, search.BestRootIndex,
                        profile, rng, out blunderRollFired);
                }
                moveStopwatch.Stop();

                accumulator.Record(search.Stats, moveStopwatch.Elapsed.TotalMilliseconds, profile.BlunderRate > 0f, blunderRollFired);

                matchDriver.PlayMove(move);
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
