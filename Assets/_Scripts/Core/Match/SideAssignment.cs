namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// Identifies a seat at the table, independent of which chess color that seat controls.
    /// PlayerA/PlayerB rather than "player/opponent" so the same struct works for local hot-seat
    /// play (both seats are the device) and multiplayer (each seat is a distinct connection).
    /// </summary>
    public enum Seat
    {
        PlayerA,
        PlayerB
    }

    /// <summary>
    /// The result of deciding which seat controls White and which controls Black for a match.
    /// White always moves first (orthodox chess rules are untouched) — only the seat-to-color
    /// mapping is randomized.
    /// </summary>
    public readonly struct SideAssignment
    {
        public readonly Seat White;
        public readonly Seat Black;

        public SideAssignment(Seat white, Seat black)
        {
            White = white;
            Black = black;
        }
    }
}
