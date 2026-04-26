using System.Collections.Generic;
using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.Logic.Movement
{
    /// <summary>
    /// Contract that all piece movement strategies must implement.
    /// Each piece type (Pawn, Knight, etc.) will have its own implementation.
    /// </summary>
    public interface IPieceMovement
    {
        /// <summary>
        /// Populates the provided buffer with all physically possible moves for this piece type.
        /// Zero-allocation.
        /// Does NOT validate if moves leave the King in check - that's the engine's job.
        /// Returns fully-formed MoveCommands with all metadata (captures, special moves, etc).
        /// Pieces requiring move history (Pawn for En Passant, King for Castling) can access board.MoveHistory directly.
        /// </summary>
        /// <param name="board">The current board state (includes MoveHistory for special moves)</param>
        /// <param name="piece">The piece to generate moves for</param>
        /// <param name="buffer">Pre-allocated list to populate with raw moves</param>
        void GetRawMoves(BoardState board, PieceData piece, List<MoveCommand> buffer);
    }
}