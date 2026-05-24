using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Core.Movement
{
    /// <summary>
    /// Movement strategy for the Pawn piece.
    /// Handles: forward moves, double-step from start, diagonal captures, en passant, promotion.
    /// Most complex piece due to special rules and asymmetric movement.
    /// </summary>
    public class PawnMovement : IPieceMovement
    {
        public void GetRawMoves(BoardState board, PieceData piece, Vector2Int pos, List<MoveCommand> buffer)
        {
            int dir = piece.MoveDirection; // +1 for white (moving up), -1 for black (moving down)

            // 1. Single Forward Move
            Vector2Int oneForward = new Vector2Int(pos.x, pos.y + dir);
            if (board.IsValidIndex(oneForward) && board.GetPiece(oneForward).IsEmpty)
            {
                AddMoveOrPromotion(board, buffer, pos, oneForward, piece, default);

                // 2. Double Forward Move (only from starting position and if one forward is clear)
                if (pos.y == piece.StartRow && !piece.HasMoved)
                {
                    Vector2Int twoForward = new Vector2Int(pos.x, pos.y + (dir * 2));
                    if (board.IsValidIndex(twoForward) && board.GetPiece(twoForward).IsEmpty)
                    {
                        buffer.Add(MoveCommand.CreateStandardMove(pos, twoForward, piece, default, board));
                    }
                }
            }

            // 3. Diagonal Captures
            TryAddDiagonalCapture(board, buffer, piece, pos, new Vector2Int(pos.x - 1, pos.y + dir));
            TryAddDiagonalCapture(board, buffer, piece, pos, new Vector2Int(pos.x + 1, pos.y + dir));

            // 4. En Passant (history-dependent; uses board.EnPassantFile)
            TryAddEnPassant(board, buffer, piece, pos, dir);
        }

        /// <summary>
        /// Checks if a diagonal square contains an enemy piece and adds the capture move.
        /// </summary>
        private void TryAddDiagonalCapture(BoardState board, List<MoveCommand> buffer, PieceData pawn, Vector2Int start, Vector2Int target)
        {
            if (!board.IsValidIndex(target)) return;

            PieceData targetPiece = board.GetPiece(target);
            if (!targetPiece.IsEmpty && targetPiece.Team != pawn.Team)
            {
                AddMoveOrPromotion(board, buffer, start, target, pawn, targetPiece);
            }
        }

        /// <summary>
        /// Adds a pawn's forward move. If the pawn is about to reach the last rank, we generate all four promotion options instead so the player (or AI) can choose.
        /// </summary>
        private void AddMoveOrPromotion(BoardState board, List<MoveCommand> buffer, Vector2Int start, Vector2Int target, PieceData pawn, PieceData captured)
        {
            // Check if pawn reaches the promotion rank
            int promotionRank = (pawn.MoveDirection == 1) ? board.TileCountY - 1 : 0;

            if (target.y == promotionRank)
            {
                // Generate all promotion options
                // In a real game, the player chooses; in AI, we evaluate all possibilities
                buffer.Add(MoveCommand.CreatePromotionMove(start, target, pawn, ChessPieceType.Queen, captured, board));
                buffer.Add(MoveCommand.CreatePromotionMove(start, target, pawn, ChessPieceType.Rook, captured, board));
                buffer.Add(MoveCommand.CreatePromotionMove(start, target, pawn, ChessPieceType.Knight, captured, board));
                buffer.Add(MoveCommand.CreatePromotionMove(start, target, pawn, ChessPieceType.Bishop, captured, board));
            }
            else
            {
                buffer.Add(MoveCommand.CreateStandardMove(start, target, pawn, captured, board));
            }
        }

        /// <summary>
        /// Checks if an en passant capture is available and adds it if so. We use the board's EnPassantFile directly rather than scanning move history.
        /// </summary>
        private void TryAddEnPassant(BoardState board, List<MoveCommand> buffer, PieceData pawn, Vector2Int pos, int dir)
        {
            // Simply check if an En Passant file is currently active on the board state
            if (!board.EnPassantFile.HasValue) return;

            int epFileX = board.EnPassantFile.Value;

            // Check if the vulnerable pawn is directly adjacent to our pawn
            if (System.Math.Abs(epFileX - pos.x) == 1)
            {
                // The enemy pawn must be on the same Y rank as our pawn
                PieceData enemyPawn = board.GetPiece(new Vector2Int(epFileX, pos.y));

                if (!enemyPawn.IsEmpty && enemyPawn.Team != pawn.Team && enemyPawn.Type == ChessPieceType.Pawn)
                {
                    // Target is one square diagonal forward
                    Vector2Int enPassantTarget = new Vector2Int(epFileX, pos.y + dir);
                    Vector2Int capturePosition = new Vector2Int(epFileX, pos.y);

                    buffer.Add(MoveCommand.CreateEnPassantMove(pos, enPassantTarget, pawn, enemyPawn, capturePosition, board));
                }
            }
        }
    }
}