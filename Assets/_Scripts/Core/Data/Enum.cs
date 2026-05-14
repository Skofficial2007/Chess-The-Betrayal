namespace ChessTheMasterPiece.Data
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
    // The Betrayal mechanic runs through RetributionPending → ResolutionFailed/ForcedSave.
    // If you're adding a new phase, make sure GameManager's TransitionToPhase() handles it.
    public enum TurnPhase
    {
        Normal,               // Standard chess movement
        RetributionPending,   // Player chose Betrayal, must capture Betrayer
        ResolutionFailed,     // Betrayer defected — resolve check if needed
        ForcedSave,           // Player's King is in check after defection
        GameOver              // Game is inactive/finished
    }
}