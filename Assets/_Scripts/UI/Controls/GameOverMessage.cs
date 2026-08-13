using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.UI.Controls
{
    /// <summary>
    /// Turns a finished game into the line the player reads on the game-over panel.
    ///
    /// The wording is the only thing telling someone why the game stopped, and several of the
    /// endings look identical from the board — two kings shuffling reach a repetition, a fifty-move
    /// draw and a stalemate through positions a player cannot tell apart. Getting the wrong one is
    /// not a cosmetic slip: it is the game explaining itself incorrectly at the one moment it has to.
    ///
    /// Plain string building with no Unity types, so the panel's wording can be pinned by a test
    /// rather than checked by eye against a running scene.
    /// </summary>
    public static class GameOverMessage
    {
        /// <summary>
        /// Builds the headline. A win reads as a win whatever ended it; a draw has to name itself,
        /// because the reasons are what separate the endings a player cannot see.
        /// </summary>
        public static string Build(Team? winner, GameEndReason reason, bool byTimeout = false)
        {
            string prefix = byTimeout ? "Time Out!\n" : string.Empty;

            return winner switch
            {
                Team.White => $"{prefix}White Team Won!",
                Team.Black => $"{prefix}Black Team Won!",
                _          => DrawText(reason, byTimeout)
            };
        }

        private static string DrawText(GameEndReason reason, bool byTimeout)
        {
            // A drawn game that ran out of clock is a draw because neither side had the material to
            // mate with, so it says that rather than naming whatever the position happened to be.
            if (byTimeout) return "Time Out!\nDraw (Insufficient Material)";

            return reason switch
            {
                GameEndReason.Repetition    => "Draw by Repetition.",
                GameEndReason.FiftyMoveRule => "Draw. Fifty moves without a capture or a pawn move.",
                _                           => "Stalemate! Draw."
            };
        }
    }
}
