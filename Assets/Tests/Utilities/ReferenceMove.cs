using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// One deep search's answer for a single position — the "what would far more thinking conclude?"
    /// side of an agreement measurement.
    ///
    /// Carries <see cref="ZobristSchemeVersion"/> alongside the hash it was stored under because a
    /// cached answer is only meaningful while the hash still means the same thing. If the key
    /// scheme changes, a cached entry's hash silently starts describing a DIFFERENT position, and
    /// an oracle that answers about the wrong position is worse than no oracle at all: it reports
    /// confident agreement numbers that measure nothing. Stamping the scheme lets a stale entry be
    /// rejected loudly instead.
    /// </summary>
    public readonly struct ReferenceMove
    {
        /// <summary>Hash of the position this answer is about.</summary>
        public readonly ulong PositionHash;

        /// <summary>The key scheme the hash above was produced under.</summary>
        public readonly ulong ZobristSchemeVersion;

        /// <summary>The move the deep search chose.</summary>
        public readonly MoveCommand Move;

        /// <summary>The chosen move's score, from the moving side's point of view.</summary>
        public readonly int ScoreCp;

        /// <summary>The depth the deep search was asked for and, being depth-bound rather than
        /// clock-bound, actually completed.</summary>
        public readonly int Depth;

        /// <summary>How long the deep search took. Recorded because the practical question "can we
        /// afford a deeper reference?" is otherwise unanswerable without re-running everything.</summary>
        public readonly double ElapsedMs;

        public ReferenceMove(ulong positionHash, ulong zobristSchemeVersion, MoveCommand move,
            int scoreCp, int depth, double elapsedMs)
        {
            PositionHash = positionHash;
            ZobristSchemeVersion = zobristSchemeVersion;
            Move = move;
            ScoreCp = scoreCp;
            Depth = depth;
            ElapsedMs = elapsedMs;
        }

        /// <summary>
        /// Whether this answer can still be trusted for the given position. Both halves matter: the
        /// hash proves it is about THIS position, the scheme proves the hash was built the same way
        /// this run builds hashes.
        /// </summary>
        public bool IsValidFor(ulong positionHash, ulong zobristSchemeVersion) =>
            PositionHash == positionHash && ZobristSchemeVersion == zobristSchemeVersion;
    }
}
