using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    [TestFixture]
    public class BetrayalDefensiveOverrideTests
    {
        private List<MoveCommand> _moveBuffer;

        [SetUp]
        public void Setup()
        {
            _moveBuffer = new List<MoveCommand>();
        }

        [Test]
        public void GetForcedSaveMoves_CapturingDefectedPieceResolvesCheck_IncludedInResults()
        {
            // Arrange: White in check from a newly defected Black piece on e4
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.Black, ChessPieceType.Rook) // Defected piece delivering check
                .WithPiece("h4", Team.White, ChessPieceType.Rook) // Friendly piece that can capture it
                .WithTurn(Team.White);

            // Act
            ChessEngine.GetForcedSaveMoves(board, Team.White, _moveBuffer);

            // Assert
            bool foundCapture = false;
            foreach (var move in _moveBuffer)
            {
                if (move.EndPosition == TestBoardSetupUtility.AlgebraicToVector("e4")) foundCapture = true;
                Assert.That(move.Stage, Is.EqualTo(BetrayalStage.DefensiveOverride), "Moves must be correctly tagged.");
            }
            Assert.That(foundCapture, Is.True, "Capturing the defected piece is a valid save.");
        }

        [Test]
        public void GetForcedSaveMoves_NoLegalSaveExists_ReturnsEmpty()
        {
            // Arrange: True unblockable smothered mate scenario post-defection
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Rook)   // Blocks King escape
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)   // Blocks King escape
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)   // Blocks King escape
                .WithPiece("c2", Team.Black, ChessPieceType.Knight) // Defected piece delivering fatal check
                .WithTurn(Team.White);

            // Act
            ChessEngine.GetForcedSaveMoves(board, Team.White, _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "No legal saves means list is empty, which implies checkmate.");
        }
    }
}