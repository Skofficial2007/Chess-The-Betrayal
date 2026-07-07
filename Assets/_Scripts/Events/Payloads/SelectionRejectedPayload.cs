using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events.Payloads
{
    /// <summary>
    /// Raised when the player taps a piece that belongs to the team whose turn it currently is,
    /// but the piece has zero legal moves right now (pinned, stuck behind a forced Betrayal
    /// sub-phase, etc.) — SelectionController.TrySelect resolves this and declines to select into
    /// it. Deliberately NOT raised for taps on an opponent's piece, an empty square, or any other
    /// "not a valid selection" case (see SelectionController.HandleTileActivated's final `else`) —
    /// those are silent no-ops today and stay that way; this event exists specifically for "yes,
    /// this is your piece, but it can't move," so a View can shake it to say so.
    /// </summary>
    public readonly struct SelectionRejectedPayload
    {
        public readonly Vector2Int Position;

        public SelectionRejectedPayload(Vector2Int position)
        {
            Position = position;
        }

        public override string ToString() => $"Position={Position}";
    }
}
