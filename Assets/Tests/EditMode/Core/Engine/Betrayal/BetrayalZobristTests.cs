using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    [TestFixture]
    public class BetrayalZobristTests
    {
        [Test]
        public void ZobristHash_BetrayalRightConsumed_AltersHash()
        {
            // Arrange
            BoardState boardWithRight = TestBoardSetupUtility.CreateStandard().WithBetrayalRight(true);
            BoardState boardWithoutRight = TestBoardSetupUtility.CreateStandard().WithBetrayalRight(false);

            // Act
            boardWithRight.ComputeFullZobristHash();
            boardWithoutRight.ComputeFullZobristHash();

            // Assert
            Assert.That(boardWithRight.ZobristHash, Is.Not.EqualTo(boardWithoutRight.ZobristHash),
                "Consuming the right must alter the hash to prevent transposition table errors.");
        }

        [Test]
        public void ZobristHash_ActMoveThenFullUndo_RestoresOriginalHash()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook)
                .WithPiece("e5", Team.White, ChessPieceType.Pawn)
                .WithBetrayalRight(true);

            board.ComputeFullZobristHash();
            ulong originalHash = board.ZobristHash;

            MoveCommand actMove = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e4"),
                TestBoardSetupUtility.AlgebraicToVector("e5"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e4")),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e5")),
                board).WithStage(BetrayalStage.Act);

            // Act
            ChessEngine.ApplyMoveToBoard(board, actMove, false);
            ChessEngine.UndoMoveOnBoard(board, actMove, false);

            // Assert
            Assert.That(board.ZobristHash, Is.EqualTo(originalHash));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
        }

        [Test]
        public void ZobristHash_DefectionThenUndoDefection_RestoresOriginalHash()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Knight);

            board.ComputeFullZobristHash();
            ulong originalHash = board.ZobristHash;
            Vector2Int square = TestBoardSetupUtility.AlgebraicToVector("e4");

            // Act
            board.DefectPiece(square);
            ChessEngine.UndoDefection(board, square, Team.White);

            // Assert
            Assert.That(board.ZobristHash, Is.EqualTo(originalHash));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
        }
    }
}