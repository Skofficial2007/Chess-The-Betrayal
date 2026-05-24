using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Movement
{
    [TestFixture]
    public class KingMovementTests
    {
        private List<MoveCommand> _outputBuffer;

        [SetUp]
        public void Setup() { _outputBuffer = new List<MoveCommand>(); }

        [Test]
        public void GetLegalMoves_KingInCentre_ReturnsEightAdjacentMoves()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(0);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("d4"), _outputBuffer);

            // Assert
            Assert.That(_outputBuffer.Count, Is.EqualTo(8), "King in the centre of an empty board should have exactly 8 legal moves.");
        }

        [Test]
        public void GetLegalMoves_KingCannotMoveIntoCheck_AttackedSquaresExcluded()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("d8", Team.Black, ChessPieceType.Rook) // Controls the entire D-file
                .WithPiece("h8", Team.Black, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _outputBuffer);

            // Assert
            HashSet<Vector2Int> dests = TestBoardSetupUtility.GetDestinations(_outputBuffer);
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("d1")), Is.False, "King cannot move into check on d1.");
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("d2")), Is.False, "King cannot move into check on d2.");

            // Safe squares
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("e2")), Is.True);
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("f1")), Is.True);
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("f2")), Is.True);
        }

        [Test]
        public void GetLegalMoves_KingCanCaptureAttackerIfSquareIsDefended_ReturnsMoveExcluded()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e2", Team.Black, ChessPieceType.Rook)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook) // Defends the e2 rook
                .WithPiece("h8", Team.Black, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _outputBuffer);

            // Assert
            HashSet<Vector2Int> dests = TestBoardSetupUtility.GetDestinations(_outputBuffer);
            Assert.That(dests.Contains(TestBoardSetupUtility.AlgebraicToVector("e2")), Is.False, "King cannot capture a defended piece (would walk into check).");
        }

        [Test]
        public void GetLegalMoves_KingCanCaptureUndefendedAttacker()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e2", Team.Black, ChessPieceType.Rook) // Undefended
                .WithPiece("h8", Team.Black, ChessPieceType.King);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _outputBuffer);

            // Assert
            bool foundCapture = false;
            foreach (var move in _outputBuffer)
            {
                if (move.EndPosition == TestBoardSetupUtility.AlgebraicToVector("e2"))
                {
                    foundCapture = true;
                    Assert.That(move.HasCapture, Is.True);
                    Assert.That(move.CapturedType, Is.EqualTo(ChessPieceType.Rook));
                }
            }
            Assert.That(foundCapture, Is.True, "King should legally capture an undefended attacker.");
        }

        [Test]
        public void GetLegalMoves_KingsideCastling_ReturnsKingsideCastlingMove()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King, hasMoved: false)
                .WithPiece("h1", Team.White, ChessPieceType.Rook, hasMoved: false)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(1); // Assuming bit 0 (1) is White Kingside.

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _outputBuffer);

            // Assert
            bool foundCastling = false;
            foreach (var move in _outputBuffer)
            {
                if (move.IsCastling && move.EndPosition == TestBoardSetupUtility.AlgebraicToVector("g1"))
                {
                    foundCastling = true;
                    Assert.That(move.RookStartPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("h1")));
                    Assert.That(move.RookEndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("f1")));
                }
            }
            Assert.That(foundCastling, Is.True, "Kingside castling move was not generated.");
        }

        [Test]
        public void GetLegalMoves_QueensideCastling_ReturnsQueensideCastlingMove()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King, hasMoved: false)
                .WithPiece("a1", Team.White, ChessPieceType.Rook, hasMoved: false)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(2); // Assuming bit 1 (2) is White Queenside.

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _outputBuffer);

            // Assert
            bool foundCastling = false;
            foreach (var move in _outputBuffer)
            {
                if (move.IsCastling && move.EndPosition == TestBoardSetupUtility.AlgebraicToVector("c1"))
                {
                    foundCastling = true;
                    Assert.That(move.RookStartPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a1")));
                    Assert.That(move.RookEndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("d1")));
                }
            }
            Assert.That(foundCastling, Is.True, "Queenside castling move was not generated.");
        }

        [Test]
        public void GetLegalMoves_CastlingBlockedByInterveningPiece_NoCastlingMoveGenerated()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King, hasMoved: false)
                .WithPiece("h1", Team.White, ChessPieceType.Rook, hasMoved: false)
                .WithPiece("f1", Team.White, ChessPieceType.Bishop) // Intervening piece
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(1);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _outputBuffer);

            // Assert
            foreach (var move in _outputBuffer)
            {
                Assert.That(move.IsCastling, Is.False, "Castling should be blocked physically.");
            }
        }

        [Test]
        public void GetLegalMoves_CastlingWhileInCheck_NoCastlingMoveGenerated()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King, hasMoved: false)
                .WithPiece("h1", Team.White, ChessPieceType.Rook, hasMoved: false)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook) // Puts king in check
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(1);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _outputBuffer);

            // Assert
            foreach (var move in _outputBuffer)
            {
                Assert.That(move.IsCastling, Is.False, "Cannot castle out of check.");
            }
        }

        [Test]
        public void GetLegalMoves_CastlingThroughAttackedSquare_NoCastlingMoveGenerated()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King, hasMoved: false)
                .WithPiece("h1", Team.White, ChessPieceType.Rook, hasMoved: false)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook) // Attacks f1 (the pass-through square)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(1);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _outputBuffer);

            // Assert
            foreach (var move in _outputBuffer)
            {
                Assert.That(move.IsCastling, Is.False, "Cannot castle through check.");
            }
        }

        [Test]
        public void GetLegalMoves_KingHasMoved_CastlingRightRevokedByHasMovedFlag()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King, hasMoved: true) // KING MOVED
                .WithPiece("h1", Team.White, ChessPieceType.Rook, hasMoved: false)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(1);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _outputBuffer);

            // Assert
            foreach (var move in _outputBuffer)
            {
                Assert.That(move.IsCastling, Is.False);
            }
        }

        [Test]
        public void GetLegalMoves_RookHasMoved_CastlingRightRevokedByHasMovedFlag()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King, hasMoved: false)
                .WithPiece("h1", Team.White, ChessPieceType.Rook, hasMoved: true) // ROOK MOVED
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(1);

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _outputBuffer);

            // Assert
            foreach (var move in _outputBuffer)
            {
                Assert.That(move.IsCastling, Is.False);
            }
        }

        [Test]
        public void GetLegalMoves_CastlingRightRevokedByCastlingMask_NoCastlingEvenWithUnmovedPieces()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King, hasMoved: false)
                .WithPiece("h1", Team.White, ChessPieceType.Rook, hasMoved: false)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(0); // MASK CLEARED

            // Act
            ChessEngine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("e1"), _outputBuffer);

            // Assert
            foreach (var move in _outputBuffer)
            {
                Assert.That(move.IsCastling, Is.False);
            }
        }

        [Test]
        public void GetLegalMoves_EnemyRookCapturedOnCornerSquare_RevokesCorrespondingCastlingRight()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateStandard(); // Sets all 15 castling rights.

            // Sneak a White Queen onto g8 so she can capture the Black Rook on h8.
            board.WithPiece("g8", Team.White, ChessPieceType.Queen, hasMoved: true);

            // Act: Create the capture move and apply it to the board directly.
            MoveCommand captureMove = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("g8"),
                TestBoardSetupUtility.AlgebraicToVector("h8"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("g8")),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("h8")),
                board);

            ChessEngine.ApplyMoveToBoard(board, captureMove); // Triggers ComputeNewCastlingMask internally

            // Assert
            // 4 is the bitmask for Black Kingside (Bit 2).
            Assert.That((board.CastlingRights & 4), Is.EqualTo(0), "Black's kingside castling right was not revoked upon capture of the h8 rook.");
        }
    }
}