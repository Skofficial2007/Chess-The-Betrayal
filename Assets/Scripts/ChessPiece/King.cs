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

        public override SpecialMove GetSpecialMoves(ChessPiece[,] board, List<Vector2Int[]> moveList, List<Vector2Int> availableMoves)
        {
            SpecialMove specialMove = SpecialMove.None;

            // King must not have moved
            if (hasMoved)
            {
                return SpecialMove.None;
            }

            // Find the partner Rooks (Queenside at x=0, Kingside at x=7)

            // Left Rook
            if (CheckCastle(board, -1, 0, currentY, new int[] { 1, 2, 3 }))
            {
                // King moves 2 steps left (to X = 2)
                availableMoves.Add(new Vector2Int(2, currentY));
                specialMove = SpecialMove.Castling;
            }

            // Right Rook
            if (CheckCastle(board, 1, 7, currentY, new int[] { 5, 6 }))
            {
                // King moves 2 steps right (to X = 6)
                availableMoves.Add(new Vector2Int(6, currentY));
                specialMove = SpecialMove.Castling;
            }

            return specialMove;
        }

        /// <summary>
        /// Validates if castling is possible towards a specific rook.
        /// </summary>
        /// <param name="direction"> -1 for Left, +1 for Right (not strictly used here but good for context)</param>
        /// <param name="rookX">The X index where the Rook should be (0 or 7)</param>
        /// <param name="rankY">The Y index (Rank) we are checking (0 or 7)</param>
        /// <param name="emptyIndexes">The X indexes between King and Rook that must be empty</param>
        private bool CheckCastle(ChessPiece[,] board, int direction, int rookX, int rankY, int[] emptyIndexes)
        {
            ChessPiece rook = board[rookX, rankY];

            // Rook must exist, be on our team, and be a Rook
            if (rook == null || rook.team != team || rook.type != ChessPieceType.Rook)
            {
                return false;
            }

            // Rook must not have moved
            if (rook.hasMoved)
            {
                return false;
            }

            // Path must be clear
            for (int i = 0; i < emptyIndexes.Length; i++)
            {
                if (board[emptyIndexes[i], rankY] != null)
                {
                    return false;
                }
            }

            // NOTE: Technically we also need to check "IsSquareUnderAttack" here.
            // You cannot castle out of, through, or into check. 
            // Since we haven't implemented attack maps yet, we skip this for now.

            return true;
        }
    }
}
