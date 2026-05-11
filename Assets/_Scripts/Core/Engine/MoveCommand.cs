using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.Logic
{
    /// <summary>
    /// A snapshot of one move — where a piece came from, where it went, what was captured, and any special move type.
    /// Because it's a readonly struct, it's safe to store and pass around without worrying about data being changed underneath you.
    /// </summary>
    public readonly struct MoveCommand
    {
        public readonly Vector2Int StartPosition;
        public readonly Vector2Int EndPosition;

        // Snapshot of the moved piece
        public readonly Team PieceTeam;
        public readonly ChessPieceType PieceType;
        public readonly int PieceMoveDirection; // +1 for White (moving up the board), -1 for Black (moving down)
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

        // Snapshots of board special states BEFORE this move (for perfect Make/Unmake)
        public readonly int PreviousCastlingMask;
        public readonly int? PreviousEnPassantFile;

        /// <summary>
        /// Builds a move command from two piece references. We pull the data we need out of them immediately so the command doesn't hold live references.
        /// </summary>
        public MoveCommand(
            Vector2Int startPosition,
            Vector2Int endPosition,
            PieceData pieceMoved,
            PieceData pieceCaptured = default,
            SpecialMove specialMoveType = SpecialMove.None,
            ChessPieceType promotedTo = ChessPieceType.None,
            Vector2Int? rookStartPosition = null,
            Vector2Int? rookEndPosition = null,
            Vector2Int? enPassantCapturePosition = null,
            int previousCastlingMask = 0,
            int? previousEnPassantFile = null)
        {
            StartPosition = startPosition;
            EndPosition = endPosition;

            if (!pieceMoved.IsEmpty)
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

            if (!pieceCaptured.IsEmpty)
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
            PreviousCastlingMask = previousCastlingMask;
            PreviousEnPassantFile = previousEnPassantFile;
        }

        #region Factory Methods

        public static MoveCommand CreateStandardMove(Vector2Int from, Vector2Int to, PieceData piece, PieceData captured = default, BoardState board = null)
        {
            return new MoveCommand(from, to, piece, captured,
                previousCastlingMask: board?.CastlingRights ?? 0,
                previousEnPassantFile: board?.EnPassantFile);
        }

        public static MoveCommand CreateCastlingMove(Vector2Int kingFrom, Vector2Int kingTo, PieceData king, Vector2Int rookFrom, Vector2Int rookTo, BoardState board = null)
        {
            return new MoveCommand(kingFrom, kingTo, king, default, SpecialMove.Castling, ChessPieceType.None, rookFrom, rookTo, null,
                board?.CastlingRights ?? 0, board?.EnPassantFile);
        }

        public static MoveCommand CreateEnPassantMove(Vector2Int from, Vector2Int to, PieceData pawn, PieceData capturedPawn, Vector2Int capturePosition, BoardState board = null)
        {
            return new MoveCommand(from, to, pawn, capturedPawn, SpecialMove.EnPassant, ChessPieceType.None, null, null, capturePosition,
                board?.CastlingRights ?? 0, board?.EnPassantFile);
        }

        public static MoveCommand CreatePromotionMove(Vector2Int from, Vector2Int to, PieceData pawn, ChessPieceType promotedTo, PieceData captured = default, BoardState board = null)
        {
            return new MoveCommand(from, to, pawn, captured, SpecialMove.Promotion, promotedTo, null, null, null,
                board?.CastlingRights ?? 0, board?.EnPassantFile);
        }

        #endregion

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