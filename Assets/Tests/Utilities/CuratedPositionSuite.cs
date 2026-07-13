using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// A fixed set of early-middlegame starting positions for the strength-ordering harness. Each
    /// position is reached by replaying a short line of real opening moves through the actual
    /// engine from the standard starting position — never hand-placed piece by piece, so every
    /// position in the suite is guaranteed reachable and legal by construction.
    ///
    /// A tournament run always plays each position from both colors (see MatchSimulator callers),
    /// which cancels the first-move advantage baked into any single position — the position count
    /// times two is the tournament's real sample size for a deterministic (zero-blunder,
    /// zero-tie-break-window) profile pair, since a single fixed position played once is otherwise
    /// not "N independent games" at all.
    /// </summary>
    public static class CuratedPositionSuite
    {
        /// <summary>
        /// Each line is coordinate notation, one token per ply, space-separated — the same format
        /// AI/OpeningBook/Data/openings.book.txt uses. Kept short (early-middlegame, not deep
        /// theory) so the harness spends its search budget on the actual test, not replaying moves.
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

        /// <summary>
        /// Replays position <paramref name="index"/>'s line from the standard start and returns the
        /// resulting board, Betrayal enabled and turn/Zobrist state fully consistent. Throws if the
        /// authored line is somehow no longer legal against the current engine — the same
        /// fail-loud contract the opening book compiler uses, since a silently-skipped bad line
        /// would quietly shrink the suite's sample size.
        /// </summary>
        public static BoardState Build(int index)
        {
            string[] tokens = Lines[index].Split(' ');

            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.BetrayalRightAvailable = true;
            board.ComputeFullZobristHash();

            var engine = new ChessEngineAdapter();
            var resolver = new TurnResolver();
            var legalMoves = new List<MoveCommand>(64);

            foreach (string token in tokens)
            {
                Vector2Int from = TestBoardSetupUtility.AlgebraicToVector(token.Substring(0, 2));
                Vector2Int to = TestBoardSetupUtility.AlgebraicToVector(token.Substring(2, 2));

                legalMoves.Clear();
                engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);

                MoveCommand? match = null;
                for (int i = 0; i < legalMoves.Count; i++)
                {
                    if (legalMoves[i].StartPosition == from && legalMoves[i].EndPosition == to)
                    {
                        match = legalMoves[i];
                        break;
                    }
                }

                if (match == null)
                {
                    throw new System.InvalidOperationException(
                        $"CuratedPositionSuite position {index}: move '{token}' is not legal — the authored line no longer matches the engine.");
                }

                resolver.Advance(board, match.Value);
            }

            return board;
        }
    }
}
