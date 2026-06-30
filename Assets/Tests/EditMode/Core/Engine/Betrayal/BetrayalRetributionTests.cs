using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    [TestFixture]
    public class BetrayalRetributionTests
    {
        private List<MoveCommand> _moveBuffer;

        [SetUp]
        public void Setup()
        {
            _moveBuffer = new List<MoveCommand>();
        }

        [Test]
        public void GetRetributionMoves_KingCanActAsExecutioner_IncludedInResults()
        {
            // Arrange: King and Rook both able to reach Betrayer
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("h2", Team.White, ChessPieceType.Rook)
                .WithPiece("e2", Team.White, ChessPieceType.Pawn) // The Betrayer
                .WithPendingBetrayer("e2", Team.White);

            // Act
            ChessEngine.GetRetributionMoves(board, Team.White, TestBoardSetupUtility.AlgebraicToVector("e2"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.EqualTo(2), "King and Rook should both be valid executioners.");
        }

        [Test]
        public void GetRetributionMoves_OnlyCapableExecutionerIsPinned_ReturnsEmpty()
        {
            // Arrange: THE CANONICAL PIN TEST. Executioner is pinned to King.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook) // The only piece that can reach d4, but it is pinned to e1
                .WithPiece("e8", Team.Black, ChessPieceType.Rook) // Pinning piece
                .WithPiece("d4", Team.White, ChessPieceType.Knight) // The Betrayer
                .WithPendingBetrayer("d4", Team.White)
                .WithTurn(Team.White);

            // Act
            ChessEngine.GetRetributionMoves(board, Team.White, TestBoardSetupUtility.AlgebraicToVector("d4"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "Pinned Executioner naturally returns empty, triggering Defection natively.");
        }

        [Test]
        public void GetRetributionMoves_MultipleCapableExecutioners_AllNonPinnedOnesIncluded()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook) // Pinned (invalid)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook) // Pinning piece
                .WithPiece("a4", Team.White, ChessPieceType.Rook) // Unpinned (valid)
                .WithPiece("d4", Team.White, ChessPieceType.Knight) // The Betrayer
                .WithPendingBetrayer("d4", Team.White)
                .WithTurn(Team.White);

            // Act
            ChessEngine.GetRetributionMoves(board, Team.White, TestBoardSetupUtility.AlgebraicToVector("d4"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.EqualTo(1));
            Assert.That(_moveBuffer[0].StartPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a4")), "Only the unpinned Executioner should be returned.");
        }

        [Test]
        public void GetRetributionMoves_TargetMustBeBetrayerSquareExactly_OtherCapturesExcluded()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Queen) // Executioner
                .WithPiece("d8", Team.Black, ChessPieceType.Rook) // Standard enemy target
                .WithPiece("h4", Team.White, ChessPieceType.Knight) // The Betrayer
                .WithPendingBetrayer("h4", Team.White);

            // Act
            ChessEngine.GetRetributionMoves(board, Team.White, TestBoardSetupUtility.AlgebraicToVector("h4"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.EqualTo(1));
            Assert.That(_moveBuffer[0].EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("h4")), "Executioner must only target the Betrayer square.");
        }
    }
}