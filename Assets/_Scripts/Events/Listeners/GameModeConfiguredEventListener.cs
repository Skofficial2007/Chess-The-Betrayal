using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events
{
    public sealed class GameModeConfiguredEventListener : EventListenerBase
    {
        [SerializeField] private GameModeConfiguredEventChannel _channel;
        [SerializeField] private UnityEvent<GameModeConfig> _onEventRaised;

        protected override UnityEngine.Object ChannelObject => _channel;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(GameModeConfig p) => _onEventRaised?.Invoke(p);
    }
}
