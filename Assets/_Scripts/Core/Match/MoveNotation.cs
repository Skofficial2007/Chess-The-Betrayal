using System.Text;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// Converts a MoveCommand into a compact, human-readable one-line record — algebraic chess
    /// notation extended with the Betrayal stage tag. This is the single source of truth for
    /// "what actually happened this ply" in logs, replay files, and bug reports. Unity-free so it
    /// can run in a headless server or a unit test exactly as it runs in the Editor console.
    ///
    /// Format: "12. e4-e5" / "12. Nf3xe5" / "12. e7-e8=Q" / "12. O-O" / "12. Nc3xd5 [Act]".
    /// Deliberately NOT full SAN (no disambiguation, no +/# suffixes) — this is a debugging and
    /// replay aid, not a PGN exporter. If you need PGN export later, build it as a separate pass
    /// over MatchMoveLog rather than complicating this formatter.
    /// </summary>
    public static class MoveNotation
    {
        public static string Format(MoveCommand move, int fullMoveNumber)
        {
            var sb = new StringBuilder(32);

            sb.Append(fullMoveNumber);
            sb.Append(move.PieceTeam == Team.White ? ". " : "... ");

            if (move.IsCastling)
            {
                sb.Append(move.EndPosition.x > move.StartPosition.x ? "O-O" : "O-O-O");
            }
            else if (move.Stage == BetrayalStage.Defection)
            {
                // Defection is a same-square team-flip, not a move from A to B — StartPosition
                // equals EndPosition, so the normal "A-B" rendering would misleadingly print
                // "e5-e5". Render it as what actually happened: the piece switched sides in place.
                if (move.PieceType != ChessPieceType.Pawn)
                {
                    sb.Append(PieceLetter(move.PieceType));
                }
                sb.Append(Square(move.StartPosition));
                sb.Append(" defects");
            }
            else
            {
                if (move.PieceType != ChessPieceType.Pawn)
                {
                    sb.Append(PieceLetter(move.PieceType));
                }

                sb.Append(Square(move.StartPosition));
                sb.Append(move.HasCapture ? 'x' : '-');
                sb.Append(Square(move.EndPosition));

                if (move.IsEnPassant)
                {
                    sb.Append(" e.p.");
                }

                if (move.IsPromotion)
                {
                    sb.Append('=');
                    sb.Append(PieceLetter(move.PromotedTo));
                }
            }

            if (move.Stage != BetrayalStage.None)
            {
                sb.Append(" [");
                sb.Append(move.Stage);
                sb.Append(']');
            }

            return sb.ToString();
        }

        /// <summary>Appends a result suffix (+, #, or nothing) — call after the engine evaluates the
        /// resulting GameState, since a MoveCommand alone doesn't know what it led to.</summary>
        public static string WithResultSuffix(string formattedMove, GameState resultingState)
        {
            return resultingState switch
            {
                GameState.Checkmate => formattedMove + "#",
                GameState.Check => formattedMove + "+",
                _ => formattedMove
            };
        }

        private static char PieceLetter(ChessPieceType type) => type switch
        {
            ChessPieceType.Knight => 'N',
            ChessPieceType.Bishop => 'B',
            ChessPieceType.Rook => 'R',
            ChessPieceType.Queen => 'Q',
            ChessPieceType.King => 'K',
            _ => '?'
        };

        private static string Square(Vector2Int pos) => $"{(char)('a' + pos.x)}{pos.y + 1}";
    }
}
