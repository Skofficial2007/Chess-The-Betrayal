using UnityEngine;
using UnityEngine.Events;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events
{
    public sealed class TeamSelectedEventListener : EventListenerBase
    {
        [SerializeField] private TeamSelectedEventChannel _channel;
        [SerializeField] private UnityEvent<Team> _onEventRaised;

        protected override UnityEngine.Object ChannelObject => _channel;

        private void OnEnable()  => _channel?.Register(HandleEvent);
        private void OnDisable() => _channel?.Unregister(HandleEvent);
        private void HandleEvent(Team p) => _onEventRaised?.Invoke(p);
    }
}
