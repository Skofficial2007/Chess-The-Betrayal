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
            {
                moves.Add(CoordinateNotation.ParseToken(
                    token, reason => new OpeningBookParseException(sourceLineNumber, reason)));
            }

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
