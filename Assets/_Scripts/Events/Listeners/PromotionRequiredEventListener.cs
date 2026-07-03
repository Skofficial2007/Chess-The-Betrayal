using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Events
{
    public sealed class PromotionRequiredEventListener : EventListenerBase
    {
        [SerializeField] private PromotionRequiredEventChannel _channel;
        [SerializeField] private UnityEvent<PromotionRequiredPayload> _onEventRaised;

        protected override UnityEngine.Object ChannelObject => _channel;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(PromotionRequiredPayload p) => _onEventRaised?.Invoke(p);
    }
}
