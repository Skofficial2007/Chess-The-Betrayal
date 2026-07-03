using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Events.Payloads;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Lifts the selected piece on PieceSelected and lowers it back on SelectionCleared. Pure
    /// View reaction — knows nothing about selection rules, only that a piece at a given
    /// position should visually rise or fall. Wire this to PieceSelectedEventChannel and the
    /// SelectionCleared GameEventChannel via PieceSelectedEventListener/GameEventListener in the
    /// Inspector, same pattern as every other BoardVisuals reaction (see HandleGameStarted etc.).
    /// </summary>
    public class PieceLiftView : MonoBehaviour
    {
        [SerializeField] private BoardVisuals boardVisuals;

        // SelectionClearedEvent carries no payload, so we remember what we lifted.
        private Vector2Int _liftedTile = Vector2Int.Invalid;

        public void HandlePieceSelected(PieceSelectedPayload payload)
        {
            if (boardVisuals == null) return;

            _liftedTile = payload.Position;
            boardVisuals.LiftPieceAt(_liftedTile);
        }

        public void HandleSelectionCleared()
        {
            if (boardVisuals == null || _liftedTile == Vector2Int.Invalid) return;

            boardVisuals.LowerPieceAt(_liftedTile);
            _liftedTile = Vector2Int.Invalid;
        }
    }
}
