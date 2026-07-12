using System;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.EditorTools.OpeningBook
{
    /// <summary>
    /// One parsed line from an opening book source file: an ordered list of moves in coordinate
    /// notation (the same notation UCI engines exchange, e.g. "e2e4" or "e7e8q" for a promotion),
    /// an optional weight, and the source line number for error messages.
    /// </summary>
    public sealed class OpeningBookLine
    {
        public readonly int SourceLineNumber;
        public readonly IReadOnlyList<(Vector2Int From, Vector2Int To, ChessPieceType Promotion)> Moves;
        public readonly ushort Weight;

        public OpeningBookLine(
            int sourceLineNumber,
            IReadOnlyList<(Vector2Int From, Vector2Int To, ChessPieceType Promotion)> moves,
            ushort weight)
        {
            SourceLineNumber = sourceLineNumber;
            Moves = moves;
            Weight = weight;
        }

        /// <summary>
        /// Parses a single non-blank, non-comment source line. Returns null for a line that's
        /// entirely a comment or whitespace, so the caller can just skip it.
        /// </summary>
        public static OpeningBookLine Parse(string rawLine, int sourceLineNumber)
        {
            string content = StripComment(rawLine).Trim();
            if (content.Length == 0)
                return null;

            string movesPart = content;
            ushort weight = 1;

            int weightSeparator = content.IndexOf('|');
            if (weightSeparator >= 0)
            {
                movesPart = content.Substring(0, weightSeparator).Trim();
                weight = ParseWeight(content.Substring(weightSeparator + 1).Trim(), sourceLineNumber);
            }

            string[] tokens = movesPart.Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0)
                throw new OpeningBookParseException(sourceLineNumber, "Line has a weight but no moves.");

            var moves = new List<(Vector2Int, Vector2Int, ChessPieceType)>(tokens.Length);
            foreach (string token in tokens)
                moves.Add(ParseToken(token, sourceLineNumber));

            return new OpeningBookLine(sourceLineNumber, moves, weight);
        }

        private static string StripComment(string line)
        {
            int commentStart = line.IndexOf('#');
            return commentStart >= 0 ? line.Substring(0, commentStart) : line;
        }

        private static ushort ParseWeight(string weightExpression, int sourceLineNumber)
        {
            if (!weightExpression.StartsWith("w=", StringComparison.OrdinalIgnoreCase))
            {
                throw new OpeningBookParseException(
                    sourceLineNumber,
                    $"Expected a weight in the form 'w=N' after '|', found '{weightExpression}'.");
            }

            string number = weightExpression.Substring(2);
            if (!ushort.TryParse(number, out ushort weight) || weight == 0)
            {
                throw new OpeningBookParseException(
                    sourceLineNumber,
                    $"Weight must be a positive whole number, found '{number}'.");
            }

            return weight;
        }

        /// <summary>
        /// Parses one coordinate-notation token: two squares, e.g. "e2e4", with an optional
        /// trailing promotion letter for a pawn reaching the last rank, e.g. "e7e8q".
        /// </summary>
        private static (Vector2Int From, Vector2Int To, ChessPieceType Promotion) ParseToken(
            string token, int sourceLineNumber)
        {
            if (token.Length != 4 && token.Length != 5)
            {
                throw new OpeningBookParseException(
                    sourceLineNumber,
                    $"Move '{token}' isn't in coordinate notation (expected something like 'e2e4' or 'e7e8q').");
            }

            Vector2Int from = ParseSquare(token.Substring(0, 2), token, sourceLineNumber);
            Vector2Int to = ParseSquare(token.Substring(2, 2), token, sourceLineNumber);

            ChessPieceType promotion = ChessPieceType.None;
            if (token.Length == 5)
                promotion = ParsePromotionLetter(token[4], token, sourceLineNumber);

            return (from, to, promotion);
        }

        private static Vector2Int ParseSquare(string square, string token, int sourceLineNumber)
        {
            char file = char.ToLowerInvariant(square[0]);
            char rank = square[1];

            if (file < 'a' || file > 'h' || rank < '1' || rank > '8')
            {
                throw new OpeningBookParseException(
                    sourceLineNumber,
                    $"Move '{token}' contains an out-of-range square '{square}'.");
            }

            return new Vector2Int(file - 'a', rank - '1');
        }

        private static ChessPieceType ParsePromotionLetter(char letter, string token, int sourceLineNumber)
        {
            switch (char.ToLowerInvariant(letter))
            {
                case 'q': return ChessPieceType.Queen;
                case 'r': return ChessPieceType.Rook;
                case 'b': return ChessPieceType.Bishop;
                case 'n': return ChessPieceType.Knight;
                default:
                    throw new OpeningBookParseException(
                        sourceLineNumber,
                        $"Move '{token}' has an unrecognized promotion letter '{letter}' (expected q, r, b, or n).");
            }
        }
    }

    /// <summary>Thrown for any source line that can't be parsed or, once replayed, isn't legal.</summary>
    public sealed class OpeningBookParseException : Exception
    {
        public OpeningBookParseException(int sourceLineNumber, string reason)
            : base($"Opening book line {sourceLineNumber}: {reason}")
        {
        }
    }
}
