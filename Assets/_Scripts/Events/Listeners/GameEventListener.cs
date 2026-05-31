using UnityEngine;
using UnityEngine.Events;

namespace ChessTheBetrayal.Events
{
    /// <summary>
    /// Attach to any GameObject to respond to a zero-payload event channel.
    /// </summary>
    public sealed class GameEventListener : MonoBehaviour
    {
        [Tooltip("The channel this listener subscribes to.")]
        [SerializeField] private GameEventChannel _channel;

        [Tooltip("Invoked each time the channel fires.")]
        [SerializeField] private UnityEvent _onEventRaised;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent() => _onEventRaised?.Invoke();
    }
}
