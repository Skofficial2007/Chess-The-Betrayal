using System.Collections.Generic;
using UnityEngine;

namespace ChessTheBetrayal.Events
{
    /// <summary>
    /// A ScriptableObject acting as a typed publish/subscribe hub with no payload.
    /// Use for signals that carry no data, such as "GameReset" or "MatchStartRequested".
    /// Listeners register at runtime; this asset holds no MonoBehaviour references.
    /// </summary>
    [CreateAssetMenu(menuName = "Chess/Events/Game Event (Void)", fileName = "NewGameEvent")]
    public class GameEventChannel : ScriptableObject
    {
        [Header("Debug")]
        [Tooltip("Log every Raise() call to the Console while in Play Mode.")]
        public bool DebugTrace = false;

        [SerializeField] private List<string> _debugLog = new List<string>(8);

        private readonly List<System.Action> _listeners = new List<System.Action>(4);

        // ScriptableObjects persist across play sessions when Domain Reload is disabled.
        // We MUST wipe the listener lists on enable/disable to purge "zombie" delegates 
        // from objects that failed to unregister correctly during teardown.

        protected virtual void OnEnable()
        {
            _listeners.Clear();
            _debugLog.Clear();
        }

        protected virtual void OnDisable()
        {
            _listeners.Clear();
        }

        /// <summary>
        /// Fires the event. Iterates backwards so listeners can safely
        /// unregister themselves during the callback without invalidating the loop.
        /// Must be called from the Unity main thread.
        /// </summary>
        public void Raise()
        {
            RecordDebugTrace();
            for (int i = _listeners.Count - 1; i >= 0; i--)
                _listeners[i]?.Invoke();
        }

        public void Register(System.Action listener)
        {
            if (!_listeners.Contains(listener))
                _listeners.Add(listener);
        }

        public void Unregister(System.Action listener) =>
            _listeners.Remove(listener);

        /// <summary>Listener count. Available to editor tooling during Play Mode.</summary>
        public int ListenerCount => _listeners.Count;

        private void RecordDebugTrace()
        {
            if (!DebugTrace) return;
            string entry = $"{System.DateTime.Now:HH:mm:ss.fff} | Listeners: {_listeners.Count}";
            if (_debugLog.Count >= 8) _debugLog.RemoveAt(0);
            _debugLog.Add(entry);
            UnityEngine.Debug.Log($"[EventChannel] {name} raised. {entry}", this);
        }

        public void ClearDebugLog() => _debugLog.Clear();

        [ContextMenu("Raise (Debug — Play Mode only)")]
        private void RaiseFromEditor()
        {
            if (!Application.isPlaying)
            {
                UnityEngine.Debug.LogWarning("[EventChannel] Cannot raise outside Play Mode.");
                return;
            }
            Raise();
        }
    }

    /// <summary>
    /// A typed publish/subscribe channel carrying a value payload of type T.
    /// T must be a struct: this enforces value-type copy semantics per listener
    /// and ensures that one listener cannot accidentally mutate data read by another.
    /// Subclass this with [CreateAssetMenu] for each distinct event type.
    /// </summary>
    public abstract class GameEventChannel<T> : ScriptableObject where T : struct
    {
        [Header("Debug")]
        public bool DebugTrace = false;

        [SerializeField]
        private List<string> _debugLog = new List<string>(8);

        private readonly List<System.Action<T>> _listeners = new List<System.Action<T>>(4);

        protected virtual void OnEnable()
        {
            _listeners.Clear();
            _debugLog.Clear();
        }

        protected virtual void OnDisable()
        {
            _listeners.Clear();
        }

        /// <summary>
        /// Raises the event, distributing a copy of the payload to each listener.
        /// Iterates backwards to allow safe unregistration during the callback.
        /// </summary>
        public void Raise(T payload)
        {
            RecordDebugTrace(payload);
            for (int i = _listeners.Count - 1; i >= 0; i--)
                _listeners[i]?.Invoke(payload);
        }

        public void Register(System.Action<T> listener)
        {
            if (!_listeners.Contains(listener))
                _listeners.Add(listener);
        }

        public void Unregister(System.Action<T> listener) =>
            _listeners.Remove(listener);

        public int ListenerCount => _listeners.Count;

        private void RecordDebugTrace(T payload)
        {
            if (!DebugTrace) return;
            string entry = $"{System.DateTime.Now:HH:mm:ss.fff} | {payload} | Listeners: {_listeners.Count}";
            if (_debugLog.Count >= 8) _debugLog.RemoveAt(0);
            _debugLog.Add(entry);
            UnityEngine.Debug.Log($"[EventChannel<{typeof(T).Name}>] {name} raised. Payload: {payload}", this);
        }

        public void ClearDebugLog() => _debugLog.Clear();
    }
}