using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Events
{
    public sealed class LowTimeAlertEventListener : MonoBehaviour
    {
        [SerializeField] private LowTimeAlertEventChannel _channel;
        [SerializeField] private UnityEvent<LowTimeAlertPayload> _onEventRaised;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(LowTimeAlertPayload p) => _onEventRaised?.Invoke(p);
    }
}
