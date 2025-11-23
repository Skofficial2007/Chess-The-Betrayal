using System.Collections.Generic;
using UnityEngine;

namespace ChessTheMasterPiece.ChessPiece
{
    public class Pawn : ChessPiece
    {
        public override List<Vector2Int> GetAvailableMoves(ChessPiece[,] board, int tileCountX, int tileCountY)
        {
            List<Vector2Int> moves = new List<Vector2Int>();

            // Movement direction: white (team == 0) moves +1 in Y, black (team == 1) moves -1.
            // UNLESS we are playing as Black, in which case Black moves +1.
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
                    // We use initialY to reliably check if it's the first move, regardless of team orientation
                    int startRow = (initialY >= 0) ? initialY : ((direction == 1) ? 1 : (tileCountY - 2));
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

        public override SpecialMove GetSpecialMoves(ChessPiece[,] board, List<Vector2Int[]> moveHistory, List<Vector2Int> availableMoves)
        {
            // FIX: Determine promotion rank based on Move Direction, not strictly team color.
            // If moving UP (+1), promotion is at the top (Height - 1).
            // If moving DOWN (-1), promotion is at the bottom (0).
            int promotionRank = (moveDirection == 1) ? board.GetLength(1) - 1 : 0;

            foreach (Vector2Int move in availableMoves)
            {
                if (move.y == promotionRank)
                {
                    return SpecialMove.Promotion;
                }
            }

            // Check for En Passant
            if (moveHistory.Count == 0)
            {
                return SpecialMove.None;
            }

            // Retrieve details of the opponent's last move
            Vector2Int[] lastMove = moveHistory[moveHistory.Count - 1];
            Vector2Int lastMoveStart = lastMove[0];
            Vector2Int lastMoveEnd = lastMove[1];

            ChessPiece enemyPiece = board[lastMoveEnd.x, lastMoveEnd.y];

            // Rule 1: The piece that moved last must be an enemy Pawn
            if (enemyPiece == null || enemyPiece.type != ChessPieceType.Pawn || enemyPiece.team == team)
            {
                return SpecialMove.None;
            }

            // Rule 2: The enemy pawn must be on the same Rank (Y) as us
            if (enemyPiece.currentY != currentY)
            {
                return SpecialMove.None;
            }

            // Rule 3: The enemy pawn must be directly adjacent (Left or Right)
            bool isAdjacent = Mathf.Abs(enemyPiece.currentX - currentX) == 1;
            if (!isAdjacent)
            {
                return SpecialMove.None;
            }

            // Rule 4: The enemy pawn must have moved exactly 2 squares vertically (the double-step)
            int moveDistance = Mathf.Abs(lastMoveEnd.y - lastMoveStart.y);
            if (moveDistance != 2)
            {
                return SpecialMove.None;
            }

            // --- Logic Valid: En Passant Available ---

            // The target move is the empty square *behind* the enemy pawn
            availableMoves.Add(new Vector2Int(enemyPiece.currentX, currentY + moveDirection));

            return SpecialMove.EnPassant;
        }
    }
}