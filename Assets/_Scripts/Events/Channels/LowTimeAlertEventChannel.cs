using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Core.Data;
using UnityEngine;

namespace ChessTheBetrayal.Events
{
    [CreateAssetMenu(menuName = "Chess/Events/Low Time Alert", fileName = "LowTimeAlertEvent")]
    public sealed class LowTimeAlertEventChannel : GameEventChannel<LowTimeAlertPayload>
    {
        [Header("Debug Test Payload")]
        [SerializeField] private Team _debugTeam = Team.White;
        [SerializeField] private long _debugRemainingMs = 10000;

        [ContextMenu("Raise with Debug Payload")]
        private void RaiseDebug()
        {
            if (!Application.isPlaying) return;
            Raise(new LowTimeAlertPayload(_debugTeam, _debugRemainingMs));
        }
    }
}
