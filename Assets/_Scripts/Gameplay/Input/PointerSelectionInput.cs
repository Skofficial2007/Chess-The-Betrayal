using System;
using UnityEngine;
using UnityEngine.InputSystem;
using ChessTheBetrayal.UI;
using ChessTheBetrayal.Infrastructure;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Gameplay
{
    /// <summary>
    /// Mouse- and touch-driven ISelectionInput. Raises OnTileActivated on pointer-up as a tap:
    /// press and release must land on the same tile. No positional drag-follow — this is what
    /// makes the two-tap model mobile-correct (Chess.com/Lichess-style): the finger can lift
    /// mid-gesture without cancelling anything, because nothing was "held" in the first place.
    ///
    /// This script knows nothing about chess rules or selection state — it only resolves screen
    /// space to a board tile and reports taps. SelectionController interprets what a tap means.
    /// </summary>
    public class PointerSelectionInput : MonoBehaviour, ISelectionInput
    {
        [Header("Input Settings")]
        [SerializeField] private LayerMask raycastMask = ~0; // Default: raycast everything

        [Header("Debug")]
        [SerializeField] private bool showDebugRays = false;

        public event Action<Vector2Int> OnTileActivated;

        private Camera mainCamera;
        private UIManager _uiManager;
        private GameManager _gameManager;
        private BoardVisuals _boardVisuals;

        // Tracks the tile a press started on, so release can confirm it landed on the same tile.
        private bool _isPressed;
        private Vector2Int _pressStartTile = Vector2Int.Invalid;

        private void Awake()
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("[PointerSelectionInput] No main camera found!");
            }
        }

        private void Start()
        {
            _uiManager = ServiceLocator.Instance.Resolve<UIManager>();
            _gameManager = ServiceLocator.Instance.Resolve<GameManager>();
            _boardVisuals = ServiceLocator.Instance.Resolve<BoardVisuals>();
        }

        private void Update()
        {
            if (mainCamera == null) return;
            if (_uiManager.IsUIBlocking()) return;
            if (!_gameManager.IsGameActive) return;

            if (!TryGetPointerPosition(out Vector2 pointerPos)) return;

            HandlePointer(pointerPos);
        }

        /// <summary>
        /// Attempts to get the current pointer position supporting both PC (Mouse) and Mobile (Touch).
        /// Returns true if an active input device is found.
        /// </summary>
        private bool TryGetPointerPosition(out Vector2 pos)
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                pos = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

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

            bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, 200f, raycastMask);
            Vector2Int hoverIndex = Vector2Int.Invalid;

            if (hitSomething)
            {
                hoverIndex = _boardVisuals.GetTileIndexFromTransform(hit.transform);
                _boardVisuals.UpdateHoverHighlight(hoverIndex);
            }
            else
            {
                _boardVisuals.ClearHoverHighlight();
            }

            if (WasPointerPressed())
            {
                _isPressed = true;
                _pressStartTile = hoverIndex;
            }

            if (WasPointerReleased())
            {
                // A tap requires press and release on the same valid tile. Anything else
                // (released off-board, or dragged to a different tile before release) is not
                // a tile activation — the two-tap model has no use for drag gestures.
                if (_isPressed && hoverIndex != Vector2Int.Invalid && hoverIndex == _pressStartTile)
                {
                    OnTileActivated?.Invoke(hoverIndex);
                }

                _isPressed = false;
                _pressStartTile = Vector2Int.Invalid;
            }
        }

        private bool WasPointerPressed()
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                return true;

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                return true;

            return false;
        }

        private bool WasPointerReleased()
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
                return true;

            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
                return true;

            return false;
        }
    }
}
