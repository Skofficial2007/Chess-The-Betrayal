using NUnit.Framework;
using System.Collections.Generic;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;
using ChessTheMasterPiece.Tests.Utilities;

namespace ChessTheMasterPiece.Tests.EditMode.Engine
{
    [TestFixture]
    public class MoveCommandTests
    {
        [Test]
        public void MoveCommand_Constructor_PieceDataSnapshotIsImmutable()
        {
            // Arrange
            PieceData pawn = new PieceData(Team.White, ChessPieceType.Pawn, 1, 1, false);
            Vector2Int from = new Vector2Int(0, 1);
            Vector2Int to = new Vector2Int(0, 2);

            // Act
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, pawn, default);

            // Assert
            Assert.That(move.PieceTeam, Is.EqualTo(Team.White), "MoveCommand should accurately store the PieceTeam.");
            Assert.That(move.PieceType, Is.EqualTo(ChessPieceType.Pawn), "MoveCommand should accurately store the PieceType.");
        }

        [Test]
        public void MoveCommand_IsCapture_ReturnsCorrectBool()
        {
            // Arrange: Capture
            PieceData queen = new PieceData(Team.White, ChessPieceType.Queen, 1, 0, false);
            PieceData rook = new PieceData(Team.Black, ChessPieceType.Rook, -1, 7, false);
            MoveCommand captureMove = MoveCommand.CreateStandardMove(Vector2Int.Zero, Vector2Int.One, queen, rook);

            // Arrange: Non-Capture
            MoveCommand standardMove = MoveCommand.CreateStandardMove(Vector2Int.Zero, Vector2Int.One, queen, default);

            // Assert
            Assert.That(captureMove.IsCapture, Is.True);
            Assert.That(captureMove.CapturedType, Is.EqualTo(ChessPieceType.Rook));
            Assert.That(standardMove.IsCapture, Is.False);
        }

        [Test]
        public void ApplyMoveToBoard_StandardMove_PieceArrivesAtDestination()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateStandard();
            Vector2Int e2 = TestBoardSetupUtility.AlgebraicToVector("e2");
            Vector2Int e4 = TestBoardSetupUtility.AlgebraicToVector("e4");
            MoveCommand move = MoveCommand.CreateStandardMove(e2, e4, board.GetPiece(e2), default, board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, move);

            // Assert
            Assert.That(board.GetPiece(e4).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(board.GetPiece(e2).IsEmpty, Is.True);
        }

        [Test]
        public void ApplyMoveToBoard_CaptureMove_PieceAddedToGraveyard()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.Rook)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King);

            MoveCommand capture = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e1"),
                TestBoardSetupUtility.AlgebraicToVector("e5"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e1")),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e5")),
                board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, capture);

            // Assert
            Assert.That(board.BlackCaptured.Count, Is.EqualTo(1));
            Assert.That(board.BlackCaptured[0].Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e5")).Team, Is.EqualTo(Team.White));
        }

        [Test]
        public void ApplyMoveToBoard_PromotionMove_PawnReplacedByQueen()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            MoveCommand promotion = MoveCommand.CreatePromotionMove(
                TestBoardSetupUtility.AlgebraicToVector("e7"),
                TestBoardSetupUtility.AlgebraicToVector("e8"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e7")),
                ChessPieceType.Queen,
                default,
                board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, promotion);

            // Assert
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e8")).Type, Is.EqualTo(ChessPieceType.Queen));
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e7")).IsEmpty, Is.True);
        }

        [Test]
        public void ApplyMoveToBoard_EnPassant_CapturedPawnRemovedFromCorrectSquare()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e5", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("f5", Team.Black, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithEnPassantFile(5); // f-file

            MoveCommand epMove = MoveCommand.CreateEnPassantMove(
                TestBoardSetupUtility.AlgebraicToVector("e5"),
                TestBoardSetupUtility.AlgebraicToVector("f6"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e5")),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("f5")),
                TestBoardSetupUtility.AlgebraicToVector("f5"),
                board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, epMove);

            // Assert
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("f5")).IsEmpty, Is.True, "Captured pawn must be removed from f5.");
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("f6")).Type, Is.EqualTo(ChessPieceType.Pawn), "White pawn must arrive at f6.");
        }
    }
}