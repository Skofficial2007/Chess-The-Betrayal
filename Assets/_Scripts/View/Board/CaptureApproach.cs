using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Match;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.View
{
    /// <summary>
    /// Decides whether a capture should be walked into before it is struck, and from which square.
    ///
    /// The strike itself — the crouch, the leap over the victim's head, the stomp — is built for
    /// contact between neighbours, and it reads beautifully when a pawn takes the piece in front of
    /// it. Stretched across half a board it stops being a pounce: a bishop leaving c1 for g5 covers
    /// eight units in the same third of a second and simply appears on top of its victim. So a
    /// piece with ground to cover now closes it first and strikes from the square next door, which
    /// is both the better read and the one a person would describe if asked what happened.
    ///
    /// No Unity types here — board squares in, board square out. BoardVisuals turns the answer into
    /// a world position, because tile size and board origin are its business, not this rule's.
    /// </summary>
    public static class CaptureApproach
    {
        /// <summary>
        /// Reports the square a capture should be launched from, or false when the attacker should
        /// simply strike from where it stands.
        ///
        /// Two cases never get a run-up. Neighbours are already in range, which covers every pawn
        /// and king capture outright. And a knight is a leaper — closing distance on the ground is
        /// the one thing it does not do, and it is also the only piece whose approach square is not
        /// guaranteed to be empty. For rooks, bishops and queens the path is clear by the rules of
        /// chess, so the run-up can never glide onto an occupied square.
        /// </summary>
        public static bool TryPlanStagingSquare(Vector2Int from, Vector2Int to, ChessPieceType pieceType, out Vector2Int staging)
        {
            staging = Vector2Int.Invalid;

            if (pieceType == ChessPieceType.Knight) return false;
            if (MoveTravelTiming.SquaresApart(from.x, from.y, to.x, to.y) < 2) return false;

            staging = new Vector2Int(to.x - Step(to.x - from.x), to.y - Step(to.y - from.y));
            return true;
        }

        private static int Step(int delta)
        {
            if (delta > 0) return 1;
            return delta < 0 ? -1 : 0;
        }
    }
}
