using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Logic
{
    /// <summary>
    /// What a from/to square pair represents before any move-legality validation runs.
    /// </summary>
    public enum MoveIntent
    {
        NormalMove,
        Capture,
        Castling,
        BetrayalAct,
        Illegal
    }

    /// <summary>
    /// Pure classification of a move request's intent, based only on what occupies the
    /// origin and destination squares. Contains no legality/pathing checks — those remain
    /// the engine's job. Used to decide which validation path to run, so that "friendly
    /// square, but not actually a Betrayal" (e.g. a King) never reaches Betrayal validation.
    /// </summary>
    public static class MoveClassifier
    {
        public static MoveIntent ClassifyMove(PieceData piece, PieceData target, bool betrayalRightAvailable)
        {
            if (target.IsEmpty) return MoveIntent.NormalMove;

            if (target.Team != piece.Team) return MoveIntent.Capture;

            // Target is friendly from here on.
            if (piece.Type == ChessPieceType.King && target.Type == ChessPieceType.Rook)
            {
                return MoveIntent.Castling;
            }

            if (!betrayalRightAvailable) return MoveIntent.Illegal;
            if (piece.Type == ChessPieceType.King) return MoveIntent.Illegal;
            if (target.Type == ChessPieceType.King) return MoveIntent.Illegal;

            return MoveIntent.BetrayalAct;
        }
    }
}
