using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events.Payloads
{
    /// <summary>
    /// Carries the start and end positions of a rejected move so visual elements can snap back to origin.
    /// </summary>
    public readonly struct MoveRejectedPayload
    {
        public readonly Vector2Int FromPosition;
        public readonly Vector2Int ToPosition;

        public MoveRejectedPayload(Vector2Int from, Vector2Int to)
        {
            FromPosition = from;
            ToPosition   = to;
        }
    }
}
