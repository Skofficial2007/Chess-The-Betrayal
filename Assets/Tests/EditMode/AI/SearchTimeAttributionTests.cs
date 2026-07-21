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
        public void FindBestMove_CaptureRichPosition_RecordsTimeForEverySection()
        {
            // A real search scores positions, generates moves, probes/stores the table, and drops
            // into quiescence at the horizon — so after one, every section must have been sampled at
            // least once and accumulated some ticks. This proves the timers are wired into the live
            // paths, not that any particular split is "correct" (the sampled ms is an estimate).
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Queen)
                .WithPiece("d5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithComputedHash();
            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            SearchStats stats = _search.Stats;
            Assert.That(stats.EvalCalls, Is.GreaterThan(0), "No eval calls were counted.");
            Assert.That(stats.EvalSamples, Is.GreaterThan(0), "Eval was never sampled.");
            Assert.That(stats.MoveGenSamples, Is.GreaterThan(0), "Move generation was never sampled.");
            Assert.That(stats.TTSamples, Is.GreaterThan(0), "The transposition table was never sampled.");
            Assert.That(stats.QuiescenceSamples, Is.GreaterThan(0), "Quiescence was never sampled.");
            Assert.That(stats.EvalSampledTicks, Is.GreaterThanOrEqualTo(0), "Eval ticks went negative.");
        }

        [Test]
        public void EstimatedSectionMs_ScalesSampledTicksBackUpByTheSamplingRate()
        {
            // With one call in N sampled, the estimate multiplies the sampled ticks by (calls/samples).
            // 100 calls, 1 sampled, 1 second of ticks => ~100 seconds estimated across all calls.
            var stats = new SearchStats();
            long oneSecondOfTicks = System.Diagnostics.Stopwatch.Frequency;

            double estimatedMs = stats.EstimatedSectionMs(sampledTicks: oneSecondOfTicks, samples: 1, calls: 100);

            Assert.That(estimatedMs, Is.EqualTo(100_000.0).Within(1.0),
                "Estimated section ms must scale the sampled ticks up by calls/samples and convert to ms.");
            Assert.That(stats.EstimatedSectionMs(0, 0, 0), Is.EqualTo(0.0),
                "With nothing sampled the estimate must be 0, not a divide-by-zero.");
        }

        [Test]
        public void FindBestMove_RunTwiceFromCleanState_VisitsTheSameNodesAndPicksTheSameForcingMove()
        {
            // The section timers only read a clock — they never touch a score, a bound, or the move
            // order — so they cannot change the search's decision. Pin that: two FRESH searches (each
            // with its own table and history) on the same position must visit the exact same number
            // of nodes and pick the same move. If a timer leaked into control flow, one would drift.
            //
            // The position is deliberately ASYMMETRIC with a single clearly-best capture, not the
            // opening: from the symmetric start there are genuinely equal moves (the mirror-image
            // knight developments score identically), and which of two equal moves wins is a
            // tie-break the search has never promised to make deterministically — pinning it there
            // would test tie-break luck, not instrumentation. A position with one dominant move
            // removes the tie so this tests only what it means to.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Rook)
                .WithPiece("d7", Team.White, ChessPieceType.Knight)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithComputedHash();
            var settings = new AISearchSettings(maxDepth: 6, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            var firstSearch = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));
            MoveCommand firstMove = firstSearch.FindBestMove(board, settings, CancellationToken.None);
            long firstNodes = firstSearch.Stats.NodesVisited;

            var secondSearch = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));
            MoveCommand secondMove = secondSearch.FindBestMove(board, settings, CancellationToken.None);
            long secondNodes = secondSearch.Stats.NodesVisited;

            Assert.That(secondNodes, Is.EqualTo(firstNodes),
                "Node count drifted between two identical fresh searches — a timer leaked into the search's control flow.");
            Assert.That(secondMove, Is.EqualTo(firstMove),
                "Two identical fresh searches picked different moves — the search is not deterministic on this position.");
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
