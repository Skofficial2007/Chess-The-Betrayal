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

        /// <summary>
        /// Fills the buffer with every square this piece <em>attacks</em> — i.e. every square on which
        /// it could capture an enemy piece, judged purely by geometry and line-of-sight against the
        /// current occupancy, and independent of what (if anything) actually sits on the target square.
        ///
        /// Precisely:
        /// <list type="bullet">
        /// <item>Jumpers (Knight, King) attack every in-bounds offset square, regardless of occupancy.
        /// Castling is a move but never a capture, so it is deliberately omitted here.</item>
        /// <item>Sliders (Rook, Bishop, Queen) attack every empty square along each ray up to and
        /// <em>including</em> the first occupied square (of either team); nothing beyond the blocker.</item>
        /// <item>Pawns attack their two diagonal-forward squares, whether or not those squares are
        /// occupied. Forward pushes and en passant are moves, not attacks, so they are omitted.</item>
        /// </list>
        ///
        /// This is the canonical "attack map" primitive shared by check detection
        /// (<c>ChessEngine.IsSquareUnderAttack</c>) and Betrayal Act-target generation
        /// (<c>ChessEngine.GetBetrayalTargets</c>). It runs the movement geometry exactly once per
        /// piece, with no board mutation, so both callers avoid the O(n²) "disguise trick" of flipping
        /// a candidate victim's team and re-deriving raw moves per pair.
        /// </summary>
        /// <param name="board">The current board state.</param>
        /// <param name="piece">The attacking piece.</param>
        /// <param name="position">The position of the attacking piece.</param>
        /// <param name="buffer">The list to populate with attacked squares.</param>
        void GetAttackedSquares(BoardState board, PieceData piece, Vector2Int position, List<Vector2Int> buffer);
    }
}