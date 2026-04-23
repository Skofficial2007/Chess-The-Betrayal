using System.Collections.Generic;
using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.Logic.Movement
{
    /// <summary>
    /// Movement strategy for the Pawn piece.
    /// Handles: forward moves, double-step from start, diagonal captures, en passant, promotion.
    /// Most complex piece due to special rules and asymmetric movement.
    /// </summary>
    public class PawnMovement : IPieceMovement
    {
        public void GetRawMoves(BoardState board, PieceData piece, List<MoveCommand> buffer)
        {
            // Delegate to history-aware implementation using the board's move history
            GetRawMovesWithHistory(board, piece, buffer, board.MoveHistory);
        }

        public void GetRawMovesWithHistory(BoardState board, PieceData piece, List<MoveCommand> buffer, List<Vector2Int> moveHistory)
        {
            Vector2Int pos = new Vector2Int(piece.CurrentX, piece.CurrentY);
            int dir = piece.MoveDirection; // +1 for white (moving up), -1 for black (moving down)

            // 1. Single Forward Move
            Vector2Int oneForward = new Vector2Int(pos.x, pos.y + dir);
            if (board.IsValidIndex(oneForward) && board.GetPiece(oneForward) == null)
            {
                CheckAndAddPromotion(board, buffer, pos, oneForward, piece, null);

                // 2. Double Forward Move (only from starting position and if one forward is clear)
                if (pos.y == piece.InitialY && !piece.HasMoved)
                {
                    Vector2Int twoForward = new Vector2Int(pos.x, pos.y + (dir * 2));
                    if (board.IsValidIndex(twoForward) && board.GetPiece(twoForward) == null)
                    {
                        buffer.Add(MoveCommand.CreateStandardMove(pos, twoForward, piece));
                    }
                }
            }

            // 3. Diagonal Captures
            Vector2Int leftCapture = new Vector2Int(pos.x - 1, pos.y + dir);
            Vector2Int rightCapture = new Vector2Int(pos.x + 1, pos.y + dir);

            EvaluateCapture(board, buffer, piece, pos, leftCapture);
            EvaluateCapture(board, buffer, piece, pos, rightCapture);

            // 4. En Passant (needs the move history)
            EvaluateEnPassant(board, buffer, piece, pos, dir, moveHistory);
        }

        /// <summary>
        /// Checks if a diagonal square contains an enemy piece and adds the capture move.
        /// </summary>
        private void EvaluateCapture(BoardState board, List<MoveCommand> buffer, PieceData pawn, Vector2Int start, Vector2Int target)
        {
            if (!board.IsValidIndex(target)) return;

            PieceData targetPiece = board.GetPiece(target);
            if (targetPiece != null && targetPiece.Team != pawn.Team)
            {
                CheckAndAddPromotion(board, buffer, start, target, pawn, targetPiece);
            }
        }

        /// <summary>
        /// Adds a move, checking if it results in promotion.
        /// If yes, generates all 4 promotion options (Queen, Rook, Knight, Bishop).
        /// </summary>
        private void CheckAndAddPromotion(BoardState board, List<MoveCommand> buffer, Vector2Int start, Vector2Int target, PieceData pawn, PieceData captured)
        {
            // Check if pawn reaches the promotion rank
            int promotionRank = (pawn.MoveDirection == 1) ? board.TileCountY - 1 : 0;

            if (target.y == promotionRank)
            {
                // Generate all promotion options
                // In a real game, the player chooses; in AI, we evaluate all possibilities
                buffer.Add(MoveCommand.CreatePromotionMove(start, target, pawn, ChessPieceType.Queen, captured));
                buffer.Add(MoveCommand.CreatePromotionMove(start, target, pawn, ChessPieceType.Rook, captured));
                buffer.Add(MoveCommand.CreatePromotionMove(start, target, pawn, ChessPieceType.Knight, captured));
                buffer.Add(MoveCommand.CreatePromotionMove(start, target, pawn, ChessPieceType.Bishop, captured));
            }
            else
            {
                buffer.Add(MoveCommand.CreateStandardMove(start, target, pawn, captured));
            }
        }

        /// <summary>
        /// Evaluates if en passant capture is legal.
        /// Requirements: Last move was a 2-square pawn move, landing beside this pawn.
        /// </summary>
        private void EvaluateEnPassant(BoardState board, List<MoveCommand> buffer, PieceData pawn, Vector2Int pos, int dir, List<Vector2Int> moveHistory)
        {
            if (moveHistory == null || moveHistory.Count < 2) return;

            int lastIndex = moveHistory.Count - 1;
            Vector2Int lastEnd = moveHistory[lastIndex];
            Vector2Int lastStart = moveHistory[lastIndex - 1];

            PieceData lastMovedPiece = board.GetPiece(lastEnd);

            // Verify last move was an enemy pawn
            if (lastMovedPiece == null ||
                lastMovedPiece.Type != ChessPieceType.Pawn ||
                lastMovedPiece.Team == pawn.Team)
            {
                return;
            }

            // Check if the enemy pawn is beside us
            bool isBeside = lastMovedPiece.CurrentY == pawn.CurrentY &&
                           System.Math.Abs(lastMovedPiece.CurrentX - pawn.CurrentX) == 1;

            if (!isBeside) return;

            // Check if the last move was a 2-square advance
            int moveDistance = System.Math.Abs(lastEnd.y - lastStart.y);
            if (moveDistance == 2)
            {
                // En passant target square is diagonal to our pawn
                Vector2Int enPassantTarget = new Vector2Int(lastMovedPiece.CurrentX, pos.y + dir);
                Vector2Int capturePosition = new Vector2Int(lastMovedPiece.CurrentX, lastMovedPiece.CurrentY);

                buffer.Add(MoveCommand.CreateEnPassantMove(pos, enPassantTarget, pawn, lastMovedPiece, capturePosition));
            }
        }
    }
}