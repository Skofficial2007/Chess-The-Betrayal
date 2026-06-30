using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    [TestFixture]
    public class BetrayalActTests
    {
        private List<MoveCommand> _moveBuffer;

        [SetUp]
        public void Setup()
        {
            _moveBuffer = new List<MoveCommand>();
        }

        [Test]
        public void GetBetrayalTargets_KingAsBetrayer_ReturnsEmpty()
        {
            // Arrange: White King adjacent to White Pawn
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e2", Team.White, ChessPieceType.Pawn)
                .WithBetrayalRight(true);

            // Act
            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "The King is immune from acting as a Betrayer.");
        }

        [Test]
        public void GetBetrayalTargets_FriendlyKingAsVictim_NeverIncluded()
        {
            // Arrange: White Rook looking at White King
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Rook)
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithBetrayalRight(true);

            // Act
            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e4"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "The King is immune from being a Victim.");
        }

        [Test]
        public void GetBetrayalTargets_BetrayalRightAlreadyConsumed_ReturnsEmpty()
        {
            // Arrange: Perfect betrayal setup, but right is consumed
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Rook)
                .WithPiece("e5", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.King) // Required for check validation
                .WithBetrayalRight(false);

            // Act
            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e4"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "Cannot initiate Betrayal if the global right is exhausted.");
        }

        [Test]
        public void GetBetrayalTargets_MoveExposesOwnKingToCheck_ExcludedFromResults()
        {
            // Arrange: White Rook pinned to White King by Black Bishop
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook) // Pinned
                .WithPiece("e8", Team.Black, ChessPieceType.Rook) // Pinning piece
                .WithPiece("d4", Team.White, ChessPieceType.Pawn) // Potential victim
                .WithTurn(Team.White)
                .WithBetrayalRight(true); // FIX: Explicitly grant the right so it tests the pin, not the global lock

            // Act
            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e4"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "Pinned piece cannot initiate an Act that breaks the pin.");
        }

        [Test]
        public void GetBetrayalTargets_RespectsPieceGeometry_BishopOnlyTargetsDiagonalFriendlies()
        {
            // Arrange: White Bishop surrounded by friendlies orthogonally and diagonally
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.Bishop)
                .WithPiece("d5", Team.White, ChessPieceType.Pawn) // Orthogonal (invalid)
                .WithPiece("e5", Team.White, ChessPieceType.Pawn) // Diagonal (valid)
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(true); // FIX: Explicitly grant the right so geometry evaluates

            // Act
            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("d4"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.EqualTo(1));
            Assert.That(_moveBuffer[0].EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("e5")), "Bishop must respect diagonal capture geometry for friendly-fire.");
        }
    }
}