using System.Collections.Generic;
using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.Logic.Movement
{
    /// <summary>
    /// Movement strategy for the King piece.
    /// Kings move one square in any direction (8 possible moves).
    /// Special move: Castling (with Rook) under specific conditions.
    /// Most important piece - losing the King ends the game.
    /// </summary>
    public class KingMovement : IPieceMovement
    {
        // 8 adjacent squares (one step in each direction)
        private static readonly int[,] Offsets = new int[,]
        {
            { 0, 1 },   // Up
            { 0, -1 },  // Down
            { 1, 0 },   // Right
            { -1, 0 },  // Left
            { 1, 1 },   // Up-Right
            { -1, 1 },  // Up-Left
            { 1, -1 },  // Down-Right
            { -1, -1 }  // Down-Left
        };

        // The squares that must be empty for castling to work on each side.
        private static readonly int[] QueensideEmptyPaths = { 1, 2, 3 };
        private static readonly int[] KingsideEmptyPaths = { 5, 6 };

        public void GetRawMoves(BoardState board, PieceData piece, Vector2Int pos, List<MoveCommand> buffer)
        {
            // 1. Standard one-step moves in all 8 directions
            for (int i = 0; i < Offsets.GetLength(0); i++)
            {
                Vector2Int target = new Vector2Int(pos.x + Offsets[i, 0], pos.y + Offsets[i, 1]);

                if (!board.IsValidIndex(target)) continue;

                PieceData targetPiece = board.GetPiece(target);

                // Can move to empty squares or capture enemy pieces
                if (targetPiece.IsEmpty || targetPiece.Team != piece.Team)
                {
                    buffer.Add(MoveCommand.CreateStandardMove(pos, target, piece, targetPiece, board));
                }
            }

            // 2. Castling (only if King hasn't moved)
            if (!piece.HasMoved)
            {
                // Queenside Castling (left side)
                // King at e1 → c1, Rook at a1 → d1
                EvaluateCastling(
                    board, buffer, piece, pos,
                    rookX: 0,
                    emptyXPaths: QueensideEmptyPaths,
                    kingTargetX: 2,
                    rookTargetX: 3
                );

                // Kingside Castling (right side)
                // King at e1 → g1, Rook at h1 → f1
                EvaluateCastling(
                    board, buffer, piece, pos,
                    rookX: 7,
                    emptyXPaths: KingsideEmptyPaths,
                    kingTargetX: 6,
                    rookTargetX: 5
                );
            }
        }

        /// <summary>
        /// Checks if the King can castle in a given direction. Verifies the rook is in place, hasn't moved, and the path between them is clear.
        /// The engine handles the 'can't castle through check' rule separately.
        /// </summary>
        /// <param name="board">Current board state</param>
        /// <param name="moves">List to add the castling move to if valid</param>
        /// <param name="king">The King piece data</param>
        /// <param name="kingPos">King's current position</param>
        /// <param name="rookX">X coordinate of the Rook</param>
        /// <param name="emptyXPaths">X coordinates that must be empty</param>
        /// <param name="kingTargetX">Where the King will land</param>
        /// <param name="rookTargetX">Where the Rook will land</param>
        private void EvaluateCastling(
            BoardState board,
            List<MoveCommand> buffer,
            PieceData king,
            Vector2Int kingPos,
            int rookX,
            int[] emptyXPaths,
            int kingTargetX,
            int rookTargetX)
        {
            Vector2Int rookPos = new Vector2Int(rookX, kingPos.y);
            PieceData rook = board.GetPiece(rookPos);

            // Validate Rook exists, is correct type, same team, and hasn't moved
            if (rook.IsEmpty ||
                rook.Type != ChessPieceType.Rook ||
                rook.Team != king.Team ||
                rook.HasMoved)
            {
                return;
            }

            // Check if the path between King and Rook is clear
            foreach (int x in emptyXPaths)
            {
                Vector2Int checkPos = new Vector2Int(x, kingPos.y);
                if (!board.GetPiece(checkPos).IsEmpty)
                {
                    return; // Path is blocked
                }
            }

            // All physical prerequisites met - generate the castling command
            Vector2Int kingTarget = new Vector2Int(kingTargetX, kingPos.y);
            Vector2Int rookTarget = new Vector2Int(rookTargetX, kingPos.y);

            // Note: The ChessEngine will automatically verify that:
            // 1. King is not currently in check
            // 2. King doesn't pass through check
            // 3. King doesn't land in check
            // This is handled by DoesMoveLeaveKingInCheck() on each move candidate
            buffer.Add(MoveCommand.CreateCastlingMove(kingPos, kingTarget, king, rookPos, rookTarget, board));
        }
    }
}