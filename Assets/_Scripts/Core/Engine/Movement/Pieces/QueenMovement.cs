using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Core.Movement
{
    /// <summary>
    /// Movement strategy for the Queen piece.
    /// Queens move like a Rook + Bishop combined: straight or diagonal, any distance.
    /// Cannot jump over pieces - stops when encountering any piece.
    /// Most powerful piece in standard chess.
    /// </summary>
    public class QueenMovement : IPieceMovement
    {
        // All 8 directions combined (4 straight + 4 diagonal)
        private static readonly int[,] Directions = new int[,]
        {
            // Straight directions (Rook-like)
            { 0, 1 },   // Up
            { 0, -1 },  // Down
            { 1, 0 },   // Right
            { -1, 0 },  // Left
            // Diagonal directions (Bishop-like)
            { 1, 1 },   // Up-Right
            { -1, 1 },  // Up-Left
            { 1, -1 },  // Down-Right
            { -1, -1 }  // Down-Left
        };

        public void GetRawMoves(BoardState board, PieceData piece, Vector2Int pos, List<MoveCommand> buffer)
        {
            // Slide in each of the 8 directions
            for (int d = 0; d < Directions.GetLength(0); d++)
            {
                int stepX = Directions[d, 0];
                int stepY = Directions[d, 1];

                // Keep sliding until we hit a piece or edge of board
                for (int step = 1; ; step++)
                {
                    Vector2Int target = new Vector2Int(pos.x + stepX * step, pos.y + stepY * step);

                    // Stop if we're off the board
                    if (!board.IsValidIndex(target)) break;

                    PieceData targetPiece = board.GetPiece(target);

                    if (targetPiece.IsEmpty)
                    {
                        // Empty square - can move here and continue sliding
                        buffer.Add(MoveCommand.CreateStandardMove(pos, target, piece, default, board));
                    }
                    else
                    {
                        // Hit a piece - check if we can capture it
                        if (targetPiece.Team != piece.Team)
                        {
                            buffer.Add(MoveCommand.CreateStandardMove(pos, target, piece, targetPiece, board));
                        }
                        // Either way, we're blocked - stop sliding in this direction
                        break;
                    }
                }
            }
        }

        public void GetAttackedSquares(BoardState board, PieceData piece, Vector2Int pos, List<Vector2Int> buffer)
        {
            // Queen = Rook + Bishop: along each of the 8 rays, every square up to AND INCLUDING the
            // first occupied square is attacked; nothing past the blocker. See
            // IPieceMovement.GetAttackedSquares for the full attack-map contract.
            for (int d = 0; d < Directions.GetLength(0); d++)
            {
                int stepX = Directions[d, 0];
                int stepY = Directions[d, 1];

                for (int step = 1; ; step++)
                {
                    Vector2Int target = new Vector2Int(pos.x + stepX * step, pos.y + stepY * step);

                    if (!board.IsValidIndex(target)) break;

                    buffer.Add(target);

                    if (!board.GetPiece(target).IsEmpty) break;
                }
            }
        }
    }
}