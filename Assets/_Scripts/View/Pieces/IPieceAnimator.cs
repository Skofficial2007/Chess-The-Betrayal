using System;
using UnityEngine;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// The visual shape of a piece-swap transition (promotion, defection). Squash is an
    /// anticipation-style scale-down/up used when the swap should read as "this piece becomes a
    /// new piece." Spin is a 180-degree Y rotation used when the swap should read as "this piece
    /// turns around to reveal its new side" — defection keeps this one on purpose. PromotionMorph
    /// is the same squash scale tween as Squash, plus a dissolve/burning-edge shader effect (see
    /// Custom/PieceLitRimGlow.shader) blended in on top via PrimeTweenPieceAnimator, so promotion
    /// reads as both shrinking/growing AND dissolving/reforming at once.
    /// </summary>
    public enum PieceTransitionStyle
    {
        Squash,
        Spin,
        PromotionMorph
    }

    /// <summary>
    /// The visual feel of a board-move glide. Quiet is a plain slide; Capture adds a landing
    /// impact punch to sell contact; Knight arcs over the board (it "hops" rather than slides
    /// through occupied squares, matching how the piece actually moves); Promotion is a slower,
    /// punch-free glide since the morph itself (PlayTransitionOut/In) is the payoff beat.
    /// </summary>
    public enum MoveStyle
    {
        Quiet,
        Capture,
        Knight,
        Promotion
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
        /// Slides the piece toward worldPos with a specific move feel — see MoveStyle. This is the
        /// board-move entry point (AnimateMove); the plain MoveTo above stays for callers that
        /// don't carry move context (death-pile placement, selection snap-back).
        /// </summary>
        void MoveTo(Vector3 worldPos, MoveStyle style, bool force = false);

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
        /// Tweens the dissolve shader effect (see Custom/PieceLitRimGlow.shader) from its current
        /// value to targetAmount over duration seconds — 0 is fully intact, 1 is fully dissolved
        /// away. Used to blend a dissolve pass on top of the existing PromotionMorph squash tween.
        /// </summary>
        void DissolveTo(float targetAmount, float duration, System.Action onComplete = null);

        /// <summary>
        /// Instantly sets the dissolve amount with no tween — used to snap a freshly-spawned piece
        /// to fully-dissolved before its reform tween plays.
        /// </summary>
        void SetDissolveImmediate(float amount);

        /// <summary>
        /// Briefly flashes the rim glow in the given color and back off, cycles times — used for
        /// the king's "you are now in check" threat pulse on a Forced Save. Independent of
        /// SetHighlighted's persistent Betrayer glow (this restores whatever glow state was active
        /// before the flash once it finishes).
        /// </summary>
        void FlashGlow(Color color, float intensity, float flashDuration, int cycles);

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
