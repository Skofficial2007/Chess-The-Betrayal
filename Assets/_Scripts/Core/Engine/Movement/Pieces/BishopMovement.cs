using System.Collections.Generic;
using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.Logic.Movement
{
    /// <summary>
    /// Movement strategy for the Bishop piece.
    /// Bishops move diagonally, any distance.
    /// Cannot jump over pieces - stops when encountering any piece.
    /// </summary>
    public class BishopMovement : IPieceMovement
    {
        // Diagonal directions: Up-Right, Up-Left, Down-Right, Down-Left
        private static readonly int[,] Directions = new int[,]
        {
            { 1, 1 },    // Up-Right
            { -1, 1 },   // Up-Left
            { 1, -1 },   // Down-Right
            { -1, -1 }   // Down-Left
        };

        public void GetRawMoves(BoardState board, PieceData piece, Vector2Int pos, List<MoveCommand> buffer)
        {
            // Slide in each of the 4 diagonal directions
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
    }
}