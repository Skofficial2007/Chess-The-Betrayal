using ChessTheBetrayal.Core.Data;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Core.Match
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
    /// Sits here for the same reason MoveVisualDurationEstimator does, not because staging a
    /// capture is a rule of chess: the view plays this and the move pacing has to predict it, and
    /// those two live in assemblies that cannot reference each other. One definition keeps the
    /// animation and the time budgeted for it from disagreeing.
    ///
    /// Answers in board squares only. Turning a square into a world position needs to know how big
    /// a tile is and where the board starts, which is the view's business.
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

            if (RunUpSquares(from, to, pieceType) == 0) return false;

            staging = new Vector2Int(to.x - Step(to.x - from.x), to.y - Step(to.y - from.y));
            return true;
        }

        /// <summary>
        /// How far the attacker travels before it strikes, or zero when it strikes where it stands.
        /// Separate from the square itself so the move pacing can budget for the run-up without
        /// caring where on the board it happens.
        /// </summary>
        public static int RunUpSquares(Vector2Int from, Vector2Int to, ChessPieceType pieceType)
        {
            if (pieceType == ChessPieceType.Knight) return 0;

            int squares = MoveTravelTiming.SquaresApart(from.x, from.y, to.x, to.y);
            return squares < 2 ? 0 : squares - 1;
        }

        /// <summary>
        /// The same run-up measured as ground rather than squares, which is what pacing it needs: a
        /// bishop walking four diagonal steps covers half again as much floor as a rook walking
        /// four along a rank, and giving them the same duration makes the bishop the faster piece.
        /// Zero when the attacker strikes where it stands.
        ///
        /// Both the animator and the move pacing ask this rather than each deriving it from the
        /// staging square, so neither can end up disagreeing with the other about how far the
        /// attacker had to come.
        /// </summary>
        public static float RunUpTiles(Vector2Int from, Vector2Int to, ChessPieceType pieceType)
        {
            if (!TryPlanStagingSquare(from, to, pieceType, out Vector2Int staging)) return 0f;

            return MoveTravelTiming.TilesApart(from.x, from.y, staging.x, staging.y);
        }

        private static int Step(int delta)
        {
            if (delta > 0) return 1;
            return delta < 0 ? -1 : 0;
        }
    }
}
