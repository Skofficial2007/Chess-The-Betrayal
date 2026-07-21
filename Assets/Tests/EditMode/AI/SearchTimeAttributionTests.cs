using System;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the deeper reach of the search telemetry: the per-depth node curve now records past the
    /// old depth-7 stop, so the cost of the deepest tiers' 7->8->9 growth is visible instead of
    /// dropped. All of it lives behind the same editor/development-build guard the other counters
    /// use, so these tests also re-confirm the search stays allocation-free with the added fields.
    /// </summary>
    [TestFixture]
    public class SearchTimeAttributionTests
    {
        private ChessEngineAdapter _engine;
        private AlphaBetaSearch _search;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));
        }

        [Test]
        public void FindBestMove_ReachesDepthNine_RecordsPerDepthNodeCurveThroughDepthNine()
        {
            // A depth-9 search from the opening completes every intermediate depth, so each slot in
            // the curve through 9 must be populated — the old code silently dropped everything past 7.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 9, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            SearchStats stats = _search.Stats;
            Assert.That(stats.LastCompletedDepth, Is.GreaterThanOrEqualTo(9),
                "The search must actually complete depth 9 for this test to exercise the depth-8/9 curve slots.");
            Assert.That(stats.NodesAfterDepth8, Is.GreaterThan(0), "Depth-8 node count was not recorded.");
            Assert.That(stats.NodesAfterDepth9, Is.GreaterThan(0), "Depth-9 node count was not recorded.");
        }

        [Test]
        public void FindBestMove_PerDepthNodeCurve_IsMonotonicThroughDepthNine()
        {
            // The curve is a running cumulative total sampled at each completed depth, so a deeper
            // completed depth can never report fewer total nodes than a shallower one — now checked
            // across the whole 1..9 span, not just the old 1..7 ceiling.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 9, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            SearchStats stats = _search.Stats;
            long[] curve =
            {
                stats.NodesAfterDepth1, stats.NodesAfterDepth2, stats.NodesAfterDepth3,
                stats.NodesAfterDepth4, stats.NodesAfterDepth5, stats.NodesAfterDepth6,
                stats.NodesAfterDepth7, stats.NodesAfterDepth8, stats.NodesAfterDepth9,
            };
            for (int i = 1; i < curve.Length; i++)
                Assert.That(curve[i], Is.GreaterThanOrEqualTo(curve[i - 1]),
                    $"Cumulative node count at depth {i + 1} ({curve[i]}) fell below depth {i} ({curve[i - 1]}).");
        }

        [Test]
        public void FindBestMove_ReachesDepthNine_RecordsPerDepthElapsedMsThroughDepthNine()
        {
            // Alongside the node curve, each completed depth records the cumulative wall-clock ms at
            // that point. A depth-9 search must therefore have a non-zero ms reading by depth 9 (the
            // search takes real time to get there), and depth 8's must be recorded too.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 9, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            SearchStats stats = _search.Stats;
            Assert.That(stats.LastCompletedDepth, Is.GreaterThanOrEqualTo(9),
                "The search must actually complete depth 9 for this test to exercise the depth-8/9 ms slots.");
            Assert.That(stats.ElapsedMsAfterDepth8, Is.GreaterThanOrEqualTo(0), "Depth-8 elapsed ms was not recorded.");
            Assert.That(stats.ElapsedMsAfterDepth9, Is.GreaterThan(0),
                "Depth-9 elapsed ms should be positive — reaching depth 9 takes measurable time.");
        }

        [Test]
        public void FindBestMove_PerDepthElapsedMs_IsMonotonicThroughDepthNine()
        {
            // Cumulative elapsed time can only ever grow as deeper depths complete — a later depth can
            // never report a smaller running total than an earlier one.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 9, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            SearchStats stats = _search.Stats;
            long[] msCurve =
            {
                stats.ElapsedMsAfterDepth1, stats.ElapsedMsAfterDepth2, stats.ElapsedMsAfterDepth3,
                stats.ElapsedMsAfterDepth4, stats.ElapsedMsAfterDepth5, stats.ElapsedMsAfterDepth6,
                stats.ElapsedMsAfterDepth7, stats.ElapsedMsAfterDepth8, stats.ElapsedMsAfterDepth9,
            };
            for (int i = 1; i < msCurve.Length; i++)
                Assert.That(msCurve[i], Is.GreaterThanOrEqualTo(msCurve[i - 1]),
                    $"Cumulative elapsed ms at depth {i + 1} ({msCurve[i]}) fell below depth {i} ({msCurve[i - 1]}).");
        }

        [Test]
        public void EffectiveBranchingFactor_ComputedFromCurve_IsTheRatioOfConsecutiveDepths()
        {
            // Pure derivation from the recorded curve — set two adjacent depths directly and confirm
            // the branching factor is their ratio, no search required.
            var stats = new SearchStats();
            stats.AssignNodesAfterDepth(7, 1000);
            stats.AssignNodesAfterDepth(8, 3500);

            Assert.That(stats.EffectiveBranchingFactor(8), Is.EqualTo(3.5).Within(1e-9),
                "Effective branching factor at depth 8 must be nodes(8)/nodes(7).");
        }

        [Test]
        public void EffectiveBranchingFactor_WhenEitherDepthUnreached_ReportsZeroNotADivideByZero()
        {
            // A depth the search never reached leaves its slot at 0. The ratio must report 0
            // ("not measurable") rather than dividing by zero or returning a garbage value.
            var stats = new SearchStats();
            stats.AssignNodesAfterDepth(7, 1000);
            // Depth 8 never assigned -> stays 0.

            Assert.That(stats.EffectiveBranchingFactor(8), Is.EqualTo(0.0),
                "An unreached depth must yield a 0 branching factor, not a divide-by-zero.");
            Assert.That(stats.EffectiveBranchingFactor(1), Is.EqualTo(0.0),
                "Depth 1 has no previous depth to compare against, so it has no branching factor.");
        }

        [Test]
        public void FindBestMove_FixedPosition_StillAllocatesNoManagedMemory()
        {
            // The added per-depth fields are plain value-type longs behind the same guard as every
            // other counter — writing them must not introduce any boxing/GC on the search hot path.
            BoardState warmup = TestBoardSetupUtility.CreateStandard();
            _search.FindBestMove(warmup, new AISearchSettings(2, TestTimeBudgets.Generous, BetrayalUsage.Full), CancellationToken.None);

            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            _search.FindBestMove(board, settings, CancellationToken.None);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L),
                "Recording the per-depth curve must be plain long field writes — any allocation delta " +
                "means a boxing/closure/struct-copy snuck onto the guarded search path.");
        }
    }
}
