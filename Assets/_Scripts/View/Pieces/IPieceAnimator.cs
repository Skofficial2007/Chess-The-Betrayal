using System;
using UnityEngine;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// The visual shape of a piece-swap transition (promotion, defection). Squash is an
    /// anticipation-style scale-down/up used when the swap should read as "this piece becomes a
    /// new piece." Spin is a 180-degree Y rotation used when the swap should read as "this piece
    /// turns around to reveal its new side."
    /// </summary>
    public enum PieceTransitionStyle
    {
        Squash,
        Spin
    }

    /// <summary>
    /// Owns how a single piece's transform animates in response to state changes ChessPiece is
    /// told about. Kept as an interface (DIP) so BoardVisuals can keep orchestrating what happens
    /// to a piece without knowing or caring how it's animated — and so AI self-play / headless
    /// tests can swap in an instant, tween-free implementation instead of dragging PrimeTween
    /// (and real frame time) into a hot search loop.
    /// </summary>
    public interface IPieceAnimator
    {
        /// <summary>
        /// Slides the piece toward worldPos. force = true snaps instantly with no interpolation
        /// (used for illegal-move revert and optimistic promotion-square snapping, where a visible
        /// tween would fight with what the player just did).
        /// </summary>
        void MoveTo(Vector3 worldPos, bool force = false);

        /// <summary>
        /// Scales the piece toward scale. force = true snaps instantly with no interpolation
        /// (used for initial spawn sizing).
        /// </summary>
        void ScaleTo(Vector3 scale, bool force = false);

        /// <summary>
        /// Instantly turns the piece to face lookDirection. Not currently tweened — kept as its
        /// own seam method so a future pass can animate it without touching BoardVisuals.
        /// </summary>
        void FaceDirection(Vector3 lookDirection);

        /// <summary>
        /// Toggles the "Betrayer" glow. Instant (a material property, not a transform), but routed
        /// through the seam so BoardVisuals never touches a Renderer directly.
        /// </summary>
        void SetHighlighted(bool active);

        /// <summary>
        /// Plays the "vanish" half of a piece-swap transition (promotion or defection) on the
        /// outgoing piece, then invokes onComplete — the moment BoardVisuals should Destroy this
        /// GameObject and spawn its replacement. Callers must not assume onComplete fires on the
        /// same frame; it's driven by a tween.
        /// </summary>
        void PlayTransitionOut(PieceTransitionStyle style, Action onComplete);

        /// <summary>
        /// Plays the "reveal" half of a piece-swap transition on a freshly-spawned piece — the
        /// counterpart to PlayTransitionOut, called on the new GameObject right after spawning it
        /// at the same square.
        /// </summary>
        void PlayTransitionIn(PieceTransitionStyle style);

        /// <summary>
        /// Plays the tap-to-select "pick up" animation: an anticipatory squash, then a rise with a
        /// slight overshoot, settling into a subtle idle bob for as long as the piece stays
        /// selected. The lift height and per-type feel (e.g. a King rising more than a Pawn) are
        /// owned here rather than by the caller, so BoardVisuals never has to know piece-type
        /// specifics to orchestrate a selection.
        /// </summary>
        void LiftSelect();

        /// <summary>
        /// Plays the "set down" animation: stops the idle bob and eases the piece back to the
        /// exact position it was lifted from, with no overshoot (a lift feels snappy; a landing
        /// feels gentle). Safe to call even if the piece was never lifted.
        /// </summary>
        void LowerDeselect();

        /// <summary>
        /// Immediately stops any lift/bob tweens with no landing animation — for use when the
        /// piece itself is about to be destroyed (captured while selected) and there is no "down"
        /// left to ease into. LowerDeselect is for the normal deselect path; this is for teardown.
        /// </summary>
        void CancelSelectionAnimation();
    }
}
