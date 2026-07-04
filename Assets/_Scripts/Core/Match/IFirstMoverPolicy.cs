using ChessTheBetrayal.Core.Utils;

namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// Decides which seat controls White at match start. Kept as an interface (DIP) so the
    /// randomized policy can be swapped for a fixed/deterministic one in tests without touching
    /// any caller.
    /// </summary>
    public interface IFirstMoverPolicy
    {
        SideAssignment Assign(IRandomSource rng);
    }
}
