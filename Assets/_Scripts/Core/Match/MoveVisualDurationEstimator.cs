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
        /// anticipation crouch, the leap, the impact squash, the recovery overshoot and the bob.
        /// Not the run-up, which varies with distance and is added separately.
        /// </summary>
        private const float CaptureStrikeSeconds = 0.79f;

        private const float CastlingSeconds = 0.45f;
        private const float PromotionSeconds = 0.5f;

        /// <summary>
        /// A move can match more than one shape (e.g. a capturing promotion) — callers only ever
        /// need to wait out the longest one, so this returns the max across every shape the move
        /// actually has, not a sum.
        /// </summary>
        public static float EstimateSeconds(MoveCommand move)
        {
            int squares = MoveTravelTiming.SquaresApart(
                move.StartPosition.x, move.StartPosition.y,
                move.EndPosition.x, move.EndPosition.y);

            float seconds = MoveTravelTiming.SecondsForSquares(squares);

            if (move.IsCapture) seconds = Max(seconds, CaptureSeconds(move));
            if (move.IsCastling) seconds = Max(seconds, CastlingSeconds);
            if (move.IsPromotion) seconds = Max(seconds, PromotionSeconds);

            return seconds + PaddingSeconds;
        }

        /// <summary>
        /// The strike, plus however long the attacker spends closing on its victim first. An
        /// attacker already beside its target strikes immediately and only owes the strike.
        /// </summary>
        private static float CaptureSeconds(MoveCommand move)
        {
            int runUp = CaptureApproach.RunUpSquares(move.StartPosition, move.EndPosition, move.PieceType);
            if (runUp == 0) return CaptureStrikeSeconds;

            return CaptureStrikeSeconds + MoveTravelTiming.ChargeSecondsForSquares(runUp);
        }

        private static float Max(float a, float b) => a > b ? a : b;
    }
}
