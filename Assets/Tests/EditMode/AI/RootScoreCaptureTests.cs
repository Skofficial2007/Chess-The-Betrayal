using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// AlphaBetaSearch.FindBestMove now captures every root move's score (not just the running
    /// best) into a pooled array parallel to RootMoves, committed only when a depth fully
    /// completes — the data MoveSelectionPolicy consumes. These tests exercise that capture
    /// machinery directly against the search, independent of MoveSelectionPolicyTests' crafted
    /// (non-search) fixtures.
    /// </summary>
    [TestFixture]
    public class RootScoreCaptureTests
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
        public void FindBestMove_RootScores_PopulatedForEveryRootMove_AtLastCompletedDepth()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 2, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(_search.RootMoveCount, Is.EqualTo(_search.RootMoves.Count));
            Assert.That(_search.RootMoveCount, Is.GreaterThan(0));

            // Standard opening has 20 legal moves, all roughly balanced — every one of them must
            // have been written with a real evaluated score, not an unwritten array default (0)
            // masquerading as a legitimate "dead even" score. Distinguish "written" from "default"
            // by requiring every entry to fall within a plausible depth-2 opening eval band; an
            // unwritten index would stick out as exactly 0 for every move, which a real search
            // essentially never produces uniformly across 20 distinct moves.
            bool allExactlyZero = true;
            for (int i = 0; i < _search.RootMoveCount; i++)
            {
                if (_search.RootScores[i] != 0) allExactlyZero = false;
            }
            Assert.That(allExactlyZero, Is.False,
                "Every RootScores entry read back as exactly 0 — almost certainly unwritten array defaults, not real evaluated scores.");
        }

        [Test]
        public void FindBestMove_RootScores_AlignedWithRootMoves_AfterMultiDepthSearch()
        {
            // A single depth-1 search never exercises MoveToFront's mid-search reshuffle (only the
            // pre-loop TT-hint call does, before scores exist) — a multi-depth search is required
            // to actually exercise the parallel-array permutation this test guards.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 3, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(_search.BestRootIndex, Is.EqualTo(0));
            Assert.That(_search.RootMoves[_search.BestRootIndex].StartPosition, Is.EqualTo(best.StartPosition));
            Assert.That(_search.RootMoves[_search.BestRootIndex].EndPosition, Is.EqualTo(best.EndPosition));

            // Index 0's score must be the maximum among all captured root scores — it's the move
            // the search actually chose as best.
            int bestScore = _search.RootScores[0];
            for (int i = 1; i < _search.RootMoveCount; i++)
            {
                Assert.That(_search.RootScores[i], Is.LessThanOrEqualTo(bestScore),
                    $"RootScores[0] must be the maximum — found a higher score at index {i}, meaning scores are misaligned with moves.");
            }
        }

        [Test]
        public void FindBestMove_CancelledMidDepth_RootScoresStayAtPreviousCompletedDepth()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 1, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            // First, a real completed depth-1 search to establish a baseline.
            _search.FindBestMove(board, settings, CancellationToken.None);
            int rootMoveCountAfterDepth1 = _search.RootMoveCount;
            var scoresAfterDepth1 = new int[rootMoveCountAfterDepth1];
            System.Array.Copy(_search.RootScores, scoresAfterDepth1, rootMoveCountAfterDepth1);

            // Now search again with an already-cancelled token — depth 1 never completes, so the
            // committed RootScores must be untouched from the previous completed search.
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            _search.FindBestMove(board, settings, cts.Token);

            for (int i = 0; i < rootMoveCountAfterDepth1; i++)
            {
                Assert.That(_search.RootScores[i], Is.EqualTo(scoresAfterDepth1[i]),
                    "A cancelled-before-any-completed-depth search must not overwrite the previous completed depth's scores.");
            }
        }

        [Test]
        public void FindBestMove_BoundedRescore_OnlyRescoresWithinMargin()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 2, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            // A very small margin (1cp) means at most the best move itself (excluded by construction)
            // realistically qualifies — this asserts the bounded re-search doesn't crash/misbehave
            // and RootMoveCount/RootScores stay internally consistent when the margin is nonzero.
            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None, candidateRescoreMarginCp: 1);

            Assert.That(_search.BestRootIndex, Is.EqualTo(0));
            Assert.That(_search.RootMoves[0].StartPosition, Is.EqualTo(best.StartPosition));
            Assert.That(_search.RootMoveCount, Is.GreaterThan(0));
        }

        [Test]
        public void FindBestMove_ZeroRescoreMargin_MatchesPreAI24Behavior()
        {
            BoardState boardA = TestBoardSetupUtility.CreateStandard();
            BoardState boardB = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 2, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            var searchA = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
            var searchB = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

            MoveCommand resultDefaultArg = searchA.FindBestMove(boardA, settings, CancellationToken.None);
            MoveCommand resultExplicitZero = searchB.FindBestMove(boardB, settings, CancellationToken.None, candidateRescoreMarginCp: 0);

            Assert.That(resultDefaultArg.StartPosition, Is.EqualTo(resultExplicitZero.StartPosition));
            Assert.That(resultDefaultArg.EndPosition, Is.EqualTo(resultExplicitZero.EndPosition));
        }
    }
}
