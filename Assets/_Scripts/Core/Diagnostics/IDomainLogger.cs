namespace ChessTheBetrayal.Core.Diagnostics
{
    /// <summary>
    /// Defines how domain classes emit diagnostic output.
    /// Implementations should reside in the presentation or server layers to maintain strict domain decoupling.
    /// </summary>
    public interface IDomainLogger
    {
        /// <summary>
        /// Indicates whether verbose logging is enabled.
        /// Check this before building a detail string so we skip the formatting work when nobody is listening.
        /// </summary>
        bool IsVerbose { get; }

        void LogInfo(DomainLogEvent evt);
        void LogWarning(DomainLogEvent evt);
        void LogError(DomainLogEvent evt);
    }

    /// <summary>
    /// A value-typed log payload. A readonly struct so passing one around
    /// does not create garbage at call sites.
    /// </summary>
    public readonly struct DomainLogEvent
    {
        public readonly DomainEventCode Code;

        /// <summary>
        /// Optional human-readable detail. Only pass a string when a verbosity
        /// check has already confirmed it will actually be used.
        /// </summary>
        public readonly string Message;

        /// <summary>
        /// Optional integer payload for supplementary data (e.g., board square index, ply depth, move count).
        /// </summary>
        public readonly int AuxInt;

        public DomainLogEvent(DomainEventCode code, string message = null, int auxInt = 0)
        {
            Code = code;
            Message = message;
            AuxInt = auxInt;
        }

        public override string ToString() =>
            Message != null ? $"[{Code}] {Message}" : $"[{Code}]";
    }

    /// <summary>
    /// Stable, versioned event codes for structured telemetry and diagnostics.
    /// </summary>
    public enum DomainEventCode
    {
        // ── Engine / Move Execution ───────────────────────────────────────────
        Engine_PromotionPieceNotFound   = 1001,
        Engine_KingNotFound             = 1002,
        Engine_IllegalMoveRequested     = 1003,
        Engine_MoveHistoryUnderflow     = 1004,

        // ── Board State ───────────────────────────────────────────────────────
        Board_PieceSetOutOfBounds       = 2001,
        Board_ZobristDesync             = 2002,

        // ── Special Mechanics ─────────────────────────────────────────────────
        Betrayal_RightAlreadyConsumed   = 3001,
        Betrayal_KingTargetedAsVictim   = 3002,
        Betrayal_KingTargetedAsBetrayer = 3003,
        Betrayal_RetributionPieceNone   = 3004,
        Betrayal_DefectionResolved      = 3005,
        Betrayal_ForcedSaveRequired     = 3006,
        Betrayal_ForcedSaveInvariantViolated = 3007,
        Betrayal_RetributionSkipped     = 3008,

        // ── AI Search ─────────────────────────────────────────────────────────
        AI_TranspositionHashCollision   = 4001,
        AI_SearchDepthExceeded          = 4002,
        AI_BetrayalBranchExpansion      = 4003,
    }
}