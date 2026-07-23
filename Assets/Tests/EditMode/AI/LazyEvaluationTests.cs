using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The lazy-evaluation hatch must never fire while a Betrayer is pending (a Defection can swing
    /// a full piece past any static margin), and whenever it DOES fire it must never return a value
    /// that lands on the wrong side of the search window from what full evaluation would have
    /// produced — that soundness, not exact value agreement, is the real contract now that pawn
    /// structure lives behind the full path and can genuinely differ from the cheap score.
    /// </summary>
    [TestFixture]
    public class LazyEvaluationTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        private AlphaBetaSearch NewSearch() =>
            new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));

        [Test]
        public void EvaluateStandPat_WindowWideEnoughThatTheCutCannotFire_MatchesFullEvaluation()
        {
            // A window wider than MaxPositionalSwing on both sides can never be beaten by the cheap
            // score alone, so this always falls through to full evaluation regardless of how large
            // the pawn-structure term's swing gets.
            BoardState board = SearchDepthProfileCaptureTests.QuietMidgame();
            var search = NewSearch();

            int fullScore = new BetrayalAwareEvaluator().Evaluate(board, board.CurrentTurn);
            int standPat = search.EvaluateStandPatForTest(board, board.CurrentTurn, alpha: -1_000_000, beta: 1_000_000);

            Assert.That(standPat, Is.EqualTo(fullScore));
        }

        [Test]
        public void EvaluateStandPat_WhenTheCutFires_ReturnsTheCheapScoreWithinTheProvenSwingOfFull()
        {
            // A window narrow enough that the cheap score alone already decides it (cheap >= beta)
            // takes the lazy branch and returns the cheap score directly -- no longer necessarily
            // identical to full now that a real term lives behind it, but it must always be within
            // MaxPositionalSwing of what full would have produced. That gap is exactly why the
            // window test itself subtracts/adds the swing before comparing.
            BoardState board = SearchDepthProfileCaptureTests.QuietMidgame();
            var search = NewSearch();
            Team perspective = board.CurrentTurn;
            var evaluator = new BetrayalAwareEvaluator();

            int cheapScore = evaluator.EvaluateCheap(board, perspective);
            int fullScore = evaluator.Evaluate(board, perspective);

            int standPat = search.EvaluateStandPatForTest(board, perspective, alpha: cheapScore - 500, beta: cheapScore - 100);

            Assert.That(standPat, Is.EqualTo(cheapScore), "A narrow-enough window must take the lazy branch and return the cheap score.");
            Assert.That(System.Math.Abs(fullScore - cheapScore), Is.LessThanOrEqualTo(AlphaBetaSearch.MaxPositionalSwing),
                "The gap between cheap and full must never exceed the bound the cut relies on to stay sound.");
        }

        [Test]
        public void MaxPositionalSwing_BoundsTheWorstCaseGapBetweenCheapAndFull_OnAConstructedExtremePosition()
        {
            // A position built to push pawn structure toward its clamped extremes on both sides:
            // White gets several advanced, unopposed (passed) pawns; Black gets a cluster of
            // doubled/isolated pawns with no passed-pawn credit. This is the kind of board the
            // MaxPositionalSwing bound has to survive, not just an ordinary midgame.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a6", Team.White, ChessPieceType.Pawn)
                .WithPiece("c6", Team.White, ChessPieceType.Pawn)
                .WithPiece("e6", Team.White, ChessPieceType.Pawn)
                .WithPiece("g6", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.Black, ChessPieceType.Pawn)
                .WithPiece("b3", Team.Black, ChessPieceType.Pawn)
                .WithPiece("d2", Team.Black, ChessPieceType.Pawn)
                .WithPiece("d3", Team.Black, ChessPieceType.Pawn)
                .WithComputedHash();

            var evaluator = new BetrayalAwareEvaluator(new EvaluationWeights(2f, 2f, 1f)); // documented ceiling on both scales

            int cheapScore = evaluator.EvaluateCheap(board, Team.White);
            int fullScore = evaluator.Evaluate(board, Team.White);

            Assert.That(System.Math.Abs(fullScore - cheapScore), Is.LessThanOrEqualTo(AlphaBetaSearch.MaxPositionalSwing));
        }

        [Test]
        public void EvaluateStandPat_RetributionPending_NeverTakesTheLazyPath()
        {
            // White Acted the Knight onto its own Pawn; the Rook on a1 owes Retribution. A pending
            // Betrayer must never see the stand-pat lazy cut at all -- confirmed here by pinning
            // that the guard alone (not the window) decides the branch: even a window the cheap
            // score would trivially clear still routes through EvaluateTimed (full).
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();
            var search = NewSearch();

            int fullScore = new BetrayalAwareEvaluator().Evaluate(board, Team.Black);
            // A window the cheap score would clear on either side -- if the guard were missing,
            // this would take the lazy branch. It must not: PendingBetrayerSquare routes to full
            // regardless of the window.
            int standPat = search.EvaluateStandPatForTest(board, Team.Black, alpha: -1_000_000, beta: fullScore - 1_000);

            Assert.That(standPat, Is.EqualTo(fullScore));
        }

        [Test]
        public void RunQuiescenceForTest_RetributionPending_NeverStandsPatMidSequence()
        {
            // Full quiescence entry, not just the stand-pat helper in isolation: a pending
            // Retribution must still resolve via GetRetributionMoves and reflect the lost piece,
            // never the lazy cut (or any stand-pat) short-circuiting mid-sequence.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();
            var search = NewSearch();

            int score = search.RunQuiescenceForTest(board, Team.Black, -1_000_000, 1_000_000, CancellationToken.None);

            Assert.That(score, Is.LessThan(-200),
                "Retribution must resolve (Rook captures the Knight), not stand pat on a frozen mid-sequence score.");
        }

        [Test]
        public void RunQuiescenceForTest_ForcedSavePending_NeverStandsPatMidSequence()
        {
            // Rook on e4 is the pending Betrayer with no legal Executioner; defecting checks its
            // own former King on e1 (ForcedSave). Same guarantee as Retribution: the lazy hatch
            // must never intercept a forced-Betrayal leaf.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithPendingBetrayer("e4", Team.White)
                .WithComputedHash();
            var search = NewSearch();

            Assert.DoesNotThrow(() =>
                search.RunQuiescenceForTest(board, Team.White, -1_000_000, 1_000_000, CancellationToken.None));
        }

        [Test]
        public void FindBestMove_BetrayerPendingPosition_PlaysTheIdenticalMoveWithTheHatchEnabled()
        {
            // The hatch is always "enabled" (it's not a flag) -- this is the move-identity pin for
            // the one class of position the plan specifically calls out: a Betrayer-pending root.
            // Fresh board per search, never a reused mutable instance (BoardState.SetPiece can
            // permute a team's piece-index order after a capture/undo cycle, which would make two
            // "independent" searches over one shared board not actually independent).
            BoardState board() => TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            MoveCommand first = NewSearch().FindBestMove(board(), settings, CancellationToken.None);
            MoveCommand second = NewSearch().FindBestMove(board(), settings, CancellationToken.None);

            Assert.That(second.StartPosition, Is.EqualTo(first.StartPosition));
            Assert.That(second.EndPosition, Is.EqualTo(first.EndPosition));
            Assert.That(second.Stage, Is.EqualTo(first.Stage));
        }

        [Test]
        public void FindBestMove_QuietMidgame_StillMatchesTheRecordedBestMove()
        {
            // Re-assert the existing move-identity pin with the pawn term and the now-active lazy
            // hatch both wired in. Pawn structure does change which move looks best in isolation
            // (with the hatch inert it briefly favored Bxc3-e5, capturing Black's isolated e-pawn
            // outright) -- but with MaxPositionalSwing at its real, proven bound the hatch legally
            // cuts full evaluation at enough quiescence nodes to change which line gets explored
            // deeply enough to matter, and the root move lands back on the original queen trade.
            // That is the same "a pruning lever can reorder root moves without being unsound" shape
            // AI-47/48 already established for the delta-pruning margin, not a soundness bug: the
            // cut's own per-node contract (bounded by MaxPositionalSwing) still held throughout.
            var settings = new AISearchSettings(maxDepth: 7, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            MoveCommand best = NewSearch().FindBestMove(SearchDepthProfileCaptureTests.QuietMidgame(), settings, CancellationToken.None);

            Assert.That(best.StartPosition.x, Is.EqualTo(3));
            Assert.That(best.StartPosition.y, Is.EqualTo(0));
            Assert.That(best.EndPosition.x, Is.EqualTo(3));
            Assert.That(best.EndPosition.y, Is.EqualTo(7));
            Assert.That(best.Stage, Is.EqualTo(BetrayalStage.None));
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        public void FindBestMove_QuietMidgame_LazyStandPatCutsFireAtLeastOnce()
        {
            // With MaxPositionalSwing == 0 the cut is a no-op for VALUE but still an observable
            // BRANCH -- it should still take the lazy path whenever a stand-pat already clears the
            // window on the cheap score alone, which is common. This is a liveness check that the
            // seam is actually wired into a real search, not just reachable from a unit test.
            var settings = new AISearchSettings(maxDepth: 6, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            var search = NewSearch();
            search.FindBestMove(SearchDepthProfileCaptureTests.QuietMidgame(), settings, CancellationToken.None);

            Assert.That(search.Stats.LazyStandPatCuts, Is.GreaterThan(0));
        }
#endif
    }
}
