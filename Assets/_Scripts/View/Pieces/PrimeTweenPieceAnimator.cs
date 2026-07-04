using System;
using PrimeTween;
using UnityEngine;

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

        private static readonly Color BetrayerGlowColor = Color.red * 2f;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly Transform _transform;
        private readonly Renderer _renderer;

        private Tween _moveTween;
        private Tween _scaleTween;
        private Sequence _transitionSequence;
        private MaterialPropertyBlock _mpb;

        public PrimeTweenPieceAnimator(Transform transform, Renderer renderer)
        {
            _transform = transform;
            _renderer = renderer;
        }

        public void MoveTo(Vector3 worldPos, bool force = false)
        {
            if (!IsFinite(worldPos))
            {
                Debug.LogWarning($"[{nameof(PrimeTweenPieceAnimator)}] MoveTo given non-finite vector for {_transform.name}. Ignoring.");
                return;
            }

            _moveTween.Stop();

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
