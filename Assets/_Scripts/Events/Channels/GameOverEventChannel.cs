using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Core.Data;
using UnityEngine;

namespace ChessTheBetrayal.Events
{
    [CreateAssetMenu(menuName = "Chess/Events/Game Over", fileName = "GameOverEvent")]
    public sealed class GameOverEventChannel : GameEventChannel<GameOverPayload>
    {
        [Header("Debug Test Payload")]
        [SerializeField] private GameEndReason _debugReason = GameEndReason.Checkmate;
        [SerializeField] private bool _debugIsTimeout = false;

        [ContextMenu("Raise with Debug Payload")]
        private void RaiseDebug()
        {
            if (!Application.isPlaying) return;
            Raise(new GameOverPayload(Team.White, _debugReason, _debugIsTimeout));
        }
    }
}
