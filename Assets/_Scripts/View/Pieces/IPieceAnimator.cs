using System;
using UnityEngine;

namespace ChessTheBetrayal.View
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
        /// The rook's half of a castling move: an InOutCubic glide identical in feel to
        /// MoveTo(..., MoveStyle.Quiet), but delayed by startDelay seconds so BoardVisuals can
        /// stagger it slightly behind the king (the king leads, the rook tucks in behind it —
        /// see BoardVisuals.AnimateMove). Kept as its own seam rather than adding a startDelay
        /// parameter to every MoveTo overload, since castling is the only caller that needs one.
        /// Ends with a tiny settle bob (shared with the king's via BoardVisuals) once both pieces
        /// have arrived.
        /// </summary>
        void MoveToForCastle(Vector3 worldPos, float startDelay, Action onSettled = null);

        /// <summary>
        /// Plays a small (millimeter-scale) settle bob in place — the tail end of the castling
        /// choreography once a piece has already arrived at its destination. Not a standalone
        /// move; callers are expected to have already positioned the piece.
        /// </summary>
        void PlaySettleBob();

        /// <summary>
        /// The attacker's half of a capture "stamp": anticipation pull-back, a leap that clears the
        /// victim's head while swelling ~1.15x mid-air, a straight drop onto the target, a hard
        /// flat impact squash, and an overshoot recovery back to rest scale — a cartoon
        /// power-stomp rather than a plain slide. Fires onDescentStart the frame the downward leg
        /// of the leap begins (NOT at impact): the victim's cower-shrink (PlayStompedDeath) is
        /// timed to the same fall-duration constant, so starting it at descent guarantees the
        /// victim is already small when the attacker arrives — the two never overlap at full size.
        /// Fires onSettled once the ENTIRE stamp (impact, recover, settle bob) has finished — the
        /// moment BoardVisuals can safely start any animation that must play AFTER this piece's own
        /// capture reads as complete (e.g. a Betrayal Defection spin queued on the same piece — see
        /// BoardVisuals.SwapPieceTeam). See PrimeTweenPieceAnimator for the full timing breakdown.
        /// </summary>
        void PlayCaptureStamp(Vector3 worldPos, Action onDescentStart = null, Action onSettled = null);

        /// <summary>
        /// The victim's half of a capture "stamp", started at the attacker's DESCENT (not impact):
        /// cowers/shrinks under the falling piece for exactly the attacker's fall duration, then is
        /// slammed to a pancake and sunk into the tile the instant the attacker lands, then shrinks
        /// away to nothing. Calls onVanished once fully collapsed — the moment BoardVisuals should
        /// reposition it (scale/facing back up) at the death pile, mirroring the deferred-swap
        /// pattern PlayTransitionOut already uses for promotion.
        /// </summary>
        void PlayStompedDeath(Action onVanished);

        /// <summary>
        /// The en passant victim's death: since the attacker never visually lands on this piece's
        /// square (en passant captures on a different tile than the one the attacker ends up on),
        /// there's no impact to crush against — instead the piece plays its own small hop-and-shrink
        /// glide directly to graveyardWorldPos, arriving already at vanished scale. Calls onArrived
        /// once the glide completes — the moment BoardVisuals should restore its death-pile
        /// scale/facing there (mirroring PlayStompedDeath's onVanished pattern).
        /// </summary>
        void PlayEnPassantDeath(Vector3 graveyardWorldPos, Action onArrived);

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
        /// Rattles the piece side-to-side and settles back to its exact current position — the
        /// king's "I'm in check" cue, played the instant a move delivers check (see
        /// BoardVisuals.AnimateMove). Independent of any move/lift tween in flight; reads the
        /// piece's live position as its own rest point rather than assuming it's already settled,
        /// so it's safe to call in the same frame a board-move glide just started.
        /// </summary>
        void Shake();

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
        /// selected, plus a golden inverted-hull outline ring (see
        /// Custom/PieceSelectionOutline.shader) that pops on with the lift. The lift height and
        /// per-type feel (e.g. a King rising more than a Pawn) are owned here rather than by the
        /// caller, so BoardVisuals never has to know piece-type specifics to orchestrate a
        /// selection.
        /// </summary>
        void LiftSelect();

        /// <summary>
        /// Plays the "set down" animation: stops the idle bob and eases the piece back to the
        /// exact position it was lifted from, with no overshoot (a lift feels snappy; a landing
        /// feels gentle), while the selection outline ring shrinks away. Safe to call even if the
        /// piece was never lifted.
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
