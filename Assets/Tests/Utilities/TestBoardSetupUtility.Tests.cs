using System;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// Unit tests for TestBoardSetupUtility.
    /// All tests in this class are EditMode, require no scene loading,
    /// and have no dependency on UnityEngine.Time or MonoBehaviour lifecycle.
    /// </summary>
    [TestFixture]
    public class TestBoardSetupUtilityTests
    {
        [TestCase("a1", 0, 0)]
        [TestCase("h8", 7, 7)]
        [TestCase("e4", 4, 3)]
        [TestCase("A1", 0, 0)] // Case insensitivity check
        public void AlgebraicToVector_ValidCoordinates_ReturnsCorrectVector2Int(string algebraic, int expectedX, int expectedY)
        {
            // Arrange
            Vector2Int expected = new Vector2Int(expectedX, expectedY);

            // Act
            Vector2Int actual = TestBoardSetupUtility.AlgebraicToVector(algebraic);

            // Assert
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("a")]
        [TestCase("a12")]
        public void AlgebraicToVector_InvalidLength_ThrowsArgumentException(string algebraic)
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentException>(() => TestBoardSetupUtility.AlgebraicToVector(algebraic));
        }

        [TestCase("i1")] // File out of bounds
        [TestCase("a9")] // Rank out of bounds
        [TestCase("a0")] // Rank out of bounds
        [TestCase("1a")] // Reversed format
        public void AlgebraicToVector_OutOfBoundsCharacters_ThrowsArgumentException(string algebraic)
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentException>(() => TestBoardSetupUtility.AlgebraicToVector(algebraic));
        }

        [Test]
        public void CreateEmpty_WhenCalled_ReturnsEmptyBoardWithZeroCastlingAndWhiteTurn()
        {
            // Arrange & Act
            BoardState board = TestBoardSetupUtility.CreateEmpty();

            // Assert
            Assert.That(board.CastlingRights, Is.EqualTo(0), "Empty board should have no castling rights.");
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.White));
            Assert.That(board.EnPassantFile, Is.Null);

            // Verify all 64 squares are actually empty
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    Assert.That(board.GetPiece(x, y).IsEmpty, Is.True, $"Square ({x},{y}) was not empty.");
                }
            }
        }

        [Test]
        public void CreateStandard_WhenCalled_Returns32PiecesWithCorrectPositions()
        {
            // Arrange & Act
            BoardState board = TestBoardSetupUtility.CreateStandard();

            // Assert
            Assert.That(board.CastlingRights, Is.EqualTo(BoardState.CastlingAllRights));
            Assert.That(board.ZobristHash, Is.Not.EqualTo(0UL), "Zobrist hash was not computed for the standard board.");

            // Verify specific anchor pieces
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e1")).Type, Is.EqualTo(ChessPieceType.King));
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e1")).Team, Is.EqualTo(Team.White));

            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e8")).Type, Is.EqualTo(ChessPieceType.King));
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e8")).Team, Is.EqualTo(Team.Black));

            // Verify total piece counts via the O(N) index lists
            int whiteCount = board.GetPieceIndices(Team.White).Count;
            int blackCount = board.GetPieceIndices(Team.Black).Count;

            Assert.That(whiteCount, Is.EqualTo(16), "White should have exactly 16 pieces.");
            Assert.That(blackCount, Is.EqualTo(16), "Black should have exactly 16 pieces.");
        }

        [Test]
        public void WithPiece_FluentChaining_PlacesMultiplePiecesCorrectly()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty();

            // Act
            board.WithPiece("e4", Team.White, ChessPieceType.Pawn, hasMoved: false)
                 .WithPiece("d5", Team.Black, ChessPieceType.Pawn, hasMoved: true)
                 .WithPiece("a1", Team.White, ChessPieceType.Rook);

            // Assert
            PieceData e4Piece = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e4"));
            Assert.That(e4Piece.Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(e4Piece.Team, Is.EqualTo(Team.White));
            Assert.That(e4Piece.HasMoved, Is.False);

            PieceData d5Piece = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("d5"));
            Assert.That(d5Piece.Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(d5Piece.Team, Is.EqualTo(Team.Black));
            Assert.That(d5Piece.HasMoved, Is.True);

            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a1")).Type, Is.EqualTo(ChessPieceType.Rook));
        }
    }
}