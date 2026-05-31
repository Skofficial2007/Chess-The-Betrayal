using UnityEngine;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events
{
    /// <summary>
    /// Holds the most recent clock snapshot.
    /// The gameplay layer writes this every frame, and clock display widgets poll it.
    /// Value is null when no clock is active (e.g., untimed games).
    /// </summary>
    [CreateAssetMenu(menuName = "Chess/Shared/Clock State Reference", fileName = "SharedClockState")]
    public sealed class SharedClockStateSO : ScriptableObject
    {
        public ClockState? Value { get; private set; }
        
        /// <summary>
        /// Sets the current clock state.
        /// </summary>
        public void Set(ClockState? state) => Value = state;
    }
}
