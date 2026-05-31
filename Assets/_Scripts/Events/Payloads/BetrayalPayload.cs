using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events.Payloads
{
    /// <summary>
    /// Describes a phase transition in the Betrayal mechanic.
    /// </summary>
    public readonly struct BetrayalPayload
    {
        public readonly Team InitiatingTeam;
        public readonly Vector2Int BetrayerPosition;
        public readonly BetrayalPhase Phase;

        public BetrayalPayload(Team team, Vector2Int pos, BetrayalPhase phase)
        {
            InitiatingTeam   = team;
            BetrayerPosition = pos;
            Phase            = phase;
        }
    }

    /// <summary>
    /// Discrete steps of the Betrayal mechanic state machine.
    /// </summary>
    public enum BetrayalPhase
    {
        Initiated,          // Phase 1 complete — Betrayer has captured the Victim.
        RetributionPending, // Phase 2 — Player must select an Executioner.
        Resolved,           // Retribution succeeded — Betrayer removed from the board.
        DefectionOccurred,  // Retribution failed — Betrayer joined the opponent's army.
        ForcedSaveActive,   // Defection caused self-check — forced Save turn required.
    }
}
