using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Events
{
    public sealed class PieceSelectedEventListener : EventListenerBase
    {
        [SerializeField] private PieceSelectedEventChannel _channel;
        [SerializeField] private UnityEvent<PieceSelectedPayload> _onEventRaised;

        protected override UnityEngine.Object ChannelObject => _channel;

        private void OnEnable() => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(PieceSelectedPayload p) => _onEventRaised?.Invoke(p);
    }
}
