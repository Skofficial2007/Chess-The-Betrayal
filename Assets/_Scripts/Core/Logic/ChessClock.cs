using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Logic
{
    /// <summary>
    /// A deterministic, framework-agnostic chess clock.
    /// Operates purely on injected time deltas to remain thread-safe and decoupled from the presentation frame rate.
    /// </summary>
    public sealed class ChessClock
    {
        private readonly GameModeConfig     _config;
        private readonly IClockEventHandler _handler;
        private          ClockState         _state;

        private bool _whiteLowTimeWarningFired;
        private bool _blackLowTimeWarningFired;

        private const long LowTimeThresholdMs = 10_000L;

        public ChessClock(GameModeConfig config, IClockEventHandler handler)
        {
            _config  = config;
            _handler = handler;

            _state = new ClockState
            {
                WhiteRemainingMs = config.BaseTimeMs,
                BlackRemainingMs = config.BaseTimeMs,
                ActiveSide       = Team.White,
                IsRunning        = false,
                IsExpired        = false
            };
        }

        /// <summary>
        /// Retrieves a value-type copy of the current clock state.
        /// </summary>
        public ClockState State => _state;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public void Start()  => _state.IsRunning = true;
        public void Pause()  => _state.IsRunning = false;
        public void Resume() => _state.IsRunning = !_config.IsUnlimited;

        // ── Per-Frame Tick ────────────────────────────────────────────────────────

        /// <summary>
        /// Advances the clock by the specified milliseconds.
        /// </summary>
        /// <param name="deltaMs">Elapsed time in milliseconds since the last update.</param>
        public void Tick(long deltaMs)
        {
            if (_config.IsUnlimited || !_state.IsRunning || _state.IsExpired)
            {
                return;
            }

            long remaining = _state.GetRemaining(_state.ActiveSide) - deltaMs;

            if (remaining <= 0L)
            {
                _state.SetRemaining(_state.ActiveSide, 0L);
                _state.IsExpired = true;
                _state.IsRunning = false;
                _handler.OnClockTimeout(_state.ActiveSide);
                return;
            }

            _state.SetRemaining(_state.ActiveSide, remaining);
            CheckLowTimeWarning(_state.ActiveSide, remaining);
        }

        // ── Move Confirmation ─────────────────────────────────────────────────────

        /// <summary>
        /// Registers a completed move. Applies the Fischer increment to the acting side,
        /// switches the active timer, and ensures the clock is running.
        /// </summary>
        public void OnMoveMade(Team teamThatMoved)
        {
            if (_config.IsUnlimited) return;

            long current = _state.GetRemaining(teamThatMoved);
            _state.SetRemaining(teamThatMoved, current + _config.IncrementMs);

            _state.ActiveSide = teamThatMoved == Team.White ? Team.Black : Team.White;
            
            // The clock does not strictly start counting down until after the first move completes.
            _state.IsRunning = true;
        }

        // ── Private Helpers ───────────────────────────────────────────────────────

        private void CheckLowTimeWarning(Team team, long remaining)
        {
            if (remaining > LowTimeThresholdMs) return;

            bool alreadyFired = team == Team.White
                ? _whiteLowTimeWarningFired
                : _blackLowTimeWarningFired;

            if (alreadyFired) return;

            if (team == Team.White) _whiteLowTimeWarningFired = true;
            else                    _blackLowTimeWarningFired = true;

            _handler.OnLowTimeWarning(team, remaining);
        }
    }
}
