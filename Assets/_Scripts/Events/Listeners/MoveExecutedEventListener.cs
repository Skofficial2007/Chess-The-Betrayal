using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Events
{
    public sealed class MoveExecutedEventListener : MonoBehaviour
    {
        [SerializeField] private MoveExecutedEventChannel _channel;
        [SerializeField] private UnityEvent<MoveExecutedPayload> _onEventRaised;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(MoveExecutedPayload p) => _onEventRaised?.Invoke(p);
    }
}
