using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// How a match ended, in one line, for a report somebody reads later.
    ///
    /// Separate from GameOverMessage, which writes the same fact for the player on the game-over
    /// panel and is shaped for that job - it opens with "Time Out!" on its own line and speaks in
    /// exclamations. A report wants one flat sentence with no line break in the middle of it, so the
    /// two are deliberately not shared. What they must agree on is which ending happened, and that
    /// comes from the same payload rather than from either of them working it out again.
    /// </summary>
    public static class MatchResultLine
    {
        /// <summary>
        /// Reads the reason rather than the timeout flag, because the driver has already folded one
        /// into the other: any game that ran out of clock arrives here as Timeout whatever the
        /// position was. A drawn timeout is a draw for want of mating material, which is the only
        /// way a clock produces one.
        /// </summary>
        public static string Describe(Team? winner, GameEndReason reason)
        {
            if (winner.HasValue)
            {
                string how = reason switch
                {
                    GameEndReason.Timeout => "on time",
                    GameEndReason.Resignation => "by resignation",
                    _ => "by checkmate",
                };

                return $"{winner.Value} won {how}.";
            }

            return reason switch
            {
                GameEndReason.Timeout => "Draw on time, neither side with the material to mate.",
                GameEndReason.Repetition => "Draw by repetition.",
                GameEndReason.FiftyMoveRule => "Draw by the fifty-move rule.",
                _ => "Draw by stalemate.",
            };
        }
    }
}
