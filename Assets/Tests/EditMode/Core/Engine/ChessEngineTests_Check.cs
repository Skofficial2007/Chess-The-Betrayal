using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine
{
    /// <summary>
    /// Tests for ChessEngine check detection and game state evaluation.
    /// Also includes critical bug detection tests.
    /// </summary>
    [TestFixture]
    public class ChessEngineTests_Check
    {
        private List<MoveCommand> _masterBuffer;

        [SetUp]
        public void Setup()
        {
            _masterBuffer = new List<MoveCommand>();
        }

        [Test]
        public void EvaluateGameState_KingInCheckWithLegalEscapes_ReturnsCheck()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook) // Checks along e-file
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White);

            // Act
            GameState state = ChessEngine.EvaluateGameState(board, Team.White);

            // Assert
            Assert.That(state, Is.EqualTo(GameState.Check), "King is in check but can escape to d1 or f1.");
        }

        [Test]
        public void GetAllLegalMoves_WhenCurrentTurnDoesNotMatchTeamParam_ReturnsMovesCorrectly()
        {
            // Arrange: Standard position with White's turn
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.CurrentTurn = Team.White;
            _masterBuffer.Clear();

            // Act: Request Black's moves even though it's White's turn
            ChessEngine.GetAllLegalMoves(board, Team.Black, _masterBuffer);

            // Assert: Black should have legal moves regardless of CurrentTurn
            Assert.That(_masterBuffer.Count, Is.GreaterThan(0),
                "GetAllLegalMoves correctly generates Black's moves even when CurrentTurn=White. " +
                "This is essential for AI evaluation where both teams' moves must be analyzed regardless of whose turn it is.");

            // Verify moves are actually for Black pieces
            foreach (var move in _masterBuffer)
            {
                Assert.That(move.PieceTeam, Is.EqualTo(Team.Black),
                    "All generated moves should be for Black pieces");
            }
        }

        [Test]
        public void EvaluateGameState_KingInCheckWithNoLegalMoves_ReturnsCheckmate()
        {
            // Arrange: Recreate the Fool's Mate without manual movement noise.
            BoardState board = TestBoardSetupUtility.CreateStandard();

            // Remove the f2 and g2 pawns to open the fatal diagonal
            board.SetPiece(PieceData.Empty, 5, 1); // f2
            board.SetPiece(PieceData.Empty, 6, 1); // g2

            // Move Black Queen to h4 delivering the mate
            board.SetPiece(PieceData.Empty, 3, 7); // Remove from d8
            board.WithPiece("h4", Team.Black, ChessPieceType.Queen, hasMoved: true);

            board.WithTurn(Team.White);

            // Act
            GameState state = ChessEngine.EvaluateGameState(board, Team.White);

            // Assert
            Assert.That(state, Is.EqualTo(GameState.Checkmate), "Fool's mate position must return Checkmate.");
        }

        [Test]
        public void EvaluateGameState_KingNotInCheckWithNoLegalMoves_ReturnsStalemate()
        {
            // Arrange: Classic stalemate position
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("b3", Team.Black, ChessPieceType.Queen) // Controls a2, b1, b2. Does NOT control a1.
                .WithPiece("c2", Team.Black, ChessPieceType.King)  // Protects the Queen
                .WithTurn(Team.White);

            // Act
            GameState state = ChessEngine.EvaluateGameState(board, Team.White);

            // Assert
            Assert.That(state, Is.EqualTo(GameState.Stalemate), "King is not in check, but has no legal moves.");
        }

        [Test]
        public void EvaluateGameState_KingNotInCheckWithLegalMoves_ReturnsNormal()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.WithTurn(Team.White);

            // Act
            GameState state = ChessEngine.EvaluateGameState(board, Team.White);

            // Assert
            Assert.That(state, Is.EqualTo(GameState.Normal), "Starting position must return Normal.");
        }

        [Test]
        public void IsKingInCheck_KingAttackedByRook_ReturnsTrue()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook);

            // Act
            bool inCheck = ChessEngine.IsKingInCheck(board, Team.White);

            // Assert
            Assert.That(inCheck, Is.True);
        }

        [Test]
        public void IsKingInCheck_KingNotUnderAttack_ReturnsFalse()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("d8", Team.Black, ChessPieceType.Rook) // Not on e-file
                .WithPiece("h8", Team.Black, ChessPieceType.King);

            // Act
            bool inCheck = ChessEngine.IsKingInCheck(board, Team.White);

            // Assert
            Assert.That(inCheck, Is.False);
        }

        [Test]
        public void IsKingInCheck_KingProtectedByInterveningFriendlyPiece_ReturnsFalse()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook) // Blocks the e-file
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)
                .WithPiece("h8", Team.Black, ChessPieceType.King);

            // Act
            bool inCheck = ChessEngine.IsKingInCheck(board, Team.White);

            // Assert
            Assert.That(inCheck, Is.False, "Friendly piece blocks the check.");
        }

        [Test]
        public void IsKingInCheck_DoubleCheck_ReturnsTrue()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)   // Attack 1: Vertical
                .WithPiece("h7", Team.Black, ChessPieceType.Bishop) // Attack 2: Diagonal
                .WithPiece("a8", Team.Black, ChessPieceType.King);

            // Act
            bool inCheck = ChessEngine.IsKingInCheck(board, Team.White);

            // Assert
            Assert.That(inCheck, Is.True, "King attacked by two pieces is still in check.");
        }

        [Test]
        public void EvaluateGameState_DoubleCheck_OnlyKingMoveIsLegal_ReturnsCheck()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.King)
                .WithPiece("a4", Team.White, ChessPieceType.Rook)   // Could interpose a single check, but not double
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)   // Check 1
                .WithPiece("h7", Team.Black, ChessPieceType.Bishop) // Check 2
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White);

            // Act
            ChessEngine.GetAllLegalMoves(board, Team.White, _masterBuffer);
            GameState state = ChessEngine.EvaluateGameState(board, Team.White);

            // Assert
            Assert.That(state, Is.EqualTo(GameState.Check));
            Assert.That(_masterBuffer.Count, Is.GreaterThan(0), "King must have an escape move.");
            foreach (var move in _masterBuffer)
            {
                Assert.That(move.StartPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("e4")),
                    "During double check, ONLY King moves are legal. Rook on a4 should not have generated moves.");
            }
        }

        [Test]
        public void EvaluateGameState_SmearedCheckmate_ReturnsCheckmate()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("a3", Team.Black, ChessPieceType.Rook) // Checks a1, a2
                .WithPiece("b3", Team.Black, ChessPieceType.Rook) // Controls b1, b2
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White);

            // Act
            GameState state = ChessEngine.EvaluateGameState(board, Team.White);

            // Assert
            Assert.That(state, Is.EqualTo(GameState.Checkmate), "King is in check and all adjacent squares are controlled by Rooks.");
        }

        [Test]
        public void EvaluateGameState_PromotionDeliversCheck_ReturnsCheck()
        {
            // Arrange: White pawn promotes to Queen next to Black King, delivering check
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithTurn(Team.White);

            // Get the promotion move
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("d7"), _masterBuffer);
            MoveCommand? promotionMove = null;
            foreach (var move in _masterBuffer)
            {
                if (move.IsPromotion && move.PromotedTo == ChessPieceType.Queen)
                {
                    promotionMove = move;
                    break;
                }
            }

            Assert.That(promotionMove.HasValue, Is.True, "Promotion move should be available");

            // Act: Apply the promotion
            ChessEngine.ApplyMoveToBoard(board, promotionMove.Value, recordHistory: false);
            board.CurrentTurn = Team.Black;

            // Assert: Black is now in check
            GameState state = ChessEngine.EvaluateGameState(board, Team.Black);
            Assert.That(state, Is.EqualTo(GameState.Check),
                "Promotion to Queen next to enemy King should deliver check");
        }

        [Test]
        public void EvaluateGameState_PromotionCaptureDeliversCheckmate_ReturnsCheckmate()
        {
            // Arrange: Back-rank mate via capture-promotion
            // White pawn at e7 captures Black Rook at f8, promotes to Queen
            // Black King at h8 is smothered by own pawns at g7 and h7
            // After exf8=Q: Queen checks along 8th rank, King has no escape
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook) // Will be captured
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn, hasMoved: true) // Smothers king
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn, hasMoved: true) // Smothers king
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithTurn(Team.White);

            // Get the capture-promotion move
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e7"), _masterBuffer);
            MoveCommand? capturePromotionMove = null;
            foreach (var move in _masterBuffer)
            {
                if (move.IsPromotion && move.HasCapture && move.PromotedTo == ChessPieceType.Queen)
                {
                    capturePromotionMove = move;
                    break;
                }
            }

            Assert.That(capturePromotionMove.HasValue, Is.True, "Capture-promotion move should be available");

            // Act: Apply the capture-promotion
            ChessEngine.ApplyMoveToBoard(board, capturePromotionMove.Value, recordHistory: false);
            board.CurrentTurn = Team.Black;

            // Assert: Black is in checkmate
            GameState state = ChessEngine.EvaluateGameState(board, Team.Black);
            Assert.That(state, Is.EqualTo(GameState.Checkmate),
                "Capture-promotion delivering mate validates full chain: capture → promotion → hash update → checkmate detection");
        }

        [Test]
        public void GetLegalMoves_CalledOnPieceNotBelongingToCurrentTurn_ReturnsEmpty()
        {
            // Arrange: It's White's turn, but we ask for moves of a Black piece
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.CurrentTurn = Team.White;
            _masterBuffer.Clear();

            // Act: Request legal moves for a Black pawn (at e7)
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e7"), _masterBuffer);

            // Assert: Should return empty list
            Assert.That(_masterBuffer.Count, Is.EqualTo(0),
                "Legal moves should not be generated for pieces belonging to the opposing team");
        }

        [Test]
        public void TryFindKing_BoardWithNoKing_ReturnsFalseAndInvalidPosition()
        {
            // Arrange: Empty board with no kings
            BoardState board = TestBoardSetupUtility.CreateEmpty();

            // Act: Try to find White's king
            bool found = board.TryFindKing(Team.White, out Vector2Int kingPos);

            // Assert: Should return false with invalid position
            Assert.That(found, Is.False, "TryFindKing should return false when king is not present");
            Assert.That(kingPos.x, Is.LessThan(0).Or.GreaterThanOrEqualTo(8),
                "King position should be invalid when not found");
        }

        [Test]
        public void GetMaterialAdvantage_AfterQueenCapture_ReturnsCorrectDelta()
        {
            // Arrange: Standard position minus White's Queen (d1)
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.SetPiece(PieceData.Empty, 3, 0); // Remove White Queen at d1

            // Act: Calculate material advantage
            int materialAdvantage = ChessEngine.GetMaterialAdvantage(board);

            // Assert: Black is ahead by 9 points (Queen value)
            Assert.That(materialAdvantage, Is.EqualTo(-9),
                "Material advantage should be -9 when White is missing a Queen. " +
                "Positive values mean White is ahead, negative means Black is ahead.");
        }
    }
}