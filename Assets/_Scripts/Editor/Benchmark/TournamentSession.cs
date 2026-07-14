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

        public TournamentGameRecord(int pairIndex, int positionIndex, string whiteId, string blackId, MatchStatsResult result)
        {
            PairIndex = pairIndex;
            PositionIndex = positionIndex;
            WhiteId = whiteId;
            BlackId = blackId;
            Result = result;
        }
    }

    /// <summary>
    /// A whole tournament (pairings x positions x both colors) broken into single-game steps, so a
    /// caller decides the pacing: BenchmarkRunner drains it in one tight loop for batch/CI runs,
    /// while the tournament window plays one game per editor tick so the UI stays responsive and a
    /// run can be cancelled mid-way. Both callers go through this exact class with the exact same
    /// seeding, which is what makes a number seen in the window trustworthy — an interactive run at
    /// seed N is bit-identical to a batch run at seed N, by construction rather than by discipline.
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
        private readonly IReadOnlyList<(string Subject, string Opponent)> _pairs;
        private readonly List<PendingGame> _games;
        private readonly PairTally[] _tallies;
        private readonly Dictionary<string, TierAccumulator> _tierAccumulators = new Dictionary<string, TierAccumulator>();
        private readonly MatchSimulator _simulator = new MatchSimulator();
        private int _nextGame;

        public event Action<TournamentGameRecord> OnGameCompleted;
        public event Action OnSessionCompleted;

        public int TotalGames => _games.Count;
        public int GamesCompleted => _nextGame;
        public bool IsComplete => _nextGame >= _games.Count;

        private TournamentSession(string modeLabel, int runSeed, int plyCap,
            IReadOnlyList<(string Subject, string Opponent)> pairs,
            Func<string, AIProfile> resolve, int positionCount)
        {
            _modeLabel = modeLabel;
            _runSeed = runSeed;
            _plyCap = plyCap;
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

        public static TournamentSession CreateQuick(int runSeed, IReadOnlyList<AIProfile> roster,
            int plyCap = MatchSimulator.DefaultPlyCap)
        {
            return new TournamentSession(BenchmarkMode.Quick.ToString(), runSeed, plyCap,
                AdjacentPairs, id => ResolveInRoster(roster, id),
                Math.Min(QuickPositionCount, CuratedPositionSuite.Count));
        }

        public static TournamentSession CreateFull(int runSeed, IReadOnlyList<AIProfile> roster,
            int plyCap = MatchSimulator.DefaultPlyCap)
        {
            return new TournamentSession(BenchmarkMode.Full.ToString(), runSeed, plyCap,
                AllPairsRoundRobin(roster), id => ResolveInRoster(roster, id),
                CuratedPositionSuite.Count);
        }

        /// <summary>
        /// One pairing of the caller's choosing — including hand-built/custom profiles that exist in
        /// no roster, which is why this resolves against the two profiles directly instead of by id.
        /// Playing a profile against itself is legal (both colors just report under the same id).
        /// </summary>
        public static TournamentSession CreateHeadToHead(int runSeed, AIProfile subject, AIProfile opponent,
            int positionCount, int plyCap = MatchSimulator.DefaultPlyCap)
        {
            var pair = new[] { (subject.Id, opponent.Id) };
            return new TournamentSession("HeadToHead", runSeed, plyCap,
                pair, id => id == subject.Id ? subject : opponent,
                Math.Min(Math.Max(positionCount, 1), CuratedPositionSuite.Count));
        }

        /// <summary>
        /// Plays the next game synchronously (a real search at the profiles' real depths — seconds
        /// to tens of seconds for the deep tiers), updates the running tallies, and raises
        /// OnGameCompleted, plus OnSessionCompleted if it was the last one. Returns false without
        /// side effects once the session is finished, so a drain loop is just
        /// <c>while (session.RunNextGame()) { }</c>.
        /// </summary>
        public bool RunNextGame()
        {
            if (IsComplete) return false;

            PendingGame game = _games[_nextGame];

            int seedWhite = TournamentSeeding.DeriveSeed(_runSeed, game.PositionIndex, game.PairIndex, gameIndex: 0, side: 0);
            int seedBlack = TournamentSeeding.DeriveSeed(_runSeed, game.PositionIndex, game.PairIndex, gameIndex: 0, side: 1);

            BoardState position = CuratedPositionSuite.Build(game.PositionIndex);
            MatchStatsResult result = _simulator.PlayGameWithStats(position, game.White, game.Black, seedWhite, seedBlack, _plyCap);

            PairTally tally = _tallies[game.PairIndex];
            tally.Games++;
            switch (result.Result.Outcome)
            {
                case MatchOutcome.WhiteWon:
                    if (game.SubjectIsWhite) tally.SubjectWins++; else tally.OpponentWins++;
                    break;
                case MatchOutcome.BlackWon:
                    if (game.SubjectIsWhite) tally.OpponentWins++; else tally.SubjectWins++;
                    break;
                default:
                    tally.Draws++;
                    break;
            }

            AccumulateTier(game.White.Id, result.WhiteStats);
            AccumulateTier(game.Black.Id, result.BlackStats);

            _nextGame++;
            OnGameCompleted?.Invoke(new TournamentGameRecord(
                game.PairIndex, game.PositionIndex, game.White.Id, game.Black.Id, result));
            if (IsComplete) OnSessionCompleted?.Invoke();

            return true;
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
            private int _blunderRollOffered;
            private int _blunderRollFired;

            public void Add(MatchSideStats stats)
            {
                _movesSampled += stats.MoveCount;
                _totalNodes += stats.TotalNodesVisited + stats.TotalQNodesVisited;
                _totalElapsedMs += stats.TotalElapsedMs;
                if (stats.DeepestCompletedDepth > _deepestCompletedDepth)
                    _deepestCompletedDepth = stats.DeepestCompletedDepth;
                _blunderRollOffered += stats.BlunderRollOffered;
                _blunderRollFired += stats.BlunderRollFired;
            }

            public TierPerformance ToTierPerformance(string profileId) => new TierPerformance(
                profileId,
                _movesSampled,
                meanNodesPerMove: _movesSampled == 0 ? 0 : _totalNodes / _movesSampled,
                meanMsPerMove: _movesSampled == 0 ? 0 : _totalElapsedMs / _movesSampled,
                deepestCompletedDepth: _deepestCompletedDepth,
                observedBlunderActuationRate: _blunderRollOffered == 0 ? 0f : (float)_blunderRollFired / _blunderRollOffered);
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
