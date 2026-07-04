using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Real, tweened IPieceAnimator used for human play. Animates a single piece's Transform via
    /// PrimeTween — chosen over a hand-rolled per-frame Lerp because PrimeTween safely no-ops
    /// against a target destroyed mid-tween (pieces get Destroy()d for capture, promotion, and
    /// defection while a move or lift tween could still be running) and is zero-allocation.
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

        // Promotion/defection transition timings: "out" is a quick anticipation beat, "in" is the
        // slightly longer payoff so the swap reads as deliberate rather than a glitch.
        private const float SquashOutDuration = 0.12f;
        private const float SquashInDuration = 0.2f;
        private const float SpinOutDuration = 0.15f;
        private const float SpinInDuration = 0.2f;

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

        private static readonly Color BetrayerGlowColor = Color.red * 2f;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly Transform _transform;
        private readonly Renderer _renderer;
        private readonly Func<ChessPieceType> _getType;

        private Tween _moveTween;
        private Tween _scaleTween;
        private Sequence _transitionSequence;
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

        public void MoveTo(Vector3 worldPos, bool force = false)
        {
            if (!IsFinite(worldPos))
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] MoveTo given non-finite vector for {_transform.name}. Ignoring.");
                return;
            }

            _moveTween.Stop();

            // A caller driving MoveTo directly (a board move, castling, snap-back) means the piece
            // is no longer conceptually "lifted" — stop any in-flight lift/bob so they can't fight
            // over the Transform. In normal play selection is always cleared before a move
            // executes, so this is defense-in-depth rather than a path that fires every move.
            StopLiftTweens();
            _liftRestPosition = null;

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

            _moveTween = Tween.Position(_transform, worldPos, MoveDuration, MoveEase);
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

            _scaleTween = Tween.Scale(_transform, scale, ScaleDuration, ScaleEase);
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
            _mpb.SetColor(EmissionColorId, active ? BetrayerGlowColor : Color.black);
            _renderer.SetPropertyBlock(_mpb);
        }

        public void PlayTransitionOut(PieceTransitionStyle style, Action onComplete)
        {
            _transitionSequence.Stop();

            switch (style)
            {
                case PieceTransitionStyle.Spin:
                    // Quarter-turn to edge-on, as if the piece is turning away from the camera.
                    // The swap happens the instant it's edge-on, so the incoming prefab's face is
                    // what rotates back into view during PlayTransitionIn — the spin sells "this
                    // piece turned into something else" without any shader or dissolve work.
                    _transitionSequence = Sequence.Create(Tween.LocalRotation(_transform, new Vector3(0f, 90f, 0f), SpinOutDuration, Ease.InQuad))
                        .ChainCallback(onComplete);
                    break;

                case PieceTransitionStyle.Squash:
                default:
                    // Anticipation squash down to near-zero scale, then swap — reads as "this piece
                    // collapses into its promoted form" rather than a jump-cut.
                    _transitionSequence = Sequence.Create(Tween.Scale(_transform, VanishedScale, SquashOutDuration, Ease.InBack))
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
                    // Start edge-on relative to this piece's own resting rotation (mirroring where
                    // the outgoing piece left off in PlayTransitionOut), then spin the remaining
                    // quarter-turn back to facing forward. Computed relative to the resting
                    // rotation rather than a hardcoded value because enemy-facing prefabs are
                    // pre-rotated 180° at spawn (see BoardVisuals.SpawnSinglePiece).
                    Quaternion restingRotation = _transform.localRotation;
                    Vector3 restingEuler = restingRotation.eulerAngles;
                    _transform.localRotation = restingRotation * Quaternion.Euler(0f, -90f, 0f);
                    _transitionSequence = Sequence.Create(Tween.LocalRotation(_transform, restingEuler, SpinInDuration, Ease.OutBack));
                    break;

                case PieceTransitionStyle.Squash:
                default:
                    // Spawn at vanished scale and pop back up to whatever scale BoardVisuals just
                    // set (pieceScaleMultiplier), with a slight overshoot for punch.
                    Vector3 targetScale = _transform.localScale;
                    _transform.localScale = Vector3.one * VanishedScale;
                    _transitionSequence = Sequence.Create(Tween.Scale(_transform, targetScale, SquashInDuration, Easing.Overshoot(1.5f)));
                    break;
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

            _liftSequence = Sequence.Create()
                // 1. Quick anticipatory squash — sells the weight of the piece being gripped.
                .Chain(Tween.Scale(_transform, squashScale, LiftSquashDuration, Ease.OutQuad))
                // 2. Rise to lift height and recover scale at the same time (Group, not Chain —
                // both must play in parallel), each with a slight overshoot so the settle feels
                // springy rather than mechanical.
                .Chain(Tween.Position(_transform, liftedPosition, LiftRiseDuration, Easing.Overshoot(LiftOvershootStrength)))
                .Group(Tween.Scale(_transform, _liftRestScale, LiftRiseDuration, Easing.Overshoot(LiftOvershootStrength)))
                .ChainCallback(StartBobLoop);
        }

        public void LowerDeselect()
        {
            StopLiftTweens();

            if (!_liftRestPosition.HasValue) return;

            // No overshoot on the way down — a lift feels snappy and eager, a landing should feel
            // like gently setting the piece back on the board.
            Tween.Position(_transform, _liftRestPosition.Value, LiftLowerDuration, Ease.OutQuad);
            Tween.Scale(_transform, _liftRestScale, LiftLowerDuration, Ease.OutQuad);

            _liftRestPosition = null;
        }

        public void CancelSelectionAnimation()
        {
            StopLiftTweens();
            _liftRestPosition = null;
        }

        private void StartBobLoop()
        {
            // A very subtle infinite up/down drift while the piece stays selected — 2-3mm of travel
            // is intentionally barely perceptible; it's there to make the selection feel alive, not
            // to draw attention to itself. cycles: -1 + CycleMode.Yoyo loops until explicitly
            // stopped by StopLiftTweens (LowerDeselect/CancelSelectionAnimation/a fresh LiftSelect).
            float baseY = _transform.position.y;
            _bobTween = Tween.PositionY(_transform, baseY, baseY + BobAmplitude, BobDuration / 2f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo);
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
