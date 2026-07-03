using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Events
{
    public sealed class PieceSelectedEventListener : MonoBehaviour
    {
        [SerializeField] private PieceSelectedEventChannel _channel;
        [SerializeField] private UnityEvent<PieceSelectedPayload> _onEventRaised;

        private void OnEnable() => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(PieceSelectedPayload p) => _onEventRaised?.Invoke(p);
    }
}
