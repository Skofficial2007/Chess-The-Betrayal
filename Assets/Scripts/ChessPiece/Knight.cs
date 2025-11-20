using System.Collections.Generic;
using UnityEngine;

namespace ChessTheMasterPiece.ChessPiece
{
    public class Knight : ChessPiece
    {
        public override List<Vector2Int> GetAvailableMoves(ref ChessPiece[,] board, int tileCountX, int tileCountY)
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

            // All 8 knight L-shape offsets
            int[,] offsets =
            {
                { +1, +2 }, { +2, +1 },
                { +2, -1 }, { +1, -2 },
                { -1, -2 }, { -2, -1 },
                { -2, +1 }, { -1, +2 }
            };

            // Evaluate each potential move
            for (int i = 0; i < offsets.GetLength(0); i++)
            {
                int tx = x + offsets[i, 0];
                int ty = y + offsets[i, 1];

                if (!IsInside(tx, ty))
                {
                    continue;
                }

                ChessPiece occupant = board[tx, ty];

                // Empty square -> allowed
                if (occupant == null)
                {
                    moves.Add(new Vector2Int(tx, ty));
                    continue;
                }

                // Occupied by opponent -> capture allowed
                if (occupant.team != team)
                {
                    moves.Add(new Vector2Int(tx, ty));
                }
            }

            return moves;
        }
    }
}
