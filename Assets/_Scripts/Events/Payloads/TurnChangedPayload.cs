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

        /// <summary>Plies played, not turns — a turn covers a move from each side, and a Betrayal
        /// sub-sequence spends several plies without the turn passing at all.</summary>
        public readonly int PlyNumber;

        public readonly TurnSource Source;

        public TurnChangedPayload(Team team, int plyNumber, TurnSource source)
        {
            CurrentTeam = team;
            PlyNumber   = plyNumber;
            Source      = source;
        }

        public override string ToString() => $"Team={CurrentTeam} Ply={PlyNumber} Src={Source}";
    }

    public enum TurnSource { HumanLocal, HumanNetwork, AI }
}
