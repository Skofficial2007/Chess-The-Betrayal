using System;
using UnityEngine;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// An IPieceAnimator that applies every change instantly with no tween. A Transform still gets
    /// updated (unlike a true no-op) so board state remains correct and immediately queryable —
    /// only the interpolation is skipped. Used to keep AI self-play and headless/EditMode tests
    /// free of PrimeTween and real frame time.
    /// </summary>
    public sealed class NullPieceAnimator : IPieceAnimator
    {
        // Matches PrimeTweenPieceAnimator's lift height so headless/AI selection logic that reads
        // world position back (if any ever does) sees the same rest/lifted offset either way.
        private const float LiftHeight = 0.3f;

        private readonly Transform _transform;
        private Vector3? _restPosition;

        public NullPieceAnimator(Transform transform)
        {
            _transform = transform;
        }

        public void MoveTo(Vector3 worldPos, bool force = false) => _transform.position = worldPos;

        public void ScaleTo(Vector3 scale, bool force = false) => _transform.localScale = scale;

        public void FaceDirection(Vector3 lookDirection) =>
            _transform.rotation = Quaternion.LookRotation(lookDirection == Vector3.zero ? Vector3.forward : lookDirection);

        // No visual highlight in a headless context — nothing reads it, so there's nothing to do.
        public void SetHighlighted(bool active) { }

        // There's no tween to play, but onComplete must still fire — and fire synchronously, since
        // BoardVisuals relies on it to know the instant it should Destroy the outgoing piece and
        // spawn its replacement. Skipping the callback here would leave promotion/defection stuck
        // forever in headless/AI runs.
        public void PlayTransitionOut(PieceTransitionStyle style, Action onComplete) => onComplete?.Invoke();

        // The replacement piece is already in its final state the moment SpawnSinglePiece places
        // it — there's no "reveal" to play without a tween.
        public void PlayTransitionIn(PieceTransitionStyle style) { }

        public void LiftSelect()
        {
            _restPosition = _transform.position;
            _transform.position += new Vector3(0f, LiftHeight, 0f);
        }

        public void LowerDeselect()
        {
            if (_restPosition.HasValue)
            {
                _transform.position = _restPosition.Value;
                _restPosition = null;
            }
        }

        // No tween is ever running here, so cancelling is just forgetting the remembered rest
        // position — there's nothing to stop.
        public void CancelSelectionAnimation() => _restPosition = null;
    }
}
