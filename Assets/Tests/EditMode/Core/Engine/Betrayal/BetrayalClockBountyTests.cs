using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Logic;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    /// <summary>
    /// Validates the Time Bounty system and its interaction with standard chess clocks.
    /// Ensures that Betrayal rewards compound correctly with Fischer increments without overwriting them.
    /// </summary>
    [TestFixture]
    public class BetrayalClockBountyTests
    {
        private class MockHandler : IClockEventHandler
        {
            public void OnClockTimeout(Team timedOutTeam) { }
            public void OnLowTimeWarning(Team team, long remainingMs) { }
        }

        [Test]
        public void ApplyBetrayalBounty_NotCappedAtBaseTime_PushesClockAboveStartingLimit()
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
        public void ApplyBetrayalBounty_UnlimitedMode_TimeRemainsMaxed()
        {
            // Arrange
            GameModeConfig config = GameModeConfig.Unlimited;
            ChessClock clock = new ChessClock(config, new MockHandler(), Team.White);

            // Act
            clock.ApplyBetrayalBounty(Team.White, 30_000L);

            // Assert
            Assert.That(clock.State.WhiteRemainingMs, Is.EqualTo(long.MaxValue),
                "Unlimited mode should ignore bounties and remain at long.MaxValue.");
        }

        [Test]
        public void ClockState_RetainsRunningStatus_WhenResumed()
        {
            // Arrange
            GameModeConfig config = new GameModeConfig("Blitz", 300_000L, 5_000L);
            ChessClock clock = new ChessClock(config, new MockHandler(), Team.White);

            // Act & Assert
            clock.Start();
            Assert.That(clock.State.IsRunning, Is.True, "Clock should be running after Start.");

            clock.Pause();
            Assert.That(clock.State.IsRunning, Is.False, "Clock should stop during Pause.");

            clock.Resume();
            Assert.That(clock.State.IsRunning, Is.True, "Clock must correctly resume and track time. This ensures mid-sequence Betrayals keep pressure on the player.");
        }

        [Test]
        public void StandardIncrement_AndBetrayalBounty_CompoundWithoutOverwriting()
        {
            // Arrange: Blitz 5|5 (300,000ms base, 5,000ms increment)
            GameModeConfig config = new GameModeConfig("Blitz", 300_000L, 5_000L);
            ChessClock clock = new ChessClock(config, new MockHandler(), Team.White);

            clock.Start();

            // We must simulate 10 seconds of thinking time (10,000 ms) passing manually
            // so the clock drops below the BaseTime capacity. 
            // If the clock is full, the standard Fischer increment gets clamped and 'eaten'!
            clock.Tick(10000L);

            // Act 1: The turn ends, Fischer increment applies
            clock.OnMoveMade(Team.White);

            // Assert 1: 300k - 10k + 5k = 295k
            Assert.That(clock.State.WhiteRemainingMs, Is.EqualTo(295_000L), "Fischer increment of 5s should apply to the remaining 290s.");

            // Act 2: The Retribution succeeds, 12s Time Bounty applies
            clock.ApplyBetrayalBounty(Team.White, 12_000L);

            // Assert 2: 295k + 12k = 307k
            Assert.That(clock.State.WhiteRemainingMs, Is.EqualTo(307_000L), "The 12s Time Bounty must compound on top of the 5s Fischer increment sequentially.");
        }

        [TestCase(60_000L, 0L, 3_000L, "Bullet 1|0")]
        [TestCase(120_000L, 1_000L, 5_000L, "Bullet 2|1")]
        [TestCase(180_000L, 0L, 8_000L, "Blitz 3|0")]
        [TestCase(300_000L, 5_000L, 12_000L, "Blitz 5|5")]
        [TestCase(600_000L, 0L, 20_000L, "Rapid 10|0")]
        [TestCase(900_000L, 10_000L, 30_000L, "Rapid 15|10")]
        public void ApplyBetrayalBounty_AllTimedPresets_AwardCorrectExactMilliseconds(long baseMs, long incMs, long expectedBountyMs, string modeName)
        {
            // Arrange
            GameModeConfig config = new GameModeConfig(modeName, baseMs, incMs);
            ChessClock clock = new ChessClock(config, new MockHandler(), Team.White);

            // Simulate 10 seconds passing so we don't hit the capacity cap during the test
            clock.Start();
            clock.Tick(10000L);
            long expectedTime = (baseMs - 10000L) + expectedBountyMs;

            // Act
            clock.ApplyBetrayalBounty(Team.White, expectedBountyMs);

            // Assert
            Assert.That(clock.State.WhiteRemainingMs, Is.EqualTo(expectedTime),
                $"{modeName} preset must successfully apply an exact {expectedBountyMs}ms bounty.");
        }
    }
}