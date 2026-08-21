using NUnit.Framework;
using System;
using System.Threading;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Guards the zero-GC search-loop requirement: per-ply move buffers are pre-sized once in the
    /// constructor and reused via Clear(), move ordering is an in-place insertion sort, and the
    /// root Betrayal filter is an in-place swap-remove — none of that should allocate once the
    /// search itself is running. A regression here (a LINQ call, a closure, a List.Sort(lambda))
    /// shows up as thread-allocated bytes climbing during FindBestMove, not before or after it.
    /// </summary>
    [TestFixture]
    public class SearchAllocationTests
    {
        private ChessEngineAdapter _engine;
        private AlphaBetaSearch _search;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

            // Warm up the buffers array and JIT the search path once outside the measured window —
            // the constructor and first-call JIT both allocate legitimately and aren't part of the
            // zero-GC contract, which only covers the steady-state per-node search loop.
            BoardState warmup = TestBoardSetupUtility.CreateStandard();
            _search.FindBestMove(warmup, new AISearchSettings(2, 5000, BetrayalUsage.Full), CancellationToken.None);
        }

        [Test]
        public void FindBestMove_FixedPosition_AllocatesNoManagedMemory()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 3, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            _search.FindBestMove(board, settings, CancellationToken.None);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L),
                "FindBestMove must not allocate managed memory once its buffers are warmed up — " +
                "any delta here means a per-node allocation snuck into the search loop.");
        }
    }
}
