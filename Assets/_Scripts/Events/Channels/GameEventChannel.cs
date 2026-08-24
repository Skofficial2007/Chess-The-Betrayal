using System.Collections.Generic;
using UnityEngine;

namespace ChessTheBetrayal.Events.Channels
{
    /// <summary>
    /// A ScriptableObject acting as a typed publish/subscribe hub with no payload.
    /// Use for signals that carry no data, such as "GameReset" or "MatchStartRequested".
    /// Listeners register at runtime; this asset holds no MonoBehaviour references.
    /// </summary>
    [CreateAssetMenu(menuName = "Chess/Events/Game Event (Void)", fileName = "NewGameEvent")]
    public class GameEventChannel : EventChannelBase
    {
        private readonly List<System.Action> _listeners = new List<System.Action>(4);

        // ScriptableObjects persist across play sessions when Domain Reload is disabled.
        // We MUST wipe the listener lists on enable/disable to purge "zombie" delegates
        // from objects that failed to unregister correctly during teardown.

        protected virtual void OnEnable()
        {
            _listeners.Clear();
            ClearDebugLog();
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

        public override int ListenerCount => _listeners.Count;

        private void RecordDebugTrace()
        {
            if (!DebugTrace) return;
            string entry = $"{System.DateTime.Now:HH:mm:ss.fff} | Listeners: {_listeners.Count}";
            AppendTrace(entry);
            Debug.Log($"[EventChannel] {name} raised. {entry}", this);
        }

        [ContextMenu("Raise (Debug — Play Mode only)")]
        private void RaiseFromEditor()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[EventChannel] Cannot raise outside Play Mode.");
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
    public abstract class GameEventChannel<T> : EventChannelBase where T : struct
    {
        private readonly List<System.Action<T>> _listeners = new List<System.Action<T>>(4);

        protected virtual void OnEnable()
        {
            _listeners.Clear();
            ClearDebugLog();
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

        public override int ListenerCount => _listeners.Count;

        private void RecordDebugTrace(T payload)
        {
            if (!DebugTrace) return;
            string entry = $"{System.DateTime.Now:HH:mm:ss.fff} | {payload} | Listeners: {_listeners.Count}";
            AppendTrace(entry);
            Debug.Log($"[EventChannel<{typeof(T).Name}>] {name} raised. Payload: {payload}", this);
        }
    }
}
