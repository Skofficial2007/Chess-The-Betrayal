using System.Collections.Generic;
using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.Logic.Movement
{
    /// <summary>
    /// Movement strategy for the Knight piece.
    /// Knights move in an "L" shape: 2 squares in one direction, 1 square perpendicular.
    /// Knights can jump over other pieces.
    /// </summary>
    public class KnightMovement : IPieceMovement
    {
        // All 8 possible L-shaped moves relative to current position
        private static readonly int[,] KnightOffsets = new int[,]
        {
            { +1, +2 }, { +2, +1 }, { +2, -1 }, { +1, -2 },
            { -1, -2 }, { -2, -1 }, { -2, +1 }, { -1, +2 }
        };

        public void GetRawMoves(BoardState board, PieceData piece, List<MoveCommand> buffer)
        {
            Vector2Int startPos = new Vector2Int(piece.CurrentX, piece.CurrentY);

            for (int i = 0; i < KnightOffsets.GetLength(0); i++)
            {
                Vector2Int target = new Vector2Int(
                    startPos.x + KnightOffsets[i, 0],
                    startPos.y + KnightOffsets[i, 1]
                );

                // Skip if target is out of bounds
                if (!board.IsValidIndex(target)) continue;

                PieceData targetPiece = board.GetPiece(target);

                // Can move to empty squares or capture enemy pieces
                if (targetPiece == null || targetPiece.Team != piece.Team)
                {
                    buffer.Add(MoveCommand.CreateStandardMove(startPos, target, piece, targetPiece));
                }
            }
        }
    }
}