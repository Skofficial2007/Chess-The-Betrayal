namespace ChessTheBetrayal.Core.Data
{
    public enum ChessPieceType
    {
        None = 0,
        Pawn = 1,
        Rook = 2,
        Knight = 3,
        Bishop = 4,
        Queen = 5,
        King = 6
    }

    public enum SpecialMove
    {
        None = 0,
        EnPassant = 1,
        Castling = 2,
        Promotion = 3,
    }

    public enum Team
    {
        None  = -1,
        White =  0,
        Black =  1
    }

    /// <summary>
    /// State Machine Phases for Game Flow. This is the ground truth for the Betrayal
    /// state machine — if you're adding a phase or a BetrayalStage, update this diagram
    /// first, then make TurnResolver and GameManager's TransitionToPhase() match it.
    ///
    /// <code>
    /// Normal ──Act──► RetributionPending ──Retribution──► Normal
    ///                         │
    ///                    (no legal retribution)
    ///                         ▼
    ///                    Defection ──(self-check?)──► ForcedSave ──DefensiveSave──► Normal
    ///                         └────────(no check)──────────────────────────────► Normal
    /// </code>
    ///
    /// Act, Retribution, DefensiveSave, and Defection are <see cref="ChessTheBetrayal.Core.Engine.BetrayalStage"/>
    /// values tagged on the MoveCommand that drives each transition — they are not TurnPhase
    /// values themselves. Defection is resolved synchronously inside TurnResolver.Advance
    /// (via ChessEngine.ResolveFailedRetribution) in the same call that detects "no legal
    /// retribution," so it is never an observable resting TurnPhase; ResolutionFailed was
    /// removed for exactly this reason — it never described anything the state machine
    /// actually paused in.
    /// </summary>
    public enum TurnPhase
    {
        Starting,             // Board setup complete, waiting for presentation layer to finish animations
        Normal,               // Standard chess movement
        RetributionPending,   // Player chose Betrayal, must capture Betrayer
        ForcedSave,           // Player's King is in check after defection
        GameOver              // Game is inactive/finished
    }
}