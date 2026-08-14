using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI.OpeningBook
{
    /// <summary>
    /// Decides whether a given difficulty tier is still allowed to answer from the opening book in
    /// the position it has been handed.
    ///
    /// This is deliberately separate from OpeningBookLookup, which answers a different question:
    /// the lookup knows what the book contains, while this knows whether the AI should be asking.
    /// Keeping them apart leaves the lookup a plain function of (book, position) and puts the
    /// difficulty rules in one place that can be tested without building a search or an agent.
    /// </summary>
    public static class OpeningBookPolicy
    {
        /// <summary>
        /// True if <paramref name="profile"/> should consult the book for <paramref name="board"/>.
        /// A profile that opts out of the book entirely, or one that has already played as far
        /// into the game as its repertoire is allowed to reach, searches instead.
        /// </summary>
        public static bool ShouldConsult(AIProfile profile, BoardState board)
        {
            if (!profile.UseOpeningBook) return false;
            if (board == null) return false;
            if (profile.OpeningBookDepthPlies <= 0) return true;

            // Read straight off the board rather than tracked by the agent, so taking a move back
            // moves the book boundary back with it. A Betrayal sequence records every one of its
            // steps there even though the turn only passes once, so the count runs slightly ahead
            // during one — which costs nothing, since the book declines outright while a Betrayal
            // is in progress and no book line contains one.
            return board.PliesPlayed < profile.OpeningBookDepthPlies;
        }
    }
}
