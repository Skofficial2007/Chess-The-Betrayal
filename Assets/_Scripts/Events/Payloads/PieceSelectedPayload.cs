using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events.Payloads
{
    /// <summary>
    /// A piece was selected (tap 1 of the two-tap model). Carries only the position — legal
    /// moves are intentionally not included here. GameManager.GetLegalMovesAt returns a reused,
    /// mutable list, so a struct payload can't safely snapshot it; listeners that need the legal
    /// move set (MoveHighlightView) re-query GameManager.Instance.GetLegalMovesAt(Position)
    /// themselves at the moment they handle this event.
    /// </summary>
    public readonly struct PieceSelectedPayload
    {
        public readonly Vector2Int Position;

        public PieceSelectedPayload(Vector2Int position)
        {
            Position = position;
        }

        public override string ToString() => $"Position={Position}";
    }
}
