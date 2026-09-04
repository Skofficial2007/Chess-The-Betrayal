using System;
using UnityEngine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Gameplay.Interaction;
using ChessTheBetrayal.Infrastructure;
using ChessTheBetrayal.View.Board;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.View.Input
{
    /// <summary>
    /// Mouse- and touch-driven ISelectionInput. Raises OnTileActivated on pointer-up as a tap:
    /// press and release must land on the same tile. No positional drag-follow — this is what
    /// makes the two-tap model mobile-correct (Chess.com/Lichess-style): the finger can lift
    /// mid-gesture without cancelling anything, because nothing was "held" in the first place.
    ///
    /// This script knows nothing about chess rules or selection state — it only resolves screen
    /// space to a board tile and reports taps. SelectionController interprets what a tap means.
    /// The hardware read lives behind IPointerDevice and the tap rules in PointerTapRecognizer, so
    /// what remains here is the part that genuinely needs a scene: a camera, a raycast, and the
    /// hover highlight.
    /// </summary>
    public class PointerSelectionInput : MonoBehaviour, ISelectionInput
    {
        [Header("Input Settings")]
        [SerializeField] private LayerMask raycastMask = ~0; // Default: raycast everything
        [Tooltip("Minimum real-time seconds between two accepted tile activations. A single physical tap can only ever produce one activation already, but this guards against rapid double-taps/mashing — e.g. a fast player tapping twice before the first tap's animation has visually settled — from both being processed as separate activations.")]
        [SerializeField, Range(0f, 0.5f)] private float minTimeBetweenActivations = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool showDebugRays = false;

        public event Action<Vector2Int> OnTileActivated;

        // Spelled out in full because this assembly has a Camera namespace of its own, which sits
        // closer than the UnityEngine import and would otherwise be what the bare name finds.
        private UnityEngine.Camera mainCamera;
        private IUiBlockingState _uiBlockingState;
        private IBoardQuery _gameManager;
        private IBoardHitTest _boardHitTest;

        private IPointerDevice _pointer;
        private PointerTapRecognizer _recognizer;

        // What was last reported as swallowing taps, so only the changes reach the log. Ignoring
        // taps is the ordinary state while a panel is up, and a line per frame would bury the one
        // moment worth reading.
        private string _lastReportedGate;

        private void Awake()
        {
            mainCamera = UnityEngine.Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("[PointerSelectionInput] No main camera found!");
            }

            _pointer = new UnityPointerDevice();
            _recognizer = new PointerTapRecognizer(minTimeBetweenActivations);
        }

        private void Start()
        {
            _uiBlockingState = ServiceLocator.Instance.Resolve<IUiBlockingState>();
            _gameManager = ServiceLocator.Instance.Resolve<IBoardQuery>();
            _boardHitTest = ServiceLocator.Instance.Resolve<IBoardHitTest>();
        }

        private void Update()
        {
            // Before the gates below, not after: the pointer decides which finger it is following
            // from what happened this frame, and a frame it never saw is a press or a lift it will
            // be missing the other half of once the board is live again.
            _pointer.ReadThisFrame();

            if (mainCamera == null)
            {
                ReportInputGate("there is no main camera");
                return;
            }

            string blocking = _uiBlockingState.DescribeBlocking();
            if (blocking != null)
            {
                ReportInputGate(blocking);
                return;
            }

            if (!_gameManager.IsGameActive)
            {
                ReportInputGate("the match is over");
                return;
            }

            ReportInputGate(null);

            if (!PointerTapRecognizer.HasUsablePosition(_pointer.ReportsPositionWhileIdle, _pointer.IsPressed, _pointer.WasReleased))
            {
                // Nothing is on the glass, so nothing is hovered. Left lit, the last square a
                // finger touched would keep glowing for the rest of the game.
                _boardHitTest.ClearHoverHighlight();
                return;
            }

            Vector2Int tile = ResolveTileUnderPointer(_pointer.Position);

            if (_recognizer.Observe(_pointer.WasPressed, _pointer.WasReleased, tile, Time.unscaledTime))
            {
                OnTileActivated?.Invoke(tile);
            }
        }

        /// <summary>
        /// Records the moment the board stops reading taps, and the moment it starts again.
        ///
        /// Three separate things can hold the board still and a player cannot tell them apart: all
        /// three look like a board that has stopped responding while the buttons around it still
        /// work. Naming the one in force turns a report of "it froze" into a line that says which,
        /// which matters most in the case where nothing is drawn to explain it.
        ///
        /// Deliberately not behind an editor-only guard: the freeze this answers was hit on a
        /// release build on a phone, which is exactly where a guarded version would say nothing.
        /// The message is built only when it changes, so a blocked frame costs a reference compare.
        /// </summary>
        private void ReportInputGate(string reason)
        {
            if (reason == _lastReportedGate) return;

            _lastReportedGate = reason;

            Debug.Log(reason == null
                ? "[PointerSelectionInput] board taps are being read again"
                : $"[PointerSelectionInput] board taps ignored: {reason}");
        }

        /// <summary>
        /// Casts into the board and reports which tile the pointer is over, updating the hover
        /// highlight to match. Returns Vector2Int.Invalid when the pointer is off the board.
        /// </summary>
        private Vector2Int ResolveTileUnderPointer(Vector2 screenPos)
        {
            Ray ray = mainCamera.ScreenPointToRay(screenPos);

            if (showDebugRays)
            {
                Debug.DrawRay(ray.origin, ray.direction * 100f, Color.yellow);
            }

            if (!Physics.Raycast(ray, out RaycastHit hit, 200f, raycastMask))
            {
                _boardHitTest.ClearHoverHighlight();
                return Vector2Int.Invalid;
            }

            Vector2Int tile = _boardHitTest.GetTileIndexFromTransform(hit.transform);
            _boardHitTest.UpdateHoverHighlight(tile);
            return tile;
        }
    }
}
