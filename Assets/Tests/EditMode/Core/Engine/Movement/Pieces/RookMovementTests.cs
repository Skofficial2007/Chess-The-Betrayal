using NUnit.Framework;
using System.Collections.Generic;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;
using ChessTheMasterPiece.Tests.Utilities;

namespace ChessTheMasterPiece.Tests.EditMode.Movement
{
    [TestFixture]
    public class RookMovementTests
    {
        private List<MoveCommand> _outputBuffer;

        [SetUp]
        public void Setup() { _outputBuffer = new List<MoveCommand>(); }

        [Test]
        public void GetLegalMoves_RookOnEmptyBoard_ReturnsAllFourteenRayMoves()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.Rook)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("d4"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(14));
            foreach (var move in _outputBuffer)
            {
                // Must be on the same file (x==3) or rank (y==3) as d4
                Assert.That(move.EndPosition.x == 3 || move.EndPosition.y == 3, Is.True);
            }
        }

        [Test]
        public void GetLegalMoves_RookBlockedByFriendlyPiece_DoesNotJumpOrCaptureFriendly()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("a4", Team.White, ChessPieceType.Pawn) // Blocks upward ray
                .WithPiece("h1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("a1"), _outputBuffer);

            // Assert
            HashSet<Vector2Int> dests = TestBoardSetupUtility.GetDestinations(_outputBuffer);

            // Fixed: Using HashSet.Contains() directly to avoid NUnit generic ambiguity
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("a2")), Is.True);
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("a3")), Is.True);
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("a4")), Is.False, "Should not target friendly piece.");
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("a5")), Is.False, "Should not pass through friendly piece.");
        }

        [Test]
        public void GetLegalMoves_RookCanCaptureBlockingEnemyButNotPassThrough()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("a5", Team.Black, ChessPieceType.Pawn) // Enemy piece
                .WithPiece("h1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("a1"), _outputBuffer);

            // Assert
            HashSet<Vector2Int> dests = TestBoardSetupUtility.GetDestinations(_outputBuffer);

            // Fixed: Using HashSet.Contains() directly
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("a5")), Is.True);
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("a6")), Is.False, "Should not pass through enemy piece.");

            int captures = 0;
            foreach (var move in _outputBuffer)
            {
                if (move.EndPosition == TestBoardSetupUtility.AlgebraicToVector("a5"))
                {
                    Assert.That(move.HasCapture, Is.True);
                    captures++;
                }
            }
            Assert.That(captures, Is.EqualTo(1));
        }

        [Test]
        public void GetLegalMoves_RookOnEdgeOfBoard_DoesNotGenerateOutOfBoundsMoves()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("h8", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("a1"), _outputBuffer);

            // Assert
            foreach (var move in _outputBuffer)
            {
                Assert.That(move.EndPosition.x, Is.GreaterThanOrEqualTo(0));
                Assert.That(move.EndPosition.y, Is.GreaterThanOrEqualTo(0));
            }
        }
    }
}