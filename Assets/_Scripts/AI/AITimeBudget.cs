using System;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// A search's wall-clock allowance, expressed as a pair rather than a single number: SoftMs is
    /// the target the search tries to land under, HardMs is the ceiling it must never cross no
    /// matter what it finds along the way. Keeping these as one deliberate value instead of a bare
    /// int is what lets a search stay a little past its target when the position looks unsettled —
    /// without that second number, "stay a bit longer to be sure" and "no cap at all" would be the
    /// same thing.
    /// </summary>
    public readonly struct AITimeBudget
    {
        public readonly int SoftMs;
        public readonly int HardMs;

        public AITimeBudget(int softMs, int hardMs)
        {
            if (softMs < 1)
                throw new ArgumentOutOfRangeException(nameof(softMs), softMs, "Soft time budget must be at least 1ms.");
            if (hardMs < softMs)
                throw new ArgumentOutOfRangeException(nameof(hardMs), hardMs, "Hard time budget must be >= the soft time budget.");

            SoftMs = softMs;
            HardMs = hardMs;
        }
    }
}
