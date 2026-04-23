using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.Logic
{
    /// <summary>
    /// Encapsulates all data required to execute (or undo) a single move.
    /// Uses pure value-type snapshots to prevent reference mutation bugs.
    /// </summary>
    public readonly struct MoveCommand
    {
        public readonly Vector2Int StartPosition;
        public readonly Vector2Int EndPosition;

        // Snapshot of the moved piece
        public readonly Team PieceTeam;
        public readonly ChessPieceType PieceType;
        public readonly int PieceMoveDirection;
        public readonly bool PieceHadMoved;

        // Snapshot of the captured piece (if any)
        public readonly bool HasCapture;
        public readonly Team CapturedTeam;
        public readonly ChessPieceType CapturedType;
        public readonly bool CapturedHadMoved;

        public readonly SpecialMove SpecialMoveType;
        public readonly ChessPieceType PromotedTo;

        // Additional metadata for special moves
        public readonly Vector2Int? RookStartPosition;
        public readonly Vector2Int? RookEndPosition;
        public readonly Vector2Int? EnPassantCapturePosition;

        /// <summary>
        /// Master constructor that flattens PieceData objects into primitive snapshots.
        /// </summary>
        public MoveCommand(
            Vector2Int startPosition,
            Vector2Int endPosition,
            PieceData pieceMoved,
            PieceData pieceCaptured = null,
            SpecialMove specialMoveType = SpecialMove.None,
            ChessPieceType promotedTo = ChessPieceType.None,
            Vector2Int? rookStartPosition = null,
            Vector2Int? rookEndPosition = null,
            Vector2Int? enPassantCapturePosition = null)
        {
            StartPosition = startPosition;
            EndPosition = endPosition;

            // Extract Moving Piece Snapshot
            if (pieceMoved != null)
            {
                PieceTeam = pieceMoved.Team;
                PieceType = pieceMoved.Type;
                PieceMoveDirection = pieceMoved.MoveDirection;
                PieceHadMoved = pieceMoved.HasMoved;
            }
            else
            {
                PieceTeam = Team.White;
                PieceType = ChessPieceType.None;
                PieceMoveDirection = 0;
                PieceHadMoved = false;
            }

            // Extract Captured Piece Snapshot
            if (pieceCaptured != null)
            {
                HasCapture = true;
                CapturedTeam = pieceCaptured.Team;
                CapturedType = pieceCaptured.Type;
                CapturedHadMoved = pieceCaptured.HasMoved;
            }
            else
            {
                HasCapture = false;
                CapturedTeam = Team.White;
                CapturedType = ChessPieceType.None;
                CapturedHadMoved = false;
            }

            SpecialMoveType = specialMoveType;
            PromotedTo = promotedTo;
            RookStartPosition = rookStartPosition;
            RookEndPosition = rookEndPosition;
            EnPassantCapturePosition = enPassantCapturePosition;
        }

        #region Factory Methods
        
        public static MoveCommand CreateStandardMove(Vector2Int from, Vector2Int to, PieceData piece, PieceData captured = null)
        {
            return new MoveCommand(from, to, piece, captured);
        }

        public static MoveCommand CreateCastlingMove(Vector2Int kingFrom, Vector2Int kingTo, PieceData king, Vector2Int rookFrom, Vector2Int rookTo)
        {
            return new MoveCommand(kingFrom, kingTo, king, null, SpecialMove.Castling, ChessPieceType.None, rookFrom, rookTo, null);
        }

        public static MoveCommand CreateEnPassantMove(Vector2Int from, Vector2Int to, PieceData pawn, PieceData capturedPawn, Vector2Int capturePosition)
        {
            return new MoveCommand(from, to, pawn, capturedPawn, SpecialMove.EnPassant, ChessPieceType.None, null, null, capturePosition);
        }

        public static MoveCommand CreatePromotionMove(Vector2Int from, Vector2Int to, PieceData pawn, ChessPieceType promotedTo, PieceData captured = null)
        {
            return new MoveCommand(from, to, pawn, captured, SpecialMove.Promotion, promotedTo, null, null, null);
        }

        #endregion

        /// <summary>
        /// Convenience properties for readability.
        /// </summary>
        public bool IsCapture => HasCapture;
        public bool IsPromotion => PromotedTo != ChessPieceType.None;
        public bool IsCastling => SpecialMoveType == SpecialMove.Castling;
        public bool IsEnPassant => SpecialMoveType == SpecialMove.EnPassant;
        public bool IsSpecialMove => SpecialMoveType != SpecialMove.None;

        public override string ToString()
        {
            string baseString = $"{PieceTeam} {PieceType} from {StartPosition} to {EndPosition}";

            if (HasCapture)
                baseString += $" capturing {CapturedType}";

            if (SpecialMoveType != SpecialMove.None)
                baseString += $" [{SpecialMoveType}]";

            if (PromotedTo != ChessPieceType.None)
                baseString += $" (Promoted to {PromotedTo})";

            return baseString;
        }
    }
}