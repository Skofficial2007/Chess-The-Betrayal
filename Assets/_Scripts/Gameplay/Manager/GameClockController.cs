using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Logic;

namespace ChessTheBetrayal.Gameplay
{
    /// <summary>
    /// Acts as the Unity lifecycle bridge for the pure-C# ChessClock.
    /// Responsible for converting Unity's floating-point delta time into discrete milliseconds
    /// to drive the deterministic domain clock without coupling the domain to the Unity Engine.
    /// </summary>
    public sealed class GameClockController : MonoBehaviour
    {
        private ChessClock _clock;
        private bool _active;

        /// <summary>
        /// Returns a value-type snapshot of the current clock state.
        /// Returns a default state (all fields zero/false) when no clock is active.
        /// </summary>
        public ClockState CurrentState => _clock?.State ?? default;

        /// <summary>
        /// Assigns the domain clock instance and enables per-frame ticking.
        /// </summary>
        public void Initialize(ChessClock clock)
        {
            _clock = clock;
            _active = true;
        }

        /// <summary>
        /// Halts ticking operations. The component remains attached but dormant.
        /// </summary>
        public void Deactivate()
        {
            _active = false;
        }

        private void Update()
        {
            if (!_active || _clock == null)
            {
                return;
            }

            // Convert Unity's frame time to milliseconds for the domain layer.
            long deltaMs = (long)(Time.deltaTime * 1000f);
            _clock.Tick(deltaMs);
        }
    }
}