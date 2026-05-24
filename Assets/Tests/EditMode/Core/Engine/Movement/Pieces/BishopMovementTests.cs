using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;
using System;

namespace ChessTheBetrayal.Tests.EditMode.Core.Movement
{
    [TestFixture]
    public class BishopMovementTests
    {
        private List<MoveCommand> _outputBuffer;

        [SetUp]
        public void Setup() { _outputBuffer = new List<MoveCommand>(); }

        [Test]
        public void GetLegalMoves_BishopOnEmptyBoard_ReturnsThirteenDiagonalMoves()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.Bishop)
                .WithPiece("a2", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("d4"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(13));
            Vector2Int d4 = TestBoardSetupUtility.AlgebraicToVector("d4");
            foreach (var move in _outputBuffer)
            {
                // Delta X must equal Delta Y for diagonal movement
                Assert.That(Math.Abs(move.EndPosition.x - d4.x), Is.EqualTo(Math.Abs(move.EndPosition.y - d4.y)));
            }
        }

        [Test]
        public void GetLegalMoves_BishopBlockedByFriendlyPieceOnDiagonal_CannotPassThrough()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("c1", Team.White, ChessPieceType.Bishop)
                .WithPiece("f4", Team.White, ChessPieceType.Pawn) // On the c1-h6 diagonal
                .WithPiece("h1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("c1"), _outputBuffer);

            // Assert
            HashSet<Vector2Int> dests = TestBoardSetupUtility.GetDestinations(_outputBuffer);

            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("d2")), Is.True);
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("e3")), Is.True);
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("f4")), Is.False, "Cannot capture friendly.");
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("g5")), Is.False, "Cannot pass through.");
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("h6")), Is.False);
        }

        [Test]
        public void GetLegalMoves_BishopColorBound_NeverLandsOnOppositeColor()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("c1", Team.White, ChessPieceType.Bishop) // c1 is a dark square (x=2, y=0)
                .WithPiece("a1", Team.White, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("c1"), _outputBuffer);

            // Assert
            Vector2Int c1 = TestBoardSetupUtility.AlgebraicToVector("c1");
            int startColor = (c1.x + c1.y) % 2;
            foreach (var move in _outputBuffer)
            {
                Assert.That((move.EndPosition.x + move.EndPosition.y) % 2, Is.EqualTo(startColor), "Bishop landed on wrong color square.");
            }
        }
    }
}