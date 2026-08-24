using System.Collections.Generic;
using UnityEngine;

namespace ChessTheBetrayal.Events.Channels
{
    /// <summary>
    /// What every event channel has in common regardless of whether it carries a payload: the
    /// trace switch, the short log behind it, and a listener count for whoever is watching.
    ///
    /// It exists mostly so that editor tooling has one type to ask for. The two channel shapes
    /// cannot share a listener list — one hands out a bare callback and the other hands out a
    /// payload — so anything wanting to list every channel in the project previously had to know
    /// about both, and the event monitor only knew about one.
    /// </summary>
    public abstract class EventChannelBase : ScriptableObject
    {
        /// <summary>Enough recent raises to see a pattern in the Inspector, few enough that the
        /// asset does not grow while a scene sits in Play Mode.</summary>
        private const int TraceLogLimit = 8;

        [Header("Debug")]
        [Tooltip("Log every Raise() call to the Console while in Play Mode.")]
        public bool DebugTrace = false;

        [SerializeField] private List<string> _debugLog = new List<string>(TraceLogLimit);

        /// <summary>How many listeners are attached. Available to editor tooling during Play Mode.</summary>
        public abstract int ListenerCount { get; }

        public void ClearDebugLog() => _debugLog.Clear();

        /// <summary>Records one line for the Inspector, dropping the oldest once the log is full.
        /// What the line says is left to the channel, because only it knows what it just carried.</summary>
        protected void AppendTrace(string entry)
        {
            if (_debugLog.Count >= TraceLogLimit) _debugLog.RemoveAt(0);
            _debugLog.Add(entry);
        }
    }
}
