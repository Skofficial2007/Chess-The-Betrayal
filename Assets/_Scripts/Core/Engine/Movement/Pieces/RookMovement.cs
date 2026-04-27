using System.Collections.Generic;
using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.Logic.Movement
{
    /// <summary>
    /// Movement strategy for the Rook piece.
    /// Rooks move in straight lines: horizontally or vertically, any distance.
    /// Cannot jump over pieces - stops when encountering any piece.
    /// </summary>
    public class RookMovement : IPieceMovement
    {
        // Straight directions: Up, Down, Right, Left
        private static readonly int[,] Directions = new int[,]
        {
            { 0, 1 },   // Up
            { 0, -1 },  // Down
            { 1, 0 },   // Right
            { -1, 0 }   // Left
        };

        public void GetRawMoves(BoardState board, PieceData piece, List<MoveCommand> buffer)
        {
            Vector2Int pos = new Vector2Int(piece.CurrentX, piece.CurrentY);

            // Slide in each of the 4 straight directions
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

                    if (targetPiece == null)
                    {
                        // Empty square - can move here and continue sliding
                        buffer.Add(MoveCommand.CreateStandardMove(pos, target, piece, null, board));
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
    }
}