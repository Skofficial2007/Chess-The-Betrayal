using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.View
{
    /// <summary>What becomes of the piece a move takes.</summary>
    public enum CapturedPieceFate
    {
        /// <summary>Nothing was taken.</summary>
        NothingCaptured,

        /// <summary>
        /// Dies on its own, straight away: a hop-and-shrink glide to the death pile. For every
        /// capture where the attacker does not end up standing on this piece as itself.
        /// </summary>
        DiesOnItsOwn,

        /// <summary>
        /// Waits to be crushed. The attacker is about to leap onto this square, so the death is
        /// held back and timed against the landing.
        /// </summary>
        CrushedByTheStamp
    }

    /// <summary>
    /// Which death a captured piece plays, decided from the move alone.
    ///
    /// This used to be settled halfway down the branch that animates the *attacker*, which meant
    /// the attacker's shape silently decided the victim's fate — and a promotion, whose branch runs
    /// before the stamp's, walked off without burying anything. The captured piece was left standing
    /// on the board belonging to no collection at all: not on the board, not in the death pile, and
    /// so not even reachable by the teardown that clears a match. Deciding it here, once, from the
    /// move itself, is what stops a future branch doing the same thing.
    /// </summary>
    public static class CaptureFate
    {
        public static CapturedPieceFate Of(MoveCommand move)
        {
            if (!move.IsCapture) return CapturedPieceFate.NothingCaptured;

            // En passant takes a piece standing on a different square from the one the attacker
            // lands on, so there is nothing underneath to crush.
            if (move.IsEnPassant) return CapturedPieceFate.DiesOnItsOwn;

            // A promotion replaces the attacker with a different piece the moment it arrives, so
            // the piece that would do the stomping no longer exists to do it. The victim goes
            // first and clears the square the promoted piece appears on.
            if (move.IsPromotion) return CapturedPieceFate.DiesOnItsOwn;

            return CapturedPieceFate.CrushedByTheStamp;
        }
    }
}
