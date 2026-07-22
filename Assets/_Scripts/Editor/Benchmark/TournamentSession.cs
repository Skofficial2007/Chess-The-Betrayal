using System;
using System.Collections.Generic;
using System.Linq;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>One finished tournament game, as announced to OnGameCompleted subscribers.</summary>
    public sealed class TournamentGameRecord
    {
        public readonly int PairIndex;
        public readonly int PositionIndex;
        public readonly string WhiteId;
        public readonly string BlackId;
        public readonly MatchStatsResult Result;

        /// <summary>Which color the pairing's SUBJECT profile played THIS game. Carried explicitly
        /// rather than re-derived from WhiteId/the pairing table, because a head-to-head pairing a
        /// profile plays against itself has no id-based way to tell subject from opponent.</summary>
        public readonly bool SubjectIsWhite;

        public TournamentGameRecord(int pairIndex, int positionIndex, string whiteId, string blackId, MatchStatsResult result, bool subjectIsWhite)
        {
            PairIndex = pairIndex;
            PositionIndex = positionIndex;
            WhiteId = whiteId;
            BlackId = blackId;
            Result = result;
            SubjectIsWhite = subjectIsWhite;
        }
    }

    /// <summary>
    /// A whole tournament (pairings x positions x both colors) broken into single-game steps, so a
    /// caller decides the pacing: BenchmarkRunner drains it in one tight loop for batch/CI runs,
    /// while the tournament window plays one game per editor tick so the UI stays responsive and a
    /// run can be cancelled mid-way. Both callers go through this exact class with the exact same
    /// seeding, so the pairings, positions, and RNG streams of a window run and a batch run at the
    /// same seed are identical by construction. Individual game OUTCOMES carry one caveat: the
    /// simulator plays under the profiles' real time budgets, so a search that hits its clock may
    /// complete a different depth on a slower or busier machine and pick a different move — the
    /// statistics over a whole run absorb that, and a bit-exact single-game reproduction is still
    /// available by running the simulator with MatchTimeControl.Uncapped.
    ///
    /// Dev/editor-only, same category as the opening-book compiler — never a player feature.
    /// </summary>
    public sealed class TournamentSession
    {
        /// <summary>Quick mode's position budget. Deliberately a small slice of the curated suite —
        /// the routine sanity check trades statistical confidence for finishing in minutes.</summary>
        public const int QuickPositionCount = 4;

        /// <summary>The strength-chain adjacency the preset table promises — same pairs
        /// AIProfileStrengthOrderingTests checks, so Quick mode measures the exact relationships
        /// the table's own regression tests already gate on.</summary>
        private static readonly (string Subject, string Opponent)[] AdjacentPairs =
        {
            ("normal", "easy"),
            ("hard", "normal"),
            ("extreme", "hard"),
            ("impossible", "extreme"),
            ("aggressive", "normal"),
            ("aggressive", "easy"),
        };

        private readonly struct PendingGame
        {
            public readonly int PairIndex;
            public readonly int PositionIndex;
            public readonly AIProfile White;
            public readonly AIProfile Black;
            public readonly bool SubjectIsWhite;

            public PendingGame(int pairIndex, int positionIndex, AIProfile white, AIProfile black, bool subjectIsWhite)
            {
                PairIndex = pairIndex;
                PositionIndex = positionIndex;
                White = white;
                Black = black;
                SubjectIsWhite = subjectIsWhite;
            }
        }

        private sealed class PairTally
        {
            public int SubjectWins, OpponentWins, Draws, Games;
        }

        private readonly string _modeLabel;
        private readonly int _runSeed;
        private readonly int _plyCap;
        private readonly MatchTimeControl _timeControl;
        private readonly IReadOnlyList<(string Subject, string Opponent)> _pairs;
        private readonly List<PendingGame> _games;
        private readonly PairTally[] _tallies;
        private readonly Dictionary<string, TierAccumulator> _tierAccumulators = new Dictionary<string, TierAccumulator>();
        private readonly MatchSimulator _simulator;
        private int _nextGame;

        public event Action<TournamentGameRecord> OnGameCompleted;
        public event Action OnSessionCompleted;

        public int TotalGames => _games.Count;
        public int GamesCompleted => _nextGame;
        public bool IsComplete => _nextGame >= _games.Count;

        private TournamentSession(string modeLabel, int runSeed, int plyCap, MatchTimeControl timeControl,
            IReadOnlyList<(string Subject, string Opponent)> pairs,
            Func<string, AIProfile> resolve, int positionCount)
        {
            _modeLabel = modeLabel;
            _runSeed = runSeed;
            _plyCap = plyCap;
            _timeControl = timeControl;
            _simulator = new MatchSimulator(timeControl);
            _pairs = pairs;
            _tallies = new PairTally[pairs.Count];

            // The full game list is laid out up front: pair by pair, position by position, subject
            // playing White then Black against the same position (color-swapping cancels first-move
            // advantage — see CuratedPositionSuite). This fixed order is part of the reproducibility
            // contract, so RunNextGame only ever walks it forward.
            _games = new List<PendingGame>(pairs.Count * positionCount * 2);
            for (int pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
            {
                _tallies[pairIndex] = new PairTally();
                AIProfile subject = resolve(pairs[pairIndex].Subject);
                AIProfile opponent = resolve(pairs[pairIndex].Opponent);

                for (int positionIndex = 0; positionIndex < positionCount; positionIndex++)
                {
                    _games.Add(new PendingGame(pairIndex, positionIndex, subject, opponent, subjectIsWhite: true));
                    _games.Add(new PendingGame(pairIndex, positionIndex, opponent, subject, subjectIsWhite: false));
                }
            }
        }

        /// <summary>
        /// timeControl defaults to ProductionBudget — a real tournament run should measure the
        /// engine as it ships, hard-budget cancellation included. Pass Uncapped for a run that
        /// needs genuinely bit-reproducible outcomes (e.g. a reproducibility regression test) — a
        /// budget-bound search's OWN move choice can vary run to run under CPU contention/thermal
        /// throttling, since the budget timer races real wall-clock time; that variance is not a
        /// bug in this session, it's what a time-bounded engine actually does under load.
        /// </summary>
        public static TournamentSession CreateQuick(int runSeed, IReadOnlyList<AIProfile> roster,
            int plyCap = MatchSimulator.DefaultPlyCap, MatchTimeControl timeControl = MatchTimeControl.ProductionBudget)
        {
            return new TournamentSession(BenchmarkMode.Quick.ToString(), runSeed, plyCap, timeControl,
                AdjacentPairs, id => ResolveInRoster(roster, id),
                Math.Min(QuickPositionCount, CuratedPositionSuite.Count));
        }

        /// <summary>See CreateQuick's doc comment for the timeControl parameter.</summary>
        public static TournamentSession CreateFull(int runSeed, IReadOnlyList<AIProfile> roster,
            int plyCap = MatchSimulator.DefaultPlyCap, MatchTimeControl timeControl = MatchTimeControl.ProductionBudget)
        {
            return new TournamentSession(BenchmarkMode.Full.ToString(), runSeed, plyCap, timeControl,
                AllPairsRoundRobin(roster), id => ResolveInRoster(roster, id),
                CuratedPositionSuite.Count);
        }

        /// <summary>
        /// One pairing of the caller's choosing — including hand-built/custom profiles that exist in
        /// no roster, which is why this resolves against the two profiles directly instead of by id.
        /// Playing a profile against itself is legal (both colors just report under the same id).
        /// See CreateQuick's doc comment for the timeControl parameter.
        /// </summary>
        public static TournamentSession CreateHeadToHead(int runSeed, AIProfile subject, AIProfile opponent,
            int positionCount, int plyCap = MatchSimulator.DefaultPlyCap, MatchTimeControl timeControl = MatchTimeControl.ProductionBudget)
        {
            var pair = new[] { (subject.Id, opponent.Id) };
            return new TournamentSession("HeadToHead", runSeed, plyCap, timeControl,
                pair, id => id == subject.Id ? subject : opponent,
                Math.Min(Math.Max(positionCount, 1), CuratedPositionSuite.Count));
        }

        /// <summary>
        /// Plays the next game synchronously (a real search at the profiles' real depths — seconds
        /// to tens of seconds for the deep tiers), updates the running tallies, and raises
        /// OnGameCompleted, plus OnSessionCompleted if it was the last one. Returns false without
        /// side effects once the session is finished, so a drain loop is just
        /// <c>while (session.RunNextGame()) { }</c>.
        ///
        /// Plays with this session's own long-lived simulator, so games run one at a time on the
        /// caller's thread — the interactive tournament window's per-tick pacing depends on that.
        /// For a batch/CI run where nothing needs to observe individual game completion in real
        /// time, ParallelTournamentExecutor.RunRemainingGames plays the same games (same seeds,
        /// same pairing order) across several worker threads and is dramatically faster wall-clock.
        /// </summary>
        public bool RunNextGame()
        {
            if (IsComplete) return false;

            PendingGame game = _games[_nextGame];
            TournamentGameRecord record = PlayOneGame(_simulator, game);
            ApplyCompletedGame(record);
            return true;
        }

        /// <summary>Plays exactly one pending game (no session-state mutation) — the unit of work a
        /// parallel executor fans out across worker threads. Public so ParallelTournamentExecutor
        /// can call it without this session needing to know anything about how it's scheduled.</summary>
        internal TournamentGameRecord PlayOneGame(MatchSimulator simulator, int gameIndex)
        {
            return PlayOneGame(simulator, _games[gameIndex]);
        }

        internal int PendingGameCount => _games.Count - _nextGame;

        /// <summary>The time control every game in this session plays under — a parallel executor
        /// needs this to build its own per-worker MatchSimulators the same way, since PlayOneGame
        /// accepts any caller-supplied simulator rather than always using this session's own.</summary>
        internal MatchTimeControl TimeControl => _timeControl;

        private TournamentGameRecord PlayOneGame(MatchSimulator simulator, PendingGame game)
        {
            int seedWhite = TournamentSeeding.DeriveSeed(_runSeed, game.PositionIndex, game.PairIndex, gameIndex: 0, side: 0);
            int seedBlack = TournamentSeeding.DeriveSeed(_runSeed, game.PositionIndex, game.PairIndex, gameIndex: 0, side: 1);

            BoardState position = CuratedPositionSuite.Build(game.PositionIndex);
            MatchStatsResult result = simulator.PlayGameWithStats(position, game.White, game.Black, seedWhite, seedBlack, _plyCap);

            return new TournamentGameRecord(game.PairIndex, game.PositionIndex, game.White.Id, game.Black.Id, result, game.SubjectIsWhite);
        }

        /// <summary>
        /// Folds one already-played game's result into the running tallies/tier accumulators and
        /// raises OnGameCompleted (plus OnSessionCompleted if this was the last one). Called by
        /// RunNextGame right after playing a game itself, and by ParallelTournamentExecutor once
        /// per completed game, IN THE SESSION'S ORIGINAL GAME ORDER regardless of which worker
        /// thread actually played it or what order they finished in — this is what keeps a
        /// parallel run's report byte-identical to a sequential run's: the games are played
        /// out-of-order across threads, but always folded into state in-order on one thread.
        /// </summary>
        internal void ApplyCompletedGame(TournamentGameRecord record)
        {
            PairTally tally = _tallies[record.PairIndex];
            tally.Games++;

            switch (record.Result.Result.Outcome)
            {
                case MatchOutcome.WhiteWon:
                    if (record.SubjectIsWhite) tally.SubjectWins++; else tally.OpponentWins++;
                    break;
                case MatchOutcome.BlackWon:
                    if (record.SubjectIsWhite) tally.OpponentWins++; else tally.SubjectWins++;
                    break;
                default:
                    tally.Draws++;
                    break;
            }

            AccumulateTier(record.WhiteId, record.Result.WhiteStats);
            AccumulateTier(record.BlackId, record.Result.BlackStats);

            _nextGame++;
            OnGameCompleted?.Invoke(record);
            if (IsComplete) OnSessionCompleted?.Invoke();
        }

        /// <summary>
        /// Snapshot of everything measured so far. Safe to call mid-run (the window renders live
        /// standings from it) — a pairing that hasn't played yet just shows zero games. After the
        /// last game this is the final artifact, identical to what a batch run would have returned.
        /// </summary>
        public BenchmarkReport BuildReport()
        {
            var report = new BenchmarkReport { RunSeed = _runSeed, Mode = _modeLabel };

            for (int pairIndex = 0; pairIndex < _pairs.Count; pairIndex++)
            {
                PairTally tally = _tallies[pairIndex];
                report.PairResults.Add(new PairResult(
                    _pairs[pairIndex].Subject, _pairs[pairIndex].Opponent,
                    tally.Games, tally.SubjectWins, tally.OpponentWins, tally.Draws));
            }

            foreach (KeyValuePair<string, TierAccumulator> entry in _tierAccumulators)
                report.TierPerformances.Add(entry.Value.ToTierPerformance(entry.Key));

            return report;
        }

        private void AccumulateTier(string profileId, MatchSideStats stats)
        {
            if (!_tierAccumulators.TryGetValue(profileId, out TierAccumulator accumulator))
            {
                accumulator = new TierAccumulator();
                _tierAccumulators[profileId] = accumulator;
            }
            accumulator.Add(stats);
        }

        /// <summary>All unordered pairs among the roster's tiers — the on-demand matrix, not the
        /// routine check.</summary>
        private static (string, string)[] AllPairsRoundRobin(IReadOnlyList<AIProfile> roster)
        {
            string[] ids = roster.Select(p => p.Id).ToArray();
            var pairs = new List<(string, string)>();

            for (int i = 0; i < ids.Length; i++)
                for (int j = i + 1; j < ids.Length; j++)
                    pairs.Add((ids[i], ids[j]));

            return pairs.ToArray();
        }

        private static AIProfile ResolveInRoster(IReadOnlyList<AIProfile> roster, string id) =>
            roster.Single(p => p.Id == id);

        /// <summary>Sums MatchSideStats across every game a tier appeared in this run, on either color.</summary>
        private sealed class TierAccumulator
        {
            private int _movesSampled;
            private double _totalNodes;
            private double _totalElapsedMs;
            private int _deepestCompletedDepth;
            private int _shallowestCompletedDepth;
            private long _completedDepthSum;
            private readonly int[] _depthHistogram = new int[MatchSideStats.DepthHistogramCapacity];
            private bool _hasAnyMoves;
            private int _blunderRollOffered;
            private int _blunderRollFired;
            private int _actsPlayed;
            private int _actsResolvedByRetribution;
            private int _actsResolvedByDefection;

            public void Add(MatchSideStats stats)
            {
                if (stats.MoveCount == 0) return;

                _movesSampled += stats.MoveCount;
                _totalNodes += stats.TotalNodesVisited + stats.TotalQNodesVisited;
                _totalElapsedMs += stats.TotalElapsedMs;
                if (stats.DeepestCompletedDepth > _deepestCompletedDepth)
                    _deepestCompletedDepth = stats.DeepestCompletedDepth;
                if (!_hasAnyMoves || stats.ShallowestCompletedDepth < _shallowestCompletedDepth)
                    _shallowestCompletedDepth = stats.ShallowestCompletedDepth;
                _hasAnyMoves = true;
                _completedDepthSum += stats.CompletedDepthSum;
                for (int i = 0; i < _depthHistogram.Length; i++)
                    _depthHistogram[i] += stats.DepthHistogram[i];
                _blunderRollOffered += stats.BlunderRollOffered;
                _blunderRollFired += stats.BlunderRollFired;
                _actsPlayed += stats.ActsPlayed;
                _actsResolvedByRetribution += stats.ActsResolvedByRetribution;
                _actsResolvedByDefection += stats.ActsResolvedByDefection;
            }

            public TierPerformance ToTierPerformance(string profileId) => new TierPerformance(
                profileId,
                _movesSampled,
                meanNodesPerMove: _movesSampled == 0 ? 0 : _totalNodes / _movesSampled,
                meanMsPerMove: _movesSampled == 0 ? 0 : _totalElapsedMs / _movesSampled,
                deepestCompletedDepth: _deepestCompletedDepth,
                meanCompletedDepth: _movesSampled == 0 ? 0 : (double)_completedDepthSum / _movesSampled,
                shallowestCompletedDepth: _shallowestCompletedDepth,
                depthHistogram: (int[])_depthHistogram.Clone(),
                observedBlunderActuationRate: _blunderRollOffered == 0 ? 0f : (float)_blunderRollFired / _blunderRollOffered,
                actsPlayed: _actsPlayed,
                actsResolvedByRetribution: _actsResolvedByRetribution,
                actsResolvedByDefection: _actsResolvedByDefection);
        }
    }

    /// <summary>Statistics helpers shared by anything presenting tournament win rates.</summary>
    public static class TournamentStatistics
    {
        /// <summary>
        /// Half-width of a 95% confidence interval around an observed win rate, in win-rate points.
        /// Win-rate over N games is binomial, so its standard error is at most 0.5/sqrt(N); at 95%
        /// that's 1.96 x 0.5/sqrt(N), i.e. roughly plus-or-minus 10 points at 100 games. A pairing
        /// with no games yet has no information, so the margin saturates at 1 (anything possible).
        /// </summary>
        public static float WinRateMargin95(int games) =>
            games <= 0 ? 1f : 0.98f / (float)Math.Sqrt(games);
    }
}
