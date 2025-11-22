using System.Collections.Generic;
using UnityEngine;

namespace ChessTheMasterPiece.ChessPiece
{
    public enum ChessPieceType
    {
        None = 0,
        Pawn = 1,
        Rook = 2,
        Knight = 3,
        Bishop = 4,
        Queen = 5,
        King = 6
    }

    public enum SpecialMove
    {
        None = 0,
        EnPassant = 1,
        Castling = 2,
        Promotion = 3,
    }

    /// <summary>
    /// Lightweight piece component.
    /// -> stores board coords + type + simple lerp smoothing for position and scale
    /// -> SetPosition/SetScale validate inputs and provide force option
    /// </summary>
    public class ChessPiece : MonoBehaviour
    {
        [Tooltip("0 = white, 1 = black")]
        public int team;

        [Tooltip("Grid X coordinate")]
        public int currentX;

        [Tooltip("Grid Y coordinate")]
        public int currentY;

        [Tooltip("Piece logical type")]
        public ChessPieceType type = ChessPieceType.None;

        [Tooltip("Movement direction for this piece: +1 means increasing Y (up the board), -1 means decreasing Y (down the board).")]
        public int moveDirection = 1; // default to +1 for compatibility

        [Tooltip("The Y index (row) where this piece was spawned. Used to determine a pawn's initial rank for 2-step moves.")]
        public int initialY = -1;

        // internal smoothing targets
        private Vector3 targetPosition;
        private Vector3 targetScale = Vector3.one;

        // smoothing speeds
        private const float PositionLerpSpeed = 12f;
        private const float ScaleLerpSpeed = 12f;

        private void Awake()
        {
            targetPosition = transform.position;
            targetScale = transform.localScale;
        }

        private void Update()
        {
            // basic smoothing -> avoids abrupt snapping unless forced
            if ((transform.position - targetPosition).sqrMagnitude > 0.000001f)
                transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * PositionLerpSpeed);
            else
                transform.position = targetPosition;

            if ((transform.localScale - targetScale).sqrMagnitude > 0.000001f)
                transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * ScaleLerpSpeed);
            else
                transform.localScale = targetScale;
        }

        /// <summary>
        /// Set target world position for this piece
        /// -> force true will immediately set transform.position and reset target
        /// </summary>
        public virtual void SetPosition(Vector3 worldPos, bool force = false)
        {
            if (!IsFinite(worldPos))
            {
                Debug.LogWarning($"[ChessPiece] SetPosition given non-finite vector for {name}. Ignoring.");
                return;
            }

            targetPosition = worldPos;
            if (force) transform.position = worldPos;
        }

        /// <summary>
        /// Set target local scale
        /// -> force true immediately applies scale
        /// </summary>
        public virtual void SetScale(Vector3 scale, bool force = false)
        {
            if (!IsFinite(scale) || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
            {
                Debug.LogWarning($"[ChessPiece] SetScale given invalid scale {scale} for {name}. Ignoring.");
                return;
            }

            targetScale = scale;
            if (force) transform.localScale = scale;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

        public virtual List<Vector2Int> GetAvailableMoves(ChessPiece[,] board, int tileCountX, int tileCountY)
        {
            List<Vector2Int> r = new List<Vector2Int>();

            r.Add(new Vector2Int(3, 3));
            r.Add(new Vector2Int(3, 4));
            r.Add(new Vector2Int(4, 3));
            r.Add(new Vector2Int(4, 4));

            return r;
        }

        public virtual SpecialMove GetSpecialMoves(ChessPiece[,] board, List<Vector2Int[]> moveList, List<Vector2Int> availableMoves)
        {
            return SpecialMove.None;
        }
    }
}
