using ChessTheBetrayal.Core.Data;
using UnityEngine;

namespace ChessTheBetrayal.Events
{
    [CreateAssetMenu(menuName = "Chess/Events/Team Selected", fileName = "TeamSelectedEvent")]
    public sealed class TeamSelectedEventChannel : GameEventChannel<Team>
    {
        [Header("Debug Test Payload")]
        [SerializeField] private Team _debugTeam = Team.White;

        [ContextMenu("Raise with Debug Payload")]
        private void RaiseDebug()
        {
            if (!Application.isPlaying) return;
            Raise(_debugTeam);
        }
    }
}
