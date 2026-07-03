using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Events
{
    public sealed class MoveRejectedEventListener : EventListenerBase
    {
        [SerializeField] private MoveRejectedEventChannel _channel;
        [SerializeField] private UnityEvent<MoveRejectedPayload> _onEventRaised;

        protected override UnityEngine.Object ChannelObject => _channel;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(MoveRejectedPayload p) => _onEventRaised?.Invoke(p);
    }
}
