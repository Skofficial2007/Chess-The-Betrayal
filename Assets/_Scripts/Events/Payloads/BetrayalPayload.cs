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

        /// <summary>
        /// True when MatchDriver already knows, at the moment this phase is raised, that no legal
        /// Retribution move exists and Defection will happen this same Act — i.e. TurnAdvanceResult
        /// .DidDefect was true before Initiated/RetributionPending were even raised. Lets
        /// BoardVisuals skip the Betrayer glow entirely on Initiated/RetributionPending when the
        /// glow would just be immediately spun away with nothing for the player to react to (there
        /// is no Retribution choice to make), rather than flashing on for one frame and off again.
        /// </summary>
        public readonly bool WillDefect;

        public BetrayalPayload(Team team, Vector2Int pos, BetrayalPhase phase, bool willDefect = false)
        {
            InitiatingTeam   = team;
            BetrayerPosition = pos;
            Phase            = phase;
            WillDefect       = willDefect;
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
