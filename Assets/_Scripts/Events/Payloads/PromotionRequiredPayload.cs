using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events.Payloads
{
    /// <summary>
    /// Carries the pawn's origin and target for triggering promotion UI and the optimistic visual
    /// glide onto the promotion square. IsCapture is included so the View can clear a captured
    /// piece off ToPosition before gliding the pawn there — the promotion move's legality (and
    /// therefore whether it's a capture) is already fully known by LocalMoveExecutor at the moment
    /// this fires, well before the player has chosen a promoted piece type, so this costs nothing
    /// to include.
    /// </summary>
    public readonly struct PromotionRequiredPayload
    {
        public readonly Vector2Int FromPosition;
        public readonly Vector2Int ToPosition;
        public readonly bool IsCapture;

        public PromotionRequiredPayload(Vector2Int from, Vector2Int to, bool isCapture)
        {
            FromPosition = from;
            ToPosition   = to;
            IsCapture    = isCapture;
        }
    }
}
