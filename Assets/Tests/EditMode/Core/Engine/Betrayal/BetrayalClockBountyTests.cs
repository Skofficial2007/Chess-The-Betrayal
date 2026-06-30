using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Logic;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    [TestFixture]
    public class BetrayalClockBountyTests
    {
        private class MockHandler : IClockEventHandler
        {
            public void OnClockTimeout(Team timedOutTeam) { }
            public void OnLowTimeWarning(Team team, long remainingMs) { }
        }

        [Test]
        public void ApplyBetrayalBounty_NotCappedAtBaseTime_UnlikeStandardIncrement()
        {
            // Arrange: Blitz config with 5 minutes (300,000 ms)
            GameModeConfig config = new GameModeConfig("Test", 300_000L, 5_000L);
            ChessClock clock = new ChessClock(config, new MockHandler(), Team.White);

            // Act: Apply a 12s bounty to a clock already at max capacity
            clock.ApplyBetrayalBounty(Team.White, 12_000L);

            // Assert
            Assert.That(clock.State.WhiteRemainingMs, Is.EqualTo(312_000L),
                "Bounty must push the time ABOVE BaseTimeMs as an uncapped reward.");
        }

        [Test]
        public void ApplyBetrayalBounty_UnlimitedMode_AddsNothing()
        {
            // Arrange
            GameModeConfig config = GameModeConfig.Unlimited;
            ChessClock clock = new ChessClock(config, new MockHandler(), Team.White);

            // Act
            clock.ApplyBetrayalBounty(Team.White, 30_000L);

            // Assert
            Assert.That(clock.State.WhiteRemainingMs, Is.EqualTo(long.MaxValue));
        }
    }
}