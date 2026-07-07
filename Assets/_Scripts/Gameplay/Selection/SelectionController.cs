using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Infrastructure;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Gameplay
{
    /// <summary>
    /// The two-tap selection state machine (tap piece -> highlights persist -> tap destination
    /// completes the move). Owns no device code — it only reacts to ISelectionInput.OnTileActivated
    /// and asks GameManager whether a square is selectable / what its legal moves are. Because
    /// GameManager.CanSelectPiece and GetLegalMovesAt already route through MatchDriver's
    /// Betrayal-phase-aware logic (Normal / RetributionPending / ForcedSave), this controller
    /// never needs to know about Betrayal phases itself — it stays a dumb two-state machine and
    /// the domain decides legality.
    ///
    /// States: None (nothing selected) and Selected(selectedTile). See RequestMove/PieceSelectedEvent
    /// consumers (PieceLiftView, MoveHighlightView) for the View-layer reaction to each transition.
    /// </summary>
    public class SelectionController : MonoBehaviour
    {
        [Header("Input Source")]
        [Tooltip("Must implement ISelectionInput. PointerSelectionInput today; KeyboardSelectionInput later — this field is the only thing that changes.")]
        [SerializeField] private MonoBehaviour selectionInputBehaviour;

        [Header("Event Channels")]
        [SerializeField] private ChessTheBetrayal.Events.PieceSelectedEventChannel _pieceSelectedChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _selectionClearedChannel;
        [SerializeField] private ChessTheBetrayal.Events.SelectionRejectedEventChannel _selectionRejectedChannel;

        private ISelectionInput _selectionInput;
        private GameManager _gameManager;
        private Vector2Int _selectedTile = Vector2Int.Invalid;

        private bool IsSelected => _selectedTile != Vector2Int.Invalid;

        private void Awake()
        {
            _selectionInput = selectionInputBehaviour as ISelectionInput;
            if (_selectionInput == null)
            {
                Debug.LogError($"[SelectionController] {nameof(selectionInputBehaviour)} does not implement ISelectionInput.");
            }
        }

        private void Start()
        {
            _gameManager = ServiceLocator.Instance.Resolve<GameManager>();
        }

        private void OnEnable()
        {
            if (_selectionInput != null) _selectionInput.OnTileActivated += HandleTileActivated;
        }

        private void OnDisable()
        {
            if (_selectionInput != null) _selectionInput.OnTileActivated -= HandleTileActivated;
        }

        private void HandleTileActivated(Vector2Int tile)
        {
            if (!IsSelected)
            {
                TrySelect(tile);
                return;
            }

            // Already have a selection — resolve against it.
            if (tile == _selectedTile)
            {
                // Tap same piece again = cancel.
                Deselect();
                return;
            }

            if (IsLegalDestination(_selectedTile, tile))
            {
                _gameManager.RequestMove(_selectedTile, tile);
                Deselect();
                return;
            }

            if (_gameManager.CanSelectPiece(tile))
            {
                // Switch selection to the newly tapped own piece.
                Deselect();
                TrySelect(tile);
                return;
            }

            // Empty square, enemy piece, or otherwise illegal target — cancel.
            Deselect();
        }

        private void TrySelect(Vector2Int tile)
        {
            if (!_gameManager.CanSelectPiece(tile)) return;

            // A selectable piece with zero legal moves (e.g. pinned during Retribution, or a
            // piece that isn't the forced Executioner/Save piece) offers nothing to select into.
            // CanSelectPiece already confirmed this IS the current turn's own piece — so this is
            // specifically "your piece, but it can't move right now," not an invalid target. Raise
            // SelectionRejected (rather than just returning silently) so a View can shake the piece
            // to say so, without ever firing for a tap on an opponent's piece or empty square (see
            // HandleTileActivated's final else, which stays a silent Deselect for those).
            var legalMoves = _gameManager.GetLegalMovesAt(tile);
            if (legalMoves.Count == 0)
            {
                _selectionRejectedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.SelectionRejectedPayload(tile));
                return;
            }

            _selectedTile = tile;
            _pieceSelectedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.PieceSelectedPayload(tile));
        }

        private void Deselect()
        {
            if (!IsSelected) return;

            _selectedTile = Vector2Int.Invalid;
            _selectionClearedChannel?.Raise();
        }

        private bool IsLegalDestination(Vector2Int from, Vector2Int to)
        {
            var legalMoves = _gameManager.GetLegalMovesAt(from);
            for (int i = 0; i < legalMoves.Count; i++)
            {
                if (legalMoves[i].EndPosition == to) return true;
            }
            return false;
        }
    }
}
