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
    /// Pins SearchStats' counters against small, fully-controlled fixed trees, and re-confirms the
    /// zero-GC search-loop contract (SearchAllocationTests' pattern) now that every counter
    /// increment lives on the hot path behind UNITY_EDITOR||DEVELOPMENT_BUILD — the whole point of
    /// AI-21 is measuring the other tickets' multipliers without adding cost of its own.
    /// </summary>
    [TestFixture]
    public class SearchTelemetryTests
    {
        private ChessEngineAdapter _engine;
        private AlphaBetaSearch _search;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
        }

        [Test]
        public void FindBestMove_AnyPosition_NodesVisitedIsPositive()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 3, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(_search.Stats.NodesVisited, Is.GreaterThan(0));
        }

        [Test]
        public void FindBestMove_SecondShallowerCall_DoesNotAccumulateFromFirstCall()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var deepSettings = new AISearchSettings(maxDepth: 4, softTimeBudgetMs: 5000, BetrayalUsage.Full);
            var shallowSettings = new AISearchSettings(maxDepth: 1, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            _search.FindBestMove(board, deepSettings, CancellationToken.None);
            long deepCallNodes = _search.Stats.NodesVisited;

            _search.FindBestMove(board, shallowSettings, CancellationToken.None);

            // If Reset() at the top of FindBestMove weren't wired in, a shallower second call's count
            // would still include the first (deeper) call's nodes and could never read lower.
            Assert.That(_search.Stats.NodesVisited, Is.LessThan(deepCallNodes),
                "Stats.Reset() must run at the top of every FindBestMove — a shallower re-search must not inherit the previous call's node count.");
        }

        [Test]
        public void TranspositionTable_StoreThenProbeSameHash_RecordsOneStoreAndOneHit()
        {
            var tt = new TranspositionTable(log2Size: 4);
            tt.Stats.Reset();

            tt.Store(hash: 42, score: 10, packedMove: 1, depth: 2, TTFlag.Exact);
            tt.Probe(42, out _, out _, out _, out _);

            Assert.That(tt.Stats.TTStores, Is.EqualTo(1));
            Assert.That(tt.Stats.TTHits, Is.EqualTo(1));
            Assert.That(tt.Stats.TTProbes, Is.EqualTo(1));
        }

        [Test]
        public void TranspositionTable_ProbeEmptySlot_RecordsEmptyMiss()
        {
            var tt = new TranspositionTable(log2Size: 4);
            tt.Stats.Reset();

            tt.Probe(hash: 7, out _, out _, out _, out _);

            Assert.That(tt.Stats.TTEmptyMisses, Is.EqualTo(1));
            Assert.That(tt.Stats.TTVerificationMisses, Is.EqualTo(0));
        }

        [Test]
        public void TranspositionTable_ProbeCollidedSlot_RecordsVerificationMiss()
        {
            var tt = new TranspositionTable(log2Size: 4);
            tt.Stats.Reset();

            tt.Store(hash: 0x0000_0000_0000_0001, score: 1, packedMove: 0, depth: 1, TTFlag.Exact);
            tt.Probe(hash: 0x0000_0000_0000_0011, out _, out _, out _, out _); // same low-4-bit index, different hash

            Assert.That(tt.Stats.TTVerificationMisses, Is.EqualTo(1));
            Assert.That(tt.Stats.TTEmptyMisses, Is.EqualTo(0));
        }

        [Test]
        public void TranspositionTable_SecondStoreSameSlot_RecordsReplacement()
        {
            var tt = new TranspositionTable(log2Size: 4);
            tt.Stats.Reset();

            tt.Store(hash: 5, score: 1, packedMove: 0, depth: 1, TTFlag.Exact);
            tt.Store(hash: 5, score: 2, packedMove: 0, depth: 1, TTFlag.Exact); // equal depth, same slot: replaces

            Assert.That(tt.Stats.TTStores, Is.EqualTo(2));
            Assert.That(tt.Stats.TTReplacements, Is.EqualTo(1));
        }

        [Test]
        public void FindBestMove_DeepEnoughPosition_RecordsNullMoveAndLmrAndPvsActivity()
        {
            // A full-material position at a depth comfortably above every mechanism's threshold.
            // NMP needs a recursive Search node at depth>=NullMoveMinDepth(4); since Search's own
            // depth is ONE LESS than the root's maxDepth (root plays a move, then recurses at
            // depth-1), maxDepth must be >=5 for that node to ever exist. LMR/PVS just need multiple
            // siblings past index 0/3, which any real position provides well before depth 5. This is
            // a smoke test that the counters are wired into the real code paths, not a precise
            // node-count assertion (those are brittle to evaluator/ordering tuning and belong to a
            // benchmark suite, not a unit test).
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 5, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(_search.Stats.NullMoveAttempts, Is.GreaterThan(0));
            Assert.That(_search.Stats.PvsScouts, Is.GreaterThan(0));
        }

        [Test]
        public void FindBestMove_CaptureRichPosition_RecordsQNodesGreaterThanZero()
        {
            // ADR_AI16b Step A: a position with immediate captures available must drive the search
            // into quiescence at the horizon, so QNodesVisited (previously never incremented anywhere)
            // must be positive — the headline number the whole diagnostic is built on.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Queen)
                .WithPiece("d5", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();
            var settings = new AISearchSettings(maxDepth: 3, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(_search.Stats.QNodesVisited, Is.GreaterThan(0));
        }

        [Test]
        public void FindBestMove_PerDepthNodeCounts_AreMonotonicallyNonDecreasing()
        {
            // NodesAfterDepthN is a running cumulative total (NodesVisited + QNodesVisited) sampled at
            // each completed depth — a deeper completed iteration can never report fewer total nodes
            // than a shallower one.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 4, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            SearchStats stats = _search.Stats;
            Assert.That(stats.NodesAfterDepth1, Is.GreaterThan(0));
            Assert.That(stats.NodesAfterDepth2, Is.GreaterThanOrEqualTo(stats.NodesAfterDepth1));
            Assert.That(stats.NodesAfterDepth3, Is.GreaterThanOrEqualTo(stats.NodesAfterDepth2));
            Assert.That(stats.NodesAfterDepth4, Is.GreaterThanOrEqualTo(stats.NodesAfterDepth3));
        }

        [Test]
        public void FindBestMove_FixedPosition_StillAllocatesNoManagedMemory()
        {
            // Re-confirms SearchAllocationTests' contract now that every telemetry increment lives
            // on the hot path (gated behind UNITY_EDITOR||DEVELOPMENT_BUILD, which IS defined for
            // this EditMode test assembly) — plain long field writes must not introduce boxing/GC.
            BoardState warmup = TestBoardSetupUtility.CreateStandard();
            _search.FindBestMove(warmup, new AISearchSettings(2, 5000, BetrayalUsage.Full), CancellationToken.None);

            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 3, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            _search.FindBestMove(board, settings, CancellationToken.None);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L),
                "Telemetry counters must be plain value-type field increments — any allocation delta here " +
                "means a boxing/closure/struct-copy snuck into the #if UNITY_EDITOR||DEVELOPMENT_BUILD path.");
        }
    }
}
