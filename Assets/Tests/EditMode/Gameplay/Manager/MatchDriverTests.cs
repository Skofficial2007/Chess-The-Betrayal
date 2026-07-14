using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Logic;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// Pins MatchDriver's clock-snapshot seam directly: it must depend on IClockSnapshotSource,
    /// not any concrete MonoBehaviour, so a fake source (no GameObject involved at all) is enough
    /// to prove GetCurrentClockSnapshot passes the source's value straight through.
    /// </summary>
    [TestFixture]
    public class MatchDriverTests
    {
        private sealed class FakeClockSnapshotSource : IClockSnapshotSource
        {
            public ClockState? Current { get; set; }
        }

        private static MatchDriver CreateDriver()
        {
            var engine = new ChessEngineAdapter();
            var board = new BoardState(8, 8);
            return new MatchDriver(engine, board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
        }

        [Test]
        public void GetCurrentClockSnapshot_NoClockAttached_IsNull()
        {
            MatchDriver driver = CreateDriver();

            Assert.That(driver.GetCurrentClockSnapshot(), Is.Null);
        }

        [Test]
        public void GetCurrentClockSnapshot_SourceAttachedWithNoClock_IsNull()
        {
            MatchDriver driver = CreateDriver();
            var source = new FakeClockSnapshotSource { Current = null };

            driver.AttachClock(null, source, GameModeConfig.Unlimited);

            Assert.That(driver.GetCurrentClockSnapshot(), Is.Null);
        }

        [Test]
        public void GetCurrentClockSnapshot_SourceAttachedWithAClock_ReturnsTheSourcesSnapshot()
        {
            MatchDriver driver = CreateDriver();
            var snapshot = new ClockState
            {
                WhiteRemainingMs = 60_000L,
                BlackRemainingMs = 55_000L,
                ActiveSide = Team.White,
                IsRunning = true,
                IsExpired = false
            };
            var source = new FakeClockSnapshotSource { Current = snapshot };

            driver.AttachClock(null, source, GameModeConfig.Unlimited);

            Assert.That(driver.GetCurrentClockSnapshot(), Is.EqualTo(snapshot));
        }
    }
}
