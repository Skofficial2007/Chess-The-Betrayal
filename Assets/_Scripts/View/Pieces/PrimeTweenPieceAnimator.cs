using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.Rendering;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.View
{
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

        // Per-style board-move feel. Quiet/Capture are both slightly longer than the legacy flat
        // MoveDuration so the glide reads as deliberate motion rather than a snap; Knight is a
        // touch slower still to give the arc room to read. Capture gets a brief landing punch;
        // Knight arcs over the board via an extra Y-height tween run in parallel with the XZ slide.
        private const float QuietMoveDuration = 0.22f;
        private const float CaptureMoveDuration = 0.2f;
        private const float KnightMoveDuration = 0.26f;
        private const float PromotionMoveDuration = 0.28f;
        private static readonly Ease BoardMoveEase = Ease.InOutCubic;

        // Castling's rook glide: its own (slightly shorter) duration rather than reusing
        // QuietMoveDuration, so a rook that starts CastleStartDelay seconds after the king still
        // arrives at essentially the same moment — the king leads, the rook tucks in right behind
        // it, rather than visibly trailing. Same BoardMoveEase (InOutCubic) as every other board
        // glide, per the "travel = weight" easing vocabulary.
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
        //   2. Leap: one continuous parabolic arc from start tile to landing tile, peaking well
        //      above the victim's head. Growing to 1.15x mid-air on the way up (jumping things get
        //      bigger, per every good cartoon), landing still oversized.
        //   3. Descent: onDescentStart fires at the arc's peak (t=0.5) — the earliest moment that
        //      still reads as "the attacker is now falling toward you" — so the victim's
        //      cower-shrink has the entire second half of the arc (not just a short fall leg) to
        //      get out of the way before contact.
        //   4. Impact: flatten hard against the tile, then a big springy overshoot back to rest
        //      scale — the "settling back to normal size" that closes the arc — and a settle bob.
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
        // Peak height above the higher of start/land Y. Pieces are ~1 unit tall at this board
        // scale, so 1.3 clears even the King/Queen's head with visible air underneath.
        private const float StampLeapHeight = 1.3f;
        // Mid-air growth: the attacker swells on the way up and stays swollen through the landing,
        // only releasing back to rest scale in the post-impact recover — "big things fall hard."
        private const float StampAirborneScaleFactor = 1.15f;
        // Slight overshoot on the mid-air growth so the swell pops rather than inflates linearly.
        // Growth spans the whole rise half (0 -> 0.5) of the single leap driver.
        private const float StampAirborneGrowOvershoot = 1.3f;

        private const float StampImpactSquashDuration = 0.06f;
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
        private const float EnPassantDeathDuration = 0.34f;
        private const float EnPassantDeathHopHeight = 0.22f;
        private static readonly Ease EnPassantDeathMoveEase = Ease.InOutCubic;
        private static readonly Ease EnPassantDeathHopEase = Ease.OutQuad;
        private static readonly Ease EnPassantDeathScaleEase = Ease.InQuad;

        // The arc's own ease now comes from whatever style called MoveToInternal (BoardMoveEase,
        // same InOutCubic as every other board move) via ApplyKnightArc's single Tween.Custom
        // driver — a separate arc-specific ease would fight the "travel = weight" vocabulary.
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
        private Tween _shakeTween;
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

        private Tween _moveTween;
        private Sequence _punchSequence;
        private Tween _scaleTween;
        private Sequence _transitionSequence;
        private Sequence _castleSequence;
        private Tween _settleBobTween;
        private float? _settleBobBaseY;
        private Sequence _stampSequence;
        private MaterialPropertyBlock _mpb;

        // Selection-lift state. _liftRestPosition/_liftRestScale are captured the moment
        // LiftSelect() runs, so LowerDeselect() and CancelSelectionAnimation() can restore the
        // exact pre-lift transform even if the bob loop or rise tween is still mid-flight.
        private Sequence _liftSequence;
        private Tween _bobTween;
        private Vector3? _liftRestPosition;
        private Vector3 _liftRestScale;

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

        public void MoveTo(Vector3 worldPos, MoveStyle style, bool force = false)
        {
            switch (style)
            {
                case MoveStyle.Capture:
                    MoveToInternal(worldPos, CaptureMoveDuration, BoardMoveEase, punch: true, arc: false, force);
                    break;
                case MoveStyle.Knight:
                    MoveToInternal(worldPos, KnightMoveDuration, BoardMoveEase, punch: false, arc: true, force);
                    break;
                case MoveStyle.Promotion:
                    MoveToInternal(worldPos, PromotionMoveDuration, BoardMoveEase, punch: false, arc: false, force);
                    break;
                case MoveStyle.Quiet:
                default:
                    MoveToInternal(worldPos, QuietMoveDuration, BoardMoveEase, punch: false, arc: false, force);
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

            _moveTween.Stop();
            _punchSequence.Stop();
            _castleSequence.Stop();
            _settleBobTween.Stop();
            _settleBobBaseY = null;
            _stampSequence.Stop();

            // A caller driving MoveTo directly (a board move, castling, snap-back) means the piece
            // is no longer conceptually "lifted" — stop any in-flight lift/bob so they can't fight
            // over the Transform. In normal play selection is always cleared before a move
            // executes, so this is defense-in-depth rather than a path that fires every move.
            StopLiftTweens();
            _liftRestPosition = null;
            // Same defense for the selection ring: a piece that's moving is no longer selected,
            // so if the ring's fade-out is still in flight (or a stale select left it on), kill it
            // now rather than let a glowing outline glide across the board.
            HideSelectionOutline(instant: true);

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

            _moveTween.Stop();
            _castleSequence.Stop();
            _settleBobTween.Stop();
            _settleBobBaseY = null;
            _stampSequence.Stop();
            StopLiftTweens();
            _liftRestPosition = null;
            HideSelectionOutline(instant: true);

            // The startDelay is what makes this a staggered two-piece maneuver rather than a
            // simultaneous teleport: the rook's glide is chained after a Delay so it visibly
            // starts a beat behind the king (BoardVisuals kicks off the king's own MoveTo at the
            // same instant, with startDelay = 0), then the same InOutCubic "travel = weight"
            // easing every other board glide uses. PlaySettleBob at the end is the "tucks in"
            // beat — a barely-there bob rather than the overshoot-heavy selection lift, since this
            // is a piece arriving, not a piece being picked up.
            _castleSequence = Sequence.Create(useUnscaledTime: true)
                .Chain(Tween.Delay(startDelay, useUnscaledTime: true))
                .Chain(Tween.Position(_transform, worldPos, CastleRookMoveDuration, BoardMoveEase, useUnscaledTime: true))
                .ChainCallback(() =>
                {
                    PlaySettleBob();
                    onSettled?.Invoke();
                });
        }

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

        public void PlayCaptureStamp(Vector3 worldPos, Action onDescentStart = null, Action onSettled = null)
        {
            if (!IsFinite(worldPos))
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] PlayCaptureStamp given non-finite vector for {_transform.name}. Ignoring.");
                onDescentStart?.Invoke();
                onSettled?.Invoke();
                return;
            }

            _moveTween.Stop();
            _punchSequence.Stop();
            _castleSequence.Stop();
            _settleBobTween.Stop();
            _settleBobBaseY = null;
            _stampSequence.Stop();
            StopLiftTweens();
            _liftRestPosition = null;
            HideSelectionOutline(instant: true);

            Vector3 restScale = _transform.localScale;
            Vector3 startPos = _transform.position;
            Vector3 landPos = worldPos;
            float peakY = Mathf.Max(startPos.y, landPos.y) + StampLeapHeight;

            Vector3 crouchScale = restScale * StampAnticipationScaleFactor;
            // Pulled back along the direction of travel, away from the victim — a boxer loading a
            // punch draws the fist back first. Flattened to the XZ plane (Y untouched) since this
            // is a wind-up, not a hop.
            Vector3 towardVictim = worldPos - startPos;
            towardVictim.y = 0f;
            Vector3 pullBackPos = startPos - towardVictim.normalized * StampAnticipationPullBack * Mathf.Min(1f, towardVictim.magnitude);
            pullBackPos.y = startPos.y - StampAnticipationCrouchDrop;

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
            _stampSequence = Sequence.Create(useUnscaledTime: true)
                // 1. Anticipation: pull back and crouch down — a held breath before the pounce.
                .Group(Tween.Position(_transform, pullBackPos, StampAnticipationDuration, StampAnticipationEase, useUnscaledTime: true))
                .Group(Tween.Scale(_transform, crouchScale, StampAnticipationDuration, StampAnticipationEase, useUnscaledTime: true))
                // 2. Leap, first half (0 -> 0.5): one driver tween computes XZ (lerp) and Y (the
                // rising half of a parabola) from the same progress value every frame, so
                // "how far across" and "how high up" can never drift apart — the piece is
                // physically guaranteed to already be near peak height by the time it's over the
                // victim's tile, instead of two independently-eased tweens letting horizontal
                // catch up before vertical does (exactly the bug that caused visible overlap).
                .Chain(Tween.Custom(this, 0f, 0.5f, halfDuration, (self, t) => self.ApplyStampArc(t, startPos, landPos, peakY), Ease.OutQuad, useUnscaledTime: true))
                // Swell from the crouch to the airborne size across the rise, with a small
                // overshoot so the growth pops — jumping things get bigger, per every good cartoon.
                .Group(Tween.Scale(_transform, airborneScale, halfDuration, Easing.Overshoot(StampAirborneGrowOvershoot), useUnscaledTime: true))
                // 3. onDescentStart fires exactly at the arc's peak (t=0.5) — the earliest moment
                // that still reads as "now falling toward you" — giving the victim's cower-shrink
                // the entire second half of the arc to get small before contact.
                .ChainCallback(() => onDescentStart?.Invoke())
                // Leap, second half (0.5 -> 1): same driver, same coupled XZ/Y formula, continuing
                // seamlessly from the peak down to the landing tile.
                .Chain(Tween.Custom(this, 0.5f, 1f, halfDuration, (self, t) => self.ApplyStampArc(t, startPos, landPos, peakY), Ease.InQuad, useUnscaledTime: true))
                // 4. Impact: flatten hard against the tile (still oversized — a big flat slap)...
                .Chain(Tween.Scale(_transform, impactScale, StampImpactSquashDuration, Ease.OutQuad, useUnscaledTime: true))
                // ...then recover with a big springy overshoot back down to rest scale — this is
                // the "settling back to its normal size" beat that closes the whole arc.
                .Chain(Tween.Scale(_transform, restScale, StampImpactRecoverDuration, Easing.Overshoot(StampRecoverOvershoot), useUnscaledTime: true))
                .ChainCallback(PlaySettleBob)
                .ChainCallback(() => onSettled?.Invoke());
        }

        /// <summary>
        /// Places the transform along the stamp's leap arc at normalized progress t (0 = takeoff,
        /// 1 = landing): XZ is a straight lerp between startPos/landPos, Y is a true parabola
        /// (4 * peakOffset * t * (1-t), zero at both ends, peakOffset at t=0.5) added on top of the
        /// lerped baseline height. Driving both axes from the same t is what guarantees horizontal
        /// and vertical progress can never drift apart — see PlayCaptureStamp's call site.
        /// </summary>
        private void ApplyStampArc(float t, Vector3 startPos, Vector3 landPos, float peakY)
        {
            float x = Mathf.Lerp(startPos.x, landPos.x, t);
            float z = Mathf.Lerp(startPos.z, landPos.z, t);
            float baseline = Mathf.Lerp(startPos.y, landPos.y, t);

            // Parabola: 0 at t=0 and t=1, (peakY - higherStartLandY) at t=0.5. Added on top of the
            // straight-line baseline so the arc still smoothly reaches exactly landPos.y at t=1
            // even when startPos.y != landPos.y (a board with tilesYOffset/uneven tiles).
            float higherY = Mathf.Max(startPos.y, landPos.y);
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
            _moveTween.Stop();
            _punchSequence.Stop();
            _castleSequence.Stop();
            _settleBobTween.Stop();
            _settleBobBaseY = null;
            _stampSequence.Stop();
            StopLiftTweens();
            _liftRestPosition = null;
            HideSelectionOutline(instant: true);

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

        public void PlayEnPassantDeath(Vector3 graveyardWorldPos, Action onArrived)
        {
            if (!IsFinite(graveyardWorldPos))
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] PlayEnPassantDeath given non-finite vector for {_transform.name}. Ignoring.");
                onArrived?.Invoke();
                return;
            }

            _moveTween.Stop();
            _punchSequence.Stop();
            _castleSequence.Stop();
            _settleBobTween.Stop();
            _settleBobBaseY = null;
            _stampSequence.Stop();
            StopLiftTweens();
            _liftRestPosition = null;
            HideSelectionOutline(instant: true);

            Vector3 startPos = _transform.position;

            // The attacker never visually touches this piece (it's captured on a different square
            // than the one it lands on), so instead of a crush it plays its own "swept off the
            // board" beat: a small hop — same InOutCubic/OutQuad vocabulary as a normal board move,
            // just with the piece shrinking to nothing across the same glide rather than teleporting
            // to the graveyard at full size and only then scaling down. XZ and Y are driven by
            // separate tweens (same pattern as PlayCaptureStamp's leap) so the horizontal glide and
            // the vertical hop-arc don't fight over the Y axis.
            _stampSequence = Sequence.Create(useUnscaledTime: true)
                .Group(Tween.Position(_transform, new Vector3(graveyardWorldPos.x, startPos.y, graveyardWorldPos.z), EnPassantDeathDuration, EnPassantDeathMoveEase, useUnscaledTime: true))
                .Group(Tween.Scale(_transform, VanishedScale, EnPassantDeathDuration, EnPassantDeathScaleEase, useUnscaledTime: true))
                .Group(Sequence.Create(useUnscaledTime: true)
                    .Chain(Tween.PositionY(_transform, startPos.y + EnPassantDeathHopHeight, EnPassantDeathDuration * 0.5f, EnPassantDeathHopEase, useUnscaledTime: true))
                    .Chain(Tween.PositionY(_transform, graveyardWorldPos.y, EnPassantDeathDuration * 0.5f, Ease.InQuad, useUnscaledTime: true)))
                .ChainCallback(() => onArrived?.Invoke());
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

        public void Shake()
        {
            // A check delivered while a previous shake is still mid-flight (rare, but a fast forced
            // sequence can do it) would otherwise read the half-finished offset as the new rest
            // pose. Stop() cancels the tween but does not restore the transform, so if one was live,
            // restore the cached rest pose first, then read the true rest — the same
            // snap-before-reread guard PlaySettleBob uses for its baseY. Checked before Stop() since
            // Stop() clears isAlive.
            bool wasShaking = _shakeTween.isAlive;
            _shakeTween.Stop();
            if (wasShaking)
            {
                _transform.position = _shakeRestPosition;
                _transform.localRotation = _shakeRestRotation;
            }
            _shakeRestPosition = _transform.position;
            _shakeRestRotation = _transform.localRotation;

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

        public void LiftSelect()
        {
            // Re-lifting an already-lifted piece (a stale/duplicate select) would otherwise stack
            // a second rest position on top of the lifted one, so restart from a clean slate first.
            StopLiftTweens();

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
                Tween.Position(_transform, _liftRestPosition.Value, LiftLowerDuration, Ease.OutQuad, useUnscaledTime: true);
            }
            if (_transform.localScale != _liftRestScale)
            {
                Tween.Scale(_transform, _liftRestScale, LiftLowerDuration, Ease.OutQuad, useUnscaledTime: true);
            }

            _liftRestPosition = null;
            HideSelectionOutline(instant: false);
        }

        public void CancelSelectionAnimation()
        {
            StopLiftTweens();
            _liftRestPosition = null;
            // Teardown path — the piece may be mid-destroy, so snap the ring off with no fade.
            HideSelectionOutline(instant: true);
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
        /// Stops the lift sequence and bob loop without touching the Transform — callers decide
        /// separately whether to then restore position/scale (LowerDeselect) or leave it as-is
        /// (CancelSelectionAnimation, called right before the GameObject is destroyed anyway).
        /// </summary>
        private void StopLiftTweens()
        {
            _liftSequence.Stop();
            _bobTween.Stop();
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
