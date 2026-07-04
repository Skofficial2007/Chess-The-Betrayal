using System.Text;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// Renders a BoardState as a plain-text grid for pasting into a bug report — the thing to reach
    /// for when a checkmate/stalemate call needs to be verified against the exact final position by
    /// eye. Unity-free, same rationale as MoveNotation: it must run identically from the Editor
    /// console, a headless server, or a unit test.
    /// </summary>
    public static class BoardStateDump
    {
        /// <summary>
        /// One line per rank (uppercase for White, lowercase for Black, '.' for empty), followed by
        /// a summary line of the board's non-piece state. Rank 0 (White's back rank) prints last,
        /// matching how a player reads a board bottom-up.
        /// </summary>
        public static string ToAscii(BoardState board)
        {
            var sb = new StringBuilder((board.TileCountX + 1) * board.TileCountY + 32);

            for (int y = board.TileCountY - 1; y >= 0; y--)
            {
                for (int x = 0; x < board.TileCountX; x++)
                {
                    PieceData piece = board.GetPiece(x, y);
                    char c = piece.IsEmpty ? '.' : PieceLetter(piece.Type);
                    sb.Append(piece.IsEmpty || piece.Team == Team.White ? c : char.ToLowerInvariant(c));
                    sb.Append(' ');
                }
                sb.Append("  (rank y=").Append(y).Append(')');
                sb.AppendLine();
            }

            sb.Append("CurrentTurn=").Append(board.CurrentTurn)
              .Append(" CastlingRights=").Append(board.CastlingRights)
              .Append(" EnPassantFile=").Append(board.EnPassantFile?.ToString() ?? "none")
              .Append(" PendingBetrayerSquare=").Append(board.PendingBetrayerSquare?.ToString() ?? "none")
              .Append(" BetrayalInitiator=").Append(board.BetrayalInitiator?.ToString() ?? "none");

            return sb.ToString();
        }

        private static char PieceLetter(ChessPieceType type) => type switch
        {
            ChessPieceType.Pawn => 'P',
            ChessPieceType.Knight => 'N',
            ChessPieceType.Bishop => 'B',
            ChessPieceType.Rook => 'R',
            ChessPieceType.Queen => 'Q',
            ChessPieceType.King => 'K',
            _ => '?'
        };
    }
}
