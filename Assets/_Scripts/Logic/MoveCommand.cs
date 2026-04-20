using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.Logic
{
    /// <summary>
    /// Encapsulates all data required to execute (or undo) a single move.
    /// This is the object that will eventually be serialized and sent over a network.
    /// Immutable by design - once created, a command's data cannot be changed.
    /// </summary>
    public readonly struct MoveCommand
    {
        public readonly Vector2Int StartPosition;
        public readonly Vector2Int EndPosition;

        // We store the specific pieces involved so we can easily Undo the move later
        public readonly PieceData PieceMoved;
        public readonly PieceData PieceCaptured; // Can be null

        public readonly SpecialMove SpecialMoveType;

        // If this move resulted in a promotion, what did the piece become?
        public readonly ChessPieceType PromotedTo;

        // Additional metadata for special moves
        public readonly Vector2Int? RookStartPosition;  // For castling
        public readonly Vector2Int? RookEndPosition;    // For castling
        public readonly Vector2Int? EnPassantCapturePosition; // For en passant

        /// <summary>
        /// Standard move constructor.
        /// </summary>
        public MoveCommand(
            Vector2Int startPosition,
            Vector2Int endPosition,
            PieceData pieceMoved,
            PieceData pieceCaptured = null,
            SpecialMove specialMoveType = SpecialMove.None,
            ChessPieceType promotedTo = ChessPieceType.None)
        {
            StartPosition = startPosition;
            EndPosition = endPosition;
            PieceMoved = pieceMoved;
            PieceCaptured = pieceCaptured;
            SpecialMoveType = specialMoveType;
            PromotedTo = promotedTo;
            RookStartPosition = null;
            RookEndPosition = null;
            EnPassantCapturePosition = null;
        }

        /// <summary>
        /// Extended constructor for special moves with additional metadata.
        /// </summary>
        public MoveCommand(
            Vector2Int startPosition,
            Vector2Int endPosition,
            PieceData pieceMoved,
            PieceData pieceCaptured,
            SpecialMove specialMoveType,
            ChessPieceType promotedTo,
            Vector2Int? rookStartPosition,
            Vector2Int? rookEndPosition,
            Vector2Int? enPassantCapturePosition)
        {
            StartPosition = startPosition;
            EndPosition = endPosition;
            PieceMoved = pieceMoved;
            PieceCaptured = pieceCaptured;
            SpecialMoveType = specialMoveType;
            PromotedTo = promotedTo;
            RookStartPosition = rookStartPosition;
            RookEndPosition = rookEndPosition;
            EnPassantCapturePosition = enPassantCapturePosition;
        }

        /// <summary>
        /// Factory method: Create a standard move command.
        /// </summary>
        public static MoveCommand CreateStandardMove(Vector2Int from, Vector2Int to, PieceData piece, PieceData captured = null)
        {
            return new MoveCommand(from, to, piece, captured);
        }

        /// <summary>
        /// Factory method: Create a castling move command.
        /// </summary>
        public static MoveCommand CreateCastlingMove(
            Vector2Int kingFrom,
            Vector2Int kingTo,
            PieceData king,
            Vector2Int rookFrom,
            Vector2Int rookTo)
        {
            return new MoveCommand(
                kingFrom,
                kingTo,
                king,
                null,
                SpecialMove.Castling,
                ChessPieceType.None,
                rookFrom,
                rookTo,
                null
            );
        }

        /// <summary>
        /// Factory method: Create an en passant move command.
        /// </summary>
        public static MoveCommand CreateEnPassantMove(
            Vector2Int from,
            Vector2Int to,
            PieceData pawn,
            PieceData capturedPawn,
            Vector2Int capturePosition)
        {
            return new MoveCommand(
                from,
                to,
                pawn,
                capturedPawn,
                SpecialMove.EnPassant,
                ChessPieceType.None,
                null,
                null,
                capturePosition
            );
        }

        /// <summary>
        /// Factory method: Create a promotion move command.
        /// </summary>
        public static MoveCommand CreatePromotionMove(
            Vector2Int from,
            Vector2Int to,
            PieceData pawn,
            ChessPieceType promotedTo,
            PieceData captured = null)
        {
            return new MoveCommand(
                from,
                to,
                pawn,
                captured,
                SpecialMove.Promotion,
                promotedTo,
                null,
                null,
                null
            );
        }

        /// <summary>
        /// Convenience properties for readability.
        /// </summary>
        public bool IsCapture => PieceCaptured != null;
        public bool IsPromotion => PromotedTo != ChessPieceType.None;
        public bool IsCastling => SpecialMoveType == SpecialMove.Castling;
        public bool IsEnPassant => SpecialMoveType == SpecialMove.EnPassant;
        public bool IsSpecialMove => SpecialMoveType != SpecialMove.None;

        /// <summary>
        /// A descriptive string representation for debugging and logging.
        /// </summary>
        public override string ToString()
        {
            string baseString = $"{PieceMoved.Team} {PieceMoved.Type} from {StartPosition} to {EndPosition}";

            if (PieceCaptured != null)
                baseString += $" capturing {PieceCaptured.Type}";

            if (SpecialMoveType != SpecialMove.None)
                baseString += $" [{SpecialMoveType}]";

            if (PromotedTo != ChessPieceType.None)
                baseString += $" (Promoted to {PromotedTo})";

            if (IsCastling && RookStartPosition.HasValue && RookEndPosition.HasValue)
                baseString += $" (Rook: {RookStartPosition.Value} → {RookEndPosition.Value})";

            if (IsEnPassant && EnPassantCapturePosition.HasValue)
                baseString += $" (Captured at {EnPassantCapturePosition.Value})";

            return baseString;
        }
    }
}