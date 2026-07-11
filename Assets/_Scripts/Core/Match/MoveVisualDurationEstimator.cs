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
    /// These are hand-measured upper bounds on PrimeTweenPieceAnimator's private tween durations,
    /// not a mirror of them — that animator stays free to retune its own constants without this
    /// estimator needing to change in lockstep, as long as it stays within the padded budget below.
    /// </summary>
    public static class MoveVisualDurationEstimator
    {
        private const float QuietMoveSeconds = 0.3f;
        private const float CaptureSeconds = 0.55f;
        private const float CastlingSeconds = 0.45f;
        private const float PromotionSeconds = 0.5f;

        /// <summary>
        /// A move can match more than one shape (e.g. a capturing promotion) — callers only ever
        /// need to wait out the longest one, so this returns the max across every shape the move
        /// actually has, not a sum.
        /// </summary>
        public static float EstimateSeconds(MoveCommand move)
        {
            float seconds = QuietMoveSeconds;
            if (move.IsCapture) seconds = System.Math.Max(seconds, CaptureSeconds);
            if (move.IsCastling) seconds = System.Math.Max(seconds, CastlingSeconds);
            if (move.IsPromotion) seconds = System.Math.Max(seconds, PromotionSeconds);
            return seconds;
        }
    }
}
