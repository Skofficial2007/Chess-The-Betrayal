using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Core.Movement
{
    /// <summary>
    /// Movement strategy for the King piece.
    /// Kings move one square in any direction (8 possible moves).
    /// Special move: Castling (with Rook) under specific conditions.
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

                // Skip if target is out of bounds
                if (!board.IsValidIndex(target)) continue;

                PieceData targetPiece = board.GetPiece(target);

                // Can move to empty squares or capture enemy pieces
                if (targetPiece.IsEmpty || targetPiece.Team != piece.Team)
                {
                    buffer.Add(MoveCommand.CreateStandardMove(pos, target, piece, targetPiece, board));
                }
            }

            // 2. Castling (History and Mask dependent)
            if (!piece.HasMoved)
            {
                if (piece.Team == Team.White)
                {
                    // White Queenside (Bit 1 = Value 2)
                    TryAddCastling(board, buffer, piece, pos, 0, QueensideEmptyPaths, 2, 3, 2);
                    // White Kingside (Bit 0 = Value 1)
                    TryAddCastling(board, buffer, piece, pos, 7, KingsideEmptyPaths, 6, 5, 1);
                }
                else
                {
                    // Black Queenside (Bit 3 = Value 8)
                    TryAddCastling(board, buffer, piece, pos, 0, QueensideEmptyPaths, 2, 3, 8);
                    // Black Kingside (Bit 2 = Value 4)
                    TryAddCastling(board, buffer, piece, pos, 7, KingsideEmptyPaths, 6, 5, 4);
                }
            }
        }

        public void GetAttackedSquares(BoardState board, PieceData piece, Vector2Int pos, List<Vector2Int> buffer)
        {
            // The King attacks its 8 adjacent squares. Castling is a move, never a capture, so it
            // has no place in an attack map — omitting it is what keeps check detection correct
            // (a King never "attacks" the square it would castle to).
            for (int i = 0; i < Offsets.GetLength(0); i++)
            {
                Vector2Int target = new Vector2Int(pos.x + Offsets[i, 0], pos.y + Offsets[i, 1]);

                if (board.IsValidIndex(target))
                {
                    buffer.Add(target);
                }
            }
        }

        private void TryAddCastling(
            BoardState board,
            List<MoveCommand> buffer,
            PieceData king,
            Vector2Int kingPos,
            int rookX,
            int[] emptyXPaths,
            int kingTargetX,
            int rookTargetX,
            int requiredCastlingBit)
        {
            // 1. MUST Validate the Castling Rights Mask BEFORE physical checks
            if ((board.CastlingRights & requiredCastlingBit) == 0)
            {
                return; // Castling right has been revoked in the engine state
            }

            Vector2Int rookPos = new Vector2Int(rookX, kingPos.y);
            PieceData rook = board.GetPiece(rookPos);

            // 2. Validate physical Rook exists, is correct type, same team, and hasn't moved
            if (rook.IsEmpty ||
                rook.Type != ChessPieceType.Rook ||
                rook.Team != king.Team ||
                rook.HasMoved)
            {
                return;
            }

            // 3. Check if the path between King and Rook is clear
            foreach (int x in emptyXPaths)
            {
                Vector2Int checkPos = new Vector2Int(x, kingPos.y);
                if (!board.GetPiece(checkPos).IsEmpty)
                {
                    return; // Path is blocked
                }
            }

            // All prerequisites met - generate the raw castling command
            Vector2Int kingTarget = new Vector2Int(kingTargetX, kingPos.y);
            Vector2Int rookTarget = new Vector2Int(rookTargetX, kingPos.y);

            buffer.Add(MoveCommand.CreateCastlingMove(kingPos, kingTarget, king, rookPos, rookTarget, board));
        }
    }
}