using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.UI;
using ChessTheMasterPiece.Logic;
using ChessTheMasterPiece.View;

namespace ChessTheMasterPiece.Controllers
{
    /// <summary>
    /// Reads mouse/touch input and translates it into move requests. This script knows nothing about chess rules — it just figures out which square the player clicked and tells GameManager.
    /// </summary>
    public class BoardInputController : MonoBehaviour
    {
        [Header("Input Settings")]
        [SerializeField] private float dragHeight = 1f;
        [SerializeField] private float boardHeightOffset = 0f;
        [SerializeField] private LayerMask raycastMask = ~0; // Default: raycast everything

        [Header("Debug")]
        [SerializeField] private bool showDebugRays = false;

        private Camera mainCamera;

        // Drag State
        private bool isDragging;
        private ChessTheMasterPiece.Data.Vector2Int dragStartGridPos;
        private Transform draggedPieceTransform;

        private void Awake()
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("[BoardInputController] No main camera found!");
            }
        }

        private void Update()
        {
            // 1. Safety checks
            if (mainCamera == null) return;
            if (UIManager.Instance != null && UIManager.Instance.IsUIBlocking()) return;
            if (GameManager.Instance == null || !GameManager.Instance.IsGameActive) return;

            // 2. Get pointer position (safely handles 0,0 coordinates)
            if (!TryGetPointerPosition(out Vector2 pointerPos)) return;

            // 3. Handle input
            HandlePointer(pointerPos);
        }

        /// <summary>
        /// Attempts to get the current pointer position supporting both PC (Mouse) and Mobile (Touch).
        /// Returns true if an active input device is found.
        /// </summary>
        private bool TryGetPointerPosition(out Vector2 pos)
        {
            // Check for Mobile Touch first
            if (UnityEngine.InputSystem.Touchscreen.current != null && UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.isPressed)
            {
                pos = UnityEngine.InputSystem.Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            // Check for PC Mouse
            if (Mouse.current != null)
            {
                pos = Mouse.current.position.ReadValue();
                return true;
            }

            pos = Vector2.zero;
            return false;
        }

        private void HandlePointer(Vector2 screenPos)
        {
            Ray ray = mainCamera.ScreenPointToRay(screenPos);

            if (showDebugRays)
            {
                Debug.DrawRay(ray.origin, ray.direction * 100f, Color.yellow);
            }

            // Raycast to find what we're hovering over
            bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, 200f, raycastMask);
            ChessTheMasterPiece.Data.Vector2Int hoverIndex = ChessTheMasterPiece.Data.Vector2Int.Invalid;

            if (hitSomething && BoardVisuals.Instance != null)
            {
                hoverIndex = BoardVisuals.Instance.GetTileIndexFromTransform(hit.transform);
                BoardVisuals.Instance.UpdateHoverHighlight(hoverIndex);
            }
            else if (BoardVisuals.Instance != null)
            {
                BoardVisuals.Instance.ClearHoverHighlight();
            }

            // Handle input actions
            if (WasPointerPressed() && hitSomething && hoverIndex != ChessTheMasterPiece.Data.Vector2Int.Invalid)
            {
                TryStartDrag(hoverIndex);
            }

            if (WasPointerReleased())
            {
                TryDrop(hoverIndex);
            }

            // Update dragging piece visual
            if (isDragging && draggedPieceTransform != null)
            {
                UpdateDragVisual(ray);
            }
        }

        private void TryStartDrag(ChessTheMasterPiece.Data.Vector2Int gridPos)
        {
            // Ask GameManager if this piece can be selected
            if (!GameManager.Instance.CanSelectPiece(gridPos))
            {
                return;
            }

            isDragging = true;
            dragStartGridPos = gridPos;

            // BoardVisuals will provide these methods in the next step
            if (BoardVisuals.Instance != null)
            {
                // Get the visual GameObject transform so we can drag it
                draggedPieceTransform = BoardVisuals.Instance.GetPieceTransformAt(gridPos);

                // Get legal moves from GameManager and tell visuals to highlight them
                // Updated to use IReadOnlyList interface
                IReadOnlyList<MoveCommand> legalMoves = GameManager.Instance.GetLegalMovesAt(gridPos);
                BoardVisuals.Instance.HighlightLegalMoves(legalMoves);
            }
        }

        private void TryDrop(ChessTheMasterPiece.Data.Vector2Int dropGridPos)
        {
            if (!isDragging) return;

            isDragging = false;

            // Clear highlights
            if (BoardVisuals.Instance != null)
            {
                BoardVisuals.Instance.ClearLegalMoveHighlights();
            }

            // Request the move and let GameManager decide if it's valid. If it's not, the piece will snap back automatically.
            if (dropGridPos != ChessTheMasterPiece.Data.Vector2Int.Invalid)
            {
                // Request the move - piece stays where it landed (optimistic)
                // If illegal, GameManager will fire OnMoveRejected and BoardVisuals will snap it back
                GameManager.Instance.RequestMove(dragStartGridPos, dropGridPos);
            }
            else
            {
                // Dropped completely off the board? Request a move to its own square
                // to intentionally trigger an OnMoveRejected event and snap it back.
                GameManager.Instance.RequestMove(dragStartGridPos, dragStartGridPos);
            }

            // Clear state immediately (Do NOT force snap-backs here!)
            draggedPieceTransform = null;
        }

        private void UpdateDragVisual(Ray ray)
        {
            // Use the actual visual height instead of a hardcoded 0
            float actualBoardHeight = boardHeightOffset;

            // If BoardVisuals is available, let's get the real surface height
            if (BoardVisuals.Instance != null)
            {
                // surface = 4.0 + 0.3 = 4.3
                actualBoardHeight = BoardVisuals.Instance.transform.position.y + BoardVisuals.Instance.TileYOffset;
            }

            Plane dragPlane = new Plane(Vector3.up, Vector3.up * actualBoardHeight);

            if (dragPlane.Raycast(ray, out float enter))
            {
                Vector3 worldPos = ray.GetPoint(enter);
                worldPos.y = actualBoardHeight + dragHeight; // Now it will be 4.3 + 1.0 = 5.3

                draggedPieceTransform.position = worldPos;

                if (draggedPieceTransform.TryGetComponent(out ChessPiece pieceComponent))
                {
                    pieceComponent.SetPosition(worldPos, force: true);
                }
            }
        }

        private bool WasPointerPressed()
        {
            // Android / Mobile Touch
            if (UnityEngine.InputSystem.Touchscreen.current != null)
            {
                if (UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                    return true;
            }

            // Steam / PC Mouse
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                    return true;
            }

            return false;
        }

        private bool WasPointerReleased()
        {
            // Android / Mobile Touch
            if (UnityEngine.InputSystem.Touchscreen.current != null)
            {
                if (UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
                    return true;
            }

            // Steam / PC Mouse
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasReleasedThisFrame)
                    return true;
            }

            return false;
        }
    }
}