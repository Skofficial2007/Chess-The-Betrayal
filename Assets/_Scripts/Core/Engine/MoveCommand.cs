using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Engine
{
    /// <summary>
    /// Defines which phase of the Betrayal sequence a move command belongs to.
    /// </summary>
    public enum BetrayalStage
    {
        None,
        Act,
        Retribution,
        DefensiveSave
    }

    /// <summary>
    /// A snapshot of one move — where a piece came from, where it went, what was captured, and any special move type.
    /// Because it's a readonly struct, it's safe to store and pass around without worrying about data being changed underneath you.
    /// </summary>
    /// <remarks>
    /// TODO: Current struct size ~88 bytes impacts AI search cache locality at depth 8+.
    /// Solution: Introduce compact PackedMove : uint (32-bit) for search tree only:
    ///   - 6 bits from square (0-63)
    ///   - 6 bits to square (0-63)
    ///   - 4 bits flags (capture, promotion, castling, en passant)
    ///   - 4 bits special data (promotion piece, castling direction)
    /// Keep MoveCommand as full-fidelity type for game execution and network serialization.
    /// Conversion: PackedMove.Expand(board) → MoveCommand when applying moves.
    /// Do NOT optimize before AI sprint - multiplayer requires this full representation.
    /// </remarks>
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

        // Complete immutable snapshot of the captured piece.
        // Essential for AI unmake/remake paths where piece history must survive defection/resurrection.
        public readonly PieceData CapturedPieceFullState;

        public readonly SpecialMove SpecialMoveType;
        public readonly ChessPieceType PromotedTo;

        // Identifies whether this move constitutes a specific phase of the Betrayal Mechanic
        public readonly BetrayalStage Stage;

        // Additional metadata for special moves
        public readonly Vector2Int? RookStartPosition;
        public readonly Vector2Int? RookEndPosition;
        public readonly Vector2Int? EnPassantCapturePosition;

        // Snapshots of board special states BEFORE this move (for perfect Make/Unmake logic)
        public readonly int PreviousCastlingMask;
        public readonly int? PreviousEnPassantFile;
        public readonly bool PreviousBetrayalRightAvailable;
        public readonly Vector2Int? PreviousPendingBetrayerSquare;
        public readonly Team? PreviousBetrayalInitiator;

        // Clock snapshot at the moment this move was submitted.
        public readonly long WhiteRemainingMsAtMove;
        public readonly long BlackRemainingMsAtMove;

        /// <summary>
        /// Compatibility overload for older call sites that only passed the classic clock and stage arguments.
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
            Vector2Int? enPassantCapturePosition,
            int previousCastlingMask,
            int? previousEnPassantFile,
            long whiteRemainingMsAtMove,
            long blackRemainingMsAtMove,
            BetrayalStage stage)
            : this(
                startPosition,
                endPosition,
                pieceMoved,
                pieceCaptured,
                specialMoveType,
                promotedTo,
                rookStartPosition,
                rookEndPosition,
                enPassantCapturePosition,
                previousCastlingMask,
                previousEnPassantFile,
                true,
                null,
                null,
                whiteRemainingMsAtMove,
                blackRemainingMsAtMove,
                stage)
        {
        }

        /// <summary>
        /// Builds a move command from two piece references, capturing all board state required for a perfect unmake.
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
            int? previousEnPassantFile = null,
            bool previousBetrayalRightAvailable = true,
            Vector2Int? previousPendingBetrayerSquare = null,
            Team? previousBetrayalInitiator = null,
            long whiteRemainingMsAtMove = long.MaxValue,
            long blackRemainingMsAtMove = long.MaxValue,
            BetrayalStage stage = BetrayalStage.None)
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
                CapturedPieceFullState = pieceCaptured;
            }
            else
            {
                HasCapture = false;
                CapturedTeam = Team.White;
                CapturedType = ChessPieceType.None;
                CapturedHadMoved = false;
                CapturedPieceFullState = PieceData.Empty;
            }

            SpecialMoveType = specialMoveType;
            PromotedTo = promotedTo;
            RookStartPosition = rookStartPosition;
            RookEndPosition = rookEndPosition;
            EnPassantCapturePosition = enPassantCapturePosition;
            PreviousCastlingMask = previousCastlingMask;
            PreviousEnPassantFile = previousEnPassantFile;
            PreviousBetrayalRightAvailable = previousBetrayalRightAvailable;
            PreviousPendingBetrayerSquare = previousPendingBetrayerSquare;
            PreviousBetrayalInitiator = previousBetrayalInitiator;
            WhiteRemainingMsAtMove = whiteRemainingMsAtMove;
            BlackRemainingMsAtMove = blackRemainingMsAtMove;
            Stage = stage;
        }

        #region Factory Methods

        public static MoveCommand CreateStandardMove(Vector2Int from, Vector2Int to, PieceData piece, PieceData captured = default, BoardState board = null)
        {
            return new MoveCommand(from, to, piece, captured,
                previousCastlingMask: board?.CastlingRights ?? 0,
                previousEnPassantFile: board?.EnPassantFile,
                previousBetrayalRightAvailable: board?.BetrayalRightAvailable ?? true,
                previousPendingBetrayerSquare: board?.PendingBetrayerSquare,
                previousBetrayalInitiator: board?.BetrayalInitiator);
        }

        public static MoveCommand CreateCastlingMove(Vector2Int kingFrom, Vector2Int kingTo, PieceData king, Vector2Int rookFrom, Vector2Int rookTo, BoardState board = null)
        {
            return new MoveCommand(kingFrom, kingTo, king, default, SpecialMove.Castling, ChessPieceType.None, rookFrom, rookTo, null,
                board?.CastlingRights ?? 0, board?.EnPassantFile,
                board?.BetrayalRightAvailable ?? true, board?.PendingBetrayerSquare, board?.BetrayalInitiator);
        }

        public static MoveCommand CreateEnPassantMove(Vector2Int from, Vector2Int to, PieceData pawn, PieceData capturedPawn, Vector2Int capturePosition, BoardState board = null)
        {
            return new MoveCommand(from, to, pawn, capturedPawn, SpecialMove.EnPassant, ChessPieceType.None, null, null, capturePosition,
                board?.CastlingRights ?? 0, board?.EnPassantFile,
                board?.BetrayalRightAvailable ?? true, board?.PendingBetrayerSquare, board?.BetrayalInitiator);
        }

        public static MoveCommand CreatePromotionMove(Vector2Int from, Vector2Int to, PieceData pawn, ChessPieceType promotedTo, PieceData captured = default, BoardState board = null)
        {
            return new MoveCommand(from, to, pawn, captured, SpecialMove.Promotion, promotedTo, null, null, null,
                board?.CastlingRights ?? 0, board?.EnPassantFile,
                board?.BetrayalRightAvailable ?? true, board?.PendingBetrayerSquare, board?.BetrayalInitiator);
        }

        #endregion

        /// <summary>
        /// Returns a new MoveCommand with clock timestamps applied.
        /// </summary>
        public MoveCommand WithClockSnapshot(ClockState clock) =>
            new MoveCommand(
                StartPosition, EndPosition,
                new PieceData(PieceTeam, PieceType, PieceMoveDirection, 0, PieceHadMoved),
                CapturedPieceFullState,
                SpecialMoveType, PromotedTo,
                RookStartPosition, RookEndPosition, EnPassantCapturePosition,
                PreviousCastlingMask, PreviousEnPassantFile,
                PreviousBetrayalRightAvailable, PreviousPendingBetrayerSquare, PreviousBetrayalInitiator,
                clock.WhiteRemainingMs,
                clock.BlackRemainingMs,
                Stage);

        /// <summary>
        /// Returns a new MoveCommand with the specified BetrayalStage applied.
        /// </summary>
        public MoveCommand WithStage(BetrayalStage stage) =>
            new MoveCommand(
                StartPosition, EndPosition,
                new PieceData(PieceTeam, PieceType, PieceMoveDirection, 0, PieceHadMoved),
                CapturedPieceFullState,
                SpecialMoveType, PromotedTo,
                RookStartPosition, RookEndPosition, EnPassantCapturePosition,
                PreviousCastlingMask, PreviousEnPassantFile,
                PreviousBetrayalRightAvailable, PreviousPendingBetrayerSquare, PreviousBetrayalInitiator,
                WhiteRemainingMsAtMove,
                BlackRemainingMsAtMove,
                stage);

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

            if (Stage != BetrayalStage.None)
                baseString += $" [BetrayalStage: {Stage}]";

            return baseString;
        }
    }
}