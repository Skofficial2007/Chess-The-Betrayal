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

        // Smoothing speeds
        private const float PositionLerpSpeed = 12f;
        private const float ScaleLerpSpeed = 12f;

        private void Awake()
        {
            targetPosition = transform.position;
            targetScale = transform.localScale;
        }

        private void Update()
        {
            // Smooth position interpolation
            if ((transform.position - targetPosition).sqrMagnitude > 0.000001f)
            {
                transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * PositionLerpSpeed);
            }
            else
            {
                transform.position = targetPosition;
            }

            // Smooth scale interpolation
            if ((transform.localScale - targetScale).sqrMagnitude > 0.000001f)
            {
                transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * ScaleLerpSpeed);
            }
            else
            {
                transform.localScale = targetScale;
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
        }

        /// <summary>
        /// Validates that a vector contains finite values (not NaN or Infinity).
        /// </summary>
        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
