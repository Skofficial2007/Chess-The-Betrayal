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
    }
}
