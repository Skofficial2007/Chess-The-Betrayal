using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Events
{
    public sealed class TurnChangedEventListener : MonoBehaviour
    {
        [SerializeField] private TurnChangedEventChannel _channel;
        [SerializeField] private UnityEvent<TurnChangedPayload> _onEventRaised;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(TurnChangedPayload p) => _onEventRaised?.Invoke(p);
    }
}
