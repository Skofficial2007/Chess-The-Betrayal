using UnityEngine;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// An IPieceAnimator that applies every change instantly with no tween — a Transform still
    /// gets updated (unlike a true no-op), it just never interpolates. Used to keep AI self-play
    /// and headless/EditMode tests free of PrimeTween and real frame time while still leaving
    /// ChessPiece's Transform in a correct, immediately-queryable state.
    /// </summary>
    public sealed class NullPieceAnimator : IPieceAnimator
    {
        private readonly Transform _transform;

        public NullPieceAnimator(Transform transform)
        {
            _transform = transform;
        }

        public void MoveTo(Vector3 worldPos, bool force = false) => _transform.position = worldPos;
        public void ScaleTo(Vector3 scale, bool force = false) => _transform.localScale = scale;
        public void FaceDirection(Vector3 lookDirection) =>
            _transform.rotation = Quaternion.LookRotation(lookDirection == Vector3.zero ? Vector3.forward : lookDirection);
        public void SetHighlighted(bool active) { }
    }
}
