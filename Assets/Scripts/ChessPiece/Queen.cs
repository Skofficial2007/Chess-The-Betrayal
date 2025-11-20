using System.Collections.Generic;
using UnityEngine;

namespace ChessTheMasterPiece.ChessPiece
{
    public class Queen : ChessPiece
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

            // 8 directional vectors (rook + bishop combined)
            int[,] directions =
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

            // Scan all 8 directions
            for (int d = 0; d < directions.GetLength(0); d++)
            {
                int stepX = directions[d, 0];
                int stepY = directions[d, 1];

                for (int step = 1; ; step++)
                {
                    int tx = x + stepX * step;
                    int ty = y + stepY * step;

                    if (!IsInside(tx, ty))
                    {
                        break;
                    }

                    ChessPiece occupant = board[tx, ty];

                    if (occupant == null)
                    {
                        moves.Add(new Vector2Int(tx, ty));
                        continue;
                    }

                    if (occupant.team != team)
                    {
                        moves.Add(new Vector2Int(tx, ty));
                    }

                    break;
                }
            }

            return moves;
        }
    }
}
