using System.Text;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Logic;

namespace ChessTheBetrayal.Tests.EditMode.Core.Logic
{
    /// <summary>
    /// Test suite for the ChessClock system, validating time tracking, Fischer increments, timeout events, and formatter output.
    /// </summary>
    [TestFixture]
    public class ChessClockTests
    {
        // ── Mock Handler ───────────────────────────────────────────────────────

        private class MockClockHandler : IClockEventHandler
        {
            public Team? TimedOutTeam { get; private set; }
            public Team? LowTimeWarningTeam { get; private set; }
            public long LowTimeWarningMs { get; private set; }

            public void OnClockTimeout(Team timedOutTeam)
            {
                TimedOutTeam = timedOutTeam;
            }

            public void OnLowTimeWarning(Team team, long remainingMs)
            {
                LowTimeWarningTeam = team;
                LowTimeWarningMs = remainingMs;
            }

            public void Reset()
            {
                TimedOutTeam = null;
                LowTimeWarningTeam = null;
                LowTimeWarningMs = 0;
            }
        }

        private MockClockHandler _mockHandler;

        [SetUp]
        public void Setup()
        {
            _mockHandler = new MockClockHandler();
        }

        // ── Clock Engine Tests ─────────────────────────────────────────────────

        [Test]
        public void Clock_InitialState_IsCorrectlyConfigured()
        {
            // Arrange & Act
            var config = new GameModeConfig("Test", 60_000, 0);
            var clock = new ChessClock(config, _mockHandler, Team.White);

            // Assert
            Assert.AreEqual(60_000, clock.State.WhiteRemainingMs, "White should start with full base time.");
            Assert.AreEqual(60_000, clock.State.BlackRemainingMs, "Black should start with full base time.");
            Assert.AreEqual(Team.White, clock.State.ActiveSide, "White should be the default active side.");
            Assert.IsFalse(clock.State.IsRunning, "Clock should not be running until Start() is called.");
            Assert.IsFalse(clock.State.IsExpired, "Clock should not be expired initially.");
        }

        [Test]
        public void Clock_Start_EnablesClockRunning()
        {
            // Arrange
            var config = new GameModeConfig("Test", 60_000, 0);
            var clock = new ChessClock(config, _mockHandler, Team.White);

            // Act
            clock.Start();

            // Assert
            Assert.IsTrue(clock.State.IsRunning, "Clock should be running after Start() is called.");
        }

        [Test]
        public void Clock_Tick_DecrementsActiveTeamTime()
        {
            // Arrange
            var config = new GameModeConfig("Test", 60_000, 0);
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();

            // Act
            clock.Tick(1000); // 1 second

            // Assert
            Assert.AreEqual(59_000, clock.State.WhiteRemainingMs, "White's time should decrease by 1 second.");
            Assert.AreEqual(60_000, clock.State.BlackRemainingMs, "Black's time should remain unchanged.");
        }

        [Test]
        public void Clock_Tick_DoesNotDecrementWhenPaused()
        {
            // Arrange
            var config = new GameModeConfig("Test", 60_000, 0);
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();
            clock.Pause();

            // Act
            clock.Tick(5000);

            // Assert
            Assert.AreEqual(60_000, clock.State.WhiteRemainingMs, "White's time should not decrease when paused.");
            Assert.IsFalse(clock.State.IsRunning, "Clock should not be running after Pause().");
        }

        [Test]
        public void Clock_Tick_FiresTimeoutEvent_WhenReachingZero()
        {
            // Arrange
            var config = new GameModeConfig("Test", 5_000, 0);
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();

            // Act
            clock.Tick(6000); // Overshoot the time

            // Assert
            Assert.IsTrue(clock.State.IsExpired, "Clock should be expired when time reaches zero.");
            Assert.AreEqual(0, clock.State.WhiteRemainingMs, "White's time should be clamped at zero.");
            Assert.AreEqual(Team.White, _mockHandler.TimedOutTeam, "Timeout handler should be invoked with White team.");
            Assert.IsFalse(clock.State.IsRunning, "Clock should stop running after timeout.");
        }

        [Test]
        public void Clock_Tick_DoesNotContinueAfterTimeout()
        {
            // Arrange
            var config = new GameModeConfig("Test", 5_000, 0);
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();
            clock.Tick(6000); // Expire the clock

            _mockHandler.Reset();

            // Act
            clock.Tick(10_000); // Try to tick again

            // Assert
            Assert.AreEqual(0, clock.State.WhiteRemainingMs, "Time should remain at zero.");
            Assert.IsNull(_mockHandler.TimedOutTeam, "Timeout handler should not be invoked again.");
        }

        [Test]
        public void Clock_OnMoveMade_SwitchesActiveSide()
        {
            // Arrange
            var config = new GameModeConfig("Test", 60_000, 0);
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();

            // Act
            clock.OnMoveMade(Team.White);

            // Assert
            Assert.AreEqual(Team.Black, clock.State.ActiveSide, "Active side should switch to Black after White's move.");
        }

        [Test]
        public void Clock_OnMoveMade_AppliesIncrement()
        {
            // Arrange
            var config = new GameModeConfig("Test", 60_000, 5_000);
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();

            // Act
            clock.Tick(10_000); // White drops to 50,000
            clock.OnMoveMade(Team.White); // White finishes turn, gets +5,000 increment

            // Assert
            Assert.AreEqual(55_000, clock.State.WhiteRemainingMs, "White should gain 5 seconds from Fischer increment.");
            Assert.AreEqual(Team.Black, clock.State.ActiveSide, "Active side should switch to Black.");
        }

        [Test]
        public void Clock_OnMoveMade_CapsIncrementAtBaseTime()
        {
            // Arrange
            var config = new GameModeConfig("Test", 60_000, 5_000);
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();

            // Act
            clock.Tick(1_000); // White drops to 59,000
            clock.OnMoveMade(Team.White); // Attempt to add 5,000 (would be 64,000)

            // Assert
            Assert.AreEqual(60_000, clock.State.WhiteRemainingMs, "Increment should be capped at BaseTimeMs to prevent time hoarding.");
        }

        [Test]
        public void Clock_OnMoveMade_IncrementDoesNotExceedBase_FromMultipleMoves()
        {
            // Arrange
            var config = new GameModeConfig("Test", 60_000, 2_000);
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();

            // Act - Make multiple quick moves
            clock.Tick(500);
            clock.OnMoveMade(Team.White); // White: 59,500 + 2,000 = 61,500 -> capped at 60,000

            clock.Tick(500);
            clock.OnMoveMade(Team.Black); // Black: 59,500 + 2,000 = 61,500 -> capped at 60,000

            // Assert
            Assert.AreEqual(60_000, clock.State.WhiteRemainingMs, "White's time should be capped at base time.");
            Assert.AreEqual(60_000, clock.State.BlackRemainingMs, "Black's time should be capped at base time.");
        }

        [Test]
        public void Clock_LowTimeWarning_FiresOncePerTeam()
        {
            // Arrange
            var config = new GameModeConfig("Test", 15_000, 0);
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();

            // Act - Tick down to below 10 seconds
            clock.Tick(6_000); // 9,000 ms remaining

            // Assert
            Assert.AreEqual(Team.White, _mockHandler.LowTimeWarningTeam, "Low time warning should fire for White.");
            Assert.IsTrue(_mockHandler.LowTimeWarningMs <= 10_000, "Warning should fire when below 10 seconds.");

            // Reset mock and tick again
            _mockHandler.Reset();
            clock.Tick(1_000); // 8,000 ms remaining

            // Assert - Should not fire again
            Assert.IsNull(_mockHandler.LowTimeWarningTeam, "Low time warning should only fire once per team.");
        }

        [Test]
        public void Clock_UnlimitedMode_IgnoresTicks()
        {
            // Arrange
            var config = GameModeConfig.Unlimited;
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();

            // Act
            clock.Tick(999_999_999);

            // Assert
            Assert.IsFalse(clock.State.IsExpired, "Unlimited clock should never expire.");
            Assert.IsNull(_mockHandler.TimedOutTeam, "Timeout handler should not be invoked in unlimited mode.");
        }

        [Test]
        public void Clock_UnlimitedMode_IgnoresMoveMade()
        {
            // Arrange
            var config = GameModeConfig.Unlimited;
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();

            Team initialSide = clock.State.ActiveSide;

            // Act
            clock.OnMoveMade(Team.White);

            // Assert
            Assert.AreEqual(initialSide, clock.State.ActiveSide, "Unlimited mode should not switch active side via OnMoveMade.");
        }

        [Test]
        public void Clock_Resume_RestartsClockAfterPause()
        {
            // Arrange
            var config = new GameModeConfig("Test", 60_000, 0);
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();
            clock.Pause();

            // Act
            clock.Resume();

            // Assert
            Assert.IsTrue(clock.State.IsRunning, "Clock should be running after Resume().");
        }

        [Test]
        public void Clock_Resume_DoesNotResumeUnlimitedMode()
        {
            // Arrange
            var config = GameModeConfig.Unlimited;
            var clock = new ChessClock(config, _mockHandler, Team.White);
            clock.Start();
            clock.Pause();

            // Act
            clock.Resume();

            // Assert
            Assert.IsFalse(clock.State.IsRunning, "Unlimited mode should not resume via Resume() call.");
        }

        [Test]
        public void Clock_InitialActiveSide_CanBeBlack()
        {
            // Arrange & Act
            var config = new GameModeConfig("Test", 60_000, 0);
            var clock = new ChessClock(config, _mockHandler, Team.Black);

            // Assert
            Assert.AreEqual(Team.Black, clock.State.ActiveSide, "Clock should start with Black as active side when specified.");
        }

        [Test]
        public void Clock_Tick_DecrementsBlackTime_WhenBlackIsActive()
        {
            // Arrange
            var config = new GameModeConfig("Test", 60_000, 0);
            var clock = new ChessClock(config, _mockHandler, Team.Black);
            clock.Start();

            // Act
            clock.Tick(3000); // 3 seconds

            // Assert
            Assert.AreEqual(57_000, clock.State.BlackRemainingMs, "Black's time should decrease when Black is active.");
            Assert.AreEqual(60_000, clock.State.WhiteRemainingMs, "White's time should remain unchanged when Black is active.");
        }

        [Test]
        public void Clock_OnMoveMade_SwitchesFromBlackToWhite()
        {
            // Arrange
            var config = new GameModeConfig("Test", 60_000, 2_000);
            var clock = new ChessClock(config, _mockHandler, Team.Black);
            clock.Start();

            // Act
            clock.Tick(5_000); // Black drops to 55,000
            clock.OnMoveMade(Team.Black); // Black finishes turn, gets +2,000 increment

            // Assert
            Assert.AreEqual(57_000, clock.State.BlackRemainingMs, "Black should gain increment after move.");
            Assert.AreEqual(Team.White, clock.State.ActiveSide, "Active side should switch to White after Black's move.");
        }

        // ── Clock Formatter Tests ──────────────────────────────────────────────

        [Test]
        public void Formatter_FormatsMinutesAndSeconds_Correctly()
        {
            // Arrange
            var sb = new StringBuilder();

            // Act & Assert - 3 minutes, 5 seconds = 185,000 ms
            ClockFormatter.FormatInto(sb, 185_000);
            Assert.AreEqual("3:05", sb.ToString(), "3 minutes 5 seconds should format as '3:05'.");

            // Act & Assert - 15 minutes, 0 seconds = 900,000 ms
            ClockFormatter.FormatInto(sb, 900_000);
            Assert.AreEqual("15:00", sb.ToString(), "15 minutes should format as '15:00'.");

            // Act & Assert - 1 minute, 30 seconds = 90,000 ms
            ClockFormatter.FormatInto(sb, 90_000);
            Assert.AreEqual("1:30", sb.ToString(), "1 minute 30 seconds should format as '1:30'.");
        }

        [Test]
        public void Formatter_FormatsTenthsOfSeconds_WhenUnderOneMinute()
        {
            // Arrange
            var sb = new StringBuilder();

            // Act & Assert - 9 seconds and 4 tenths = 9,450 ms
            ClockFormatter.FormatInto(sb, 9_450);
            Assert.AreEqual("09.4", sb.ToString(), "9.4 seconds should format as '09.4'.");

            // Act & Assert - 0 seconds and 1 tenth = 199 ms
            ClockFormatter.FormatInto(sb, 199);
            Assert.AreEqual("00.1", sb.ToString(), "0.1 seconds should format as '00.1'.");

            // Act & Assert - 45 seconds and 9 tenths = 45,999 ms
            ClockFormatter.FormatInto(sb, 45_999);
            Assert.AreEqual("45.9", sb.ToString(), "45.9 seconds should format as '45.9'.");
        }

        [Test]
        public void Formatter_FormatsExactMinuteBoundary()
        {
            // Arrange
            var sb = new StringBuilder();

            // Act & Assert - Exactly 60 seconds = 60,000 ms
            ClockFormatter.FormatInto(sb, 60_000);
            Assert.AreEqual("1:00", sb.ToString(), "Exactly 1 minute should format as '1:00'.");

            // Act & Assert - 59 seconds = 59,000 ms (should show tenths)
            ClockFormatter.FormatInto(sb, 59_000);
            Assert.AreEqual("59.0", sb.ToString(), "59 seconds should format as '59.0' with tenths.");
        }

        [Test]
        public void Formatter_FormatsZero_WhenNegative()
        {
            // Arrange
            var sb = new StringBuilder();

            // Act
            ClockFormatter.FormatInto(sb, -5000);

            // Assert
            Assert.AreEqual("0:00", sb.ToString(), "Negative time should format as '0:00'.");
        }

        [Test]
        public void Formatter_FormatsZero_WhenExactlyZero()
        {
            // Arrange
            var sb = new StringBuilder();

            // Act
            ClockFormatter.FormatInto(sb, 0);

            // Assert
            Assert.AreEqual("0:00", sb.ToString(), "Zero time should format as '0:00'.");
        }

        [Test]
        public void Formatter_ReusesStringBuilder_WithoutLeakingPreviousContent()
        {
            // Arrange
            var sb = new StringBuilder();
            ClockFormatter.FormatInto(sb, 120_000); // "2:00"

            // Act
            ClockFormatter.FormatInto(sb, 5_000); // "05.0"

            // Assert
            Assert.AreEqual("05.0", sb.ToString(), "StringBuilder should be cleared before formatting new value.");
            Assert.AreEqual(4, sb.Length, "StringBuilder should only contain the new formatted time.");
        }
    }
}
