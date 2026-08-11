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
    /// The visual feel of a board-move glide. Quiet is a plain slide; Knight arcs over the board (it
    /// "hops" rather than slides through occupied squares, matching how the piece actually moves);
    /// Promotion is a slower, punch-free glide since the morph itself (PlayTransitionOut/In) is the
    /// payoff beat.
    ///
    /// Capture adds a landing impact punch to sell contact, but it is no longer how most captures
    /// look: a piece landing on its victim plays the far heavier stamp instead (PlayCaptureStamp).
    /// What is left for this style is en passant, where the attacker lands beside its victim rather
    /// than on it, and a takeback, where the victim has already gone and there is nothing to land on.
    ///
    /// Quiet and Capture both follow the distance covered. Capture used to hold a fixed duration
    /// whatever the distance, which was harmless while it only ever meant en passant — but a
    /// takeback also comes through here, and taking back a rook's capture from across the board sent
    /// it home seven tiles in a fifth of a second. Knight and Promotion keep fixed durations because
    /// each covers a known distance by definition.
    /// </summary>
    public enum MoveStyle
    {
        Quiet,
        Capture,
        Knight,
        Promotion
    }

    /// <summary>
    /// The travel leg in front of a capture, for an attacker that has ground to make up before it
    /// can strike. Default means none: the victim is already next door, which is every pawn and
    /// king capture, and the strike plays from where the piece stands.
    /// </summary>
    public readonly struct CaptureRunUp
    {
        /// <summary>Where the attacker strikes from — the square beside its victim.</summary>
        public readonly Vector3 LaunchFrom;

        /// <summary>
        /// Ground between the attacker and that square in tile widths, so the walk can be paced
        /// against what it actually covers. Tiles rather than squares because a diagonal approach
        /// is half again as long as a straight one of the same square count.
        /// </summary>
        public readonly float TilesToCover;

        public readonly bool HasGroundToCover;

        public CaptureRunUp(Vector3 launchFrom, float tilesToCover)
        {
            LaunchFrom = launchFrom;
            TilesToCover = tilesToCover;
            HasGroundToCover = true;
        }
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
        ///
        /// tilesTravelled is the ground the move covers, measured in tile widths, so a glide can be
        /// paced against what it has to make up instead of every move taking the same time whatever
        /// its length. Tiles rather than squares because a diagonal step is 1.414 tiles of real
        /// floor: pacing it as one square makes every diagonal the faster move. Callers that aren't
        /// moving a piece across the board (a promotion swap in place, a snap-back) can leave it
        /// alone.
        /// </summary>
        void MoveTo(Vector3 worldPos, MoveStyle style, float tilesTravelled = 1f, bool force = false);

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
        /// Walks a promoting pawn onto the last rank and reports when it gets there, so the morph
        /// that replaces it can wait until it has been seen arriving. A pawn that is already
        /// standing on the square reports immediately rather than tweening nowhere — that is the
        /// human's pawn, which walks across while the promotion prompt is still open.
        /// </summary>
        void PlayPromotionApproach(Vector3 worldPos, float tilesTravelled, Action onArrived);

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
        /// power-stomp rather than a plain slide.
        ///
        /// Pass a runUp when the attacker is not already beside its victim (see CaptureApproach):
        /// the piece glides in first and the pounce plays from the square next door, so the leap
        /// stays the short, close-quarters strike it is built as instead of being stretched across
        /// however much board lay between them. Fires onDescentStart the frame the downward leg
        /// of the leap begins (NOT at impact): the victim's cower-shrink (PlayStompedDeath) is
        /// timed to the same fall-duration constant, so starting it at descent guarantees the
        /// victim is already small when the attacker arrives — the two never overlap at full size.
        /// Fires onSettled once the ENTIRE stamp (impact, recover, settle bob) has finished — the
        /// moment BoardVisuals can safely start any animation that must play AFTER this piece's own
        /// capture reads as complete (e.g. a Betrayal Defection spin queued on the same piece — see
        /// BoardVisuals.SwapPieceTeam). See PrimeTweenPieceAnimator for the full timing breakdown.
        ///
        /// victimHeft says how big the thing being taken is, 0 for the smallest piece on the board
        /// and 1 for the tallest, so that felling a queen costs more effort and lands harder than
        /// swatting a pawn. Zero plays exactly what a capture has always played, which is why the
        /// pawn case cannot be changed by this.
        /// </summary>
        void PlayCaptureStamp(Vector3 worldPos, CaptureRunUp runUp = default, float victimHeft = 0f, Action onDescentStart = null, Action onImpact = null, Action onSettled = null);

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
        /// The flinch of a piece watching something cross the board to take it: it leans away along
        /// shoveDirection for seconds, then straightens.
        ///
        /// Only for a capture the attacker has to walk into. While that walk happens the piece being
        /// taken is the one thing on screen that nothing is happening to, which reads as it not
        /// having noticed. A capture struck from next door has no such gap and plays nothing here.
        ///
        /// Ends before the attacker lands, so it neither overlaps nor has to be unwound by the
        /// stomp that follows (PlayStompedDeath) — the piece is upright again by the time it is
        /// crushed, which is the pose that beat was built against.
        /// </summary>
        void PlayBrace(Vector3 shoveDirection, float seconds);

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
        /// A captured piece coming back to the board because its capture was taken back: the same
        /// hop-and-glide as the death above, run the other way — travelling from the death pile to
        /// boardWorldPos while growing from its shrunken graveyard size back to restScale, arriving
        /// with a small overshoot like every other landing. Calls onArrived once it is home, the
        /// moment BoardVisuals should treat it as standing on the board again.
        ///
        /// Deliberately not the capture stamp reversed: nothing is lifting this piece back up, and
        /// a crush played backwards reads as the victim inflating rather than returning.
        /// </summary>
        void PlayGraveyardReturn(Vector3 boardWorldPos, Vector3 restScale, Action onArrived);

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

        /// <summary>
        /// Immediately stops every tween this animator owns — move, scale, shake, outline,
        /// dissolve, transition, stamp, lift/bob — with no completion callbacks and no attempt to
        /// restore the transform. Callers must invoke this before destroying the piece's
        /// GameObject: PrimeTween keeps driving a live tween's onValueChange callback for the
        /// remainder of the frame even after Destroy() is called (destruction is deferred to
        /// end-of-frame), so a Transform write from an in-flight tween can hit an already-destroyed
        /// object and throw MissingReferenceException.
        /// </summary>
        void StopAllAnimations();
    }
}
