using ChessTheBetrayal.Core.Data;
using UnityEngine;

namespace ChessTheBetrayal.Events
{
    [CreateAssetMenu(menuName = "Chess/Events/Game Mode Configured", fileName = "GameModeConfiguredEvent")]
    public sealed class GameModeConfiguredEventChannel : GameEventChannel<GameModeConfig>
    {
        [ContextMenu("Raise with Debug Payload")]
        private void RaiseDebug()
        {
            if (!Application.isPlaying) return;
            Raise(GameModeConfig.Unlimited);
        }
    }
}
