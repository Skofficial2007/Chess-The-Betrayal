using System;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.EditorTools.OpeningBook
{
    /// <summary>
    /// One parsed record from a trap book source file:
    ///
    ///     e2e4 e7e5 g1f3 d7d6 f1c4 b8c6 b1c3 c8g4 h2h3 g4h5 f3e5 | avoid=h5d1 best=c6e5 | Legal Trap
    ///
    /// The moves run from the standard starting position up to and including the move that sets
    /// the trap, so they describe the position a player is actually looking at when the mistake is
    /// available. "avoid" is the losing move, "best" is what to play there instead.
    ///
    /// The name is a field rather than a comment above the record because it is data here, not
    /// commentary — anything that tells a player which trap they just avoided reads it. A record
    /// that lost its name to a stray edit would still compile, so the name is required.
    /// </summary>
    public sealed class TrapBookLine
    {
        public readonly int SourceLineNumber;
        public readonly IReadOnlyList<(Vector2Int From, Vector2Int To, ChessPieceType Promotion)> SetupMoves;
        public readonly (Vector2Int From, Vector2Int To, ChessPieceType Promotion) BlunderMove;
        public readonly (Vector2Int From, Vector2Int To, ChessPieceType Promotion) BestMove;
        public readonly string Name;

        public TrapBookLine(
            int sourceLineNumber,
            IReadOnlyList<(Vector2Int From, Vector2Int To, ChessPieceType Promotion)> setupMoves,
            (Vector2Int From, Vector2Int To, ChessPieceType Promotion) blunderMove,
            (Vector2Int From, Vector2Int To, ChessPieceType Promotion) bestMove,
            string name)
        {
            SourceLineNumber = sourceLineNumber;
            SetupMoves = setupMoves;
            BlunderMove = blunderMove;
            BestMove = bestMove;
            Name = name;
        }

        /// <summary>
        /// Parses a single non-blank, non-comment source line. Returns null for a line that is
        /// entirely a comment or whitespace, so the caller can just skip it.
        /// </summary>
        public static TrapBookLine Parse(string rawLine, int sourceLineNumber)
        {
            string content = StripComment(rawLine).Trim();
            if (content.Length == 0)
                return null;

            string[] sections = content.Split('|');
            if (sections.Length != 3)
            {
                throw new TrapBookParseException(
                    sourceLineNumber,
                    "Expected three sections separated by '|': the moves up to the trap, then " +
                    "'avoid=<move> best=<move>', then the trap's name.");
            }

            var setupMoves = ParseSetupMoves(sections[0], sourceLineNumber);
            var (blunder, best) = ParseMovePair(sections[1], sourceLineNumber);

            string name = sections[2].Trim();
            if (name.Length == 0)
                throw new TrapBookParseException(sourceLineNumber, "The trap has no name.");

            return new TrapBookLine(sourceLineNumber, setupMoves, blunder, best, name);
        }

        private static string StripComment(string line)
        {
            int commentStart = line.IndexOf('#');
            return commentStart >= 0 ? line.Substring(0, commentStart) : line;
        }

        private static List<(Vector2Int, Vector2Int, ChessPieceType)> ParseSetupMoves(
            string section, int sourceLineNumber)
        {
            string[] tokens = section.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                throw new TrapBookParseException(
                    sourceLineNumber,
                    "A trap needs at least one move to reach the position it happens in.");
            }

            var moves = new List<(Vector2Int, Vector2Int, ChessPieceType)>(tokens.Length);
            foreach (string token in tokens)
            {
                moves.Add(CoordinateNotation.ParseToken(
                    token, reason => new TrapBookParseException(sourceLineNumber, reason)));
            }

            return moves;
        }

        private static ((Vector2Int, Vector2Int, ChessPieceType) Blunder, (Vector2Int, Vector2Int, ChessPieceType) Best)
            ParseMovePair(string section, int sourceLineNumber)
        {
            string avoidToken = null;
            string bestToken = null;

            foreach (string field in section.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = field.IndexOf('=');
                if (equals < 0)
                    throw new TrapBookParseException(sourceLineNumber, $"Expected 'name=value', found '{field}'.");

                string key = field.Substring(0, equals).Trim().ToLowerInvariant();
                string value = field.Substring(equals + 1).Trim();

                switch (key)
                {
                    case "avoid":
                        if (avoidToken != null)
                            throw new TrapBookParseException(sourceLineNumber, "'avoid' is given more than once.");
                        avoidToken = value;
                        break;
                    case "best":
                        if (bestToken != null)
                            throw new TrapBookParseException(sourceLineNumber, "'best' is given more than once.");
                        bestToken = value;
                        break;
                    default:
                        throw new TrapBookParseException(
                            sourceLineNumber, $"Unrecognized field '{key}' (expected 'avoid' or 'best').");
                }
            }

            if (avoidToken == null)
                throw new TrapBookParseException(sourceLineNumber, "The record has no 'avoid=' move.");
            if (bestToken == null)
                throw new TrapBookParseException(sourceLineNumber, "The record has no 'best=' move.");

            Func<string, Exception> failure = reason => new TrapBookParseException(sourceLineNumber, reason);
            return (CoordinateNotation.ParseToken(avoidToken, failure),
                    CoordinateNotation.ParseToken(bestToken, failure));
        }
    }

    /// <summary>Thrown for any trap record that can't be parsed or, once replayed, isn't legal.</summary>
    public sealed class TrapBookParseException : Exception
    {
        public TrapBookParseException(int sourceLineNumber, string reason)
            : base($"Trap book line {sourceLineNumber}: {reason}")
        {
        }
    }
}
