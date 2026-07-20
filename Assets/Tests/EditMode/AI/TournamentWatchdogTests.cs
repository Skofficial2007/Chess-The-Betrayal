using System;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins TournamentWatchdog's core promise: it trips only on genuine stall (no progress for
    /// longer than the window), never on a run that's merely slow-but-alive. Every test drives an
    /// injected clock instead of real Stopwatch/Sleep time, so "does it trip after exactly this
    /// long" is both fast and deterministic rather than a real-time race.
    /// </summary>
    [TestFixture]
    public class TournamentWatchdogTests
    {
        /// <summary>A fake clock a test can advance on demand, standing in for real wall-clock time
        /// so a stall window can be crossed instantly instead of by actually waiting.</summary>
        private sealed class FakeClock : IElapsedClock
        {
            private TimeSpan _elapsed = TimeSpan.Zero;
            public TimeSpan Elapsed { get { lock (this) return _elapsed; } }
            public void Advance(TimeSpan by) { lock (this) _elapsed += by; }
        }

        [Test]
        public void DeriveStallWindow_ScalesWithPlyCapAndHardBudget()
        {
            TimeSpan window = TournamentWatchdog.DeriveStallWindow(plyCap: 120, slowestHardBudgetMs: 3000);

            // 120 * 3000ms * 2.0 safety multiplier = 720,000ms = 12 minutes.
            Assert.That(window.TotalMilliseconds, Is.EqualTo(720_000).Within(1));
        }

        [Test]
        public void DeriveStallWindow_TinyBudget_FloorsAtOneMinute()
        {
            TimeSpan window = TournamentWatchdog.DeriveStallWindow(plyCap: 1, slowestHardBudgetMs: 10);

            Assert.That(window.TotalMilliseconds, Is.EqualTo(60_000));
        }

        [Test]
        public void ReportProgress_KeepsArriving_NeverTrips_EvenPastTheWindow()
        {
            var clock = new FakeClock();
            var window = TimeSpan.FromSeconds(10);
            var trippedSignal = new ManualResetEventSlim(false);

            using (var watchdog = new TournamentWatchdog(window, TimeSpan.FromMilliseconds(10), CancellationToken.None, clock))
            {
                watchdog.Token.Register(trippedSignal.Set);

                // Simulate a slow-but-alive run: progress arrives every "5 seconds" of fake time,
                // well within the 10-second window each time, for far longer than the window itself
                // in total elapsed terms.
                for (int i = 1; i <= 20; i++)
                {
                    clock.Advance(TimeSpan.FromSeconds(5));
                    watchdog.ReportProgress(i);
                    // Give the real poll timer a moment to observe the fake clock at least once
                    // per simulated step, without depending on exact timer cadence.
                    Thread.Sleep(15);
                }

                bool trippedInTime = trippedSignal.Wait(200);
                Assert.That(trippedInTime, Is.False, "a run that keeps reporting progress inside the window must never trip.");
                Assert.That(watchdog.HasTripped, Is.False);
            }
        }

        [Test]
        public void NoProgress_PastTheWindow_Trips_AndCancelsToken()
        {
            var clock = new FakeClock();
            var window = TimeSpan.FromMilliseconds(200);
            var trippedSignal = new ManualResetEventSlim(false);

            using (var watchdog = new TournamentWatchdog(window, TimeSpan.FromMilliseconds(20), CancellationToken.None, clock))
            {
                watchdog.Token.Register(trippedSignal.Set);
                watchdog.ReportProgress(1);

                // Advance well past the window with no further progress reports — this is the
                // "deadlocked worker" shape: the clock keeps moving, the counter does not.
                clock.Advance(TimeSpan.FromMilliseconds(500));

                bool trippedInTime = trippedSignal.Wait(2000);

                Assert.That(trippedInTime, Is.True, "no progress past the stall window must trip the watchdog.");
                Assert.That(watchdog.HasTripped, Is.True);
                Assert.That(watchdog.Token.IsCancellationRequested, Is.True);
                Assert.That(watchdog.TripReason, Does.Contain("no game finished"));
            }
        }

        [Test]
        public void ExternalTokenCancelled_AlsoCancelsWatchdogToken_ButDoesNotMarkItTripped()
        {
            using (var externalSource = new CancellationTokenSource())
            using (var watchdog = new TournamentWatchdog(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(1), externalSource.Token))
            {
                externalSource.Cancel();

                Assert.That(watchdog.Token.IsCancellationRequested, Is.True,
                    "a caller-initiated cancel must flow through the watchdog's own token.");
                Assert.That(watchdog.HasTripped, Is.False,
                    "an external cancel is a deliberate stop, not a stall — TripReason/HasTripped are reserved for the watchdog's own detection.");
            }
        }
    }
}
