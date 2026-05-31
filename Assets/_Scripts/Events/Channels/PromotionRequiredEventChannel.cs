using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Core.Data;
using UnityEngine;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Events
{
    [CreateAssetMenu(menuName = "Chess/Events/Promotion Required", fileName = "PromotionRequiredEvent")]
    public sealed class PromotionRequiredEventChannel : GameEventChannel<PromotionRequiredPayload>
    {
        [Header("Debug Test Payload")]
        [SerializeField] private Vector2Int _debugFrom = new Vector2Int(0, 6);
        [SerializeField] private Vector2Int _debugTo = new Vector2Int(0, 7);

        [ContextMenu("Raise with Debug Payload")]
        private void RaiseDebug()
        {
            if (!Application.isPlaying) return;
            Raise(new PromotionRequiredPayload(_debugFrom, _debugTo));
        }
    }
}
