using System.Collections.Generic;
using UnityEngine;
using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.View
{
    /// <summary>
    /// Purely visual component attached to piece prefabs.
    /// Handles smooth interpolation (Lerping) of position and scale.
    /// Contains ZERO game logic - doesn't know chess rules, move validation, or board state.
    /// </summary>
    public class ChessPiece : MonoBehaviour
    {
        [Tooltip("Visual identifier for the inspector (set by BoardVisuals on spawn)")]
        public Team team;
        
        [Tooltip("Piece type for visual reference")]
        public ChessPieceType type;

        // Internal smoothing targets
        private Vector3 targetPosition;
        private Vector3 targetScale = Vector3.one;
        private Collider _col;

        // Smoothing speeds
        private const float PositionLerpSpeed = 12f;
        private const float ScaleLerpSpeed = 12f;

        private void Awake()
        {
            targetPosition = transform.position;
            targetScale = transform.localScale;

            _col = GetComponent<Collider>();
        }

        private void Update()
        {
            bool isMoving = false;

            // 1. Position Interpolation
            if ((transform.position - targetPosition).sqrMagnitude > 0.000001f)
            {
                transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * PositionLerpSpeed);
                isMoving = true;
            }
            else
            {
                // Snap to exact target to prevent floating point micro-drifting
                transform.position = targetPosition;
            }

            // 2. Scale Interpolation
            if ((transform.localScale - targetScale).sqrMagnitude > 0.000001f)
            {
                transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * ScaleLerpSpeed);
                isMoving = true;
            }
            else
            {
                // Snap to exact scale
                transform.localScale = targetScale;
            }

            // 3. Put the piece to sleep if it has reached its destination
            if (!isMoving)
            {
                enabled = false;
            }
        }

        /// <summary>
        /// Set target world position for this piece to smoothly slide towards.
        /// Force = true will snap instantly without interpolation.
        /// </summary>
        public void SetPosition(Vector3 worldPos, bool force = false)
        {
            if (!IsFinite(worldPos))
            {
                Debug.LogWarning($"[ChessPiece] SetPosition given non-finite vector for {name}. Ignoring.");
                return;
            }

            targetPosition = worldPos;
            if (force)
            {
                transform.position = worldPos;
            }
            else
            {
                // Wake the Update loop back up to begin interpolating
                enabled = true;
            }
        }

        /// <summary>
        /// Set target local scale (used for death pile shrinking and initial spawn).
        /// Force = true will snap instantly without interpolation.
        /// </summary>
        public void SetScale(Vector3 scale, bool force = false)
        {
            if (!IsFinite(scale) || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
            {
                Debug.LogWarning($"[ChessPiece] SetScale given invalid scale {scale} for {name}. Ignoring.");
                return;
            }

            targetScale = scale;
            if (force)
            {
                transform.localScale = scale;
            }
            else
            {
                // Wake the Update loop back up to begin interpolating
                enabled = true;
            }
        }

        /// <summary>
        /// Validates that a vector contains finite values (not NaN or Infinity).
        /// </summary>
        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
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
    }
}
