using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine
{
    /// <summary>
    /// Direct tests for UndoMoveOnBoard to verify move reversal correctness in isolation.
    /// These tests exercise the undo path without routing through GetLegalMoves,
    /// enabling precise verification of graveyard restoration, piece placement, and state rollback.
    /// </summary>
    [TestFixture]
    public class UndoMoveTests
    {
        #region Standard Move Undo

        [Test]
        public void UndoMoveOnBoard_StandardMove_RestoresOriginalPosition()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e2", Team.White, ChessPieceType.Pawn, hasMoved: false);

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e2");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("e4");
            PieceData pawn = board.GetPiece(from);
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, pawn, default, board);

            // Act: Apply then undo
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert: Piece back at original square
            Assert.That(board.GetPiece(from).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(board.GetPiece(from).HasMoved, Is.False, "HasMoved should be restored to original state");
            Assert.That(board.GetPiece(to).IsEmpty, Is.True, "Destination square should be empty after undo");
        }

        [Test]
        public void UndoMoveOnBoard_StandardMove_RestoresHasMovedFlag()
        {
            // Arrange: Piece that has already moved
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d5", Team.White, ChessPieceType.Knight, hasMoved: true);

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("d5");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("f6");
            PieceData knight = board.GetPiece(from);
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, knight, default, board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert
            PieceData restoredKnight = board.GetPiece(from);
            Assert.That(restoredKnight.HasMoved, Is.True, "Knight's HasMoved flag should be restored to true");
        }

        #endregion

        #region Capture Undo

        [Test]
        public void UndoMoveOnBoard_CaptureMove_RestoresBothPieces()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Queen, hasMoved: false)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn, hasMoved: true);

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e4");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("e5");
            PieceData queen = board.GetPiece(from);
            PieceData capturedPawn = board.GetPiece(to);
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, queen, capturedPawn, board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            Assert.That(board.BlackCaptured.Count, Is.EqualTo(1), "Pawn should be in graveyard after capture");

            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert: Both pieces restored
            Assert.That(board.GetPiece(from).Type, Is.EqualTo(ChessPieceType.Queen));
            Assert.That(board.GetPiece(to).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(board.GetPiece(to).Team, Is.EqualTo(Team.Black));
            Assert.That(board.GetPiece(to).HasMoved, Is.True, "Captured pawn's HasMoved flag should be restored");
            Assert.That(board.BlackCaptured.Count, Is.EqualTo(0), "Graveyard should be empty after undo");
        }

        [Test]
        public void UndoMoveOnBoard_MultipleCaptures_GraveyardOrderMaintained()
        {
            // Arrange: Two sequential captures
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.Queen)
                .WithPiece("d5", Team.Black, ChessPieceType.Knight)
                .WithPiece("d6", Team.Black, ChessPieceType.Rook);

            Vector2Int queenStart = TestBoardSetupUtility.AlgebraicToVector("d4");
            Vector2Int knightPos = TestBoardSetupUtility.AlgebraicToVector("d5");
            Vector2Int rookPos = TestBoardSetupUtility.AlgebraicToVector("d6");

            PieceData queen = board.GetPiece(queenStart);
            PieceData knight = board.GetPiece(knightPos);
            PieceData rook = board.GetPiece(rookPos);

            MoveCommand captureKnight = MoveCommand.CreateStandardMove(queenStart, knightPos, queen, knight, board);
            MoveCommand captureRook = MoveCommand.CreateStandardMove(knightPos, rookPos, queen.WithMoved(), rook, board);

            // Act: Apply both, then undo in reverse order
            ChessEngine.ApplyMoveToBoard(board, captureKnight, recordHistory: false);
            ChessEngine.ApplyMoveToBoard(board, captureRook, recordHistory: false);
            Assert.That(board.BlackCaptured.Count, Is.EqualTo(2));

            ChessEngine.UndoMoveOnBoard(board, captureRook, recordHistory: false);
            ChessEngine.UndoMoveOnBoard(board, captureKnight, recordHistory: false);

            // Assert: All pieces restored
            Assert.That(board.GetPiece(queenStart).Type, Is.EqualTo(ChessPieceType.Queen));
            Assert.That(board.GetPiece(knightPos).Type, Is.EqualTo(ChessPieceType.Knight));
            Assert.That(board.GetPiece(rookPos).Type, Is.EqualTo(ChessPieceType.Rook));
            Assert.That(board.BlackCaptured.Count, Is.EqualTo(0), "Graveyard should be empty after full undo");
        }

        #endregion

        #region Promotion Undo

        [Test]
        public void UndoMoveOnBoard_Promotion_RestoresPawn()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true);

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e7");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("e8");
            PieceData pawn = board.GetPiece(from);
            MoveCommand move = MoveCommand.CreatePromotionMove(from, to, pawn, ChessPieceType.Queen, default, board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            Assert.That(board.GetPiece(to).Type, Is.EqualTo(ChessPieceType.Queen), "Should be queen after promotion");

            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert: Pawn restored
            Assert.That(board.GetPiece(from).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(board.GetPiece(from).HasMoved, Is.True, "Pawn's HasMoved should be restored");
            Assert.That(board.GetPiece(to).IsEmpty, Is.True);
        }

        [Test]
        public void UndoMoveOnBoard_PromotionCapture_RestoresBothPiecesCorrectly()
        {
            // Arrange: DESIGN SMELL-001 canonical example
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook, hasMoved: false);

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e7");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("f8");
            PieceData pawn = board.GetPiece(from);
            PieceData rook = board.GetPiece(to);
            MoveCommand move = MoveCommand.CreatePromotionMove(from, to, pawn, ChessPieceType.Queen, rook, board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert: Pawn at start, Rook at destination
            PieceData restoredPawn = board.GetPiece(from);
            PieceData restoredRook = board.GetPiece(to);

            Assert.That(restoredPawn.Type, Is.EqualTo(ChessPieceType.Pawn), "Pawn should be restored, not Queen");
            Assert.That(restoredPawn.Team, Is.EqualTo(Team.White));
            Assert.That(restoredPawn.HasMoved, Is.True);

            Assert.That(restoredRook.Type, Is.EqualTo(ChessPieceType.Rook), "Captured Rook should be restored");
            Assert.That(restoredRook.Team, Is.EqualTo(Team.Black));
            Assert.That(restoredRook.HasMoved, Is.False, "Rook's HasMoved flag should be restored");

            Assert.That(board.BlackCaptured.Count, Is.EqualTo(0), "Graveyard should be empty");
        }

        #endregion

        #region Castling Undo

        [Test]
        public void UndoMoveOnBoard_Castling_RestoresBothKingAndRook()
        {
            // Arrange: White Kingside castling
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King, hasMoved: false)
                .WithPiece("h1", Team.White, ChessPieceType.Rook, hasMoved: false)
                .WithCastlingRights(BoardState.CastlingWhiteKingside);

            Vector2Int kingStart = TestBoardSetupUtility.AlgebraicToVector("e1");
            Vector2Int kingEnd = TestBoardSetupUtility.AlgebraicToVector("g1");
            Vector2Int rookStart = TestBoardSetupUtility.AlgebraicToVector("h1");
            Vector2Int rookEnd = TestBoardSetupUtility.AlgebraicToVector("f1");

            PieceData king = board.GetPiece(kingStart);
            MoveCommand move = MoveCommand.CreateCastlingMove(kingStart, kingEnd, king, rookStart, rookEnd, board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert: Both pieces back at original squares with HasMoved = false
            PieceData restoredKing = board.GetPiece(kingStart);
            PieceData restoredRook = board.GetPiece(rookStart);

            Assert.That(restoredKing.Type, Is.EqualTo(ChessPieceType.King));
            Assert.That(restoredKing.HasMoved, Is.False, "King should have HasMoved = false after undo");
            Assert.That(board.GetPiece(kingEnd).IsEmpty, Is.True);

            Assert.That(restoredRook.Type, Is.EqualTo(ChessPieceType.Rook));
            Assert.That(restoredRook.HasMoved, Is.False, "Rook should have HasMoved = false after undo");
            Assert.That(board.GetPiece(rookEnd).IsEmpty, Is.True);

            Assert.That(board.CastlingRights, Is.EqualTo(BoardState.CastlingWhiteKingside), 
                "Castling rights should be restored");
        }

        [Test]
        public void UndoMoveOnBoard_CastlingBlackQueenside_RestoresCorrectly()
        {
            // Arrange: Black Queenside castling
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e8", Team.Black, ChessPieceType.King, hasMoved: false)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook, hasMoved: false)
                .WithCastlingRights(BoardState.CastlingBlackQueenside);

            Vector2Int kingStart = TestBoardSetupUtility.AlgebraicToVector("e8");
            Vector2Int kingEnd = TestBoardSetupUtility.AlgebraicToVector("c8");
            Vector2Int rookStart = TestBoardSetupUtility.AlgebraicToVector("a8");
            Vector2Int rookEnd = TestBoardSetupUtility.AlgebraicToVector("d8");

            PieceData king = board.GetPiece(kingStart);
            MoveCommand move = MoveCommand.CreateCastlingMove(kingStart, kingEnd, king, rookStart, rookEnd, board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert
            Assert.That(board.GetPiece(kingStart).Type, Is.EqualTo(ChessPieceType.King));
            Assert.That(board.GetPiece(kingStart).HasMoved, Is.False);
            Assert.That(board.GetPiece(rookStart).Type, Is.EqualTo(ChessPieceType.Rook));
            Assert.That(board.GetPiece(rookStart).HasMoved, Is.False);
            Assert.That(board.CastlingRights, Is.EqualTo(BoardState.CastlingBlackQueenside));
        }

        #endregion

        #region En Passant Undo

        [Test]
        public void UndoMoveOnBoard_EnPassant_RestoresCapturedPawnToCorrectSquare()
        {
            // Arrange: White pawn captures Black pawn en passant
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e5", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("d5", Team.Black, ChessPieceType.Pawn, hasMoved: true)
                .WithEnPassantFile(3); // d-file

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e5");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("d6");
            Vector2Int capturePos = TestBoardSetupUtility.AlgebraicToVector("d5");

            PieceData whitePawn = board.GetPiece(from);
            PieceData blackPawn = board.GetPiece(capturePos);
            MoveCommand move = MoveCommand.CreateEnPassantMove(from, to, whitePawn, blackPawn, capturePos, board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            Assert.That(board.GetPiece(capturePos).IsEmpty, Is.True, "d5 should be empty after en passant");
            Assert.That(board.BlackCaptured.Count, Is.EqualTo(1));

            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert: Black pawn restored to d5, White pawn back to e5
            Assert.That(board.GetPiece(from).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(board.GetPiece(from).Team, Is.EqualTo(Team.White));
            Assert.That(board.GetPiece(to).IsEmpty, Is.True, "d6 should be empty after undo");

            Assert.That(board.GetPiece(capturePos).Type, Is.EqualTo(ChessPieceType.Pawn), 
                "Captured pawn should be restored to d5");
            Assert.That(board.GetPiece(capturePos).Team, Is.EqualTo(Team.Black));
            Assert.That(board.GetPiece(capturePos).HasMoved, Is.True);

            Assert.That(board.BlackCaptured.Count, Is.EqualTo(0));
            Assert.That(board.EnPassantFile, Is.EqualTo(3), "En passant file should be restored");
        }

        #endregion

        #region State Restoration

        [Test]
        public void UndoMoveOnBoard_CastlingRights_RestoredCorrectly()
        {
            // Arrange: King move revokes all castling rights for that team
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King, hasMoved: false)
                .WithCastlingRights(BoardState.CastlingAllRights);

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e1");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("e2");
            PieceData king = board.GetPiece(from);
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, king, default, board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            int rightsAfterMove = board.CastlingRights;
            Assert.That(rightsAfterMove, Is.EqualTo(BoardState.CastlingBlackKingside | BoardState.CastlingBlackQueenside),
                "White castling rights should be revoked");

            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert
            Assert.That(board.CastlingRights, Is.EqualTo(BoardState.CastlingAllRights), 
                "All castling rights should be restored");
        }

        [Test]
        public void UndoMoveOnBoard_EnPassantFile_RestoredCorrectly()
        {
            // Arrange: Non-double-pawn-push clears en passant
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Knight)
                .WithEnPassantFile(4); // e-file was available

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e4");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("f6");
            PieceData knight = board.GetPiece(from);
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, knight, default, board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            Assert.That(board.EnPassantFile, Is.Null, "En passant should be cleared after non-pawn move");

            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert
            Assert.That(board.EnPassantFile, Is.EqualTo(4), "En passant file should be restored");
        }

        [Test]
        public void UndoMoveOnBoard_ZobristHash_RestoredCorrectly()
        {
            // Arrange
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.ComputeFullZobristHash();
            ulong originalHash = board.ZobristHash;

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e2");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("e4");
            PieceData pawn = board.GetPiece(from);
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, pawn, default, board);

            // Act
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            ulong hashAfterMove = board.ZobristHash;
            Assert.That(hashAfterMove, Is.Not.EqualTo(originalHash), "Hash should change after move");

            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert
            Assert.That(board.ZobristHash, Is.EqualTo(originalHash), 
                "Hash should be restored to exact original value");
            
            // Paranoid verification: recompute and compare
            board.AssertZobristConsistency();
        }

        [Test]
        public void UndoMoveOnBoard_CapturePromotion_BoardAndHashFullyRestored()
        {
            // Arrange: Most complex move type - pawn captures and promotes
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook, hasMoved: false)
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King);

            board.ComputeFullZobristHash();
            ulong originalHash = board.ZobristHash;
            int originalGraveyardCount = board.BlackCaptured.Count;

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e7");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("f8");
            PieceData pawn = board.GetPiece(from);
            PieceData rook = board.GetPiece(to);
            MoveCommand move = MoveCommand.CreatePromotionMove(from, to, pawn, ChessPieceType.Queen, rook, board);

            // Act: Apply then immediately undo
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            Assert.That(board.GetPiece(to).Type, Is.EqualTo(ChessPieceType.Queen), "Should be Queen after promotion");
            Assert.That(board.BlackCaptured.Count, Is.EqualTo(originalGraveyardCount + 1), "Rook should be captured");

            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);

            // Assert: Complete restoration
            PieceData restoredPawn = board.GetPiece(from);
            PieceData restoredRook = board.GetPiece(to);

            Assert.That(restoredPawn.Type, Is.EqualTo(ChessPieceType.Pawn), "Pawn should be restored at start position");
            Assert.That(restoredPawn.Team, Is.EqualTo(Team.White));
            Assert.That(restoredPawn.HasMoved, Is.True);

            Assert.That(restoredRook.Type, Is.EqualTo(ChessPieceType.Rook), "Rook should be restored at capture position");
            Assert.That(restoredRook.Team, Is.EqualTo(Team.Black));
            Assert.That(restoredRook.HasMoved, Is.False);

            Assert.That(board.BlackCaptured.Count, Is.EqualTo(originalGraveyardCount), 
                "Graveyard count should be restored");

            Assert.That(board.ZobristHash, Is.EqualTo(originalHash),
                "Zobrist hash should be restored to exact original value after undoing capture-promotion");

            // Verification: hash consistency check
            board.AssertZobristConsistency();
        }

        #endregion
    }
}
