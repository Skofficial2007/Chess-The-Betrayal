using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Core.Data;
using UnityEngine;

namespace ChessTheBetrayal.Events
{
    [CreateAssetMenu(menuName = "Chess/Events/Turn Changed", fileName = "TurnChangedEvent")]
    public sealed class TurnChangedEventChannel : GameEventChannel<TurnChangedPayload>
    {
        [Header("Debug Test Payload")]
        [SerializeField] private Team _debugTeam = Team.White;
        [SerializeField] private int _debugTurnNumber = 1;

        [ContextMenu("Raise with Debug Payload")]
        private void RaiseDebug()
        {
            if (!Application.isPlaying) return;
            Raise(new TurnChangedPayload(_debugTeam, _debugTurnNumber, TurnSource.HumanLocal));
        }
    }
}
