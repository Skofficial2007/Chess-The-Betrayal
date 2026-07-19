using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Regression for the depth-7 gate-check fix: quiescence previously explored every capture to
    /// exhaustion regardless of value, which is the classic quiescence explosion on a midgame
    /// position (many pieces, many possible captures) — the reason depth-7 took ~23s instead of the
    /// ADR's 2-3s target. Delta pruning skips a capture only when even its best-case outcome can't
    /// raise alpha, so it must never change quiescence's final score/best-move, only how many
    /// hopeless lines it bothers exploring on the way there.
    /// </summary>
    [TestFixture]
    public class QuiescenceDeltaPruningTests
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
        public void RunQuiescenceForTest_HopelessPawnCaptureFarBehind_StillReturnsFiniteConsistentScore()
        {
            // White is down a queen for nothing and can only recapture a pawn — the capture cannot
            // plausibly raise alpha given how far behind White already is, so delta pruning should
            // skip exploring it, and quiescence must still resolve cleanly to a stand-pat score.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen)
                .WithPiece("h8", Team.Black, ChessPieceType.Queen)
                .WithTurn(Team.White)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;

            int score = 0;
            Assert.DoesNotThrow(() => score = _search.RunQuiescenceForTest(board, Team.White, CancellationToken.None));

            Assert.That(score, Is.GreaterThan(-1_000_000).And.LessThan(1_000_000));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void RunQuiescenceForTest_WinningQueenCapture_StillFindsIt()
        {
            // A genuinely winning capture (pawn takes an undefended queen) must NOT be pruned —
            // its optimistic gain easily clears any reasonable alpha, so quiescence must still find
            // and report the material swing.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                .WithPiece("e5", Team.Black, ChessPieceType.Queen) // undefended, capturable by the d4 pawn
                .WithTurn(Team.White)
                .WithComputedHash();

            int scoreWithCapture = _search.RunQuiescenceForTest(board, Team.White, CancellationToken.None);

            // Same position minus the hanging queen: the quiescence score must be meaningfully lower
            // than the capture case, proving the capture was actually explored and credited.
            BoardState boardNoQueen = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();
            int scoreWithoutQueen = _search.RunQuiescenceForTest(boardNoQueen, Team.White, CancellationToken.None);

            Assert.That(scoreWithCapture, Is.GreaterThan(scoreWithoutQueen),
                "Capturing the hanging queen must still be found and credited by quiescence after adding delta pruning.");
        }

        [Test]
        public void FindBestMove_MidgamePosition_StillFindsHangingQueen_WithDeltaPruningActive()
        {
            // Full-search regression: a hanging queen must still be the reported best capture at a
            // normal search depth even with quiescence delta pruning enabled everywhere below it.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Rook)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen) // hangs to Rd4-d8
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.StartPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("d4")));
            Assert.That(best.EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("d8")));
        }
    }
}
