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
        /// Generates all physically possible moves for this piece type.
        /// Does NOT validate if moves leave the King in check - that's the engine's job.
        /// Returns fully-formed MoveCommands with all metadata (captures, special moves, etc).
        /// </summary>
        /// <param name="board">The current board state</param>
        /// <param name="piece">The piece to generate moves for</param>
        /// <returns>List of possible move commands</returns>
        List<MoveCommand> GetRawMoves(BoardState board, PieceData piece);

        /// <summary>
        /// Optional: Some pieces (King, Rook, Pawn) may need access to move history
        /// for special moves like castling or en passant. Override if needed.
        /// </summary>
        /// <param name="board">The current board state</param>
        /// <param name="piece">The piece to generate moves for</param>
        /// <param name="moveHistory">The game's move history</param>
        /// <returns>List of possible move commands</returns>
        List<MoveCommand> GetRawMovesWithHistory(BoardState board, PieceData piece, List<Vector2Int> moveHistory)
        {
            // Default implementation ignores history
            return GetRawMoves(board, piece);
        }
    }
}