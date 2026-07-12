using System;
using System.Collections.Generic;
using System.Linq;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ChessTheBetrayal.Tests.EditMode")]

namespace ChessTheBetrayal.EditorTools.OpeningBook
{
    /// <summary>
    /// Turns a plain-text list of opening variations into the sorted key/move/weight arrays an
    /// OpeningBookAsset needs. Every line is replayed move-by-move through the real chess engine,
    /// exactly the way a match would be — so a book entry can never recommend a move that isn't
    /// legal, and the recorded Zobrist hash is guaranteed to match what the search computes for
    /// that same position during a real game.
    /// </summary>
    public static class OpeningBookCompiler
    {
        internal readonly struct BookEntry
        {
            public readonly ulong Hash;
            public readonly uint PackedMove;
            public readonly ushort Weight;

            public BookEntry(ulong hash, uint packedMove, ushort weight)
            {
                Hash = hash;
                PackedMove = packedMove;
                Weight = weight;
            }
        }

        /// <summary>
        /// Compiles source text (the full contents of a .book.txt file) into sorted, deduplicated
        /// key/move/weight arrays plus a scheme fingerprint. Throws OpeningBookParseException on
        /// the first malformed or illegal line, naming the line number and the offending token —
        /// a book with a silently-dropped line is worse than a build that refuses to compile.
        /// </summary>
        public static (ulong[] Keys, uint[] PackedMoves, ushort[] Weights, ulong SchemeVersion) Compile(
            string sourceText)
        {
            var engine = new ChessEngineAdapter();
            var merged = new Dictionary<(ulong Hash, uint PackedMove), long>();

            string[] rawLines = sourceText.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < rawLines.Length; i++)
            {
                int sourceLineNumber = i + 1;
                OpeningBookLine line = OpeningBookLine.Parse(rawLines[i], sourceLineNumber);
                if (line == null)
                    continue;

                foreach (BookEntry entry in ReplayLine(line, engine))
                {
                    var key = (entry.Hash, entry.PackedMove);
                    merged.TryGetValue(key, out long existingWeight);
                    merged[key] = Math.Min(existingWeight + entry.Weight, ushort.MaxValue);
                }
            }

            var sortedEntries = merged
                .Select(pair => new BookEntry(pair.Key.Hash, pair.Key.PackedMove, (ushort)pair.Value))
                .OrderBy(entry => entry.Hash)
                .ToArray();

            ulong[] keys = new ulong[sortedEntries.Length];
            uint[] packedMoves = new uint[sortedEntries.Length];
            ushort[] weights = new ushort[sortedEntries.Length];
            for (int i = 0; i < sortedEntries.Length; i++)
            {
                keys[i] = sortedEntries[i].Hash;
                packedMoves[i] = sortedEntries[i].PackedMove;
                weights[i] = sortedEntries[i].Weight;
            }

            return (keys, packedMoves, weights, BoardState.ZobristSchemeVersion);
        }

        /// <summary>
        /// Replays one line's moves from the standard starting position, yielding one entry per
        /// ply: the hash of the position BEFORE the move (the lookup key a search will present)
        /// paired with the move played there. Stops rejecting the whole line the moment a move
        /// would open a Betrayal sub-sequence — the book only ever recommends ordinary opening
        /// theory, never a Betrayal line, so nothing here needs to reason about Act/Retribution/
        /// Defection at lookup time.
        /// </summary>
        private static IEnumerable<BookEntry> ReplayLine(OpeningBookLine line, IChessEngine engine) =>
            ReplayLine(line, engine, CreateStandardStartingPosition());

        /// <summary>
        /// Internal replay entry point exposed to tests via InternalsVisibleTo, so the
        /// Betrayal-rejection and canary-hash behavior can be exercised from a custom starting
        /// position instead of only the full standard opening (some Betrayal setups take many
        /// more plies than is practical to reach from move one).
        /// </summary>
        internal static IEnumerable<BookEntry> ReplayLine(OpeningBookLine line, IChessEngine engine, BoardState board)
        {
            var entries = new List<BookEntry>(line.Moves.Count);
            var legalMoves = new List<MoveCommand>();

            for (int ply = 0; ply < line.Moves.Count; ply++)
            {
                (Vector2Int from, Vector2Int to, ChessPieceType promotion) = line.Moves[ply];

                legalMoves.Clear();
                engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);

                MoveCommand? match = FindMatch(legalMoves, from, to, promotion);
                if (match == null)
                {
                    throw new OpeningBookParseException(
                        line.SourceLineNumber,
                        $"Ply {ply + 1} ('{ToToken(from, to, promotion)}') is not a legal move in the " +
                        "position reached by the moves before it.");
                }

                if (match.Value.Stage != BetrayalStage.None)
                {
                    throw new OpeningBookParseException(
                        line.SourceLineNumber,
                        $"Ply {ply + 1} ('{ToToken(from, to, promotion)}') is a Betrayal move — the " +
                        "opening book only covers ordinary chess theory, never a Betrayal sequence.");
                }

                ulong hashBeforeMove = board.ZobristHash;
                uint packedMove = AlphaBetaSearch.PackMove(match.Value);

                engine.ApplyMove(board, match.Value);
                if (BetrayalStageRules.FlipsTurn(match.Value.Stage))
                    board.NextTurn();

                board.AssertZobristConsistency();

                entries.Add(new BookEntry(hashBeforeMove, packedMove, line.Weight));
            }

            return entries;
        }

        private static MoveCommand? FindMatch(
            List<MoveCommand> legalMoves, Vector2Int from, Vector2Int to, ChessPieceType promotion)
        {
            foreach (MoveCommand candidate in legalMoves)
            {
                if (candidate.StartPosition == from &&
                    candidate.EndPosition == to &&
                    candidate.PromotedTo == promotion)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string ToToken(Vector2Int from, Vector2Int to, ChessPieceType promotion)
        {
            string square(Vector2Int v) => $"{(char)('a' + v.x)}{v.y + 1}";
            string promoLetter = promotion switch
            {
                ChessPieceType.Queen => "q",
                ChessPieceType.Rook => "r",
                ChessPieceType.Bishop => "b",
                ChessPieceType.Knight => "n",
                _ => ""
            };
            return $"{square(from)}{square(to)}{promoLetter}";
        }

        /// <summary>
        /// Builds the standard chess starting position directly (no Betrayal state, full castling
        /// rights) — every book line starts here, since the book only ever covers openings from
        /// the game's actual opening position.
        /// </summary>
        internal static BoardState CreateStandardStartingPosition()
        {
            var board = new BoardState(8, 8);
            board.Clear();

            ChessPieceType[] backRank =
            {
                ChessPieceType.Rook, ChessPieceType.Knight, ChessPieceType.Bishop, ChessPieceType.Queen,
                ChessPieceType.King, ChessPieceType.Bishop, ChessPieceType.Knight, ChessPieceType.Rook
            };

            for (int x = 0; x < 8; x++)
            {
                board.SetPiece(new PieceData(Team.White, backRank[x], 1, 0), x, 0);
                board.SetPiece(new PieceData(Team.White, ChessPieceType.Pawn, 1, 1), x, 1);
                board.SetPiece(new PieceData(Team.Black, ChessPieceType.Pawn, -1, 6), x, 6);
                board.SetPiece(new PieceData(Team.Black, backRank[x], -1, 7), x, 7);
            }

            board.BetrayalRightAvailable = true;
            board.ComputeFullZobristHash();
            return board;
        }
    }
}
