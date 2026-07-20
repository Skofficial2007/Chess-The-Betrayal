using NUnit.Framework;
using System;
using System.Threading;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Guards the search loop's no-allocation requirement: per-ply move buffers are sized once in the
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
            // no-allocation contract, which only covers the steady-state per-node search loop.
            BoardState warmup = TestBoardSetupUtility.CreateStandard();
            _search.FindBestMove(warmup, new AISearchSettings(2, TestTimeBudgets.Generous, BetrayalUsage.Full), CancellationToken.None);
        }

        [Test]
        public void FindBestMove_FixedPosition_AllocatesNoManagedMemory()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

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

        [Test]
        public void FindBestMove_WithRescoreMargin_AllocatesNoManagedMemory()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            // Warm up the bounded-rescore path too — its own pooled arrays are already sized in the
            // constructor, but the first call still needs to JIT the new code path outside the
            // measured window.
            _search.FindBestMove(board, settings, CancellationToken.None, candidateRescoreMarginCp: 50);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            _search.FindBestMove(board, settings, CancellationToken.None, candidateRescoreMarginCp: 50);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L),
                "The bounded candidate re-search must not allocate managed memory either — " +
                "only in-place array writes and ScoreChild calls, no new lists/arrays.");
        }

        [Test]
        public void SelectFinalMove_RepeatedCalls_AllocateNoManagedMemory()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 2, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            var profile = new AIProfile("test", maxDepth: 2, timeBudget: new AITimeBudget(5000, 7500),
                blunderRate: 0.2f, blunderMarginCp: 50, betrayalAggression: 0.5f,
                attackDefenseBias: 1f, tieBreakWindowCp: 30, useOpeningBook: false);
            var rng = new SystemRandomSource(seed: 42);
            var policy = new MoveSelectionPolicy();

            _search.FindBestMove(board, settings, CancellationToken.None, candidateRescoreMarginCp: 50);

            // Warm up SelectFinalMove itself outside the measured window.
            policy.SelectFinalMove(_search.RootMoves, _search.RootScores, _search.RootMoveCount, _search.BestRootIndex, profile, rng);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            policy.SelectFinalMove(_search.RootMoves, _search.RootScores, _search.RootMoveCount, _search.BestRootIndex, profile, rng);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L),
                "MoveSelectionPolicy.SelectFinalMove must not allocate managed memory on a warmed-up instance.");
        }

        [Test]
        public void FindBestMove_NonIdentityWeightedEvaluator_AllocatesNoManagedMemory()
        {
            var weights = new EvaluationWeights(attackScale: 1.5f, defenseScale: 0.5f, betrayalOptionScale: 1.35f);
            var weightedSearch = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(weights));

            BoardState warmup = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            weightedSearch.FindBestMove(warmup, settings, CancellationToken.None);

            BoardState board = TestBoardSetupUtility.CreateStandard();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            weightedSearch.FindBestMove(board, settings, CancellationToken.None);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L),
                "The weighted evaluator (non-identity EvaluationWeights, including the king-shelter term) must not allocate managed memory during a real search.");
        }
    }
}
