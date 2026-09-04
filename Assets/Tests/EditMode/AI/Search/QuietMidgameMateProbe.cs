using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI.Agent;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.AI.Positions;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Tests.EditMode.Support;

namespace ChessTheBetrayal.Tests.EditMode.AI.Search
{
    /// <summary>
    /// Whether the quiet midgame position really holds a forced mate seven plies deep. Two devices
    /// disagree and both cannot be right.
    ///
    /// Running the impossible tier two hundred times against it, one phone reported a forced mate on
    /// three and played h2-h4, and ran out of clock at depth six on the rest. A faster phone
    /// completed depth seven on all two hundred and reported no mate at all.
    ///
    /// Worth knowing before reading the numbers: the position is mirror-symmetric and leaves the
    /// Betrayal unspent, so the tree carries Act, Retribution and Defection lines. A mate three and a
    /// half moves deep is not absurd there the way it would be in ordinary chess, and a rook-pawn
    /// push is not obviously the wrong first move for one - which is why this is measured rather
    /// than argued.
    ///
    /// The first arm settles it. An unbounded search takes as long as it needs at each depth, so
    /// whatever depth seven holds it will find. If it reports a mate then every completed
    /// depth-seven search should report one, and the faster phone completed two hundred without.
    ///
    /// Explicit and measurement-only: it runs unbounded searches deliberately and asserts no
    /// verdict. Read the table.
    /// </summary>
    [TestFixture]
    [Explicit("Measurement only - runs unbounded searches to settle what depth 7 actually holds.")]
    [Category(TestCategories.OnDemand)]
    public class QuietMidgameMateProbe
    {
        private const int TimeoutMs = 600_000;

        /// <summary>Enough repetitions to catch something a phone hit on three runs in two hundred,
        /// if it is anywhere near that likely here, without the arm outlasting anyone's patience.</summary>
        private const int BudgetedRepeats = 20;

        /// <summary>
        /// Clocks short enough to stop this machine around the depths the phone was stopped at.
        /// The tier's own three-second budget is no use for the question: a desktop is far past
        /// depth seven by then, so it never runs the search the phone was running. These bracket
        /// the unbounded climb measured in the first arm.
        /// </summary>
        private static readonly int[] BudgetsMs = { 100, 150, 200, 300, 400, 600, 800, 1200 };

        private const string TierId = "impossible";

        [Test]
        [Timeout(TimeoutMs)]
        public void WhatDepthSevenActuallyHoldsInTheQuietMidgame()
        {
            IChessEngine engine = new ChessEngineAdapter();
            AIProfile profile = new AIProfileTableProvider().Resolve(TierId);

            TestContext.WriteLine($"[{TierId}] quiet-midgame, no clock - one search per depth, each from cold.");
            TestContext.WriteLine("This is the ground truth: whatever a depth really holds, an unbounded search of it finds.");
            TestContext.WriteLine(" asked |  elapsed | stop reason  | reached | best move          | best score");
            TestContext.WriteLine("-------|----------|--------------|---------|--------------------|-----------");

            for (int depth = 1; depth <= 7; depth++)
            {
                var settings = new AISearchSettings(depth, profile.TimeBudget, BetrayalUsage.Full);
                Report($"d{depth}", RunOnce(engine, profile, settings, CancellationToken.None));
            }

            TestContext.WriteLine("");
            TestContext.WriteLine(
                $"[{TierId}] quiet-midgame, {BudgetedRepeats} repeats per clock, the tier's own ceiling of "
                + $"{profile.MaxDepth} - searches stopped mid-climb, which is the state the phone was in.");
            TestContext.WriteLine(" clock | mates | depths reached");
            TestContext.WriteLine("-------|-------|---------------");

            int totalMates = 0;
            foreach (int budgetMs in BudgetsMs)
            {
                var depthsSeen = new SortedDictionary<int, int>();
                int mates = 0;

                for (int rep = 0; rep < BudgetedRepeats; rep++)
                {
                    var settings = new AISearchSettings(profile.MaxDepth, profile.TimeBudget, BetrayalUsage.Full);

                    using var cts = new CancellationTokenSource();
                    cts.CancelAfter(budgetMs);

                    Outcome outcome = RunOnce(engine, profile, settings, cts.Token);

                    depthsSeen.TryGetValue(outcome.CompletedDepth, out int seen);
                    depthsSeen[outcome.CompletedDepth] = seen + 1;

                    if (outcome.StopReason != SearchStopReason.MateFound) continue;

                    mates++;
                    TestContext.WriteLine(
                        $"    MATE at {budgetMs}ms rep {rep + 1}: depth {outcome.CompletedDepth}, "
                        + $"{outcome.BestMove}, score {outcome.BestScore}");
                }

                totalMates += mates;
                var spread = new StringBuilder();
                foreach (KeyValuePair<int, int> entry in depthsSeen)
                {
                    if (spread.Length > 0) spread.Append(", ");
                    spread.Append($"d{entry.Key} x{entry.Value}");
                }

                TestContext.WriteLine($"{budgetMs,5}ms | {mates,5} | {spread}");
            }

            TestContext.WriteLine("");
            TestContext.WriteLine(
                $"{totalMates} forced mates over {BudgetsMs.Length * BudgetedRepeats} clock-stopped searches. "
                + "The phone reported one on 3 of 200 and the unbounded arm above finds none at any depth, so a "
                + "mate appearing here at all is a mate that only exists when the clock stops the search.");
        }

        /// <summary>
        /// The same question at the volume it needs. The phone hit this on three runs in two hundred,
        /// so a hundred and sixty searches finding none proves less than it looks - at that rate an
        /// empty result comes up about one time in eleven by luck. This narrows it to the two clocks
        /// that leave the search where the phone's was; a wide sweep at this rep count would spend
        /// its time on depths that were never in question.
        /// </summary>
        [Test]
        [Timeout(TimeoutMs)]
        public void CanTheClockManufactureAForcedMateAtDepthSeven()
        {
            IChessEngine engine = new ChessEngineAdapter();
            AIProfile profile = new AIProfileTableProvider().Resolve(TierId);

            const int repeats = 200;
            int[] clocks = { 600, 1200 };
            int totalMates = 0;

            foreach (int budgetMs in clocks)
            {
                int mates = 0;
                for (int rep = 0; rep < repeats; rep++)
                {
                    var settings = new AISearchSettings(profile.MaxDepth, profile.TimeBudget, BetrayalUsage.Full);

                    using var cts = new CancellationTokenSource();
                    cts.CancelAfter(budgetMs);

                    Outcome outcome = RunOnce(engine, profile, settings, cts.Token);
                    if (outcome.StopReason != SearchStopReason.MateFound) continue;

                    mates++;
                    TestContext.WriteLine(
                        $"    MATE at {budgetMs}ms rep {rep + 1}: depth {outcome.CompletedDepth}, "
                        + $"{outcome.BestMove}, score {outcome.BestScore}");
                }

                totalMates += mates;
                TestContext.WriteLine($"{budgetMs,5}ms x{repeats}: {mates} forced mate(s)");
            }

            TestContext.WriteLine("");
            TestContext.WriteLine(
                $"{totalMates} over {clocks.Length * repeats} searches. The unbounded arm shows depth 7 holds no "
                + "mate, so any hit here is one the clock invented; none at this volume points the cause away "
                + "from this machine and toward the device build, which no report can tie to a revision.");
        }

        private static void Report(string label, Outcome outcome)
        {
            TestContext.WriteLine(
                $"{label,6} | {outcome.ElapsedMs,6}ms | {outcome.StopReason,-12} | {outcome.CompletedDepth,7} "
                + $"| {outcome.BestMove,-18} | {outcome.BestScore,10}");
        }

        private static Outcome RunOnce(IChessEngine engine, AIProfile profile, AISearchSettings settings,
            CancellationToken ct)
        {
            // Built the way a real search is built, and fresh every time: a table carried over from
            // the previous arm would let a later depth answer from work an earlier one did, which is
            // the opposite of the cold search both phones were running.
            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(profile)),
                transpositionTable: new TranspositionTable(log2Size: AsyncAIAgent.ProductionTranspositionTableLog2Size));

            BoardState board = DepthWallPositions.QuietMidgame();

            var stopwatch = Stopwatch.StartNew();
            MoveCommand best = search.FindBestMove(board, settings, ct, profile.RescoreMarginCp,
                enableInstabilityTimeManagement: true);
            stopwatch.Stop();

            int bestScore = search.RootMoveCount > 0 ? search.RootScores[search.BestRootIndex] : 0;

            return new Outcome(stopwatch.ElapsedMilliseconds, search.StopReason,
                MoveNotation.Describe(best), bestScore, search.LastCompletedDepth);
        }

        private readonly struct Outcome
        {
            public readonly long ElapsedMs;
            public readonly SearchStopReason StopReason;
            public readonly string BestMove;
            public readonly int BestScore;
            public readonly int CompletedDepth;

            public Outcome(long elapsedMs, SearchStopReason stopReason, string bestMove, int bestScore,
                int completedDepth)
            {
                ElapsedMs = elapsedMs;
                StopReason = stopReason;
                BestMove = bestMove;
                BestScore = bestScore;
                CompletedDepth = completedDepth;
            }
        }
    }
}
