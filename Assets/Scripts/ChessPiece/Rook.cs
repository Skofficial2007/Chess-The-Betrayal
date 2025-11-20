using System.Collections.Generic;
using UnityEngine;

namespace ChessTheMasterPiece.ChessPiece
{
    public class Rook : ChessPiece
    {
        public override List<Vector2Int> GetAvailableMoves(ref ChessPiece[,] board, int tileCountX, int tileCountY)
        {
            List<Vector2Int> moves = new List<Vector2Int>();

            int x = currentX;
            int y = currentY;

            // Local helper to test board bounds
            bool IsInside(int tx, int ty)
            {
                return tx >= 0 && tx < tileCountX && ty >= 0 && ty < tileCountY;
            }

            // --- Up (increasing Y) ---
            for (int ty = y + 1; ty < tileCountY; ty++)
            {
                if (!IsInside(x, ty))
                {
                    break;
                }

                ChessPiece occupant = board[x, ty];

                if (occupant == null)
                {
                    moves.Add(new Vector2Int(x, ty));
                    continue;
                }

                // Occupied: capture if opponent, then stop scanning this direction
                if (occupant.team != team)
                {
                    moves.Add(new Vector2Int(x, ty));
                }

                break;
            }

            // --- Down (decreasing Y) ---
            for (int ty = y - 1; ty >= 0; ty--)
            {
                if (!IsInside(x, ty))
                {
                    break;
                }

                ChessPiece occupant = board[x, ty];

                if (occupant == null)
                {
                    moves.Add(new Vector2Int(x, ty));
                    continue;
                }

                if (occupant.team != team)
                {
                    moves.Add(new Vector2Int(x, ty));
                }

                break;
            }

            // --- Right (increasing X) ---
            for (int tx = x + 1; tx < tileCountX; tx++)
            {
                if (!IsInside(tx, y))
                {
                    break;
                }

                ChessPiece occupant = board[tx, y];

                if (occupant == null)
                {
                    moves.Add(new Vector2Int(tx, y));
                    continue;
                }

                if (occupant.team != team)
                {
                    moves.Add(new Vector2Int(tx, y));
                }

                break;
            }

            // --- Left (decreasing X) ---
            for (int tx = x - 1; tx >= 0; tx--)
            {
                if (!IsInside(tx, y))
                {
                    break;
                }

                ChessPiece occupant = board[tx, y];

                if (occupant == null)
                {
                    moves.Add(new Vector2Int(tx, y));
                    continue;
                }

                if (occupant.team != team)
                {
                    moves.Add(new Vector2Int(tx, y));
                }

                break;
            }

            return moves;
        }
    }
}
