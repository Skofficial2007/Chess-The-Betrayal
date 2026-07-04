using PrimeTween;
using UnityEngine;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Real, tweened IPieceAnimator used for human play. Animates a single piece's Transform via
    /// PrimeTween — chosen over a hand-rolled per-frame Lerp because PrimeTween safely no-ops
    /// against a target destroyed mid-tween (pieces get Destroy()d for capture/promotion/defection
    /// while a move or lift tween could still be running) and is zero-allocation.
    /// </summary>
    public sealed class PrimeTweenPieceAnimator : IPieceAnimator
    {
        private const float MoveDuration = 1f / 12f;
        private const float ScaleDuration = 1f / 12f;
        private static readonly Ease MoveEase = Ease.OutQuad;
        private static readonly Ease ScaleEase = Ease.OutQuad;

        private static readonly Color BetrayerGlowColor = Color.red * 2f;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly Transform _transform;
        private readonly Renderer _renderer;

        private Tween _moveTween;
        private Tween _scaleTween;
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
            }
            else
            {
                _moveTween = Tween.Position(_transform, worldPos, MoveDuration, MoveEase);
            }
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
            }
            else
            {
                _scaleTween = Tween.Scale(_transform, scale, ScaleDuration, ScaleEase);
            }
        }

        public void FaceDirection(Vector3 lookDirection)
        {
            _transform.rotation = Quaternion.LookRotation(lookDirection == Vector3.zero ? Vector3.forward : lookDirection);
        }

        public void SetHighlighted(bool active)
        {
            if (_renderer == null) return;

            _mpb ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColorId, active ? BetrayerGlowColor : Color.black);
            _renderer.SetPropertyBlock(_mpb);
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
