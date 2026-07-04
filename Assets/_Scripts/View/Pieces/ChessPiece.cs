using UnityEngine;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Purely visual component attached to piece prefabs.
    /// Contains ZERO game logic - doesn't know chess rules, move validation, or board state.
    /// Delegates all animation to an IPieceAnimator so BoardVisuals can keep orchestrating what
    /// happens to a piece without knowing or caring how it's animated.
    /// </summary>
    public class ChessPiece : MonoBehaviour
    {
        [Tooltip("Visual identifier for the inspector (set by BoardVisuals on spawn)")]
        public Team team;

        [Tooltip("Piece type for visual reference")]
        public ChessPieceType type;

        private Collider _col;
        private IPieceAnimator _animator;

        private void Awake()
        {
            _col = GetComponent<Collider>();
            _animator = new PrimeTweenPieceAnimator(transform, GetComponentInChildren<Renderer>());
        }

        /// <summary>
        /// Swaps the animator this piece uses — e.g. NullPieceAnimator for headless/EditMode
        /// tests that need an instant, tween-free Transform. Must be called before any of the
        /// methods below if the default PrimeTween-backed animator isn't wanted.
        /// </summary>
        public void SetAnimator(IPieceAnimator animator)
        {
            _animator = animator;
        }

        /// <summary>
        /// Set target world position for this piece to smoothly slide towards.
        /// Force = true will snap instantly without interpolation.
        /// </summary>
        public void SetPosition(Vector3 worldPos, bool force = false)
        {
            _animator.MoveTo(worldPos, force);
        }

        /// <summary>
        /// Set target local scale (used for death pile shrinking and initial spawn).
        /// Force = true will snap instantly without interpolation.
        /// </summary>
        public void SetScale(Vector3 scale, bool force = false)
        {
            _animator.ScaleTo(scale, force);
        }

        /// <summary>
        /// Instantly turns the piece to face lookDirection (used for death-pile facing).
        /// </summary>
        public void FaceDirection(Vector3 lookDirection)
        {
            _animator.FaceDirection(lookDirection);
        }

        /// <summary>
        /// Turns off the piece's collider so it can no longer be clicked.
        /// </summary>
        public void DisableCollider()
        {
            if (_col != null)
            {
                _col.enabled = false;
            }
        }

        /// <summary>
        /// Toggles the temporary "Betrayer" glow.
        /// </summary>
        public void SetBetrayerGlow(bool active)
        {
            _animator.SetHighlighted(active);
        }
    }
}
