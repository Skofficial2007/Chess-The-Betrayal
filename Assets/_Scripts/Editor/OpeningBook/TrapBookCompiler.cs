using System;
using System.Collections.Generic;
using System.Linq;
using ChessTheBetrayal.AI.Moves;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.EditorTools.OpeningBook
{
    /// <summary>
    /// Turns a plain-text list of opening traps into the sorted arrays a TrapBookAsset needs.
    ///
    /// Every record is replayed move by move through the real chess engine, the same way a match
    /// would play it, and both the losing move and the recommended one are checked for legality in
    /// the position actually reached. That matters more here than it does for the opening book: a
    /// trap record whose position is one ply out still looks perfectly reasonable in a text file,
    /// and would quietly protect the AI from a mistake it was never in a position to make.
    /// </summary>
    public static class TrapBookCompiler
    {
        /// <summary>
        /// Compiles source text into sorted key/blunder/best/name arrays plus a scheme fingerprint.
        /// Throws TrapBookParseException on the first bad record, naming the line and the reason —
        /// a trap book with a silently dropped record is worse than a build that refuses to run,
        /// because the gap is invisible until the AI walks into the trap that record covered.
        /// </summary>
        public static (ulong[] Keys, uint[] BlunderMoves, uint[] BestMoves, string[] Names, ulong SchemeVersion)
            Compile(string sourceText)
        {
            var engine = new ChessEngineAdapter();
            var byPosition = new Dictionary<ulong, (uint Blunder, uint Best, string Name, int Line)>();

            string[] rawLines = sourceText.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < rawLines.Length; i++)
            {
                int sourceLineNumber = i + 1;
                TrapBookLine line = TrapBookLine.Parse(rawLines[i], sourceLineNumber);
                if (line == null)
                    continue;

                (ulong key, uint blunder, uint best) = Replay(line, engine);

                if (byPosition.TryGetValue(key, out var existing))
                {
                    // The same position can legitimately be reached by two records — the Fried Liver
                    // and the Lolli reach one identical position, for instance, and two records can
                    // arrive by different move orders that transpose.
                    //
                    // Which move LOSES is the fact a trap records, so two records disagreeing about
                    // that contradict each other and would otherwise resolve by whichever happened
                    // to be read last. Which move to play INSTEAD is not unique — a position with one
                    // losing move usually has several sound replies, and two sources naming
                    // different ones are both right. So that difference merges, keeping the first,
                    // rather than failing a build over a disagreement that isn't one.
                    if (existing.Blunder != blunder)
                    {
                        throw new TrapBookParseException(
                            sourceLineNumber,
                            $"'{line.Name}' describes the same position as '{existing.Name}' on line " +
                            $"{existing.Line} but disagrees about which move loses.");
                    }

                    continue;
                }

                byPosition[key] = (blunder, best, line.Name, sourceLineNumber);
            }

            var sorted = byPosition.OrderBy(pair => pair.Key).ToArray();

            ulong[] keys = new ulong[sorted.Length];
            uint[] blunderMoves = new uint[sorted.Length];
            uint[] bestMoves = new uint[sorted.Length];
            string[] names = new string[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                keys[i] = sorted[i].Key;
                blunderMoves[i] = sorted[i].Value.Blunder;
                bestMoves[i] = sorted[i].Value.Best;
                names[i] = sorted[i].Value.Name;
            }

            return (keys, blunderMoves, bestMoves, names, BoardState.ZobristSchemeVersion);
        }

        /// <summary>
        /// Replays a record's setup moves from the standard starting position and returns the hash
        /// of the position they reach, together with the losing and recommended moves packed the
        /// same way the search packs them.
        /// </summary>
        private static (ulong Key, uint Blunder, uint Best) Replay(TrapBookLine line, IChessEngine engine) =>
            Replay(line, engine, OpeningBookCompiler.CreateStandardStartingPosition());

        /// <summary>
        /// Replay from a caller-supplied position, exposed to tests. Some rules — Betrayal
        /// rejection in particular — apply to positions that would take far more plies of ordinary
        /// opening theory to reach than is practical to write into a test.
        /// </summary>
        internal static (ulong Key, uint Blunder, uint Best) Replay(
            TrapBookLine line, IChessEngine engine, BoardState board)
        {
            var legalMoves = new List<MoveCommand>();

            for (int ply = 0; ply < line.SetupMoves.Count; ply++)
            {
                var (from, to, promotion) = line.SetupMoves[ply];

                legalMoves.Clear();
                engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);

                MoveCommand? match = FindMatch(legalMoves, from, to, promotion);
                if (match == null)
                {
                    throw new TrapBookParseException(
                        line.SourceLineNumber,
                        $"Ply {ply + 1} ('{CoordinateNotation.ToToken(from, to, promotion)}') is not a legal " +
                        "move in the position reached by the moves before it.");
                }

                RejectBetrayal(line, match.Value, $"Ply {ply + 1}");

                engine.ApplyMove(board, match.Value);
                if (BetrayalStageRules.FlipsTurn(match.Value.Stage))
                    board.NextTurn();

                board.AssertZobristConsistency();
            }

            legalMoves.Clear();
            engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);

            MoveCommand blunder = Require(line, legalMoves, line.BlunderMove, "avoid");
            MoveCommand best = Require(line, legalMoves, line.BestMove, "best");

            if (PackedMove.Pack(blunder) == PackedMove.Pack(best))
            {
                throw new TrapBookParseException(
                    line.SourceLineNumber,
                    "The move to avoid and the move to play instead are the same move.");
            }

            return (board.ZobristHash, PackedMove.Pack(blunder), PackedMove.Pack(best));
        }

        private static MoveCommand Require(
            TrapBookLine line,
            List<MoveCommand> legalMoves,
            (Vector2Int From, Vector2Int To, ChessPieceType Promotion) move,
            string field)
        {
            MoveCommand? match = FindMatch(legalMoves, move.From, move.To, move.Promotion);
            if (match == null)
            {
                throw new TrapBookParseException(
                    line.SourceLineNumber,
                    $"The '{field}' move '{CoordinateNotation.ToToken(move.From, move.To, move.Promotion)}' is " +
                    "not legal in the position the setup moves reach.");
            }

            RejectBetrayal(line, match.Value, $"The '{field}' move");
            return match.Value;
        }

        /// <summary>
        /// Traps are ordinary chess mistakes. A Betrayal is a once-per-match resource with its own
        /// rules, and the opening book excludes it for the same reason — nothing here should be
        /// steering that decision from a text file.
        /// </summary>
        private static void RejectBetrayal(TrapBookLine line, MoveCommand move, string what)
        {
            if (move.Stage == BetrayalStage.None) return;

            throw new TrapBookParseException(
                line.SourceLineNumber,
                $"{what} is a Betrayal move — the trap book covers ordinary chess only.");
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
    }
}
