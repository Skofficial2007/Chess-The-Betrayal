using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Events.Payloads
{
    /// <summary>
    /// One ply being taken back, announced so the board can play it in reverse.
    ///
    /// The MoveCommand carries everything a reversal needs — where the piece came from, what it
    /// captured, what a pawn was promoted to, which side a defector originally belonged to — since
    /// it was always built full-fidelity enough to unmake the move on the board.
    ///
    /// These arrive one at a time, newest ply first, which is the order they came off the board in.
    /// Anything reading them must keep that order: a captured piece goes back to the place it was
    /// taken from, and that only holds while the takeback runs backwards through history.
    /// </summary>
    public readonly struct MoveUndonePayload
    {
        public readonly MoveCommand Move;

        /// <summary>
        /// True on the last ply of a single takeback, i.e. once the board has finished moving. The
        /// press can unwind more than one ply and the board only settles at the end, so this is the
        /// point to restore anything that describes the position as a whole rather than a move.
        /// </summary>
        public readonly bool IsFinalPly;

        /// <summary>
        /// Whether the side to move is in check in the position this takeback lands on. Only
        /// meaningful alongside IsFinalPly — the positions in between are passed through rather
        /// than arrived at, and the board is not asked to describe them.
        /// </summary>
        public readonly bool LandsInCheck;

        public MoveUndonePayload(MoveCommand move, bool isFinalPly, bool landsInCheck)
        {
            Move = move;
            IsFinalPly = isFinalPly;
            LandsInCheck = landsInCheck;
        }
    }
}
