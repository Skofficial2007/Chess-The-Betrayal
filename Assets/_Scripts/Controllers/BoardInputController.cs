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
    /// Handles all physical input (Raycasting, Dragging, Clicking).
    /// Acts as the "Hands" of the player, communicating with GameManager.
    /// Zero game logic - purely translates player input into move requests.
    /// GC-optimized to work with buffer-passing pattern.
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
        private Transform draggingVisualTransform;

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

            // 2. Get pointer position (supports both Input systems)
            Vector2 pointerPos = GetPointerPosition();
            if (pointerPos == Vector2.zero) return;

            // 3. Handle input
            HandlePointer(pointerPos);
        }

        private Vector2 GetPointerPosition()
        {
            // Try new Input System first
            if (Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }

#pragma warning disable 618
            // Fallback to legacy Input
            if (Input.mousePresent)
            {
                return Input.mousePosition;
            }
#pragma warning restore 618

            return Vector2.zero;
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

            // NOTE: BoardVisuals doesn't exist yet, so this will cause compiler errors temporarily
            // We'll fix this when we create BoardVisuals in the next step
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
            if (isDragging && draggingVisualTransform != null)
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
                draggingVisualTransform = BoardVisuals.Instance.GetPieceTransformAt(gridPos);

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

            // If dropped off the board, snap back
            if (dropGridPos == ChessTheMasterPiece.Data.Vector2Int.Invalid)
            {
                if (BoardVisuals.Instance != null)
                {
                    BoardVisuals.Instance.SnapPieceBack(dragStartGridPos);
                }
                draggingVisualTransform = null;
                return;
            }

            // Request the move from GameManager
            bool moveWasLegal = GameManager.Instance.RequestMove(dragStartGridPos, dropGridPos);

            // If illegal, snap the visual back to original position
            if (!moveWasLegal)
            {
                if (BoardVisuals.Instance != null)
                {
                    BoardVisuals.Instance.SnapPieceBack(dragStartGridPos);
                }
            }

            draggingVisualTransform = null;
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

                draggingVisualTransform.position = worldPos;

                if (draggingVisualTransform.TryGetComponent(out ChessPiece pieceComponent))
                {
                    pieceComponent.SetPosition(worldPos, force: true);
                }
            }
        }

        private bool WasPointerPressed()
        {
            if (Mouse.current != null)
            {
                return Mouse.current.leftButton.wasPressedThisFrame;
            }

#pragma warning disable 618
            return Input.GetMouseButtonDown(0);
#pragma warning restore 618
        }

        private bool WasPointerReleased()
        {
            if (Mouse.current != null)
            {
                return Mouse.current.leftButton.wasReleasedThisFrame;
            }

#pragma warning disable 618
            return Input.GetMouseButtonUp(0);
#pragma warning restore 618
        }
    }
}