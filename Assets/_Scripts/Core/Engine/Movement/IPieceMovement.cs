using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Core.Movement
{
    /// <summary>
    /// The contract every piece's movement logic must follow. If you want to add a custom piece type, implement this interface.
    /// </summary>
    public interface IPieceMovement
    {
        /// <summary>
        /// Fills the buffer with every square this piece could physically move to, without worrying about whether it puts the King in check.
        /// That filtering happens in ChessEngine.
        /// For moves that depend on history (like en passant or castling), read from <c>board.EnPassantFile</c> and the piece's <c>HasMoved</c> flag.
        /// Returns fully-formed <see cref="MoveCommand"/> values with metadata (captures, special moves, etc).
        /// </summary>
        /// <param name="board">The current board state (read EnPassantFile and use piece.HasMoved for history-dependent moves)</param>
        /// <param name="piece">The piece to generate moves for</param>
        /// <param name="position">The position of the piece on the board</param>
        /// <param name="buffer">The list to populate with raw MoveCommands</param>
        void GetRawMoves(BoardState board, PieceData piece, Vector2Int position, List<MoveCommand> buffer);
    }
}