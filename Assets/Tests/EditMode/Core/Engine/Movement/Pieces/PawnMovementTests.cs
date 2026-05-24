using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Movement
{
    /// <summary>
    /// Unit tests for PawnMovement strategy and its integration with ChessEngine.
    /// All tests in this class are EditMode, require no scene loading,
    /// and have no dependency on UnityEngine.Time or MonoBehaviour lifecycle.
    /// </summary>
    [TestFixture]
    public class PawnMovementTests
    {
        private List<MoveCommand> _outputBuffer;

        [SetUp]
        public void Setup()
        {
            // Fresh buffer for every test to guarantee isolation
            _outputBuffer = new List<MoveCommand>();
        }

        [Test]
        public void GetLegalMoves_WhitePawnOnStartingRank_ReturnsTwoForwardMoves()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e2", Team.White, ChessPieceType.Pawn, hasMoved: false)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e2"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(2));
            HashSet<Vector2Int> destinations = TestBoardSetupUtility.GetDestinations(_outputBuffer);
            Assert.That(destinations, Contains.Item(TestBoardSetupUtility.AlgebraicToVector("e3")));
            Assert.That(destinations, Contains.Item(TestBoardSetupUtility.AlgebraicToVector("e4")));
        }

        [Test]
        public void GetLegalMoves_WhitePawnHasMoved_ReturnsOnlyOneForwardMove()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e3", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e3"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(1));
            Assert.That(_outputBuffer[0].EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("e4")));
        }

        [Test]
        public void GetLegalMoves_WhitePawnBlockedByFriendlyPiece_ReturnsEmptyList()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e2", Team.White, ChessPieceType.Pawn, hasMoved: false)
                .WithPiece("e3", Team.White, ChessPieceType.Rook)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e2"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(0), "Pawn should be blocked by friendly piece.");
        }

        [Test]
        public void GetLegalMoves_WhitePawnDoubleSquareBlockedByPieceOnSingleSquare_ReturnsEmptyList()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e2", Team.White, ChessPieceType.Pawn, hasMoved: false)
                .WithPiece("e3", Team.Black, ChessPieceType.Rook)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e2"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(0), "Enemy piece on single-push square must block the double-push.");
        }

        [Test]
        public void GetLegalMoves_WhitePawnWithEnemyOnDiagonals_ReturnsCaptureMovesAndForwardMoves()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("d5", Team.Black, ChessPieceType.Rook)
                .WithPiece("f5", Team.Black, ChessPieceType.Knight)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e4"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(3));
            HashSet<Vector2Int> destinations = TestBoardSetupUtility.GetDestinations(_outputBuffer);
            Assert.That(destinations, Contains.Item(TestBoardSetupUtility.AlgebraicToVector("e5"))); // Forward
            Assert.That(destinations, Contains.Item(TestBoardSetupUtility.AlgebraicToVector("d5"))); // Capture left
            Assert.That(destinations, Contains.Item(TestBoardSetupUtility.AlgebraicToVector("f5"))); // Capture right
        }

        [Test]
        public void GetLegalMoves_WhitePawnWithFriendlyPieceOnDiagonal_DoesNotGenerateFriendlyCapture()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("d5", Team.White, ChessPieceType.Rook)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e4"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(1));
            Assert.That(_outputBuffer[0].EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("e5")));
        }

        [Test]
        public void GetLegalMoves_WhitePawnOnSeventhRank_ReturnsAllFourPromotionVariants()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e7"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(4), "Pawn pushing to 8th rank must generate 4 distinct promotion moves.");

            HashSet<ChessPieceType> promotions = new HashSet<ChessPieceType>();
            foreach (var move in _outputBuffer)
            {
                Assert.That(move.EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("e8")));
                Assert.That(move.IsPromotion, Is.True);
                promotions.Add(move.PromotedTo);
            }

            Assert.That(promotions, Contains.Item(ChessPieceType.Queen));
            Assert.That(promotions, Contains.Item(ChessPieceType.Rook));
            Assert.That(promotions, Contains.Item(ChessPieceType.Knight));
            Assert.That(promotions, Contains.Item(ChessPieceType.Bishop));
        }

        [Test]
        public void GetLegalMoves_WhitePawnOnSeventhRankWithCapture_ReturnsEightPromotionVariants()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e7"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(8)); // 4 forward promotions + 4 capture promotions

            int capturePromotions = 0;
            int forwardPromotions = 0;

            foreach (var move in _outputBuffer)
            {
                Assert.That(move.IsPromotion, Is.True);
                if (move.EndPosition == TestBoardSetupUtility.AlgebraicToVector("f8"))
                {
                    Assert.That(move.HasCapture, Is.True);
                    capturePromotions++;
                }
                else if (move.EndPosition == TestBoardSetupUtility.AlgebraicToVector("e8"))
                {
                    Assert.That(move.HasCapture, Is.False);
                    forwardPromotions++;
                }
            }

            Assert.That(capturePromotions, Is.EqualTo(4));
            Assert.That(forwardPromotions, Is.EqualTo(4));
        }

        [Test]
        public void GetLegalMoves_PawnEnPassantAvailable_ReturnsEnPassantMove()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e5", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("f5", Team.Black, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithEnPassantFile(5); // File 'f' (index 5)

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e5"), _outputBuffer);

            // Assert
            MoveCommand? enPassantMove = null;
            foreach (var move in _outputBuffer)
            {
                if (move.IsEnPassant)
                {
                    enPassantMove = move;
                    break;
                }
            }

            Assert.That(enPassantMove.HasValue, Is.True, "En Passant move was not generated.");
            Assert.That(enPassantMove.Value.EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("f6")));
            Assert.That(enPassantMove.Value.EnPassantCapturePosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("f5")));
        }

        [Test]
        public void GetLegalMoves_PawnEnPassantFileClearedAfterOneMove_NoEnPassantGenerated()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e5", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("f5", Team.Black, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a1", Team.White, ChessPieceType.King);
            // Intentionally omitting .WithEnPassantFile()

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e5"), _outputBuffer);

            // Assert
            foreach (var move in _outputBuffer)
            {
                Assert.That(move.IsEnPassant, Is.False, "En Passant should not be generated if board.EnPassantFile is null.");
            }
            Assert.That(_outputBuffer.Count, Is.EqualTo(1)); // Only forward push to e6
        }

        [Test]
        public void GetLegalMoves_PawnPinnedToKingHorizontally_ReturnsEmptyList()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a4", Team.Black, ChessPieceType.Rook);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("d4"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(0), "Pinned pawn moving forward exposes king on rank 4.");
        }

        [Test]
        public void GetLegalMoves_PawnPinnedDiagonallyCanStillCaptureAlongPinLine()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("d2", Team.White, ChessPieceType.Pawn, hasMoved: false)
                .WithPiece("c3", Team.Black, ChessPieceType.Bishop)
                .WithPiece("h8", Team.Black, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("d2"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(1));
            Assert.That(_outputBuffer[0].EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("c3")), "Pawn can capture the pinning bishop.");
            Assert.That(_outputBuffer[0].HasCapture, Is.True);
        }

        [Test]
        public void GetLegalMoves_BlackPawnOnStartingRank_ReturnsCorrectDownwardMoves()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e7", Team.Black, ChessPieceType.Pawn, hasMoved: false)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.Black);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e7"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(2));
            HashSet<Vector2Int> destinations = TestBoardSetupUtility.GetDestinations(_outputBuffer);
            Assert.That(destinations, Contains.Item(TestBoardSetupUtility.AlgebraicToVector("e6")));
            Assert.That(destinations, Contains.Item(TestBoardSetupUtility.AlgebraicToVector("e5")));
        }

        [Test]
        public void WithPiece_PawnAtMidBoardWithHasMovedFalse_DoesNotGenerateDoublePush()
        {
            // Arrange: Place a White pawn at e4 with hasMoved=false (incorrect state that could arise from a bug in piece placement or undo logic)
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Pawn, hasMoved: false)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            Vector2Int pawnPos = TestBoardSetupUtility.AlgebraicToVector("e4");
            Vector2Int illegalDoublePush = TestBoardSetupUtility.AlgebraicToVector("e6");

            // Act: Get legal moves for the pawn
            ChessEngine.GetLegalMoves(board, pawnPos, _outputBuffer);

            // Assert: Should NOT contain e6 (double-push from e4)
            HashSet<Vector2Int> destinations = TestBoardSetupUtility.GetDestinations(_outputBuffer);
            Assert.That(destinations.Contains(illegalDoublePush), Is.False,
                "BUG REGRESSION: Pawn at e4 generated illegal double-push to e6. " +
                "This indicates StartRow was set to current Y position (3) instead of actual starting rank (1).");

            // Verify correct move is present
            Vector2Int correctSinglePush = TestBoardSetupUtility.AlgebraicToVector("e5");
            Assert.That(destinations.Contains(correctSinglePush), Is.True,
                "Pawn should still be able to make the single-push move to e5");
        }

        [Test]
        public void GetLegalMoves_EnPassantDiscoveredCheck_PawnCannotCaptureEnPassant()
        {
            // Arrange: White King on h5, White Pawn on e5, Black Pawn on d5, Black Rook on a5
            // If the white pawn captures en passant on d6, it removes TWO pawns from rank 5,
            // exposing the king to the rook's attack. This must be filtered out as illegal.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("h5", Team.White, ChessPieceType.King)
                .WithPiece("e5", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("d5", Team.Black, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a5", Team.Black, ChessPieceType.Rook)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithEnPassantFile(3); // d-file

            // Act: Get legal moves for the white pawn at e5
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e5"), _outputBuffer);

            // Assert: En passant capture should NOT be in the legal moves
            foreach (var move in _outputBuffer)
            {
                Assert.That(move.IsEnPassant, Is.False,
                    "En passant capture must be filtered out because it exposes the king to check. " +
                    "This requires correctly handling the case where TWO pawns are removed from the same rank.");
            }

            // Verify the pawn can still push forward
            HashSet<Vector2Int> destinations = TestBoardSetupUtility.GetDestinations(_outputBuffer);
            Assert.That(destinations.Contains(TestBoardSetupUtility.AlgebraicToVector("e6")), Is.True,
                "Pawn should still be able to push forward to e6");
        }
    }
}