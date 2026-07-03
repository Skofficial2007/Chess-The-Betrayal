using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Events
{
    public sealed class BetrayalEventListener : EventListenerBase
    {
        [SerializeField] private BetrayalEventChannel _channel;
        [SerializeField] private UnityEvent<BetrayalPayload> _onEventRaised;

        protected override UnityEngine.Object ChannelObject => _channel;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(BetrayalPayload p) => _onEventRaised?.Invoke(p);
    }
}
