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

        /// <summary>The board's own ply count after this move. Falls back when moves are unmade,
        /// which is what separates it from <see cref="PlyIndex"/>.</summary>
        public readonly int PlyNumber;

        public readonly bool IsCheck;

        /// <summary>
        /// Monotonic count of plies applied this match, starting at 1 for the first move. Unlike
        /// <see cref="PlyNumber"/> it only ever climbs — an Undo does not walk it back — which is
        /// the ordering/gap/replay signal a network consumer needs.
        /// </summary>
        public readonly int PlyIndex;

        public MoveExecutedPayload(MoveCommand move, int plyNumber, bool isCheck, int plyIndex)
        {
            Move      = move;
            PlyNumber = plyNumber;
            IsCheck   = isCheck;
            PlyIndex  = plyIndex;
        }
    }
}
