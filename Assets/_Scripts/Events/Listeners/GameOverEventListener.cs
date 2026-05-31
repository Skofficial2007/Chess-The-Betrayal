using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Events
{
    public sealed class GameOverEventListener : MonoBehaviour
    {
        [SerializeField] private GameOverEventChannel _channel;
        [SerializeField] private UnityEvent<GameOverPayload> _onEventRaised;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(GameOverPayload p) => _onEventRaised?.Invoke(p);
    }
}
