namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// The one query input scripts need from the UI layer: whether a modal panel is currently
    /// covering the board (mode select, promotion, game over, etc.), so pointer input should be
    /// ignored. Narrower than depending on the concrete <see cref="UIManager"/> — a consumer that
    /// only needs this single check shouldn't also gain compile-time visibility into panel wiring,
    /// event channels, or every other UIManager responsibility.
    /// </summary>
    public interface IUiBlockingState
    {
        /// <summary>True while a modal UI panel is open and pointer input should be ignored.</summary>
        bool IsUIBlocking();
    }
}
