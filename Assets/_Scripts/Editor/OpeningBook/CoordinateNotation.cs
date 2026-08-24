using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.EditorTools.OpeningBook
{
    /// <summary>
    /// Reads and writes single moves in the coordinate notation the book source files use — the
    /// same notation UCI engines exchange, e.g. "e2e4", or "e7e8q" for a pawn promoting.
    ///
    /// Shared by every book source format so there is only ever one parser. Two of them would drift:
    /// a notation fix applied to one and not the other would silently change what one book means
    /// while the other kept its old reading, and both would still compile.
    /// </summary>
    internal static class CoordinateNotation
    {
        /// <summary>
        /// Parses one move token. <paramref name="describeLocation"/> builds the prefix for any
        /// error message, so each caller can name its own file and line without this needing to
        /// know which kind of book it is being used by.
        /// </summary>
        public static (Vector2Int From, Vector2Int To, ChessPieceType Promotion) ParseToken(
            string token, System.Func<string, System.Exception> failure)
        {
            if (token.Length != 4 && token.Length != 5)
            {
                throw failure(
                    $"Move '{token}' isn't in coordinate notation (expected something like 'e2e4' or 'e7e8q').");
            }

            Vector2Int from = ParseSquare(token.Substring(0, 2), token, failure);
            Vector2Int to = ParseSquare(token.Substring(2, 2), token, failure);

            ChessPieceType promotion = ChessPieceType.None;
            if (token.Length == 5)
                promotion = ParsePromotionLetter(token[4], token, failure);

            return (from, to, promotion);
        }

        /// <summary>Renders a move back into the notation it was read from, for error messages.</summary>
        public static string ToToken(Vector2Int from, Vector2Int to, ChessPieceType promotion)
        {
            string promoLetter = promotion switch
            {
                ChessPieceType.Queen => "q",
                ChessPieceType.Rook => "r",
                ChessPieceType.Bishop => "b",
                ChessPieceType.Knight => "n",
                _ => ""
            };

            return $"{Square(from)}{Square(to)}{promoLetter}";
        }

        private static string Square(Vector2Int v) => $"{(char)('a' + v.x)}{v.y + 1}";

        private static Vector2Int ParseSquare(
            string square, string token, System.Func<string, System.Exception> failure)
        {
            if (!SquareNotation.TryParse(square, out Vector2Int parsed))
                throw failure($"Move '{token}' contains an out-of-range square '{square}'.");

            return parsed;
        }

        private static ChessPieceType ParsePromotionLetter(
            char letter, string token, System.Func<string, System.Exception> failure)
        {
            switch (char.ToLowerInvariant(letter))
            {
                case 'q': return ChessPieceType.Queen;
                case 'r': return ChessPieceType.Rook;
                case 'b': return ChessPieceType.Bishop;
                case 'n': return ChessPieceType.Knight;
                default:
                    throw failure($"Move '{token}' has an unrecognized promotion letter '{letter}' (expected q, r, b, or n).");
            }
        }
    }
}
