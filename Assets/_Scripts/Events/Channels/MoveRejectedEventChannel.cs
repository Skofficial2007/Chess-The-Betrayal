using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Core.Data;
using UnityEngine;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Events
{
    [CreateAssetMenu(menuName = "Chess/Events/Move Rejected", fileName = "MoveRejectedEvent")]
    public sealed class MoveRejectedEventChannel : GameEventChannel<MoveRejectedPayload>
    {
        [Header("Debug Test Payload")]
        [SerializeField] private Vector2Int _debugFrom = new Vector2Int(0, 1);
        [SerializeField] private Vector2Int _debugTo = new Vector2Int(0, 2);

        [ContextMenu("Raise with Debug Payload")]
        private void RaiseDebug()
        {
            if (!Application.isPlaying) return;
            Raise(new MoveRejectedPayload(_debugFrom, _debugTo));
        }
    }
}
