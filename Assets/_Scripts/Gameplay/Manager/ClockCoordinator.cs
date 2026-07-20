using System;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Logic;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Owns the match clock's lifecycle: constructing it for a new match (bypassed entirely for
    /// AI/Unlimited sessions), attaching it to MatchDriver, tearing it down, and answering the
    /// current snapshot. Split out of GameManager alongside AIMatchCoordinator so the
    /// clock-specific slice of match orchestration is a plain, testable C# class instead of
    /// MonoBehaviour-embedded code.
    ///
    /// Implements <see cref="IClockEventHandler"/>/<see cref="IClockSnapshotSource"/> directly
    /// (not forwarded through GameManager) — matches the MatchDriver/UndoService precedent of the
    /// interface living on the collaborator that actually owns the behavior, not the composition
    /// root. GameManager's only remaining clock-shaped responsibilities are the two constructor
    /// delegates below (routing a timeout/low-time event into match-flow/a UI channel) — narrow
    /// seams, same reasoning as AIMatchCoordinator's playMove delegate.
    /// </summary>
    public sealed class ClockCoordinator : IClockEventHandler, IClockSnapshotSource
    {
        private readonly GameSetup _setup;
        private readonly Action<Team> _onTimeout;
        private readonly Action<Team, long> _onLowTime;

        private ChessClock _clock;
        private GameClockController _clockController;

        public ClockCoordinator(GameSetup setup, Action<Team> onTimeout, Action<Team, long> onLowTime)
        {
            _setup = setup;
            _onTimeout = onTimeout;
            _onLowTime = onLowTime;
        }

        /// <summary>
        /// Returns a value-type snapshot of the clock state, or null if untimed/AI mode.
        /// </summary>
        public ClockState? Current => _clockController != null ? (ClockState?)_clockController.CurrentState : null;

        /// <summary>
        /// Builds the clock via GameSetup and hands it to MatchDriver. Bypassed entirely during
        /// AI sessions to preserve engine search performance (GameSetup.InitializeClock returns
        /// (null, null) for AI/Unlimited mode, and this method still safely attaches the nulls).
        /// </summary>
        public void Initialize(GameModeConfig selectedMode, bool isAiMode, Team initialActiveSide, GameObject host, MatchDriver matchDriver)
        {
            (_clock, _clockController) = _setup.InitializeClock(
                selectedMode, isAiMode, initialActiveSide, this, host, _clockController);

            matchDriver.AttachClock(_clock, this, selectedMode);
        }

        /// <summary>Stops the active clock controller and drops the clock reference. Safe to call with no clock active.</summary>
        public void Deactivate()
        {
            if (_clockController != null)
            {
                _clockController.Deactivate();
                _clockController = null;
            }

            _clock = null;
        }

        /// <summary>Writes the latest clock snapshot to the shared bridge. No-op (bridge left untouched) when no clock is active.</summary>
        public void PushSnapshotTo(ChessTheBetrayal.Events.SharedClockStateSO sharedClockState)
        {
            if (_clockController == null) return;
            sharedClockState?.Set(Current);
        }

        void IClockEventHandler.OnClockTimeout(Team timedOutTeam) => _onTimeout(timedOutTeam);

        void IClockEventHandler.OnLowTimeWarning(Team team, long remainingMs) => _onLowTime(team, remainingMs);
    }
}
