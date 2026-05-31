using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events.Payloads
{
    /// <summary>
    /// Fired once per side per game when remaining time drops below the critical threshold.
    /// Used to trigger audio cues or HUD flash effects.
    /// </summary>
    public readonly struct LowTimeAlertPayload
    {
        public readonly Team AffectedTeam;
        public readonly long RemainingMs;

        public LowTimeAlertPayload(Team team, long remainingMs)
        {
            AffectedTeam = team;
            RemainingMs  = remainingMs;
        }
    }
}
