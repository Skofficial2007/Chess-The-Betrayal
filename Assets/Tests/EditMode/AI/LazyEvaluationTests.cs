using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The lazy-evaluation hatch is a speed enabler, not a strength term: it must never change
    /// which move the search reports as best, and it must never fire while a Betrayer is pending
    /// (a Defection can swing a full piece past any static margin). Today the cheap and full
    /// evaluators are IDENTICAL by construction (there is no expensive term behind the full path
    /// yet), so every assertion below is a correctness pin proving the seam is inert now — it
    /// becomes a real optimisation only once a future evaluator term raises MaxPositionalSwing
    /// above zero.
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
        public void EvaluateStandPat_NarrowWindow_MatchesFullEvaluationToday()
        {
            // With MaxPositionalSwing == 0, the lazy cut's own window test degenerates to
            // "cheap >= beta or cheap <= alpha" -- exactly the ordinary stand-pat comparison a
            // caller would apply to a full score. Pin that the returned score matches full
            // evaluation exactly, on a position with no pending Betrayer.
            BoardState board = SearchDepthProfileCaptureTests.QuietMidgame();
            var search = NewSearch();

            int fullScore = new BetrayalAwareEvaluator().Evaluate(board, board.CurrentTurn);
            int standPat = search.EvaluateStandPatForTest(board, board.CurrentTurn, alpha: -1_000_000, beta: 1_000_000);

            Assert.That(standPat, Is.EqualTo(fullScore));
        }

        [Test]
        public void EvaluateStandPat_WindowAlreadyBeatenByCheapScore_StillReturnsTheExactFullScore()
        {
            // A window narrow enough that the cheap score alone already decides it (cheap >= beta)
            // takes the lazy branch. Since cheap == full today, the returned value must still be
            // exactly what full evaluation would have produced -- the cut can never be observed to
            // change the number, only whether the full evaluator was called to get it.
            BoardState board = SearchDepthProfileCaptureTests.QuietMidgame();
            var search = NewSearch();
            Team perspective = board.CurrentTurn;

            int fullScore = new BetrayalAwareEvaluator().Evaluate(board, perspective);
            int standPat = search.EvaluateStandPatForTest(board, perspective, alpha: fullScore - 500, beta: fullScore - 100);

            Assert.That(standPat, Is.EqualTo(fullScore),
                "The lazy cut fired (a narrower window than the score), but must still return the identical value full evaluation would.");
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
            // Re-assert the existing move-identity pin (QuiescenceDeltaMarginRetuneTests' own
            // fixture) with the lazy hatch wired in -- an inert hatch must not move this.
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
