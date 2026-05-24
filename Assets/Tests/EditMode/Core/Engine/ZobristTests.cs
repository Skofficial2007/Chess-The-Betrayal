using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine
{
    [TestFixture]
    public class ZobristTests
    {
        private List<MoveCommand> _moveBuffer;

        [SetUp]
        public void Setup()
        {
            _moveBuffer = new List<MoveCommand>();
        }

        [Test]
        public void ZobristHash_InitialPosition_ComputedHashIsNonZero()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateStandard();

            // Act
            board.ComputeFullZobristHash();

            // Assert
            Assert.That(board.ZobristHash, Is.Not.EqualTo(0UL), 
                "Zobrist hash of a non-empty board must not be zero to avoid transposition table false hits.");
        }

        [Test]
        public void ZobristHash_TwoIdenticalPositions_ProduceSameHash()
        {
            // Arrange: Build the same 4-piece position on two separate BoardState instances
            BoardState boardA = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Queen)
                .WithPiece("f5", Team.Black, ChessPieceType.Knight);

            BoardState boardB = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Queen)
                .WithPiece("f5", Team.Black, ChessPieceType.Knight);

            // Act
            boardA.ComputeFullZobristHash();
            boardB.ComputeFullZobristHash();

            // Assert
            Assert.That(boardA.ZobristHash, Is.EqualTo(boardB.ZobristHash),
                "Two independently constructed boards with identical positions must produce identical hashes.");
        }

        [Test]
        public void ZobristHash_AfterApplyMoveAndUndo_HashRestoredToOriginal()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.ComputeFullZobristHash();
            ulong hashBefore = board.ZobristHash;

            // Act: GetLegalMoves internally calls ApplyMoveToBoard and UndoMoveOnBoard
            // for each candidate move to test if it leaves the king in check.
            // This exercises the make/unmake cycle without exposing private methods.
            Vector2Int e2 = TestBoardSetupUtility.AlgebraicToVector("e2");
            ChessEngine.GetLegalMoves(board, e2, _moveBuffer);

            // Assert: After GetLegalMoves completes, the board should be unchanged
            ulong hashAfter = board.ZobristHash;
            Assert.That(hashAfter, Is.EqualTo(hashBefore),
                "The internal make/unmake cycle must be transparent to the Zobrist hash.");
        }

        [Test]
        public void ZobristHash_DifferentTurns_ProduceDifferentHashes()
        {
            // Arrange: Same piece configuration, different turns
            BoardState boardA = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White);

            BoardState boardB = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.Black);

            // Act
            boardA.ComputeFullZobristHash();
            boardB.ComputeFullZobristHash();

            // Assert
            Assert.That(boardA.ZobristHash, Is.Not.EqualTo(boardB.ZobristHash),
                "Same position with different turns must have different hashes to avoid transposition table collisions.");
        }

        [Test]
        public void ZobristHash_ToggleTurnHash_XorIsInvolutory()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.ComputeFullZobristHash();
            ulong hashBefore = board.ZobristHash;

            // Act: Toggle turn twice
            board.ToggleTurnHash();
            board.ToggleTurnHash();

            // Assert
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore),
                "XOR is its own inverse. Calling ToggleTurnHash() twice must restore the original hash.");
        }

        [Test]
        public void ZobristHash_TogglePieceHash_XorIsInvolutory()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.ComputeFullZobristHash();
            ulong hashBefore = board.ZobristHash;

            // Act: Toggle the same piece twice at e2 (4, 1)
            board.TogglePieceHash(Team.White, ChessPieceType.Pawn, 4, 1);
            board.TogglePieceHash(Team.White, ChessPieceType.Pawn, 4, 1);

            // Assert
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore),
                "Toggling the same piece at the same square twice must restore the original hash.");
        }

        [Test]
        public void ZobristHash_AfterApplyAndUndoCastling_HashRestoredExactly()
        {
            // Arrange: Set up castling scenario
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(BoardState.CastlingWhiteKingside)
                .WithTurn(Team.White);

            board.ComputeFullZobristHash();
            ulong hashBefore = board.ZobristHash;

            // Act: GetLegalMoves on the king will generate castling moves and test them
            // via the internal make/unmake cycle
            Vector2Int e1 = TestBoardSetupUtility.AlgebraicToVector("e1");
            ChessEngine.GetLegalMoves(board, e1, _moveBuffer);

            // Assert
            ulong hashAfter = board.ZobristHash;
            Assert.That(hashAfter, Is.EqualTo(hashBefore),
                "Castling move must be correctly undone, restoring the exact hash.");
        }

        [Test]
        public void ZobristHash_AfterApplyAndUndoEnPassant_HashRestoredExactly()
        {
            // Arrange: Set up en passant scenario
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e5", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("f5", Team.Black, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithEnPassantFile(5) // f-file
                .WithTurn(Team.White);

            board.ComputeFullZobristHash();
            ulong hashBefore = board.ZobristHash;

            // Act: GetLegalMoves will test en passant via make/unmake
            Vector2Int e5 = TestBoardSetupUtility.AlgebraicToVector("e5");
            ChessEngine.GetLegalMoves(board, e5, _moveBuffer);
            ulong hashDuring = board.ZobristHash;

            // Assert
            Assert.That(hashDuring, Is.EqualTo(hashBefore),
                "En passant move must be correctly undone, restoring the exact hash.");

            // Verify en passant move was actually generated
            bool hasEnPassant = false;
            foreach (var move in _moveBuffer)
            {
                if (move.IsEnPassant)
                {
                    hasEnPassant = true;
                    break;
                }
            }
            Assert.That(hasEnPassant, Is.True, "En passant move should have been generated.");
        }

        [Test]
        public void ZobristHash_CastlingRightsChange_AltersHash()
        {
            // Arrange: Two boards with identical pieces but different castling rights
            BoardState boardA = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(BoardState.CastlingWhiteKingside);

            BoardState boardB = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithCastlingRights(0); // No castling rights

            // Act
            boardA.ComputeFullZobristHash();
            boardB.ComputeFullZobristHash();

            // Assert
            Assert.That(boardA.ZobristHash, Is.Not.EqualTo(boardB.ZobristHash),
                "Identical piece positions with different castling rights must have different hashes.");
        }

        [Test]
        public void ZobristHash_EnPassantFileChange_AltersHash()
        {
            // Arrange: Two boards with identical pieces but different en passant states
            BoardState boardA = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e5", Team.White, ChessPieceType.Pawn)
                .WithEnPassantFile(4); // e-file

            BoardState boardB = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e5", Team.White, ChessPieceType.Pawn);
            // No en passant file set

            // Act
            boardA.ComputeFullZobristHash();
            boardB.ComputeFullZobristHash();

            // Assert
            Assert.That(boardA.ZobristHash, Is.Not.EqualTo(boardB.ZobristHash),
                "Identical piece positions with different en passant availability must have different hashes.");
        }

        [Test]
        public void ZobristHash_AfterApplyAndUndoPromotion_HashRestoredExactly()
        {
            // Arrange: Set up promotion scenario
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White);

            board.ComputeFullZobristHash();
            ulong hashBefore = board.ZobristHash;

            // Act: GetLegalMoves will test promotion via make/unmake
            Vector2Int e7 = TestBoardSetupUtility.AlgebraicToVector("e7");
            ChessEngine.GetLegalMoves(board, e7, _moveBuffer);

            // Assert
            ulong hashAfter = board.ZobristHash;
            Assert.That(hashAfter, Is.EqualTo(hashBefore),
                "Promotion move must be correctly undone, restoring the exact hash.");

            // Verify promotion moves were generated
            int promotionCount = 0;
            foreach (var move in _moveBuffer)
            {
                if (move.IsPromotion)
                {
                    promotionCount++;
                }
            }
            Assert.That(promotionCount, Is.GreaterThan(0), "Promotion moves should have been generated.");
        }

        [Test]
        public void BoardState_AssertZobristConsistency_DoesNotThrow_AfterTenMovePairs()
        {
            // Arrange: Standard position with a sequence of varied legal moves
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.ComputeFullZobristHash();

            // Define 10 algebraic moves covering varied piece types
            string[] moveSequence = new string[]
            {
                "e2-e4",   // Pawn push (2 squares)
                "e7-e5",   // Black pawn push
                "g1-f3",   // Knight jump
                "b8-c6",   // Black knight
                "f1-c4",   // Bishop slide
                "f8-c5",   // Black bishop
                "d2-d3",   // Pawn push (1 square)
                "d7-d6",   // Black pawn
                "e1-g1",   // Kingside castling (if legal)
                "g8-f6"    // Black knight
            };

            // Act & Assert: Apply each move and verify hash consistency
            for (int i = 0; i < moveSequence.Length; i++)
            {
                string moveNotation = moveSequence[i];
                string[] parts = moveNotation.Split('-');
                Vector2Int from = TestBoardSetupUtility.AlgebraicToVector(parts[0]);
                Vector2Int to = TestBoardSetupUtility.AlgebraicToVector(parts[1]);

                PieceData piece = board.GetPiece(from);
                Assert.That(piece.IsEmpty, Is.False, $"Move {i + 1} ({moveNotation}): No piece at {parts[0]}");

                // Get legal moves for this piece
                ChessEngine.GetLegalMoves(board, from, _moveBuffer);

                // Find the target move
                MoveCommand? selectedMove = null;
                foreach (var move in _moveBuffer)
                {
                    if (move.EndPosition == to)
                    {
                        selectedMove = move;
                        break;
                    }
                }

                Assert.That(selectedMove.HasValue, Is.True, 
                    $"Move {i + 1} ({moveNotation}) is not legal in current position.");

                // Apply the move
                ChessEngine.ApplyMoveToBoard(board, selectedMove.Value);
                board.NextTurn();

                // Verify hash consistency
                Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                    $"Hash desync detected after move {i + 1} ({moveNotation})");
            }
        }

        [Test]
        public void AssertZobristConsistency_AfterSuccessfulCall_DoesNotMutateZobristHash()
        {
            // Arrange: Standard position with computed hash
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.ComputeFullZobristHash();
            ulong hashBeforeCall = board.ZobristHash;

            Assert.That(hashBeforeCall, Is.Not.EqualTo(0UL), "Hash should be non-zero before test");

            // Act: Call AssertZobristConsistency
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "AssertZobristConsistency should not throw on a consistent board");

            // Assert: Hash must not have changed
            Assert.That(board.ZobristHash, Is.EqualTo(hashBeforeCall),
                "BUG REGRESSION: AssertZobristConsistency mutated the Zobrist hash. " +
                "The incremental hash was overwritten instead of being preserved.");
        }

        [Test]
        public void ApplyMoveToBoard_PromotionCapture_ZobristHashUpdatedCorrectly()
        {
            // Arrange: Position where pawn captures and promotes
            // This is the most complex hash operation: three XOR operations in sequence
            // 1. Remove captured piece's hash
            // 2. Remove pawn hash from promotion square
            // 3. Add promoted piece (Queen) hash to promotion square
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook, hasMoved: false)
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King);

            board.ComputeFullZobristHash();

            // Get the capture-promotion move
            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e7");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("f8");
            PieceData pawn = board.GetPiece(from);
            PieceData rook = board.GetPiece(to);
            MoveCommand move = MoveCommand.CreatePromotionMove(from, to, pawn, ChessPieceType.Queen, rook, board);

            // Act: Apply the capture-promotion move
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            board.CurrentTurn = Team.Black; // Keep CurrentTurn in sync with hash

            // Assert: Hash consistency check should pass
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "Capture-promotion requires three sequential hash operations: " +
                "remove captured piece, remove pawn, add promoted piece. " +
                "Hash consistency failure indicates a bug in the promotion XOR sequence.");
        }
    }
}
