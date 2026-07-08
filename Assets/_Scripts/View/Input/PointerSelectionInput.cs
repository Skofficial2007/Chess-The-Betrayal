using System;
using UnityEngine;
using UnityEngine.InputSystem;
using ChessTheBetrayal.UI;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Gameplay.Interaction;
using ChessTheBetrayal.Infrastructure;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.View
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
        [Tooltip("Minimum real-time seconds between two accepted tile activations. A single physical tap can only ever produce one activation already (see WasPointerReleased), but this guards against rapid double-taps/mashing — e.g. a fast player tapping twice before the first tap's animation has visually settled — from both being processed as separate activations.")]
        [SerializeField, Range(0f, 0.5f)] private float minTimeBetweenActivations = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool showDebugRays = false;

        public event Action<Vector2Int> OnTileActivated;

        private Camera mainCamera;
        private IUiBlockingState _uiBlockingState;
        private IBoardQuery _gameManager;
        private IBoardHitTest _boardHitTest;

        // Tracks the tile a press started on, so release can confirm it landed on the same tile.
        private bool _isPressed;
        private Vector2Int _pressStartTile = Vector2Int.Invalid;

        // Debounce: the last time OnTileActivated actually fired, in unscaled real time (so it
        // isn't affected by Time.timeScale, matching every animation tween in this codebase which
        // runs useUnscaledTime). See minTimeBetweenActivations' doc comment for why this exists.
        private float _lastActivationRealtime = float.NegativeInfinity;

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
            _uiBlockingState = ServiceLocator.Instance.Resolve<IUiBlockingState>();
            _gameManager = ServiceLocator.Instance.Resolve<IBoardQuery>();
            _boardHitTest = ServiceLocator.Instance.Resolve<IBoardHitTest>();
        }

        private void Update()
        {
            if (mainCamera == null) return;
            if (_uiBlockingState.IsUIBlocking()) return;
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
                hoverIndex = _boardHitTest.GetTileIndexFromTransform(hit.transform);
                _boardHitTest.UpdateHoverHighlight(hoverIndex);
            }
            else
            {
                _boardHitTest.ClearHoverHighlight();
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
                    // Debounced: a fast double-tap/mash landing within minTimeBetweenActivations of
                    // the last ACCEPTED activation is dropped rather than forwarded. Every downstream
                    // consumer (SelectionController's two-tap state machine, GameManager.RequestMove)
                    // already validates against authoritative logical state and is individually
                    // reentrancy-safe, so this isn't fixing a correctness bug — it's closing the gap
                    // where a fast repeated tap could visually interact with a piece/tile whose
                    // previous move animation (slide, capture stamp, castle rook, promotion swap,
                    // defection spin) hasn't settled yet, before it's even had a chance to read as
                    // finished on screen.
                    if (Time.unscaledTime - _lastActivationRealtime >= minTimeBetweenActivations)
                    {
                        _lastActivationRealtime = Time.unscaledTime;
                        OnTileActivated?.Invoke(hoverIndex);
                    }
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
