using System.Collections.Generic;
using System.Linq;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Drives MatchSimulator across a tournament and returns one BenchmarkReport covering both
    /// strength (win/draw/loss per pair) and performance (SearchStats per tier) from the same
    /// pass — a move-ordering or evaluator change usually shifts both together, so measuring them
    /// separately would cost twice the search time to diagnose the same root cause.
    ///
    /// Dev/editor-only. Nothing in Core, AI, or the shipped player build ever references this —
    /// it's the same category of tool as the opening-book compiler, not a player-facing feature.
    /// </summary>
    public static class BenchmarkRunner
    {
        // Quick mode covers less than a tenth of Full's game count, which is the point — a
        // per-commit-adjacent sanity check trades statistical confidence for finishing in minutes,
        // not the tens of minutes Full costs across every tier including the still-slow high end.
        // Full mode plays every curated position (CuratedPositionSuite.Count), no separate constant needed.
        private const int QuickPositionCount = 4;

        /// <summary>The strength-chain adjacency the preset table promises — same pairs
        /// AIProfileStrengthOrderingTests checks, reused here so Quick mode measures the exact
        /// relationships the table's own regression tests already gate on.</summary>
        private static readonly (string Subject, string Opponent)[] AdjacentPairs =
        {
            ("normal", "easy"),
            ("hard", "normal"),
            ("extreme", "hard"),
            ("impossible", "extreme"),
            ("aggressive", "normal"),
            ("aggressive", "easy"),
        };

        public static BenchmarkReport RunAll(int runSeed, BenchmarkMode mode) =>
            RunAll(runSeed, mode, AIProfileTable.BuiltIn, MatchSimulator.DefaultPlyCap);

        /// <summary>
        /// Same tournament shape as the two-argument overload, but resolves profile ids against
        /// <paramref name="roster"/> instead of the shipped built-in table, and caps each game at
        /// <paramref name="plyCap"/> plies. Exists so tests can exercise RunAll's own contract
        /// (pairing count, reproducibility, report shape) against fast, shallow fixture profiles
        /// with a tight ply cap, without paying the real tournament's search-depth-and-length cost
        /// — the real tournament (against AIProfileTable.BuiltIn, full ply cap) is only ever run
        /// from the menu/batchmode entry points.
        /// </summary>
        public static BenchmarkReport RunAll(int runSeed, BenchmarkMode mode,
            System.Collections.Generic.IReadOnlyList<AIProfile> roster, int plyCap = MatchSimulator.DefaultPlyCap)
        {
            var report = new BenchmarkReport { RunSeed = runSeed, Mode = mode.ToString() };
            var tierAccumulators = new Dictionary<string, TierAccumulator>();

            IReadOnlyList<(string Subject, string Opponent)> pairs =
                mode == BenchmarkMode.Quick ? AdjacentPairs : AllPairsRoundRobin(roster);
            int positionCount = mode == BenchmarkMode.Quick
                ? System.Math.Min(QuickPositionCount, CuratedPositionSuite.Count)
                : CuratedPositionSuite.Count;

            var simulator = new MatchSimulator();

            for (int pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
            {
                (string subjectId, string opponentId) = pairs[pairIndex];
                AIProfile subject = ResolveInRoster(roster, subjectId);
                AIProfile opponent = ResolveInRoster(roster, opponentId);

                int subjectWins = 0, opponentWins = 0, draws = 0, games = 0;

                for (int positionIndex = 0; positionIndex < positionCount; positionIndex++)
                {
                    BoardState position = CuratedPositionSuite.Build(positionIndex);

                    // Color-swapped: subject plays White then Black against the same position,
                    // cancelling first-move advantage — see CuratedPositionSuite's own doc comment.
                    ScoreGame(simulator, position, subject, opponent, subjectId, opponentId, runSeed, pairIndex, positionIndex, plyCap,
                        subjectIsWhite: true, tierAccumulators, ref subjectWins, ref opponentWins, ref draws);
                    games++;

                    ScoreGame(simulator, position, opponent, subject, opponentId, subjectId, runSeed, pairIndex, positionIndex, plyCap,
                        subjectIsWhite: false, tierAccumulators, ref subjectWins, ref opponentWins, ref draws);
                    games++;
                }

                report.PairResults.Add(new PairResult(subjectId, opponentId, games, subjectWins, opponentWins, draws));
            }

            foreach (KeyValuePair<string, TierAccumulator> entry in tierAccumulators)
                report.TierPerformances.Add(entry.Value.ToTierPerformance(entry.Key));

            return report;
        }

        /// <summary>Plays one game, records it into the running win/draw/loss tally from the
        /// SUBJECT's perspective regardless of which color the subject played, and folds both
        /// sides' search telemetry into their respective tier accumulators.</summary>
        private static void ScoreGame(
            MatchSimulator simulator, BoardState position, AIProfile whiteProfile, AIProfile blackProfile,
            string whiteId, string blackId, int runSeed, int pairIndex, int positionIndex, int plyCap, bool subjectIsWhite,
            Dictionary<string, TierAccumulator> tierAccumulators,
            ref int subjectWins, ref int opponentWins, ref int draws)
        {
            int seedWhite = TournamentSeeding.DeriveSeed(runSeed, positionIndex, pairIndex, gameIndex: 0, side: 0);
            int seedBlack = TournamentSeeding.DeriveSeed(runSeed, positionIndex, pairIndex, gameIndex: 0, side: 1);

            MatchStatsResult result = simulator.PlayGameWithStats(position, whiteProfile, blackProfile, seedWhite, seedBlack, plyCap);

            switch (result.Result.Outcome)
            {
                case MatchOutcome.WhiteWon:
                    if (subjectIsWhite) subjectWins++; else opponentWins++;
                    break;
                case MatchOutcome.BlackWon:
                    if (subjectIsWhite) opponentWins++; else subjectWins++;
                    break;
                default:
                    draws++;
                    break;
            }

            AccumulateTier(tierAccumulators, whiteId, result.WhiteStats);
            AccumulateTier(tierAccumulators, blackId, result.BlackStats);
        }

        private static void AccumulateTier(Dictionary<string, TierAccumulator> accumulators, string profileId, MatchSideStats stats)
        {
            if (!accumulators.TryGetValue(profileId, out TierAccumulator accumulator))
            {
                accumulator = new TierAccumulator();
                accumulators[profileId] = accumulator;
            }
            accumulator.Add(stats);
        }

        /// <summary>All unordered pairs among the roster's tiers — the on-demand mode, not the
        /// routine one.</summary>
        private static (string, string)[] AllPairsRoundRobin(System.Collections.Generic.IReadOnlyList<AIProfile> roster)
        {
            string[] ids = roster.Select(p => p.Id).ToArray();
            var pairs = new List<(string, string)>();

            for (int i = 0; i < ids.Length; i++)
                for (int j = i + 1; j < ids.Length; j++)
                    pairs.Add((ids[i], ids[j]));

            return pairs.ToArray();
        }

        private static AIProfile ResolveInRoster(System.Collections.Generic.IReadOnlyList<AIProfile> roster, string id) =>
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
}
