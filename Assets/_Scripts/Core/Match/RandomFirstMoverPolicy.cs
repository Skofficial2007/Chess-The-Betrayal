using ChessTheBetrayal.Core.Utils;

namespace ChessTheBetrayal.Core.Match
{
    /// Decides which seat controls White at match start.
    /// White always moves first (orthodox); only seat→color mapping is randomized.
    /// Deterministic given IRandomSource, so server and clients agree in multiplayer.
    public sealed class RandomFirstMoverPolicy : IFirstMoverPolicy
    {
        public SideAssignment Assign(IRandomSource rng)
        {
            return rng.NextBool()
                ? new SideAssignment(white: Seat.PlayerA, black: Seat.PlayerB)
                : new SideAssignment(white: Seat.PlayerB, black: Seat.PlayerA);
        }
    }
}
