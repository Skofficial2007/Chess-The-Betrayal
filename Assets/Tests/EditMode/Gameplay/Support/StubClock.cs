using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Logic;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay
{
    /// <summary>
    /// Hands back whatever the test last set, so a test can let time pass mid-question.
    ///
    /// Because the snapshot is a struct, this can never report "no clock at all" — a fixture that
    /// needs that case builds its executor with no clock source instead.
    /// </summary>
    internal sealed class StubClock : IClockSnapshotSource
    {
        public ClockState State;
        public ClockState? Current => State;
    }
}
