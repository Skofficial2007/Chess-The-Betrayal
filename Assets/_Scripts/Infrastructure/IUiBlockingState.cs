namespace ChessTheBetrayal.Infrastructure
{
    /// <summary>
    /// The one query input scripts need from the UI layer: whether a modal panel is currently
    /// covering the board (mode select, promotion, game over, etc.), so pointer input should be
    /// ignored. Narrower than depending on the manager that implements it - a consumer that only
    /// needs this single check shouldn't also gain compile-time visibility into panel wiring,
    /// event channels, or every other responsibility that manager carries.
    /// </summary>
    public interface IUiBlockingState
    {
        /// <summary>True while a modal UI panel is open and pointer input should be ignored.</summary>
        bool IsUIBlocking();

        /// <summary>
        /// Names what is currently swallowing taps, or null while the board is live. For saying so
        /// in a log and nothing else: every one of these looks the same to a player - a board that
        /// stopped responding - so without the name the difference has to be guessed at from a
        /// description written after the fact.
        /// </summary>
        string DescribeBlocking();
    }
}
