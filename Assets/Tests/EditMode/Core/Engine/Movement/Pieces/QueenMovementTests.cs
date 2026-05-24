using NUnit.Framework;
using System.Collections.Generic;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;
using ChessTheMasterPiece.Tests.Utilities;

namespace ChessTheMasterPiece.Tests.EditMode.Movement
{
    [TestFixture]
    public class QueenMovementTests
    {
        private List<MoveCommand> _outputBuffer;

        [SetUp]
        public void Setup() { _outputBuffer = new List<MoveCommand>(); }

        [Test]
        public void GetLegalMoves_QueenInCentre_ReturnsTwentySevenMoves()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.Queen)
                .WithPiece("a2", Team.White, ChessPieceType.King); // FIX: Moved King off the diagonal

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("d4"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(27), "Queen on d4 should have exactly 27 legal moves (14 straight + 13 diagonal).");
        }
    }
}