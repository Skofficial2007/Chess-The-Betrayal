using UnityEngine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Infrastructure;

namespace ChessTheBetrayal.View
{
    /// <summary>
    /// Highlights legal destination tiles on PieceSelected and clears them on SelectionCleared.
    /// Re-queries GameManager.GetLegalMovesAt at the moment of selection rather than trusting a
    /// payload-carried list — GetLegalMovesAt returns a reused mutable buffer, so the only safe
    /// time to read it is synchronously, right here. This also means highlighting is always
    /// correct for whatever Betrayal sub-phase is active (Normal/RetributionPending/ForcedSave),
    /// since GameManager/MatchDriver already resolve that — this view stays dumb.
    /// </summary>
    public class MoveHighlightView : MonoBehaviour
    {
        [SerializeField] private BoardVisuals boardVisuals;

        [Header("Event Channels")]
        [SerializeField] private ChessTheBetrayal.Events.PieceSelectedEventChannel _pieceSelectedChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _selectionClearedChannel;

        private void OnEnable()
        {
            _pieceSelectedChannel?.Register(HandlePieceSelected);
            _selectionClearedChannel?.Register(HandleSelectionCleared);
        }

        private void OnDisable()
        {
            _pieceSelectedChannel?.Unregister(HandlePieceSelected);
            _selectionClearedChannel?.Unregister(HandleSelectionCleared);
        }

        public void HandlePieceSelected(PieceSelectedPayload payload)
        {
            if (boardVisuals == null || !ServiceLocator.Instance.TryResolve(out IBoardQuery boardQuery)) return;

            var legalMoves = boardQuery.GetLegalMovesAt(payload.Position);
            boardVisuals.HighlightLegalMoves(legalMoves);
        }

        public void HandleSelectionCleared()
        {
            if (boardVisuals == null) return;

            boardVisuals.ClearLegalMoveHighlights();
        }
    }
}
