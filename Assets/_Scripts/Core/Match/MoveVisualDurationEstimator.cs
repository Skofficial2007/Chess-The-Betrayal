using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// Estimates a generous upper bound, in seconds, on how long the View layer's on-board
    /// animation for a given move will still be visibly playing. Lives in Core (rather than on
    /// BoardVisuals itself) so both View (which actually plays the animation) and Gameplay.Manager
    /// (which paces move requests against it — see MoveVisualPacingGate) can depend on the exact
    /// same numbers without Gameplay.Manager needing an assembly reference on View.
    ///
    /// Travel time is not guessed at here: it comes from MoveTravelTiming, the same source the
    /// animator glides against, so the two cannot drift. What is still hand-measured are the beats
    /// that happen once a piece has arrived — a strike, a castle's settle, a promotion's swap.
    /// Those are read off the animator's sequences and padded, and the padding is what lets the
    /// animator retune itself without this having to follow in lockstep.
    /// </summary>
    public static class MoveVisualDurationEstimator
    {
        /// <summary>
        /// Slack over every measured figure below, so a small retune on the animator's side cannot
        /// silently push a move past the window budgeted for it.
        /// </summary>
        private const float PaddingSeconds = 0.05f;

        /// <summary>
        /// The capture strike, measured from the wind-up to the last of the settle bob: the
        /// anticipation crouch, the leap, the impact squash, the hold at contact, the recovery
        /// overshoot and the bob. Not the run-up, which varies with distance and is added
        /// separately.
        ///
        /// An attacker that walks in folds its crouch into the end of that walk rather than playing
        /// it afterwards, so for those this is around a tenth of a second long. That is the right
        /// direction for a figure whose whole job is to be an upper bound.
        ///
        /// Taken at the heaviest a strike gets. The hold at contact stretches with the size of what
        /// was taken, so this is the figure for felling a king; every lighter capture comes in under
        /// it, which is the direction an upper bound is allowed to be wrong in.
        /// </summary>
        private const float CaptureStrikeSeconds = 0.89f;

        /// <summary>
        /// A victim that dies on its own — hop, shrink and glide to the death pile — rather than
        /// waiting to be stomped. Runs alongside whatever the attacker is doing, not after it.
        /// </summary>
        private const float GlideDeathSeconds = 0.34f;

        /// <summary>
        /// Collapsing the pawn away and growing its replacement back in, once the pawn has arrived.
        /// The travel that gets it there is counted separately, from the shared curve.
        /// </summary>
        private const float PromotionMorphSeconds = 0.34f;

        private const float CastlingSeconds = 0.45f;

        /// <summary>
        /// A move can match more than one shape (e.g. a capturing promotion) — callers only ever
        /// need to wait out the longest one, so this returns the max across every shape the move
        /// actually has, not a sum.
        /// </summary>
        public static float EstimateSeconds(MoveCommand move)
        {
            float tiles = MoveTravelTiming.TilesApart(
                move.StartPosition.x, move.StartPosition.y,
                move.EndPosition.x, move.EndPosition.y);

            float seconds = MoveTravelTiming.SecondsForTiles(tiles);

            if (move.IsCapture) seconds = Max(seconds, CaptureSeconds(move));
            if (move.IsCastling) seconds = Max(seconds, CastlingSeconds);
            if (move.IsPromotion) seconds = Max(seconds, PromotionSeconds(tiles));

            return seconds + PaddingSeconds;
        }

        /// <summary>
        /// How long the captured piece is still on screen, which depends on which death it plays.
        /// A victim the attacker lands on waits out the whole strike, including the run-up closing
        /// on it. One that dies on its own — en passant, or a capture that also promotes — glides
        /// off under its own steam and is gone well before that.
        /// </summary>
        private static float CaptureSeconds(MoveCommand move)
        {
            if (CaptureFate.Of(move) != CapturedPieceFate.CrushedByTheStamp) return GlideDeathSeconds;

            float runUpTiles = CaptureApproach.RunUpTiles(move.StartPosition, move.EndPosition, move.PieceType);
            if (runUpTiles <= 0f) return CaptureStrikeSeconds;

            return CaptureStrikeSeconds + MoveTravelTiming.SecondsForTiles(runUpTiles);
        }

        /// <summary>
        /// The pawn's walk onto the last rank plus the morph waiting for it there. A pawn promotes
        /// exactly one square, but the travel is taken from the same curve as any other glide
        /// rather than hard-coded, so retuning that curve carries the budget with it.
        /// </summary>
        private static float PromotionSeconds(float tiles)
        {
            return MoveTravelTiming.SecondsForTiles(tiles) + PromotionMorphSeconds;
        }

        private static float Max(float a, float b) => a > b ? a : b;
    }
}
