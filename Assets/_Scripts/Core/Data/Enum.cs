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

    // State Machine Phases for Game Flow
    // The Betrayal mechanic runs through RetributionPending → Normal/ForcedSave.
    // ResolutionFailed was removed: it was never a resting state — TurnResolver.Advance
    // always lands on Normal or ForcedSave in the same call that resolves a failed Retribution.
    // If you're adding a new phase, make sure TurnResolver and GameManager's TransitionToPhase() handle it.
    public enum TurnPhase
    {
        Starting,             // Board setup complete, waiting for presentation layer to finish animations
        Normal,               // Standard chess movement
        RetributionPending,   // Player chose Betrayal, must capture Betrayer
        ForcedSave,           // Player's King is in check after defection
        GameOver              // Game is inactive/finished
    }
}