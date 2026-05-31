using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Events
{
    public sealed class PromotionRequiredEventListener : MonoBehaviour
    {
        [SerializeField] private PromotionRequiredEventChannel _channel;
        [SerializeField] private UnityEvent<PromotionRequiredPayload> _onEventRaised;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(PromotionRequiredPayload p) => _onEventRaised?.Invoke(p);
    }
}
