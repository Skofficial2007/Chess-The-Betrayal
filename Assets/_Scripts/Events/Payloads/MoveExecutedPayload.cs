using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Events.Payloads
{
    /// <summary>
    /// Snapshot of a successfully applied move.
    /// MoveCommand is itself a readonly struct, minimizing allocation overhead.
    /// </summary>
    public readonly struct MoveExecutedPayload
    {
        public readonly MoveCommand Move;
        public readonly int TurnNumber;
        public readonly bool IsCheck;

        public MoveExecutedPayload(MoveCommand move, int turnNumber, bool isCheck)
        {
            Move       = move;
            TurnNumber = turnNumber;
            IsCheck    = isCheck;
        }
    }
}
