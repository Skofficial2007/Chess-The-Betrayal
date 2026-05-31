using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events.Payloads
{
    /// <summary>
    /// Carries the pawn's origin and target for triggering promotion UI and optimistic visual snapping.
    /// </summary>
    public readonly struct PromotionRequiredPayload
    {
        public readonly Vector2Int FromPosition;
        public readonly Vector2Int ToPosition;

        public PromotionRequiredPayload(Vector2Int from, Vector2Int to)
        {
            FromPosition = from;
            ToPosition   = to;
        }
    }
}
