using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.Rendering;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Match;

namespace ChessTheBetrayal.View.Pieces
{
    /// <summary>
    /// The animations that move a piece around the board, and so the ones that have to take turns.
    ///
    /// They all set the piece's position outright rather than nudging it, so any two running
    /// together are overwriting each other every frame and the piece ends up wherever the one that
    /// finished last left it. Naming them makes "only one of these at a time" something that can be
    /// stated and checked instead of remembered.
    /// </summary>
    internal enum PositionWriter
    {
        /// <summary>Nothing is moving the piece.</summary>
        None = 0,

        /// <summary>A board move, including a knight's hop and a snap back to where it started.</summary>
        Move,

        /// <summary>The rook's half of a castle, which trails the king by a beat.</summary>
        Castle,

        /// <summary>A pawn walking onto the last rank before it becomes anything.</summary>
        Promotion,

        /// <summary>The run-up, leap and landing of a capture.</summary>
        Stamp,

        /// <summary>A piece on its way to its side's pile, or coming back off it.</summary>
        Death,

        /// <summary>The piece is in the player's hand: raised, and drifting while it is held.</summary>
        Lift,

        /// <summary>The piece being set back down on its square after the player let go of it.</summary>
        Lower,

        /// <summary>A king rattling where it stands because it is in check.</summary>
        Shake,
    }

    /// <summary>
    /// Real, tweened IPieceAnimator used for human play. Animates a single piece's Transform via
    /// PrimeTween — chosen over a hand-rolled per-frame Lerp because PrimeTween safely no-ops
    /// against a target destroyed mid-tween (pieces get Destroy()d for capture, promotion, and
    /// defection while a move or lift tween could still be running).
    ///
    /// One instance is owned per ChessPiece (see ChessPiece.Awake) — it is not shared, so every
    /// Tween/Sequence field below belongs to exactly one piece's Transform.
    /// </summary>
    public sealed class PrimeTweenPieceAnimator : IPieceAnimator
    {
        // Board moves and lifts are snappy on purpose — these are quick slides, not the main event.
        private const float MoveDuration = 1f / 12f;
        private const float ScaleDuration = 1f / 12f;
        private static readonly Ease MoveEase = Ease.OutQuad;
        private static readonly Ease ScaleEase = Ease.OutQuad;

        // Per-style board-move feel. Quiet and Capture can each cross any distance, so their
        // durations come from MoveTravelTiming rather than a constant — a king stepping one square
        // and a rook crossing the board used to take the same time, which made the rook read as a
        // jump cut. Knight and Promotion cover a known distance by definition (every knight move is
        // the same L, a promotion is a step onto the back rank), so a duration tuned by eye for the
        // little extra room an arc needs stays a fixed value.
        private const float KnightMoveDuration = 0.26f;
        private const float PromotionMoveDuration = 0.28f;

        // Public so MoveTravelTiming's GlideEasePeakFactor can be checked against the easing it
        // claims to describe — the two together are what bound how fast a glide is allowed to get,
        // and a duration alone says nothing about that. Sine rather than cubic: both ease in and
        // out identically to the eye, but a cubic one runs its middle at three times its average
        // speed against a sine's 1.57, and the middle is where a long move breaks up into stills.
        public static readonly Ease BoardGlideEase = Ease.InOutSine;

        // Castling's rook glide: its own (slightly shorter) duration rather than reusing
        // QuietMoveDuration, so a rook that starts CastleStartDelay seconds after the king still
        // arrives at essentially the same moment — the king leads, the rook tucks in right behind
        // it, rather than visibly trailing. Same BoardGlideEase as every other board glide, per the
        // "travel = weight" easing vocabulary.
        private const float CastleRookMoveDuration = 0.24f;

        // Public: BoardVisuals needs this same value to know how long to wait before calling
        // PlayCastleMove on the rook — the king and rook are separate ChessPiece/animator
        // instances, so the stagger has to be driven from the one place (BoardVisuals) that
        // orchestrates both, rather than being baked invisibly into two independent calls.
        public const float CastleRookStartDelay = 0.06f;

        // A tiny settle bob — a single up/down tick, not the selection bob's infinite loop — that
        // both king and rook play once they've arrived, so castling reads as "two pieces settling
        // into place together" rather than sliding stops dead on arrival.
        private const float SettleBobHeight = 0.002f;
        private const float SettleBobDuration = 0.1f;

        private const float CapturePunchDuration = 0.12f;
        private const float CapturePunchScale = 1.12f;

        // The capture "stamp" — a cartoon power-stomp. The staging rule (learned the hard way,
        // twice): the attacker and victim must never overlap at full size, from any camera angle.
        // Two separate tweens for XZ-travel and Y-height (even carefully timed with Chain/Group)
        // can still let horizontal distance get ahead of vertical clearance — e.g. an eased-out XZ
        // tween covers most of its ground early while a separately-timed Y-rise is still climbing,
        // so for a window the attacker is hovering low and close/over the victim's tile at the same
        // time. Instead, a single 0->1 driver tween computes both XZ (lerp) and Y (a true parabola,
        // 4h*t*(1-t)) from the exact same t every frame, so "how far across" and "how high up" are
        // physically coupled and can never drift apart. Beats:
        //   1. Anticipation (pull back + crouch): a held breath before the pounce. No travel yet.
        //      An attacker that walked in doesn't play this as its own beat — see below.
        //   2. Leap: one continuous parabolic arc from start tile to landing tile, peaking well
        //      above the victim's head. Growing to 1.15x mid-air on the way up (jumping things get
        //      bigger, per every good cartoon), landing still oversized.
        //   3. Descent: onDescentStart fires at the arc's peak (t=0.5) — the earliest moment that
        //      still reads as "the attacker is now falling toward you" — so the victim's
        //      cower-shrink has the entire second half of the arc (not just a short fall leg) to
        //      get out of the way before contact.
        //   4. Impact: flatten hard against the tile, hold there for a beat, then a big springy
        //      overshoot back to rest scale — the "settling back to normal size" that closes the
        //      arc — and a settle bob.
        private const float StampAnticipationDuration = 0.09f;
        private const float StampAnticipationScaleFactor = 0.78f;
        private const float StampAnticipationPullBack = 0.12f;
        private const float StampAnticipationCrouchDrop = 0.03f;
        private static readonly Ease StampAnticipationEase = Ease.OutQuad;

        // Public so BoardVisuals/PlayStompedDeath can read the same total duration and the exact
        // fraction (half) at which onDescentStart fires, so the victim's cower window always matches
        // the attacker's actual remaining airtime rather than duplicating the numbers by hand.
        public const float StampLeapDuration = 0.3f;
        public const float StampDescentStartFraction = 0.5f;
        // Peak height above the higher of start/land Y.
        //
        // This does NOT clear a tall piece's head, whatever it may once have been chosen to do. The
        // pieces stand between 1.2 and 2.6 units on this board — a King is 2.6 — so a peak 1.3 above
        // the tile passes through the band a King, Queen or Bishop occupies, and only actually
        // clears a Pawn. What keeps the two from reading as interpenetrating is that the arc peaks
        // over the boundary between the two squares rather than over the victim, that the victim is
        // already shrinking by the time the attacker is above it, and that every piece tapers to
        // almost nothing at the top. It works, but it works by staging rather than by clearance, and
        // anything that changes the descent timing has to keep that in mind.
        private const float StampLeapHeight = 1.3f;
        // Mid-air growth: the attacker swells on the way up and stays swollen through the landing,
        // only releasing back to rest scale in the post-impact recover — "big things fall hard."
        private const float StampAirborneScaleFactor = 1.15f;
        // Slight overshoot on the mid-air growth so the swell pops rather than inflates linearly.
        // Growth spans the whole rise half (0 -> 0.5) of the single leap driver.
        private const float StampAirborneGrowOvershoot = 1.3f;

        // The share of a walk-in that is already crouching. A piece that has just charged across the
        // board should not stop dead and then step backwards to wind up — it has committed, and the
        // pause reads as hesitation. Instead the last part of the walk decelerates straight into the
        // loaded pose, so the charge and the crouch are one motion with no stop and no reversal in
        // between. An attacker already standing beside its victim keeps the separate wind-up, which
        // is what a close-quarters pounce wants and is the case that already read well.
        private const float StampRunUpCrouchShare = 0.35f;

        // A charging piece leans into the run, and the piece it is charging leans away from it.
        //
        // Beyond the plain fact that something moving under its own power tips forward, the lean is
        // what lets the eye read speed on a shape that cannot smear. A drawn character stretches
        // along its path when it moves quickly; a chess piece is a rigid silhouette that would have
        // to be scaled along an arbitrary world direction to do the same, which a single localScale
        // cannot express. Tipping does that job instead, and costs nothing but a rotation.
        //
        // The victim's half is smaller and answers a different problem: while the attacker crosses
        // the board, the piece being taken is the only thing on screen with nothing happening to it.
        //
        // These are large for a tilt because the window is small. Most real captures are struck from
        // two or three squares out, which is a third of a second of walking — an angle that would be
        // plenty on a piece standing still is gone before the eye finds it on a piece crossing the
        // board. Read them against the board's own tilt rather than against upright.
        private const float ChargeLeanDegrees = 24f;
        private const float BraceLeanDegrees = 15f;

        // How much of the strike answers to the size of what is being taken. Heft is 0 for the
        // smallest piece on the board and 1 for the tallest, and everything below is written so that
        // heft 0 plays exactly what a capture has always played — taking a pawn is the case that
        // already read well, so it is the floor, and weight only ever adds.
        //
        // Felling something big should cost more effort and land harder than swatting a pawn, and
        // the leap in particular has a second job: the arc is too low to clear a tall piece (see
        // StampLeapHeight), so giving the tall victims a higher one buys back some of the room the
        // constant never had.
        private const float StampLeapHeightPerHeft = 0.9f;
        private const float StampImpactHoldPerHeft = 0.05f;
        private const float StampRecoverOvershootPerHeft = 0.5f;

        private const float StampImpactSquashDuration = 0.06f;
        // A beat with nothing moving at all, at maximum squash. The victim already froze here (see
        // StampVictimHoldDuration) while the attacker sprang straight back up, so the two disagreed
        // about the moment they collided; holding both makes the contact land as one event.
        private const float StampImpactHoldDuration = 0.05f;
        private const float StampImpactRecoverDuration = 0.24f;
        private const float StampImpactWidthFactor = 1.45f;
        private const float StampImpactHeightFactor = 0.45f;
        private const float StampRecoverOvershoot = 1.7f;

        // The victim's death under the stamp, in three stages:
        //   1. Cower (spans from onDescentStart to the attacker's landing — half of
        //      StampLeapDuration): shrinks toward the tile as the falling piece closes in — this,
        //      combined with the attacker's own height clearance above, is what guarantees the two
        //      are never both at full size at the same time. Accelerating InQuad reads as mounting
        //      dread rather than a linear deflate.
        //   2. Crush (at contact): the remaining piece slams to a near-paper pancake and sinks
        //      into the tile, exactly as the attacker's own impact squash plays.
        //   3. Vanish: the pancake shrinks away to nothing under the attacker.
        private const float StampVictimCowerScaleFactor = 0.35f;
        private static readonly Ease StampVictimCowerEase = Ease.InQuad;
        private const float StampVictimSquashDuration = 0.07f;
        private const float StampVictimHoldDuration = 0.05f;
        private const float StampVictimVanishDuration = 0.16f;
        private const float StampVictimWidthFactor = 1.35f;
        private const float StampVictimHeightFactor = 0.05f;
        private const float StampVictimSinkDepth = 0.09f;

        // En passant's death: unlike a direct stamp, the attacker never visually touches this
        // piece, so it plays its own "swept away" beat instead — a quick hop-and-shrink glide
        // straight to its team's graveyard slot, matching the weight of every other board glide
        // (InOutCubic) rather than the old instant teleport.
        // The journey between the board and a side's pile, both ways. Paced off the same curve as
        // any other travel rather than a fixed duration, because the pile sits off the edge of the
        // board and the distance to it is not small: the far corner of the board to the far end of
        // a full pile is fifteen tiles, and a fixed third of a second across that is two tiles
        // between one drawn frame and the next — the piece does not glide, it streaks. It reached
        // this far because a corpse leaving the board is easy not to look at, but the same journey
        // run backwards is a piece arriving on it, which is very much looked at.
        private const float EnPassantDeathHopHeight = 0.22f;

        // How long the piece takes to shrink away, kept separate from how long it takes to get
        // there. Those used to be one number, which is what made the journey a fixed duration in the
        // first place. They answer different questions: the shrink is how long a captured piece is
        // still worth looking at, and the move pacing waits on exactly that; the travel is just a
        // corpse being carried off, and stretching it to a readable speed costs nothing because by
        // then there is almost nothing left to see.
        private const float DeathVanishSeconds = 0.34f;
        private static readonly Ease EnPassantDeathHopEase = Ease.OutQuad;
        private static readonly Ease EnPassantDeathScaleEase = Ease.InQuad;

        // The arc's own ease now comes from whatever style called MoveToInternal (BoardGlideEase,
        // the same as every other board move) via ApplyKnightArc's single Tween.Custom driver — a
        // separate arc-specific ease would fight the "travel = weight" vocabulary.
        private const float KnightArcHeight = 0.35f;

        // Promotion/defection transition timings: "out" is a quick anticipation beat, "in" is the
        // slightly longer payoff so the swap reads as deliberate rather than a glitch.
        private const float SquashOutDuration = 0.12f;
        private const float SquashInDuration = 0.2f;
        private const float SpinOutDuration = 0.15f;
        private const float SpinInDuration = 0.2f;

        // Promotion morph punch: a small extra hop/overshoot layered onto the existing squash-in so
        // the promoted piece feels like it "pops into existence" rather than just scaling up.
        private const float PromotionPopHopHeight = 0.12f;
        private const float PromotionPopHopDuration = 0.22f;

        // Scale can't tween to exactly zero (PrimeTween/Unity would treat that as degenerate), so
        // "vanished" is approximated as a small positive scale instead.
        private const float VanishedScale = 0.05f;

        // Selection lift: a quick anticipatory squash, then a rise-with-overshoot that settles at
        // the same time the squash recovers, followed by a subtle infinite idle bob. Durations and
        // strengths are tuned so the whole pickup reads in well under half a second — "a piece was
        // just picked up," not "a piece is floating."
        private const float LiftSquashDuration = 0.06f;
        private const float LiftRiseDuration = 0.18f;
        private const float LiftLowerDuration = 0.12f;
        private const float LiftOvershootStrength = 1.1f;
        private const float LiftSquashWidthFactor = 1.05f;
        private const float LiftSquashHeightFactor = 0.92f;
        private const float BobAmplitude = 0.0025f;
        private const float BobDuration = 1.2f;

        // Default lift height for every piece type. Empty by design: no per-type tuning has been
        // decided yet, but the lookup exists so adding e.g. a King-rises-higher-than-a-Pawn feel
        // later is a one-line addition here, not a re-plumbing of BoardVisuals/ChessPiece.
        private const float DefaultLiftHeight = 0.3f;
        private static readonly Dictionary<ChessPieceType, float> LiftHeightByType = new Dictionary<ChessPieceType, float>();

        // Betrayer denoter: a fresnel-driven rim glow (see Custom/PieceLitRimGlow.shader) rather
        // than a flat _EmissionColor add. Plain additive emission competes with each piece's own
        // lit albedo — the same red reads as washed-out pink on the bright piece and a
        // detail-erasing flat red on the dark one. The rim shader instead adds glow only at
        // grazing/silhouette angles, on top of full normal PBR shading, so intensity reads the same
        // regardless of the piece's baked color and the sculpted mesh detail stays visible.
        private static readonly Color BetrayerGlowColor = Color.red;
        private const float BetrayerGlowIntensity = 1.5f;
        private static readonly int RimGlowColorId = Shader.PropertyToID("_RimGlowColor");
        private static readonly int RimGlowIntensityId = Shader.PropertyToID("_RimGlowIntensity");

        // Selection outline — an inverted-hull ring (see Custom/PieceSelectionOutline.shader)
        // around the piece the player currently has picked up. Chosen over reusing the rim glow
        // because the rim is already spoken for as the Betrayer/threat denoter; selection needs
        // its own unambiguous mark. The hull is a runtime child renderer sharing the piece's mesh
        // (one extra draw call, only while something is selected), and only its width is animated
        // here — color/width/pulse are authored on the material so they're tunable in the inspector
        // without touching code. Injected via SetSelectionOutlineMaterial (see ChessPiece) rather
        // than Resources.Load: the material now lives in Assets/Material, not a Resources folder,
        // and BoardVisuals already owns/serializes every other shared visual asset (tileMaterial,
        // prefabs) — this keeps the same pattern instead of forcing the .mat back into a
        // Resources folder just to satisfy a runtime lookup.
        private const float OutlineShowDuration = 0.16f;
        private const float OutlineHideDuration = 0.1f;
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private Material _selectionOutlineMaterial;

        // King "in check" shake — a startle, not a clean wobble. Three things play at once, all off
        // the same single 0..1 driver tween so they can never drift apart or leave the king at a
        // stale offset (the capture-stamp arc's lesson): a fast decaying side-to-side vibration, a
        // small syncopated rotational tilt (a piece flinching leans, it doesn't just slide), and a
        // brief up-hop at the front of the motion (the "jolt" of being threatened). Everything
        // decays to zero by t=1 via a (1-t) envelope, so the king ends exactly where it started
        // with no settle tween needed. One Tween.Custom on unscaled time, so it plays at full speed
        // even while a hitstop/pause scales Time.timeScale.
        private const float ShakeDuration = 0.42f;
        private const float ShakePositionMagnitude = 0.06f;   // peak lateral offset (world units)
        private const float ShakeHopHeight = 0.05f;           // peak upward jolt at the front
        private const float ShakeTiltDegrees = 7f;            // peak Z-axis lean
        private const float ShakeFrequency = 32f;             // rad/sec of the lateral vibration
        private const float ShakeTiltFrequency = 26f;         // slightly detuned so tilt/slide don't lock in phase
        // Holds the wait as well as the rattle itself, so stopping the shake stops it whichever of
        // the two it is currently doing. _shaking separates them: while the piece is still walking
        // to where it will be shaken there is no rest pose worth putting back, and restoring the
        // stale one would drag the piece to wherever it last stood still.
        private Tween _shakeTween;
        private bool _shaking;
        private Vector3 _shakeRestPosition;
        private Quaternion _shakeRestRotation;

        private MeshRenderer _outlineRenderer;
        private MaterialPropertyBlock _outlineMpb;
        private Tween _outlineTween;
        private float _currentOutlineWidth;
        private float _outlineTargetWidth;

        // Dissolve — promotion's morph effect (see Custom/PieceLitRimGlow.shader), layered on top
        // of the existing squash tween rather than replacing it.
        private static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
        private Tween _dissolveTween;
        private Sequence _glowFlashSequence;
        private float _currentDissolve;

        private readonly Transform _transform;
        private readonly Renderer _renderer;
        private readonly Func<ChessPieceType> _getType;

        // Which animation is allowed to move this piece right now. Only one ever is: they all write
        // the position outright rather than adding to it, so two at once means the later frame of
        // whichever finishes last decides where the piece ended up. Naming the holder is what makes
        // that rule something a test can read, rather than something every entry point has to
        // remember to enforce by hand — which is how a warning shake came to strand a king on the
        // square it was leaving, and to leave another one standing a few centimetres off its own.
        private PositionWriter _positionOwner;

        /// <summary>Which animation currently holds this piece's position. Readable by tests.</summary>
        internal PositionWriter PositionHolder => _positionOwner;

        private Tween _moveTween;
        private Sequence _punchSequence;
        private Tween _scaleTween;
        private Sequence _transitionSequence;
        private Sequence _castleSequence;
        private Sequence _promotionApproachSequence;
        private Tween _settleBobTween;
        private float? _settleBobBaseY;
        private Sequence _stampSequence;

        // The lean runs alongside the stamp rather than inside it, because the victim plays one too
        // and it has no stamp of its own to hang it on. _leaning says whether the resting rotation
        // below is a real one worth putting back — a strike cut off halfway would otherwise leave
        // the piece tipped over for the rest of the game.
        private Tween _leanTween;
        private Quaternion _leanRestRotation;
        private bool _leaning;

        private MaterialPropertyBlock _mpb;

        // Selection-lift state. _liftRestPosition/_liftRestScale are captured the moment
        // LiftSelect() runs, so LowerDeselect() and CancelSelectionAnimation() can restore the
        // exact pre-lift transform even if the bob loop or rise tween is still mid-flight.
        private Sequence _liftSequence;
        private Tween _bobTween;
        private Vector3? _liftRestPosition;
        private Vector3 _liftRestScale;

        // The two halves of setting the piece back down, held rather than started and forgotten. A
        // tween nobody keeps is a tween nobody can stop, and this one runs on for a tenth of a
        // second after the player lets go — long enough for the piece to be picked straight back
        // up, moved, or destroyed while it is still writing the transform.
        private Tween _lowerPositionTween;
        private Tween _lowerScaleTween;

        public PrimeTweenPieceAnimator(Transform transform, Renderer renderer, Func<ChessPieceType> getType)
        {
            _transform = transform;
            _renderer = renderer;
            _getType = getType;
        }

        /// <summary>
        /// Injects the shared selection outline material — see ChessPiece.SetSelectionOutlineMaterial
        /// for why this replaced Resources.Load. Safe to call at any time before the piece is first
        /// selected; the outline renderer is only built lazily on first selection (TryEnsureOutlineRenderer).
        /// </summary>
        public void SetSelectionOutlineMaterial(Material material)
        {
            _selectionOutlineMaterial = material;
        }

        public void MoveTo(Vector3 worldPos, bool force = false)
        {
            MoveToInternal(worldPos, MoveDuration, MoveEase, punch: false, arc: false, force);
        }

        public void MoveTo(Vector3 worldPos, MoveStyle style, float tilesTravelled = 1f, bool force = false)
        {
            switch (style)
            {
                case MoveStyle.Capture:
                    MoveToInternal(worldPos, MoveTravelTiming.SecondsForTiles(tilesTravelled), BoardGlideEase, punch: true, arc: false, force);
                    break;
                case MoveStyle.Knight:
                    MoveToInternal(worldPos, KnightMoveDuration, BoardGlideEase, punch: false, arc: true, force);
                    break;
                case MoveStyle.Promotion:
                    MoveToInternal(worldPos, PromotionMoveDuration, BoardGlideEase, punch: false, arc: false, force);
                    break;
                case MoveStyle.Quiet:
                default:
                    MoveToInternal(worldPos, MoveTravelTiming.SecondsForTiles(tilesTravelled), BoardGlideEase, punch: false, arc: false, force);
                    break;
            }
        }

        private void MoveToInternal(Vector3 worldPos, float duration, Ease ease, bool punch, bool arc, bool force)
        {
            if (!IsFinite(worldPos))
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] MoveTo given non-finite vector for {_transform.name}. Ignoring.");
                return;
            }

            TakeOverPosition(PositionWriter.Move);

            if (force)
            {
                _transform.position = worldPos;
                return;
            }

            // Several callers (re-lifting an already-lifted piece, a lower that arrives after the
            // piece already moved away, etc.) can ask to move a piece to where it already is.
            // PrimeTween logs a warning for a tween whose start and end value are identical, so
            // skip it outright rather than let a harmless no-op animation spam the console.
            if (_transform.position == worldPos) return;

            if (arc)
            {
                // A knight "hops" rather than slides through occupied squares. This once ran two
                // independent tweens in parallel — Tween.Position (below, driving X/Y/Z toward
                // worldPos) and a second Tween.PositionY arcing up and back down — but both write
                // transform.position.y every single frame, and nothing guarantees whose write lands
                // last. Two competing writers on the same axis is exactly what let tiny residual
                // errors compound move after move, reading as pieces slowly floating higher off the
                // board over a game. Handled the same way the capture stamp's leap already is: one
                // driver (ApplyKnightArc) owns the whole position for the whole duration, computing
                // XZ as a lerp and Y as a straight lerp plus a parabolic bump that is mathematically
                // zero at t=0 and t=1 — so the piece is guaranteed to land exactly on worldPos with
                // no residual, no matter how many knight moves happen in a row.
                Vector3 knightStartPos = _transform.position;
                _moveTween = Tween.Custom(this, 0f, 1f, duration, (self, t) => self.ApplyKnightArc(t, knightStartPos, worldPos), ease, useUnscaledTime: true);
            }
            else
            {
                _moveTween = Tween.Position(_transform, worldPos, duration, ease, useUnscaledTime: true);
            }

            if (punch)
            {
                // Land, then a one-frame-reading scale pop — "impact" — timed to finish exactly as
                // the slide arrives. Chained onto the same sequence as the move itself (rather than
                // a separate delayed tween) so Stop()-ing _moveTween/_punchTween together can never
                // leave one half running without the other.
                Vector3 restScale = _transform.localScale;
                _punchSequence = Sequence.Create(useUnscaledTime: true)
                    .Chain(Tween.Delay(duration, useUnscaledTime: true))
                    .Chain(Tween.Scale(_transform, restScale * CapturePunchScale, CapturePunchDuration * 0.5f, Ease.OutQuad, useUnscaledTime: true))
                    .Chain(Tween.Scale(_transform, restScale, CapturePunchDuration * 0.5f, Ease.InQuad, useUnscaledTime: true));
            }
        }

        public void MoveToForCastle(Vector3 worldPos, float startDelay, Action onSettled = null)
        {
            if (!IsFinite(worldPos))
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] MoveToForCastle given non-finite vector for {_transform.name}. Ignoring.");
                return;
            }

            TakeOverPosition(PositionWriter.Castle);

            // The startDelay is what makes this a staggered two-piece maneuver rather than a
            // simultaneous teleport: the rook's glide is chained after a Delay so it visibly
            // starts a beat behind the king (BoardVisuals kicks off the king's own MoveTo at the
            // same instant, with startDelay = 0), then the same InOutCubic "travel = weight"
            // easing every other board glide uses. PlaySettleBob at the end is the "tucks in"
            // beat — a barely-there bob rather than the overshoot-heavy selection lift, since this
            // is a piece arriving, not a piece being picked up. The king's own call always passes
            // startDelay = 0 — skip the Delay chain step entirely in that case rather than
            // chaining a zero-length one, which PrimeTween's warnZeroDuration flags as a mistake.
            _castleSequence = Sequence.Create(useUnscaledTime: true);
            if (startDelay > 0f)
            {
                _castleSequence.Chain(Tween.Delay(startDelay, useUnscaledTime: true));
            }
            _castleSequence
                .Chain(Tween.Position(_transform, worldPos, CastleRookMoveDuration, BoardGlideEase, useUnscaledTime: true))
                .ChainCallback(() =>
                {
                    PlaySettleBob();
                    onSettled?.Invoke();
                });
        }

        public void PlayPromotionApproach(Vector3 worldPos, float tilesTravelled, Action onArrived)
        {
            if (!IsFinite(worldPos))
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] PlayPromotionApproach given non-finite vector for {_transform.name}. Ignoring.");
                onArrived?.Invoke();
                return;
            }

            TakeOverPosition(PositionWriter.Promotion);

            // Already standing there, so there is nothing to watch and the morph should start now.
            // This is the human's pawn: it walks onto the square while the promotion prompt is
            // open, so by the time a choice comes back it has long since arrived. Reporting
            // straight away keeps that path exactly as it was rather than making the player wait
            // through a second walk they already watched.
            if (_transform.position == worldPos)
            {
                onArrived?.Invoke();
                return;
            }

            _promotionApproachSequence = Sequence.Create(useUnscaledTime: true)
                .Chain(Tween.Position(_transform, worldPos, MoveTravelTiming.SecondsForTiles(tilesTravelled), BoardGlideEase, useUnscaledTime: true))
                .ChainCallback(() => onArrived?.Invoke());
        }

        /// <summary>
        /// The small landing beat at the end of a journey.
        ///
        /// Deliberately does not take the piece over the way a travel does (see TakeOverPosition):
        /// it is the tail of whichever animation is already carrying the piece — a castling rook
        /// settling, a piece arriving back from the pile — so that animation stays the one
        /// responsible. Anything that ever calls this on a piece standing still has to claim the
        /// piece first, or it will be nudging one that something else is already moving.
        /// </summary>
        public void PlaySettleBob()
        {
            // Restore Y to whatever it was before the previous bob started (if one is still
            // running) rather than reading the live transform, which — mid-Yoyo — could be
            // sitting anywhere between baseY and baseY + SettleBobHeight. Stop() alone does not
            // snap a tween back to its start value, so reading position.y right after Stop() used
            // to pick up that half-finished offset as the new "baseline," and each subsequent call
            // compounded a fraction of SettleBobHeight — the piece slowly floating higher every
            // move. Snapping first guarantees every PlaySettleBob starts from the same true rest Y.
            if (_settleBobBaseY.HasValue)
            {
                Vector3 pos = _transform.position;
                pos.y = _settleBobBaseY.Value;
                _transform.position = pos;
            }
            _settleBobTween.Stop();

            float baseY = _transform.position.y;
            _settleBobBaseY = baseY;
            _settleBobTween = Tween.PositionY(_transform, baseY, baseY + SettleBobHeight, SettleBobDuration / 2f,
                Ease.InOutSine, cycles: 2, cycleMode: CycleMode.Yoyo, useUnscaledTime: true)
                .OnComplete(this, self => self._settleBobBaseY = null);
        }

        public void PlayCaptureStamp(Vector3 worldPos, CaptureRunUp runUp = default, float victimHeft = 0f, Action onDescentStart = null, Action onImpact = null, Action onSettled = null)
        {
            // Anything outside 0..1 would scale a beat past what the estimator budgeted for it.
            victimHeft = Mathf.Clamp01(victimHeft);

            if (!IsFinite(worldPos) || (runUp.HasGroundToCover && !IsFinite(runUp.LaunchFrom)))
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] PlayCaptureStamp given non-finite vector for {_transform.name}. Ignoring.");
                onDescentStart?.Invoke();
                onSettled?.Invoke();
                return;
            }

            TakeOverPosition(PositionWriter.Stamp);

            Vector3 restScale = _transform.localScale;
            // Where the strike is staged from. With ground to cover that is the square next to the
            // victim, not the square the piece is standing on — so the wind-up, the arc and its peak
            // are all measured from there, and the leap stays the short pounce it was built as
            // however far away the attacker started.
            Vector3 startPos = runUp.HasGroundToCover ? runUp.LaunchFrom : _transform.position;
            Vector3 landPos = worldPos;

            Vector3 crouchScale = restScale * StampAnticipationScaleFactor;
            // Pulled back along the direction of travel, away from the victim — a boxer loading a
            // punch draws the fist back first. Flattened to the XZ plane (Y untouched) since this
            // is a wind-up, not a hop.
            Vector3 towardVictim = worldPos - startPos;
            towardVictim.y = 0f;
            Vector3 pullBackPos = startPos - towardVictim.normalized * StampAnticipationPullBack * Mathf.Min(1f, towardVictim.magnitude);
            pullBackPos.y = startPos.y - StampAnticipationCrouchDrop;

            // The leap begins where the wind-up left the piece, which is the loaded pose and not the
            // staging square. Arcing from the staging square instead put the whole pull-back back on
            // the piece in a single frame the instant the leap started — a small teleport forwards
            // that every capture in the game was paying, hidden inside the snap of the release.
            Vector3 launchPos = pullBackPos;
            float peakY = Mathf.Max(launchPos.y, landPos.y) + StampLeapHeight + StampLeapHeightPerHeft * victimHeft;

            // Swollen mid-air size — held through the landing and only released back to restScale
            // by the post-impact recover, so the piece that lands is visibly bigger than the piece
            // that took off (and than the victim cowering under it).
            Vector3 airborneScale = restScale * StampAirborneScaleFactor;
            Vector3 impactScale = new Vector3(restScale.x * StampImpactWidthFactor, restScale.y * StampImpactHeightFactor, restScale.z * StampImpactWidthFactor);

            float halfDuration = StampLeapDuration * StampDescentStartFraction;

            // The whole stamp lives on one sequence, built beat by beat with Chain (sequential)
            // and Group (parallel-with-previous) so timing is exact and every leg shares the same
            // useUnscaledTime — mixing an unscaled child into a scaled-time sequence (or vice
            // versa) is silently dropped by PrimeTween, so the sequence itself must be created
            // with useUnscaledTime up front rather than inferred from its first child.
            _stampSequence = Sequence.Create(useUnscaledTime: true);

            // 0. The run-up, when there is one: the attacker walks in and strikes from next door.
            // Stretched across half a board the leap stops being a pounce — a bishop leaving c1 for
            // g5 covers eight units in the same third of a second and simply appears on top of its
            // victim. Closing the distance first is also the honest read: the piece really did
            // travel that far. Same glide vocabulary and pace as any other board move, and it ends
            // on the loaded pose rather than on the staging square: the deceleration IS the crouch,
            // so there is no stop and no step backwards between arriving and striking. The scale
            // crouch is delayed to overlap only the tail of that walk, which is where a running
            // thing actually gathers itself.
            if (runUp.HasGroundToCover)
            {
                float runUpSeconds = MoveTravelTiming.SecondsForTiles(runUp.TilesToCover);
                float crouchSeconds = runUpSeconds * StampRunUpCrouchShare;

                _stampSequence
                    .Chain(Tween.Position(_transform, pullBackPos, runUpSeconds, BoardGlideEase, useUnscaledTime: true))
                    .Group(Sequence.Create(useUnscaledTime: true)
                        .Chain(Tween.Delay(runUpSeconds - crouchSeconds, useUnscaledTime: true))
                        .Chain(Tween.Scale(_transform, crouchScale, crouchSeconds, StampAnticipationEase, useUnscaledTime: true)));

                // Started beside the sequence rather than inside it because the piece being charged
                // plays the mirror of this and has no sequence of its own to hang it on — one
                // implementation, two callers. Both begin on the same frame, which is all the
                // synchronising they need. It writes rotation only, so it cannot fight the walk
                // above for an axis the way two position tweens would.
                //
                // Carried through the leap rather than unwound at the end of the walk. The lean is
                // strongest at the middle of whatever it spans, and spanning the walk alone put that
                // peak out in open ground and had the piece already upright by the time it gathered
                // itself — the least interesting moment to be leaning and the most interesting one
                // not to be. Across the walk and the leap together, the peak lands on the crouch and
                // the launch, and the piece rotates square again on its way down. It reaches exactly
                // upright before the impact, which is the pose the landing was built against.
                PlayLean(towardVictim, ChargeLeanDegrees, runUpSeconds + StampLeapDuration);
            }
            else
            {
                // 1. Anticipation: pull back and crouch down — a held breath before the pounce.
                // Only for an attacker that is already beside its victim and has nothing to close.
                _stampSequence
                    .Chain(Tween.Position(_transform, pullBackPos, StampAnticipationDuration, StampAnticipationEase, useUnscaledTime: true))
                    .Group(Tween.Scale(_transform, crouchScale, StampAnticipationDuration, StampAnticipationEase, useUnscaledTime: true));
            }

            _stampSequence
                // 2. Leap, first half (0 -> 0.5): one driver tween computes XZ (lerp) and Y (the
                // rising half of a parabola) from the same progress value every frame, so
                // "how far across" and "how high up" can never drift apart — the piece is
                // physically guaranteed to already be near peak height by the time it's over the
                // victim's tile, instead of two independently-eased tweens letting horizontal
                // catch up before vertical does (exactly the bug that caused visible overlap).
                //
                // Linear, not eased. Easing this driver eased the horizontal travel too, which sent
                // the piece sideways at double speed off the ground, stalled it to a dead stop in
                // mid-air at the peak, then flung it down again — a thing in flight does not stop
                // moving forwards halfway. Held level instead, the parabola alone gives the hang at
                // the top, because that is where a real arc slows down.
                .Chain(Tween.Custom(this, 0f, 0.5f, halfDuration, (self, t) => self.ApplyStampArc(t, launchPos, landPos, peakY), Ease.Linear, useUnscaledTime: true))
                // Swell from the crouch to the airborne size across the rise, with a small
                // overshoot so the growth pops — jumping things get bigger, per every good cartoon.
                .Group(Tween.Scale(_transform, airborneScale, halfDuration, Easing.Overshoot(StampAirborneGrowOvershoot), useUnscaledTime: true))
                // 3. onDescentStart fires exactly at the arc's peak (t=0.5) — the earliest moment
                // that still reads as "now falling toward you" — giving the victim's cower-shrink
                // the entire second half of the arc to get small before contact.
                .ChainCallback(() => onDescentStart?.Invoke())
                // Leap, second half (0.5 -> 1): same driver, same coupled XZ/Y formula, continuing
                // seamlessly from the peak down to the landing tile.
                .Chain(Tween.Custom(this, 0.5f, 1f, halfDuration, (self, t) => self.ApplyStampArc(t, launchPos, landPos, peakY), Ease.Linear, useUnscaledTime: true))
                // 4. Contact. Its own beat rather than something a caller has to time for itself:
                // anything that answers the collision — the camera's knock, a sound, dust — has to
                // land on this exact frame, and the only thing that knows when it arrives is the
                // sequence playing it.
                .ChainCallback(() => onImpact?.Invoke())
                // Flatten hard against the tile (still oversized — a big flat slap)...
                .Chain(Tween.Scale(_transform, impactScale, StampImpactSquashDuration, Ease.OutQuad, useUnscaledTime: true))
                // ...hold there, dead still, for the same beat the victim holds under it...
                .Chain(Tween.Delay(StampImpactHoldDuration + StampImpactHoldPerHeft * victimHeft, useUnscaledTime: true))
                // ...then recover with a big springy overshoot back down to rest scale — this is
                // the "settling back to its normal size" beat that closes the whole arc.
                .Chain(Tween.Scale(_transform, restScale, StampImpactRecoverDuration, Easing.Overshoot(StampRecoverOvershoot + StampRecoverOvershootPerHeft * victimHeft), useUnscaledTime: true))
                // The bob is the last thing anyone sees of the capture, so onSettled waits it out
                // rather than firing as it starts. Callers use that moment to begin an animation
                // that must not overlap this one — a Defection spin queued on the piece that just
                // captured — and starting one on top of a piece still bobbing is the overlap they
                // were trying to avoid.
                .ChainCallback(PlaySettleBob)
                .Chain(Tween.Delay(SettleBobDuration, useUnscaledTime: true))
                .ChainCallback(() => onSettled?.Invoke());
        }

        public void PlayBrace(Vector3 shoveDirection, float seconds)
        {
            PlayLean(shoveDirection, BraceLeanDegrees, seconds);
        }

        /// <summary>
        /// Tips the piece about the axis across the given direction — hardest halfway through and
        /// back to exactly upright by the end, which is where a glide is quickest and where a
        /// leaning thing is leaning most.
        ///
        /// Works in world space throughout, and never reads a rotation back as a set of angles: the
        /// resting rotation is cached as a quaternion and multiplied, so the piece lands on the
        /// exact pose it started from rather than on some other triple that describes the same
        /// facing. Getting that wrong is what once left defected pieces facing their old side.
        /// </summary>
        private void PlayLean(Vector3 towardDirection, float degrees, float seconds)
        {
            towardDirection.y = 0f;
            if (seconds <= 0f || towardDirection.sqrMagnitude < 0.000001f) return;

            StopLean();

            _leanRestRotation = _transform.rotation;
            _leaning = true;

            Vector3 axis = Vector3.Cross(Vector3.up, towardDirection.normalized);

            _leanTween = Tween.Custom(this, 0f, 1f, seconds,
                    (self, u) => self.ApplyLean(u, axis, degrees), Ease.Linear, useUnscaledTime: true)
                .OnComplete(this, self => self._leaning = false);
        }

        private void ApplyLean(float u, Vector3 axis, float degrees)
        {
            // Sin is zero at both ends whatever the numbers, so the piece cannot finish out of true
            // even if the tween is retimed — the same envelope trick the check shake relies on.
            float lean = Mathf.Sin(u * Mathf.PI) * degrees;
            _transform.rotation = Quaternion.AngleAxis(lean, axis) * _leanRestRotation;
        }

        /// <summary>
        /// Ends a lean and puts the piece back upright. Only restores when a lean was actually
        /// live: the cached rotation is meaningless otherwise, and writing it back would snap the
        /// piece to wherever it last happened to be leaning from.
        /// </summary>
        private void StopLean()
        {
            _leanTween.Stop();
            if (!_leaning) return;

            _transform.rotation = _leanRestRotation;
            _leaning = false;
        }

        /// <summary>
        /// Ends the whole capture beat — the sequence and the lean running beside it. Every caller
        /// that interrupts a strike needs both, since stopping the sequence alone leaves the piece
        /// tipped at whatever angle it had reached.
        /// </summary>
        private void StopStampBeat()
        {
            _stampSequence.Stop();
            StopLean();
        }

        /// <summary>
        /// Places the transform along the stamp's leap arc at normalized progress t (0 = takeoff,
        /// 1 = landing): XZ is a straight lerp between launchPos/landPos, Y is a true parabola
        /// (4 * peakOffset * t * (1-t), zero at both ends, peakOffset at t=0.5) added on top of the
        /// lerped baseline height. Driving both axes from the same t is what guarantees horizontal
        /// and vertical progress can never drift apart — see PlayCaptureStamp's call site.
        ///
        /// t is fed in level, with no easing on it. The parabola is what makes the piece hang at the
        /// top and drop away quickly at the ends; easing t as well would apply that same shaping to
        /// the horizontal travel, which is the one part of a leap that should hold its pace.
        /// </summary>
        private void ApplyStampArc(float t, Vector3 launchPos, Vector3 landPos, float peakY)
        {
            float x = Mathf.Lerp(launchPos.x, landPos.x, t);
            float z = Mathf.Lerp(launchPos.z, landPos.z, t);
            float baseline = Mathf.Lerp(launchPos.y, landPos.y, t);

            // Parabola: 0 at t=0 and t=1, (peakY - higherLaunchLandY) at t=0.5. Added on top of the
            // straight-line baseline so the arc still smoothly reaches exactly landPos.y at t=1
            // even when launchPos.y != landPos.y (a board with tilesYOffset/uneven tiles).
            float higherY = Mathf.Max(launchPos.y, landPos.y);
            float peakBump = 4f * (peakY - higherY) * t * (1f - t);

            _transform.position = new Vector3(x, baseline + peakBump, z);
        }

        /// <summary>
        /// Places the transform along a knight's hop arc at normalized progress t (0 = start tile,
        /// 1 = end tile): XZ is a straight lerp, Y is a straight lerp plus a parabolic bump
        /// (4 * KnightArcHeight * t * (1-t)) that is mathematically zero at t=0 and t=1. One driver
        /// owning the whole position for the whole move — see MoveToInternal's arc branch for why
        /// this replaced two separate Position/PositionY tweens racing to write the same axis.
        /// </summary>
        private void ApplyKnightArc(float t, Vector3 startPos, Vector3 endPos)
        {
            float x = Mathf.Lerp(startPos.x, endPos.x, t);
            float z = Mathf.Lerp(startPos.z, endPos.z, t);
            float baseline = Mathf.Lerp(startPos.y, endPos.y, t);
            float arcBump = 4f * KnightArcHeight * t * (1f - t);

            _transform.position = new Vector3(x, baseline + arcBump, z);
        }

        public void PlayStompedDeath(Action onVanished)
        {
            TakeOverPosition(PositionWriter.Death);

            Vector3 restScale = _transform.localScale;
            float restY = _transform.position.y;
            Vector3 cowerScale = restScale * StampVictimCowerScaleFactor;
            // Pancake factors apply to the cowered size, not the rest size — by crush time the
            // piece has already shrunk to StampVictimCowerScaleFactor, and the pancake should read
            // as that smaller piece being flattened, not suddenly re-widening past the attacker.
            Vector3 pancakeScale = new Vector3(cowerScale.x * StampVictimWidthFactor, cowerScale.y * StampVictimHeightFactor, cowerScale.z * StampVictimWidthFactor);

            // Called at the attacker's descent start — the arc's peak, t=0.5 — not at impact (see
            // PlayCaptureStamp). Stage 1 spans exactly the attacker's remaining airtime (half of
            // StampLeapDuration, same shared constant and unscaled clock, so no cross-object
            // callback is needed for the sync): the victim cowers — shrinks toward the tile under
            // the incoming piece — which combined with the attacker's height clearance guarantees
            // the two are never both at full size at once. Stage 2 lands exactly as the attacker
            // does: the remaining piece slams to a pancake and sinks into the tile. Stage 3: the
            // pancake shrinks away to nothing under the attacker — a stamp kills by squashing, so
            // vanishing-in-place is the honest payoff, no fly-off.
            _stampSequence = Sequence.Create(useUnscaledTime: true)
                .Group(Tween.Scale(_transform, cowerScale, StampLeapDuration * StampDescentStartFraction, StampVictimCowerEase, useUnscaledTime: true))
                .Chain(Tween.Scale(_transform, pancakeScale, StampVictimSquashDuration, Ease.InBack, useUnscaledTime: true))
                .Group(Tween.PositionY(_transform, restY - StampVictimSinkDepth, StampVictimSquashDuration, Ease.InQuad, useUnscaledTime: true))
                .Chain(Tween.Delay(StampVictimHoldDuration, useUnscaledTime: true))
                .Chain(Tween.Scale(_transform, VanishedScale, StampVictimVanishDuration, Ease.InBack, useUnscaledTime: true))
                .ChainCallback(() => onVanished?.Invoke());
        }

        public void PlayEnPassantDeath(Vector3 graveyardWorldPos, float tilesTravelled, Action onArrived)
        {
            if (!IsFinite(graveyardWorldPos))
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] PlayEnPassantDeath given non-finite vector for {_transform.name}. Ignoring.");
                onArrived?.Invoke();
                return;
            }

            TakeOverPosition(PositionWriter.Death);

            Vector3 startPos = _transform.position;
            float journeySeconds = MoveTravelTiming.SecondsForTiles(tilesTravelled);

            // The attacker never visually touches this piece (it's captured on a different square
            // than the one it lands on), so instead of a crush it plays its own "swept off the
            // board" beat: a small hop, the same glide vocabulary as a normal board move, with the
            // piece shrinking away as it goes rather than teleporting to the pile at full size and
            // only then scaling down. XZ and Y are driven by separate tweens (same pattern as
            // PlayCaptureStamp's leap) so the horizontal glide and the vertical hop don't fight over
            // the Y axis. The shrink runs on its own clock — see DeathVanishSeconds.
            _stampSequence = Sequence.Create(useUnscaledTime: true)
                .Group(Tween.Position(_transform, new Vector3(graveyardWorldPos.x, startPos.y, graveyardWorldPos.z), journeySeconds, BoardGlideEase, useUnscaledTime: true))
                .Group(Tween.Scale(_transform, VanishedScale, DeathVanishSeconds, EnPassantDeathScaleEase, useUnscaledTime: true))
                .Group(Sequence.Create(useUnscaledTime: true)
                    .Chain(Tween.PositionY(_transform, startPos.y + EnPassantDeathHopHeight, journeySeconds * 0.5f, EnPassantDeathHopEase, useUnscaledTime: true))
                    .Chain(Tween.PositionY(_transform, graveyardWorldPos.y, journeySeconds * 0.5f, Ease.InQuad, useUnscaledTime: true)))
                .ChainCallback(() => onArrived?.Invoke());
        }

        public void PlayGraveyardReturn(Vector3 boardWorldPos, Vector3 restScale, float tilesTravelled, Action onArrived)
        {
            if (!IsFinite(boardWorldPos) || !IsFinite(restScale))
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] PlayGraveyardReturn given non-finite vector for {_transform.name}. Ignoring.");
                onArrived?.Invoke();
                return;
            }

            TakeOverPosition(PositionWriter.Death);

            Vector3 startPos = _transform.position;
            float journeySeconds = MoveTravelTiming.SecondsForTiles(tilesTravelled);

            // The death glide read backwards: the same InOutCubic travel and the same hop, with the
            // piece swelling back to full size across it instead of shrinking away. The arrival
            // overshoots where the death eased flat, because this one lands somewhere — a piece
            // returning to the board should look like it means to be there.
            _stampSequence = Sequence.Create(useUnscaledTime: true)
                .Group(Tween.Position(_transform, new Vector3(boardWorldPos.x, startPos.y, boardWorldPos.z), journeySeconds, BoardGlideEase, useUnscaledTime: true))
                .Group(Tween.Scale(_transform, restScale, DeathVanishSeconds, Easing.Overshoot(1.4f), useUnscaledTime: true))
                .Group(Sequence.Create(useUnscaledTime: true)
                    .Chain(Tween.PositionY(_transform, startPos.y + EnPassantDeathHopHeight, journeySeconds * 0.5f, EnPassantDeathHopEase, useUnscaledTime: true))
                    .Chain(Tween.PositionY(_transform, boardWorldPos.y, journeySeconds * 0.5f, Ease.OutBack, useUnscaledTime: true)))
                .ChainCallback(() =>
                {
                    onArrived?.Invoke();
                    PlaySettleBob();
                });
        }

        public void ScaleTo(Vector3 scale, bool force = false)
        {
            if (!IsFinite(scale) || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] ScaleTo given invalid scale {scale} for {_transform.name}. Ignoring.");
                return;
            }

            _scaleTween.Stop();

            if (force)
            {
                _transform.localScale = scale;
                return;
            }

            // Same rationale as MoveTo above: don't tween (or warn) when there's nothing to do.
            if (_transform.localScale == scale) return;

            _scaleTween = Tween.Scale(_transform, scale, ScaleDuration, ScaleEase, useUnscaledTime: true);
        }

        public void FaceDirection(Vector3 lookDirection)
        {
            _transform.rotation = Quaternion.LookRotation(lookDirection == Vector3.zero ? Vector3.forward : lookDirection);
        }

        public void SetHighlighted(bool active)
        {
            if (_renderer == null) return;

            // A MaterialPropertyBlock lets every piece share one Chess Material instance instead
            // of Unity silently instancing a per-renderer copy the first time we'd otherwise touch
            // material.color — keeps batching intact for the whole board.
            _mpb ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(RimGlowColorId, BetrayerGlowColor);
            _mpb.SetFloat(RimGlowIntensityId, active ? BetrayerGlowIntensity : 0f);
            _renderer.SetPropertyBlock(_mpb);
        }

        public void DissolveTo(float targetAmount, float duration, Action onComplete = null)
        {
            if (_renderer == null)
            {
                onComplete?.Invoke();
                return;
            }

            _dissolveTween.Stop();
            _dissolveTween = Tween.Custom(this, _currentDissolve, targetAmount, duration, (self, val) => self.ApplyDissolve(val), Ease.Linear, useUnscaledTime: true)
                .OnComplete(() => onComplete?.Invoke());
        }

        public void SetDissolveImmediate(float amount)
        {
            _dissolveTween.Stop();
            ApplyDissolve(amount);
        }

        private void ApplyDissolve(float amount)
        {
            _currentDissolve = amount;
            if (_renderer == null) return;

            _mpb ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(DissolveAmountId, amount);
            _renderer.SetPropertyBlock(_mpb);
        }

        public void FlashGlow(Color color, float intensity, float flashDuration, int cycles)
        {
            if (_renderer == null) return;

            _glowFlashSequence.Stop();

            // Restore whatever glow state was active before the flash (e.g. a Betrayer mid-glow)
            // rather than assuming "off", so this can't stomp on SetHighlighted's own state.
            _mpb ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_mpb);
            float restoreIntensity = _mpb.GetFloat(RimGlowIntensityId);
            Color restoreColor = _mpb.GetColor(RimGlowColorId);
            if (restoreColor == Color.clear) restoreColor = BetrayerGlowColor;

            Sequence seq = Sequence.Create(useUnscaledTime: true);
            for (int i = 0; i < cycles; i++)
            {
                seq = seq
                    .ChainCallback(() => ApplyGlow(color, intensity))
                    .Chain(Tween.Delay(flashDuration * 0.5f, useUnscaledTime: true))
                    .ChainCallback(() => ApplyGlow(restoreColor, restoreIntensity))
                    .Chain(Tween.Delay(flashDuration * 0.5f, useUnscaledTime: true));
            }

            _glowFlashSequence = seq;
        }

        private void ApplyGlow(Color color, float intensity)
        {
            if (_renderer == null) return;

            _mpb ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(RimGlowColorId, color);
            _mpb.SetFloat(RimGlowIntensityId, intensity);
            _renderer.SetPropertyBlock(_mpb);
        }

        /// <summary>
        /// Rattles the piece where it stands. A piece that is not standing anywhere yet is left to
        /// finish its journey first — see StartShake for why it cannot simply begin.
        /// </summary>
        public void Shake()
        {
            float travelLeft = SecondsOfTravelLeft();

            // Whichever half of a shake is already here gives way to this one. Doing it from here
            // rather than from StartShake keeps a waiting shake from being stopped inside its own
            // completion, which is not a thing a tween will sit still for.
            StopShake();

            if (travelLeft <= 0f)
            {
                StartShake();
                return;
            }

            _shakeTween = Tween.Delay(this, travelLeft, self => self.StartShake(), useUnscaledTime: true);
        }

        /// <summary>
        /// How long until nothing is moving this piece any more, so a shake can be given the
        /// transform to itself. Zero for a piece already at rest, which is every shake that is not
        /// answering a move — a tap on a piece with nowhere to go, for one.
        /// </summary>
        private float SecondsOfTravelLeft()
        {
            if (!_moveTween.isAlive) return 0f;

            float left = _moveTween.duration - _moveTween.elapsedTime;
            return left > 0f ? left : 0f;
        }

        /// <summary>
        /// Takes the transform over and rattles it.
        ///
        /// A shake is built from an absolute pose — the place the piece is standing, plus an offset
        /// that decays to nothing — so it cannot share the transform with a glide that is still
        /// moving that place underneath it. Both would write position every frame and the shake,
        /// being the shorter of the two, would get the last word and leave the piece back where the
        /// glide started. Taking a king out of check and then taking that move back is where it
        /// showed: the king was told to return to the square it came from and told it was in check
        /// in the same breath, and it stayed standing on the square it had just left, with the
        /// check frame drawn on the empty square it should have been on.
        /// </summary>
        private void StartShake()
        {
            // The glide is over by now — the wait was measured off it — unless a frame boundary
            // landed inside its last few milliseconds. End it outright in that case so the pose
            // read below is the square the piece was heading for rather than a hair short of it,
            // which is the residual that compounds move after move. A move tween carries no
            // completion callback, so there is nothing to fire early by ending it here.
            if (_moveTween.isAlive) _moveTween.Complete();

            // A piece on its way back down is the same case, and the wait above does not measure
            // it — only the glide. It has nowhere to get to but the square underneath, so there is
            // nothing to lose by finishing it here either.
            FinishAnyDescent();

            // Nothing to clear away first: every route in here has just been through StopShake.
            _shakeRestPosition = _transform.position;
            _shakeRestRotation = _transform.localRotation;
            _shaking = true;
            _positionOwner = PositionWriter.Shake;

            // Local right/up, so the vibration and hop read identically for White and Black kings
            // despite Black's prefab being pre-rotated 180° at spawn (see SpawnSinglePiece). Tilt is
            // about the piece's own forward axis (a lean toward/away from the shake direction).
            Vector3 lateral = _transform.right;
            Vector3 up = _transform.up;

            _shakeTween = Tween.Custom(this, 0f, 1f, ShakeDuration,
                (self, t) => self.ApplyShake(t, lateral, up), Ease.Linear, useUnscaledTime: true);
        }

        /// <summary>
        /// Drives the whole check-shake from one normalized progress t (0..1): a decaying-amplitude
        /// sinusoidal lateral vibration, a detuned rotational tilt, and a single up-hop concentrated
        /// at the front of the motion. A (1-t) envelope multiplies every component so all of them
        /// reach exactly zero at t=1 — the king lands back on its exact rest transform with no
        /// separate settle tween. Everything is computed from the cached rest pose, so nothing has
        /// to be started or torn down each frame.
        /// </summary>
        private void ApplyShake(float t, Vector3 lateral, Vector3 up)
        {
            float envelope = 1f - t;               // linear decay to zero by the end
            float decay = envelope * envelope;     // squared: hits hard up front, tapers smoothly

            float sway = Mathf.Sin(t * ShakeFrequency) * ShakePositionMagnitude * decay;
            float tilt = Mathf.Sin(t * ShakeTiltFrequency) * ShakeTiltDegrees * decay;
            // Hop is a half-sine bump weighted to the front third of the motion — the initial jolt —
            // rather than oscillating the whole way through, so it reads as one recoil, not a bounce.
            float hop = Mathf.Sin(Mathf.Clamp01(t * 3f) * Mathf.PI) * ShakeHopHeight * decay;

            _transform.position = _shakeRestPosition + lateral * sway + up * hop;
            _transform.localRotation = _shakeRestRotation * Quaternion.AngleAxis(tilt, Vector3.forward);
        }

        /// <summary>
        /// Hands this piece's position to <paramref name="next"/>, taking it off whatever held it
        /// before.
        ///
        /// Everything below writes the position, so everything below has to let go. It stops more
        /// than any one caller strictly needs, and deliberately: the alternative is each entry point
        /// keeping its own list of what to stop, which is what this replaces, and which had drifted
        /// into seven nearly-identical lists that disagreed about three entries. A list that exists
        /// once is one that can be added to once when a new animation arrives.
        ///
        /// The two that restore a pose rather than just stopping — the shake and the lift — are
        /// asked through their own helpers, since a rattle or a raise cut halfway leaves the piece
        /// somewhere it never stood and nothing later puts a rotation back on its own.
        /// </summary>
        private void TakeOverPosition(PositionWriter next)
        {
            // A descent ends on the square it was returning to rather than being cut short in
            // mid-air, so whatever takes the piece over starts from where the piece belongs.
            FinishAnyDescent();

            _moveTween.Stop();
            _punchSequence.Stop();
            _castleSequence.Stop();
            _settleBobTween.Stop();
            _settleBobBaseY = null;
            StopStampBeat();
            _promotionApproachSequence.Stop();
            StopShake();
            StopLiftTweens();
            _liftRestPosition = null;

            // A piece that is being moved is no longer a piece the player is holding, so the
            // selection ring goes with the lift rather than gliding across the board still glowing.
            HideSelectionOutline(instant: true);

            _positionOwner = next;
        }

        /// <summary>
        /// Ends a shake — waiting or rattling — and puts the piece back on the pose it was rattling
        /// around, so nothing that follows inherits a half-finished offset as the piece's real
        /// place. Nothing to put back while it is still only waiting: the pose is not read until the
        /// rattle itself begins.
        /// </summary>
        private void StopShake()
        {
            bool wasShaking = _shaking && _shakeTween.isAlive;
            _shakeTween.Stop();
            _shaking = false;

            if (!wasShaking) return;

            _transform.position = _shakeRestPosition;
            _transform.localRotation = _shakeRestRotation;
        }

        public void PlayTransitionOut(PieceTransitionStyle style, Action onComplete)
        {
            _transitionSequence.Stop();

            switch (style)
            {
                case PieceTransitionStyle.Spin:
                {
                    // Quarter-turn to edge-on, as if the piece is turning away from the camera.
                    // The swap happens the instant it's edge-on, so the incoming prefab's face is
                    // what rotates back into view during PlayTransitionIn — the spin sells "this
                    // piece turned into something else" without any shader or dissolve work.
                    //
                    // Relative to the piece's own current rotation, slerped quaternion-to-quaternion
                    // via Tween.Custom rather than Tween.LocalRotation's Vector3/Euler overload —
                    // see PlayTransitionIn's Spin case for why a Euler-angle target is unsafe here.
                    // A hardcoded absolute (0, 90, 0) target used to work by coincidence for White
                    // (which rests at identity) but was wrong for Black (which rests pre-rotated 180
                    // degrees — see BoardVisuals.SpawnSinglePiece): it snapped the piece toward
                    // White's facing instead of turning another quarter away from its own facing,
                    // which is what let a defected piece finish this transition already facing the
                    // wrong way before PlayTransitionIn even ran on the freshly-spawned replacement.
                    Quaternion startRotation = _transform.localRotation;
                    Quaternion edgeOnRotation = startRotation * Quaternion.Euler(0f, 90f, 0f);
                    _transitionSequence = Sequence.Create(useUnscaledTime: true)
                        .Chain(Tween.Custom(this, 0f, 1f, SpinOutDuration,
                            (self, t) => self._transform.localRotation = Quaternion.Slerp(startRotation, edgeOnRotation, t),
                            Ease.InQuad, useUnscaledTime: true))
                        .ChainCallback(onComplete);
                    break;
                }

                case PieceTransitionStyle.PromotionMorph:
                    // Same squash-down anticipation as Squash below, plus a dissolve ramp (0 -> 1)
                    // layered on top via Group so the pawn both shrinks and burns away at once,
                    // rather than one effect replacing the other.
                    _transitionSequence = Sequence.Create(useUnscaledTime: true)
                        .Chain(Tween.Scale(_transform, VanishedScale, SquashOutDuration, Ease.InBack, useUnscaledTime: true))
                        .Group(Tween.Custom(this, _currentDissolve, 1f, SquashOutDuration, (self, val) => self.ApplyDissolve(val), Ease.OutQuad, useUnscaledTime: true))
                        .ChainCallback(onComplete);
                    break;

                case PieceTransitionStyle.Squash:
                default:
                    // Anticipation squash down to near-zero scale, then swap — reads as "this piece
                    // collapses into its promoted form" rather than a jump-cut.
                    _transitionSequence = Sequence.Create(useUnscaledTime: true)
                        .Chain(Tween.Scale(_transform, VanishedScale, SquashOutDuration, Ease.InBack, useUnscaledTime: true))
                        .ChainCallback(onComplete);
                    break;
            }
        }

        public void PlayTransitionIn(PieceTransitionStyle style)
        {
            _transitionSequence.Stop();

            switch (style)
            {
                case PieceTransitionStyle.Spin:
                {
                    // Start edge-on relative to this piece's own resting rotation (mirroring where
                    // the outgoing piece left off in PlayTransitionOut), then spin the remaining
                    // quarter-turn back to facing forward. Computed relative to the resting
                    // rotation rather than a hardcoded value because enemy-facing prefabs are
                    // pre-rotated 180 degrees at spawn (see BoardVisuals.SpawnSinglePiece) — a
                    // freshly-spawned Black piece already rests at (0, 180, 0), not identity.
                    //
                    // Driven via Tween.Custom slerping two cached quaternions end to end, rather
                    // than Tween.LocalRotation's Vector3/Euler overload: that overload interpolates
                    // Euler angles component-wise using whatever euler triple Transform.eulerAngles
                    // happens to report for the current rotation at the moment the tween is created,
                    // and Quaternion-to-Euler decomposition is not unique — composing restingRotation
                    // with a -90 degree offset can read back as a completely different (but
                    // equivalent) triple than restingEuler expects to lerp from. That mismatch was
                    // the actual bug behind a defected piece (e.g. Betrayal's failed-Retribution
                    // team flip) sometimes finishing this transition still facing its old team's
                    // direction instead of the new team's. Slerping quaternion-to-quaternion has no
                    // such ambiguity: it always interpolates the shortest path between the two exact
                    // rotations and lands exactly on restingRotation at t=1.
                    Quaternion restingRotation = _transform.localRotation;
                    Quaternion edgeOnRotation = restingRotation * Quaternion.Euler(0f, -90f, 0f);
                    _transform.localRotation = edgeOnRotation;
                    _transitionSequence = Sequence.Create(useUnscaledTime: true)
                        .Chain(Tween.Custom(this, 0f, 1f, SpinInDuration,
                            (self, t) => self._transform.localRotation = Quaternion.Slerp(edgeOnRotation, restingRotation, t),
                            Ease.OutBack, useUnscaledTime: true));
                    break;
                }

                case PieceTransitionStyle.PromotionMorph:
                {
                    // Same squash-in-with-hop as Squash below, plus the dissolve ramping back down
                    // (1 -> 0) in parallel so the promoted piece both grows in and reforms from the
                    // burning edge, rather than just popping into view at full opacity.
                    Vector3 targetScale = _transform.localScale;
                    _transform.localScale = Vector3.one * VanishedScale;
                    float restY = _transform.position.y;
                    SetDissolveImmediate(1f);
                    _transitionSequence = Sequence.Create(useUnscaledTime: true)
                        .Chain(Tween.Scale(_transform, targetScale, SquashInDuration, Easing.Overshoot(1.5f), useUnscaledTime: true))
                        .Group(Tween.PositionY(_transform, restY + PromotionPopHopHeight, restY, PromotionPopHopDuration, Ease.OutBack, useUnscaledTime: true))
                        .Group(Tween.Custom(this, 1f, 0f, SquashInDuration, (self, val) => self.ApplyDissolve(val), Ease.InQuad, useUnscaledTime: true));
                    break;
                }

                case PieceTransitionStyle.Squash:
                default:
                {
                    // Spawn at vanished scale and pop back up to whatever scale BoardVisuals just
                    // set (pieceScaleMultiplier), with a slight overshoot for punch. A small
                    // rise-and-settle hop runs alongside the scale so the promoted piece feels like
                    // it materializes with a bounce rather than just growing in place.
                    Vector3 targetScale = _transform.localScale;
                    _transform.localScale = Vector3.one * VanishedScale;
                    float restY = _transform.position.y;
                    _transitionSequence = Sequence.Create(useUnscaledTime: true)
                        .Chain(Tween.Scale(_transform, targetScale, SquashInDuration, Easing.Overshoot(1.5f), useUnscaledTime: true))
                        .Group(Tween.PositionY(_transform, restY + PromotionPopHopHeight, restY, PromotionPopHopDuration, Ease.OutBack, useUnscaledTime: true));
                    break;
                }
            }
        }

        /// <summary>
        /// Raises the piece under the player's finger.
        ///
        /// Deliberately does not take the piece over the way a travel does (see TakeOverPosition),
        /// and that is worth an argument rather than an assumption, since a travel left running
        /// under a pick-up would write the piece's position from underneath it.
        ///
        /// It cannot arrive while one is running. Everything that travels belongs to a move that
        /// has just been made, and making a move passes the turn, after which the piece that moved
        /// is not on the side that is choosing and a tap on it is refused long before it reaches
        /// here. The two phases where the turn does not pass are the two halves of a Betrayal, and
        /// neither opens the gap: the piece that moved is the Betrayer, the only moves on offer are
        /// captures of it, and the generator will not offer the Betrayer a move of its own to be
        /// selected into; by the time a forced Save is asked for instead, the Betrayer has changed
        /// sides and belongs to the other team. A takeback is held shut from the other end —
        /// selection is refused for as long as the rewind is being shown, and each ply of it is
        /// given the same room the move was paced against on the way out.
        ///
        /// What can be running is the warning rattle and the piece's own way back down, and both
        /// are ended here.
        /// </summary>
        public void LiftSelect()
        {
            // A piece still on its way back down has one place to be, the square underneath it, so
            // end the descent there rather than wherever it had got to. The rest position further
            // down is read straight off the transform, and two taps inside a tenth of a second
            // would otherwise record mid-air as the place to set the piece down — which is not
            // recovered, because every lift after it is measured from there in turn.
            FinishAnyDescent();

            // Re-lifting an already-lifted piece (a stale/duplicate select) would otherwise stack
            // a second rest position on top of the lifted one, so restart from a clean slate first.
            StopLiftTweens();

            // A rattle has the piece a few centimetres off its square at every moment except its
            // last, and the line below is about to record where the piece is standing as the place
            // to set it back down. Recorded mid-rattle that is somewhere the piece never stood, and
            // the error outlives the rattle by the whole rest of the game — worse, the next rattle
            // decays to the wrong place too, so answering three checks this way walks the piece a
            // third of the way off its square.
            //
            // Ending the rattle rather than waiting it out, which is what a move does instead: a
            // move had nobody waiting on it, a tap has the player waiting on it, and making
            // selection up to ShakeDuration late would be a worse answer than losing the rattle.
            // Nothing is lost by ending it — a player reaching for the king has plainly seen the
            // warning, and the check frame under it is a board marker that stays put regardless.
            StopShake();
            _positionOwner = PositionWriter.Lift;

            _liftRestPosition = _transform.position;
            _liftRestScale = _transform.localScale;

            float liftHeight = LiftHeightByType.TryGetValue(_getType(), out float height) ? height : DefaultLiftHeight;
            Vector3 liftedPosition = _liftRestPosition.Value + new Vector3(0f, liftHeight, 0f);
            Vector3 squashScale = Vector3.Scale(_liftRestScale, new Vector3(LiftSquashWidthFactor, LiftSquashHeightFactor, LiftSquashWidthFactor));

            // squashScale can equal _transform.localScale only in the degenerate case of a
            // zero-scale piece, which never happens in practice, but PrimeTween's start-equals-end
            // warning is about the sequence's own internal tween creation, not a live comparison
            // against the current transform (the squash always animates away from whatever the
            // piece's scale was even mid-tween). No guard needed here — see LowerDeselect for the
            // case that actually needs one (a rest-scale target the transform may already be at).
            _liftSequence = Sequence.Create(useUnscaledTime: true)
                // 1. Quick anticipatory squash — sells the weight of the piece being gripped.
                .Chain(Tween.Scale(_transform, squashScale, LiftSquashDuration, Ease.OutQuad, useUnscaledTime: true))
                // 2. Rise to lift height and recover scale at the same time (Group, not Chain —
                // both must play in parallel), each with a slight overshoot so the settle feels
                // springy rather than mechanical.
                .Chain(Tween.Position(_transform, liftedPosition, LiftRiseDuration, Easing.Overshoot(LiftOvershootStrength), useUnscaledTime: true))
                .Group(Tween.Scale(_transform, _liftRestScale, LiftRiseDuration, Easing.Overshoot(LiftOvershootStrength), useUnscaledTime: true))
                .ChainCallback(StartBobLoop);

            // The selection ring grows in alongside the lift (not after it) so the "you picked
            // this up" read is instant even before the rise settles.
            ShowSelectionOutline();
        }

        public void LowerDeselect()
        {
            StopLiftTweens();

            if (!_liftRestPosition.HasValue) return;

            // No overshoot on the way down — a lift feels snappy and eager, a landing should feel
            // like gently setting the piece back on the board. A very fast select-then-deselect
            // (faster than LiftSquashDuration) can catch the piece already sitting exactly at
            // _liftRestScale/_liftRestPosition — e.g. StopLiftTweens above cancels the squash leg
            // before it ever animates away from rest scale — so guard each tween the same way
            // MoveToInternal/ScaleTo already do, rather than let PrimeTween warn about a
            // start-equals-end tween.
            if (_transform.position != _liftRestPosition.Value)
            {
                _lowerPositionTween = Tween.Position(_transform, _liftRestPosition.Value, LiftLowerDuration, Ease.OutQuad, useUnscaledTime: true);
            }
            if (_transform.localScale != _liftRestScale)
            {
                _lowerScaleTween = Tween.Scale(_transform, _liftRestScale, LiftLowerDuration, Ease.OutQuad, useUnscaledTime: true);
            }

            _liftRestPosition = null;

            // Naming the descent matters because it writes the position for a tenth of a second
            // after the player has let go, and reporting nobody as writing it is what kept it out
            // of every list of things to stop. Both guards can be false, though — a piece already
            // standing where it is going has nothing to animate — and there is nobody to name then.
            _positionOwner = _lowerPositionTween.isAlive || _lowerScaleTween.isAlive
                ? PositionWriter.Lower
                : PositionWriter.None;
            HideSelectionOutline(instant: false);
        }

        public void CancelSelectionAnimation()
        {
            StopLiftTweens();
            _liftRestPosition = null;
            // Teardown path — the piece may be mid-destroy, so snap the ring off with no fade.
            HideSelectionOutline(instant: true);
        }

        /// <summary>
        /// Stops every tween and sequence this animator can have live.
        ///
        /// One field at a time, deliberately. Stopping everything aimed at a target instead reads
        /// as the tidier version of this and does not work: almost everything here is built as a
        /// sequence, and a tween that belongs to one can only be stopped through the sequence that
        /// owns it. Reaching for the children directly is refused, so the sweep left running
        /// exactly the animations most likely to be interrupted — a strike, a swap, a lift — while
        /// reporting that it had stopped them. The piece was then destroyed with those still
        /// writing to its Transform, which is the very thing this method exists to prevent.
        ///
        /// The cost is that a new tween field has to be added to the list below. A test asserts
        /// nothing is left running afterwards, so forgetting fails rather than going quiet.
        /// </summary>
        public void StopAllAnimations()
        {
            _moveTween.Stop();
            _scaleTween.Stop();
            _settleBobTween.Stop();
            _bobTween.Stop();
            _lowerPositionTween.Stop();
            _lowerScaleTween.Stop();
            _shakeTween.Stop();
            _shaking = false;
            _positionOwner = PositionWriter.None;
            _outlineTween.Stop();
            _dissolveTween.Stop();
            _leanTween.Stop();

            _punchSequence.Stop();
            _transitionSequence.Stop();
            _castleSequence.Stop();
            _promotionApproachSequence.Stop();
            _stampSequence.Stop();
            _liftSequence.Stop();
            _glowFlashSequence.Stop();
        }

        private void StartBobLoop()
        {
            // A very subtle infinite up/down drift while the piece stays selected — 2-3mm of travel
            // is intentionally barely perceptible; it's there to make the selection feel alive, not
            // to draw attention to itself. cycles: -1 + CycleMode.Yoyo loops until explicitly
            // stopped by StopLiftTweens (LowerDeselect/CancelSelectionAnimation/a fresh LiftSelect).
            float baseY = _transform.position.y;
            _bobTween = Tween.PositionY(_transform, baseY, baseY + BobAmplitude, BobDuration / 2f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);
        }

        /// <summary>
        /// Stops the lift sequence, the bob loop and any descent still running, without touching
        /// the Transform — callers decide separately whether to then restore position/scale
        /// (LowerDeselect) or leave it as-is (CancelSelectionAnimation, called right before the
        /// GameObject is destroyed anyway).
        /// </summary>
        private void StopLiftTweens()
        {
            _liftSequence.Stop();
            _bobTween.Stop();
            _lowerPositionTween.Stop();
            _lowerScaleTween.Stop();
        }

        /// <summary>
        /// Ends a descent on the square it was heading for. A piece being set down is the one thing
        /// here with nowhere of its own to get to, so finishing it early costs nothing and cutting
        /// it short leaves the piece standing above its square.
        /// </summary>
        private void FinishAnyDescent()
        {
            if (_lowerPositionTween.isAlive) _lowerPositionTween.Complete();
            if (_lowerScaleTween.isAlive) _lowerScaleTween.Complete();
        }

        /// <summary>
        /// Enables the inverted-hull selection ring and tweens its width from wherever it
        /// currently is up to the material-authored width, with a slight overshoot so the ring
        /// "pops" on rather than fading in. No-op (with a one-time warning) if the outline
        /// material or the piece's mesh can't be found — selection still works, just unringed.
        /// </summary>
        private void ShowSelectionOutline()
        {
            if (!TryEnsureOutlineRenderer()) return;

            _outlineRenderer.enabled = true;
            _outlineTween.Stop();
            _outlineTween = Tween.Custom(this, _currentOutlineWidth, _outlineTargetWidth, OutlineShowDuration,
                (self, width) => self.ApplyOutlineWidth(width), Ease.OutBack, useUnscaledTime: true);
        }

        /// <summary>
        /// Shrinks the ring's width back to zero and disables its renderer. instant = true snaps
        /// with no tween — for teardown (piece about to be destroyed) and the defensive path in
        /// MoveToInternal, where there's no visual moment left to ease through.
        /// </summary>
        private void HideSelectionOutline(bool instant)
        {
            if (_outlineRenderer == null) return;

            _outlineTween.Stop();

            if (instant)
            {
                ApplyOutlineWidth(0f);
                _outlineRenderer.enabled = false;
                return;
            }

            _outlineTween = Tween.Custom(this, _currentOutlineWidth, 0f, OutlineHideDuration,
                    (self, width) => self.ApplyOutlineWidth(width), Ease.InQuad, useUnscaledTime: true)
                .OnComplete(this, self =>
                {
                    if (self._outlineRenderer != null) self._outlineRenderer.enabled = false;
                });
        }

        /// <summary>
        /// Lazily builds the outline as a child renderer sharing the piece's own mesh, the first
        /// time this piece is selected. A child MeshRenderer (rather than appending a second
        /// material to the piece's renderer) keeps the piece's own material list untouched —
        /// appending would instance a per-renderer materials array and break batching for every
        /// piece that was ever selected, not just the currently selected one.
        /// </summary>
        private bool TryEnsureOutlineRenderer()
        {
            if (_outlineRenderer != null) return true;
            if (_renderer == null) return false;

            if (_selectionOutlineMaterial == null)
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] No selection outline material was " +
                    "injected (see ChessPiece.SetSelectionOutlineMaterial) — this piece will select without an outline ring.");
                return false;
            }

            // Inverted hull needs the same mesh to extrude; a piece without a MeshFilter (e.g. a
            // hypothetical skinned piece) just doesn't get a ring rather than erroring.
            MeshFilter sourceFilter = _renderer.GetComponent<MeshFilter>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null) return false;

            var outlineObject = new GameObject("SelectionOutline");
            outlineObject.transform.SetParent(_renderer.transform, false);

            MeshFilter outlineFilter = outlineObject.AddComponent<MeshFilter>();
            outlineFilter.sharedMesh = sourceFilter.sharedMesh;

            _outlineRenderer = outlineObject.AddComponent<MeshRenderer>();
            _outlineRenderer.sharedMaterial = _selectionOutlineMaterial;
            // The hull is a pure view-space marker: it must never darken the board with a second
            // shadow of the piece, and it samples no lighting, so skip every lighting system.
            _outlineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _outlineRenderer.receiveShadows = false;
            _outlineRenderer.lightProbeUsage = LightProbeUsage.Off;
            _outlineRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _outlineRenderer.enabled = false;

            // The target width is authored on the material (designer-tunable), read once here;
            // the per-frame animated value goes through a MaterialPropertyBlock so the shared
            // material asset itself is never mutated at runtime.
            _outlineTargetWidth = _selectionOutlineMaterial.GetFloat(OutlineWidthId);
            ApplyOutlineWidth(0f);
            return true;
        }

        private void ApplyOutlineWidth(float width)
        {
            _currentOutlineWidth = width;
            if (_outlineRenderer == null) return;

            _outlineMpb ??= new MaterialPropertyBlock();
            _outlineRenderer.GetPropertyBlock(_outlineMpb);
            _outlineMpb.SetFloat(OutlineWidthId, width);
            _outlineRenderer.SetPropertyBlock(_outlineMpb);
        }

        /// <summary>
        /// Guards against feeding NaN/Infinity into a tween — a stray divide-by-zero upstream
        /// would otherwise silently teleport a piece off the board instead of failing loudly.
        /// </summary>
        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
