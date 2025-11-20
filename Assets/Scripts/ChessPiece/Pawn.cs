using System.Collections.Generic;
using UnityEngine;

namespace ChessTheMasterPiece.ChessPiece
{
    public class Pawn : ChessPiece
    {
        public override List<Vector2Int> GetAvailableMoves(ref ChessPiece[,] board, int tileCountX, int tileCountY)
        {
            List<Vector2Int> moves = new List<Vector2Int>();

            // Movement direction: white (team == 0) moves +1 in Y, black (team == 1) moves -1.
            int direction = moveDirection;

            int x = currentX;
            int y = currentY;

            // Local helper to test board bounds
            bool IsInside(int tx, int ty)
            {
                return tx >= 0 && tx < tileCountX && ty >= 0 && ty < tileCountY;
            }

            // Forward one
            int oneForwardY = y + direction;

            if (IsInside(x, oneForwardY))
            {
                if (board[x, oneForwardY] == null)
                {
                    moves.Add(new Vector2Int(x, oneForwardY));

                    // Forward two (only when one-forward is empty and pawn is on its starting rank)
                    int startRow = (initialY >= 0) ? initialY : ((team == 0) ? 1 : (tileCountY - 2));
                    int twoForwardY = y + (direction * 2);

                    if (y == startRow && IsInside(x, twoForwardY))
                    {
                        if (board[x, twoForwardY] == null)
                        {
                            moves.Add(new Vector2Int(x, twoForwardY));
                        }
                    }
                }
            }

            // Captures: diagonal left and right
            int diagRightX = x + 1;
            int diagLeftX = x - 1;
            int diagY = y + direction;

            // Diagonal right capture
            if (IsInside(diagRightX, diagY))
            {
                ChessPiece target = board[diagRightX, diagY];

                if (target != null && target.team != team)
                {
                    moves.Add(new Vector2Int(diagRightX, diagY));
                }
            }

            // Diagonal left capture
            if (IsInside(diagLeftX, diagY))
            {
                ChessPiece target = board[diagLeftX, diagY];

                if (target != null && target.team != team)
                {
                    moves.Add(new Vector2Int(diagLeftX, diagY));
                }
            }

            return moves;
        }
    }
}
