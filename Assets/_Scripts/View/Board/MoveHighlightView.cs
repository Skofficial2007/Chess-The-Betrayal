using UnityEngine;
using ChessTheBetrayal.Gameplay;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.UI
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

        public void HandlePieceSelected(PieceSelectedPayload payload)
        {
            if (boardVisuals == null || GameManager.Instance == null) return;

            var legalMoves = GameManager.Instance.GetLegalMovesAt(payload.Position);
            boardVisuals.HighlightLegalMoves(legalMoves);
        }

        public void HandleSelectionCleared()
        {
            if (boardVisuals == null) return;

            boardVisuals.ClearLegalMoveHighlights();
        }
    }
}
