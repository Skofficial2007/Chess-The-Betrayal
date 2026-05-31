using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events.Payloads
{
    /// <summary>
    /// Describes a completed turn transition.
    /// Clock data is absent by design; read from SharedClockStateSO if needed.
    /// The Source field exists for telemetry; the View layer must not branch on it.
    /// </summary>
    public readonly struct TurnChangedPayload
    {
        public readonly Team CurrentTeam;
        public readonly int TurnNumber;
        public readonly TurnSource Source;

        public TurnChangedPayload(Team team, int turnNumber, TurnSource source)
        {
            CurrentTeam = team;
            TurnNumber  = turnNumber;
            Source      = source;
        }

        public override string ToString() => $"Team={CurrentTeam} Turn={TurnNumber} Src={Source}";
    }

    public enum TurnSource { HumanLocal, HumanNetwork, AI }
}
