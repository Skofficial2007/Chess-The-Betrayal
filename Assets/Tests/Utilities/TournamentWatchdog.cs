using System;
using System.Threading;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>Reports elapsed time since construction. Exists so TournamentWatchdogTests can
    /// drive time deterministically instead of relying on a real Stopwatch and a real Sleep, which
    /// would make "does the watchdog trip after exactly this long" both slow and flaky to test.</summary>
    public interface IElapsedClock
    {
        TimeSpan Elapsed { get; }
    }

    /// <summary>A real wall-clock timer — what production code uses.</summary>
    public sealed class StopwatchElapsedClock : IElapsedClock
    {
        private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        public TimeSpan Elapsed => _stopwatch.Elapsed;
    }

    /// <summary>
    /// Stops a tournament run that has genuinely stalled instead of letting it run forever — the
    /// difference between a benchmark that's merely slow and one that's dead was, until this,
    /// something a human had to guess at by watching a log go quiet.
    ///
    /// "No progress" means no game has been COMPLETED for longer than the stall window — not total
    /// elapsed time. A pairing between the two deepest tiers can legitimately take many minutes; the
    /// watchdog must never punish that. It only fires when the completion counter genuinely stops
    /// moving, which happens on deadlock, an unhandled exception swallowed somewhere in a worker, or
    /// a machine that's gone to sleep — never on a run that's simply working through a hard position.
    /// </summary>
    public sealed class TournamentWatchdog : IDisposable
    {
        private readonly IElapsedClock _clock;
        private readonly TimeSpan _stallWindow;
        private readonly CancellationTokenSource _linkedSource;
        private readonly Timer _pollTimer;
        private readonly object _gate = new object();

        private int _lastObservedCompleted = -1;
        private TimeSpan _lastProgressAt;
        private bool _tripped;

        public string TripReason { get; private set; }

        /// <summary>The token to hand to the work being watched — cancelling it is how the watchdog
        /// stops a stalled run. Also cancelled if the caller's own token fires, so both sources
        /// funnel through one place.</summary>
        public CancellationToken Token => _linkedSource.Token;

        public bool HasTripped => _tripped;

        /// <summary>
        /// stallWindow is the longest gap allowed between two completed games before the run is
        /// declared dead. Derive it from the run's own shape (ply cap x slowest hard budget x a
        /// safety multiplier) rather than a fixed constant — a constant tuned for a fast pairing
        /// would kill a legitimately slow one. pollInterval controls how often the counter is
        /// sampled; it should be well under the stall window, never close to it.
        /// </summary>
        public TournamentWatchdog(TimeSpan stallWindow, TimeSpan pollInterval, CancellationToken externalToken,
            IElapsedClock clock = null)
        {
            _clock = clock ?? new StopwatchElapsedClock();
            _stallWindow = stallWindow;
            _lastProgressAt = _clock.Elapsed;
            _linkedSource = CancellationTokenSource.CreateLinkedTokenSource(externalToken);

            _pollTimer = new Timer(_ => Poll(), null, pollInterval, pollInterval);
        }

        /// <summary>Call once per completed game (safe from any thread, including worker threads —
        /// this only ever writes an int and a TimeSpan under a lock, never touches the token or
        /// blocks). Records that progress happened, resetting the stall clock.</summary>
        public void ReportProgress(int completed)
        {
            lock (_gate)
            {
                if (completed <= _lastObservedCompleted) return;
                _lastObservedCompleted = completed;
                _lastProgressAt = _clock.Elapsed;
            }
        }

        private void Poll()
        {
            lock (_gate)
            {
                if (_tripped) return;

                TimeSpan sinceProgress = _clock.Elapsed - _lastProgressAt;
                if (sinceProgress < _stallWindow) return;

                _tripped = true;
                TripReason = $"no game finished for {sinceProgress.TotalSeconds:F0}s " +
                    $"(stall window {_stallWindow.TotalSeconds:F0}s) — last observed completion count was " +
                    $"{_lastObservedCompleted}.";
            }

            _linkedSource.Cancel();
        }

        /// <summary>
        /// Derives the stall window from what a single game could legitimately cost: a game cannot
        /// exceed plyCap moves at the slowest side's hard time budget, so that product times a
        /// safety multiplier is the longest gap between completions a healthy run could ever
        /// produce. Doubling accounts for adjudication overhead and CPU contention when many
        /// worker threads share a machine — this is deliberately generous, since the watchdog's
        /// job is catching deadlock, not catching slowness (the quick tier's small sample size is
        /// what gives fast feedback; see TournamentScope). Floors at one minute so a fixture with a
        /// tiny/instant hard budget still gets a sane minimum window.
        /// </summary>
        public static TimeSpan DeriveStallWindow(int plyCap, int slowestHardBudgetMs, double safetyMultiplier = 2.0)
        {
            double windowMs = Math.Max(60_000, plyCap * (double)slowestHardBudgetMs * safetyMultiplier);
            return TimeSpan.FromMilliseconds(windowMs);
        }

        public void Dispose()
        {
            _pollTimer.Dispose();
            _linkedSource.Dispose();
        }
    }
}
