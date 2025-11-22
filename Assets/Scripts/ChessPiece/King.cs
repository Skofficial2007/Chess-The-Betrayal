using System.Collections.Generic;
using UnityEngine;

namespace ChessTheMasterPiece.ChessPiece
{
    public class King : ChessPiece
    {
        public override List<Vector2Int> GetAvailableMoves(ChessPiece[,] board, int tileCountX, int tileCountY)
        {
            List<Vector2Int> moves = new List<Vector2Int>();

            int x = currentX;
            int y = currentY;

            // Local helper to test board bounds
            bool IsInside(int tx, int ty)
            {
                return tx >= 0 && tx < tileCountX &&
                       ty >= 0 && ty < tileCountY;
            }

            // All 8 adjacent directions
            int[,] offsets =
            {
                {  0, +1 }, // up
                {  0, -1 }, // down
                { +1,  0 }, // right
                { -1,  0 }, // left

                { +1, +1 }, // up-right
                { -1, +1 }, // up-left
                { +1, -1 }, // down-right
                { -1, -1 }  // down-left
            };

            // Evaluate each possible square
            for (int i = 0; i < offsets.GetLength(0); i++)
            {
                int tx = x + offsets[i, 0];
                int ty = y + offsets[i, 1];

                if (!IsInside(tx, ty))
                {
                    continue;
                }

                ChessPiece occupant = board[tx, ty];

                // Empty square
                if (occupant == null)
                {
                    moves.Add(new Vector2Int(tx, ty));
                    continue;
                }

                // Capture enemy piece
                if (occupant.team != team)
                {
                    moves.Add(new Vector2Int(tx, ty));
                }
            }

            return moves;
        }
    }
}
