using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Events
{
    public sealed class LowTimeAlertEventListener : EventListenerBase
    {
        [SerializeField] private LowTimeAlertEventChannel _channel;
        [SerializeField] private UnityEvent<LowTimeAlertPayload> _onEventRaised;

        protected override UnityEngine.Object ChannelObject => _channel;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(LowTimeAlertPayload p) => _onEventRaised?.Invoke(p);
    }
}
