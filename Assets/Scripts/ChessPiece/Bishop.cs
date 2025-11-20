using System.Collections.Generic;
using UnityEngine;

namespace ChessTheMasterPiece.ChessPiece
{
    public class Bishop : ChessPiece
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

            // --- Up-Right (increasing X, increasing Y) ---
            for (int step = 1; ; step++)
            {
                int tx = x + step;
                int ty = y + step;

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

            // --- Up-Left (decreasing X, increasing Y) ---
            for (int step = 1; ; step++)
            {
                int tx = x - step;
                int ty = y + step;

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

            // --- Down-Right (increasing X, decreasing Y) ---
            for (int step = 1; ; step++)
            {
                int tx = x + step;
                int ty = y - step;

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

            // --- Down-Left (decreasing X, decreasing Y) ---
            for (int step = 1; ; step++)
            {
                int tx = x - step;
                int ty = y - step;

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

            return moves;
        }
    }
}
