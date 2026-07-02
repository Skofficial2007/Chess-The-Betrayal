using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    /// <summary>
    /// Validates edge cases surrounding Castling during Betrayal sequences.
    /// Ensures that castling mechanics cannot be exploited to initiate an Act
    /// or execute a Betrayer during the Retribution phase.
    /// </summary>
    [TestFixture]
    public class BetrayalCastlingEdgeCaseTests
    {
        private List<MoveCommand> _moveBuffer;

        [SetUp]
        public void Setup()
        {
            _moveBuffer = new List<MoveCommand>();
        }

        [Test]
        public void RetributionPhase_WhiteKingsideCastling_CannotBeUsedToExecuteBetrayer()
        {
            // Arrange: White King on e1, White Rook on h1. 
            // A Betrayer sits on g1 (the exact destination square for White Kingside castling).
            // A friendly Queen sits on g4, able to legally execute the Betrayer.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("g4", Team.White, ChessPieceType.Queen)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("g1", Team.White, ChessPieceType.Knight) // The Betrayer
                .WithPendingBetrayer("g1", Team.White)
                .WithTurn(Team.White);

            board.CastlingRights = BoardState.CastlingWhiteKingside;

            // Act
            ChessEngine.GetRetributionMoves(board, Team.White, TestBoardSetupUtility.AlgebraicToVector("g1"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.GreaterThan(0), "There should be valid execution moves (e.g., from the Queen).");

            foreach (var move in _moveBuffer)
            {
                Assert.That(move.IsCastling, Is.False, "Castling is a non-capture maneuver and must NEVER be generated as a Retribution execution.");
            }
        }

        [Test]
        public void RetributionPhase_BlackQueensideCastling_CannotBeUsedToExecuteBetrayer()
        {
            // Arrange: Black King on e8, Black Rook on a8. 
            // A Betrayer sits on c8 (the exact destination square for Black Queenside castling).
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("c4", Team.Black, ChessPieceType.Rook) // An executioner
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("c8", Team.Black, ChessPieceType.Knight) // The Betrayer
                .WithPendingBetrayer("c8", Team.Black)
                .WithTurn(Team.Black);

            board.CastlingRights = BoardState.CastlingBlackQueenside;

            // Act
            ChessEngine.GetRetributionMoves(board, Team.Black, TestBoardSetupUtility.AlgebraicToVector("c8"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.GreaterThan(0), "There should be valid execution moves (e.g., from the Rook).");

            foreach (var move in _moveBuffer)
            {
                Assert.That(move.IsCastling, Is.False, "Black queenside castling must not be generated as a Retribution execution.");
            }
        }

        [Test]
        public void ActPhase_KingCastlingPathOccupied_CannotInitiateBetrayal()
        {
            // Arrange: White King on e1, White Rook on h1. 
            // A friendly Knight sits on g1 (in the castling path).
            // Proves that castling mechanics cannot be exploited to "betray" the Knight.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("g1", Team.White, ChessPieceType.Knight) // Friendly piece in castling path
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            board.CastlingRights = BoardState.CastlingWhiteKingside;

            // Act
            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _moveBuffer);

            // Assert
            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "The King is fundamentally excluded from acting as a Betrayer, therefore castling as an Act is impossible.");
        }
    }
}