using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Engine
{
    /// <summary>
    /// Defines which phase of the Betrayal sequence a move command belongs to. Names match the
    /// pitch doc's shared vocabulary: Act (the betrayal itself), Retribution (an ally executes
    /// the Betrayer), Defection (the Betrayer permanently switches sides when no Retribution is
    /// possible), and Defensive Override (the initiator's forced King move when Defection leaves
    /// them in check).
    /// </summary>
    public enum BetrayalStage
    {
        None,
        Act,
        Retribution,
        DefensiveOverride,
        Defection
    }

    /// <summary>
    /// Canonical home for the turn-flip invariant: Act and Defection are half-moves by the same
    /// player and do NOT flip side-to-move; None, Retribution, and DefensiveOverride DO. Mirrored
    /// inline in ChessEngine.ApplyZobristMove's turn-hash toggle (can't delegate here — it's woven
    /// into the hash sequence) and delegated to from AlphaBetaSearch.StageFlipsTurn and
    /// UndoService. If this rule ever changes, update ApplyZobristMove too —
    /// SearchTurnFlipAgreementTests fails otherwise.
    /// </summary>
    public static class BetrayalStageRules
    {
        public static bool FlipsTurn(BetrayalStage stage) =>
            stage != BetrayalStage.Act && stage != BetrayalStage.Defection;
    }

    /// <summary>
    /// A snapshot of one move — where a piece came from, where it went, what was captured, and any special move type.
    /// Because it's a readonly struct, it's safe to store and pass around without worrying about data being changed underneath you.
    /// </summary>
    /// <remarks>
    /// TODO: this struct is deliberately full-fidelity, which makes it fairly large. When the AI
    /// search work happens, the plan is to add a compact packed move type for the search tree only
    /// and expand it back into a MoveCommand when a move is actually applied. Don't shrink this
    /// type before then — game execution and network serialization need the full data.
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

        // Full snapshot of the captured piece, so undoing a move puts back exactly the piece
        // that was taken — including flags like HasMoved.
        public readonly PieceData CapturedPieceFullState;

        public readonly SpecialMove SpecialMoveType;
        public readonly ChessPieceType PromotedTo;

        // Identifies whether this move constitutes a specific phase of the Betrayal Mechanic
        public readonly BetrayalStage Stage;

        // Additional metadata for special moves
        public readonly Vector2Int? RookStartPosition;
        public readonly Vector2Int? RookEndPosition;
        public readonly Vector2Int? EnPassantCapturePosition;

        // Board special states as they were before this move, so an undo can restore them exactly
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

        /// <summary>
        /// A Defection is a zero-displacement "move" (start == end) that flips the Betrayer's team.
        /// Routing it through the normal MoveCommand/ApplyMoveToBoard/UndoMoveOnBoard path gives the
        /// AI search a symmetric make/unmake for the failed-retribution resolution.
        /// </summary>
        public static MoveCommand CreateDefectionMove(Vector2Int square, PieceData betrayer, BoardState board = null)
        {
            return new MoveCommand(square, square, betrayer, default, SpecialMove.None, ChessPieceType.None, null, null, null,
                board?.CastlingRights ?? 0, board?.EnPassantFile,
                board?.BetrayalRightAvailable ?? true, board?.PendingBetrayerSquare, board?.BetrayalInitiator,
                stage: BetrayalStage.Defection);
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