using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Movement.Pieces
{
    [TestFixture]
    public class KnightMovementTests
    {
        private List<MoveCommand> _outputBuffer;

        [SetUp]
        public void Setup() { _outputBuffer = new List<MoveCommand>(); }

        [Test]
        public void GetLegalMoves_KnightInCentre_ReturnsEightLMoves()
        {
            // Arrange
            BoardState board = BoardSetup.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, BoardSetup.AlgebraicToVector("d4"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(8));
            HashSet<Vector2Int> dests = BoardSetup.GetDestinations(_outputBuffer);

            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("c6")), Is.True);
            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("e6")), Is.True);
            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("f5")), Is.True);
            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("f3")), Is.True);
            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("e2")), Is.True);
            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("c2")), Is.True);
            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("b3")), Is.True);
            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("b5")), Is.True);
        }

        [Test]
        public void GetLegalMoves_KnightInCorner_ReturnsTwoLMoves()
        {
            // Arrange
            BoardState board = BoardSetup.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.Knight)
                .WithPiece("h8", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, BoardSetup.AlgebraicToVector("a1"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(2));
            HashSet<Vector2Int> dests = BoardSetup.GetDestinations(_outputBuffer);

            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("b3")), Is.True);
            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("c2")), Is.True);
        }

        [Test]
        public void GetLegalMoves_KnightJumpsOverFriendlyPieces_IsNotBlocked()
        {
            // Arrange
            BoardState board = BoardSetup.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Surround the knight with friendly pawns
            string[] surroundingSquares = { "d5", "d3", "c4", "e4", "c5", "e5", "c3", "e3" };
            foreach (var sq in surroundingSquares)
            {
                board.WithPiece(sq, Team.White, ChessPieceType.Pawn);
            }

            // Act
            ChessEngine.GetLegalMoves(board, BoardSetup.AlgebraicToVector("d4"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(8), "Knight should jump over the surrounding pawns.");
        }

        [Test]
        public void GetLegalMoves_KnightBlockedByFriendlyPiecesOnLSquares_MoveCountReduced()
        {
            // Arrange
            BoardState board = BoardSetup.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                .WithPiece("c6", Team.White, ChessPieceType.Pawn) // Blocks one L
                .WithPiece("e6", Team.White, ChessPieceType.Pawn) // Blocks another L
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, BoardSetup.AlgebraicToVector("d4"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(6));
            HashSet<Vector2Int> dests = BoardSetup.GetDestinations(_outputBuffer);

            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("c6")), Is.False);
            Assert.That(dests.Contains(BoardSetup.AlgebraicToVector("e6")), Is.False);
        }
    }
}