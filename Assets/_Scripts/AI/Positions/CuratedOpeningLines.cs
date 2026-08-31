using System;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.AI.Positions
{
    /// <summary>
    /// A fixed set of early-middlegame positions, each reached by replaying a short line of real
    /// opening moves through the actual engine from the standard start. Nothing is placed by hand, so
    /// every position is legal and reachable by construction, and a line that stops matching the
    /// engine fails loudly instead of quietly shrinking the set.
    ///
    /// These are the positions where the search has repeatedly been measured to run shortest of its
    /// configured depth. Hand-authored "hard-looking" positions turned out to be a poor guide — most
    /// of them reach their depth ceiling comfortably — while ordinary opening lines nobody picked for
    /// the purpose are where the budget actually runs out. Anything measuring how deep the search
    /// gets should measure here first.
    ///
    /// Lives in the shipping assembly rather than alongside the tests because the on-device benchmark
    /// needs the same positions and runs on a phone, where an editor-only test assembly does not
    /// exist. The strength harness reads them from here too, so there is one copy and the two cannot
    /// drift apart.
    /// </summary>
    public static class CuratedOpeningLines
    {
        /// <summary>
        /// One token per ply, space-separated coordinate notation — the same format the opening book's
        /// own source file uses. Deliberately short: these are early middlegames, not deep theory, so
        /// that anything measuring the search spends its time on the search rather than on replaying
        /// moves to reach the position.
        /// </summary>
        private static readonly string[] Lines =
        {
            "e2e4 e7e5 g1f3 b8c6 f1c4",                          // Italian Game
            "e2e4 e7e5 g1f3 b8c6 f1b5",                           // Ruy Lopez
            "e2e4 c7c5 g1f3 d7d6",                                // Sicilian, Open
            "e2e4 c7c5 g1f3 b8c6 d2d4 c5d4 f3d4",                 // Sicilian, Open with early trade
            "e2e4 e7e6 d2d4 d7d5",                                // French Defense
            "e2e4 c7c6 d2d4 d7d5",                                // Caro-Kann Defense
            "e2e4 e7e5 b1c3 g8f6",                                // Vienna Game
            "e2e4 g7g6 d2d4 f8g7",                                // Modern Defense
            "d2d4 d7d5 c2c4 e7e6",                                // Queen's Gambit Declined
            "d2d4 d7d5 c2c4 c7c6",                                // Slav Defense
            "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7",                      // King's Indian Defense
            "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4",                      // Nimzo-Indian Defense
            "d2d4 f7f5",                                          // Dutch Defense
            "d2d4 d7d5 g1f3 g8f6 c2c4",                           // Queen's Gambit, Knight development
            "c2c4 e7e5 b1c3 g8f6",                                // English Opening, reversed Sicilian
            "c2c4 g8f6 b1c3 e7e5",                                // English Opening, symmetric
            "g1f3 d7d5 c2c4",                                     // Reti Opening
            "e2e4 c7c5 g1f3 e7e6 d2d4 c5d4 f3d4 b8c6",            // Sicilian, Taimanov-ish
            "e2e4 e7e5 g1f3 g8f6",                                // Petrov Defense
            "d2d4 d7d5 b1c3 g8f6",                                // Veresov Attack
        };

        public static int Count => Lines.Length;

        /// <summary>The coordinate-notation line behind position <paramref name="index"/>, for a
        /// caller that wants to name or log the opening rather than play it.</summary>
        public static string Line(int index) => Lines[index];

        /// <summary>
        /// Replays position <paramref name="index"/>'s line from the standard start and returns the
        /// resulting board, Betrayal enabled and turn, move history and hash all consistent with
        /// having actually played the moves.
        ///
        /// Throws if an authored line is no longer legal against the current engine. That is
        /// deliberate: skipping a bad line would silently shrink the set, and a measurement taken
        /// over a different number of positions than it reports is worse than no measurement.
        /// </summary>
        public static BoardState BuildPosition(int index)
        {
            string[] tokens = Lines[index].Split(' ');

            BoardState board = StandardChessPosition.Create(betrayalRightAvailable: true);
            var engine = new ChessEngineAdapter();
            var resolver = new TurnResolver();
            var legalMoves = new List<MoveCommand>(64);

            foreach (string token in tokens)
            {
                if (!SquareNotation.TryParse(token[0], token[1], out Vector2Int from)
                    || !SquareNotation.TryParse(token[2], token[3], out Vector2Int to))
                {
                    throw new InvalidOperationException(
                        $"Curated opening line {index}: '{token}' is not a pair of squares.");
                }

                legalMoves.Clear();
                engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);

                MoveCommand? match = null;
                for (int i = 0; i < legalMoves.Count; i++)
                {
                    MoveCommand candidate = legalMoves[i];
                    if (candidate.StartPosition == from && candidate.EndPosition == to)
                    {
                        match = candidate;
                        break;
                    }
                }

                if (match == null)
                {
                    throw new InvalidOperationException(
                        $"Curated opening line {index}: move '{token}' is not legal - the authored line no longer matches the engine.");
                }

                resolver.Advance(board, match.Value);
            }

            return board;
        }
    }
}
