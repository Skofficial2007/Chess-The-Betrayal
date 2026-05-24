using NUnit.Framework;
using System.Collections.Generic;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;
using ChessTheMasterPiece.Tests.Utilities;

namespace ChessTheMasterPiece.Tests.EditMode.Engine
{
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
    }
}