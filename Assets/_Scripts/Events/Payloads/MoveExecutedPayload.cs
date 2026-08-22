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

        /// <summary>
        /// Monotonic count of plies applied this match, starting at 1 for the first move.
        /// Unlike TurnNumber (which repeats across a Betrayal sub-sequence's Act/Retribution/
        /// Defection plies since the turn hasn't flipped yet), this increments on every single
        /// applied ply — the ordering/gap/replay signal a network consumer needs.
        /// </summary>
        public readonly int PlyIndex;

        public MoveExecutedPayload(MoveCommand move, int turnNumber, bool isCheck, int plyIndex)
        {
            Move       = move;
            TurnNumber = turnNumber;
            IsCheck    = isCheck;
            PlyIndex   = plyIndex;
        }
    }
}
