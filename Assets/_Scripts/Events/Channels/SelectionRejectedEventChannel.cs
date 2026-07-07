using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Core.Data;
using UnityEngine;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Events
{
    [CreateAssetMenu(menuName = "Chess/Events/Selection Rejected", fileName = "SelectionRejectedEvent")]
    public sealed class SelectionRejectedEventChannel : GameEventChannel<SelectionRejectedPayload>
    {
        [Header("Debug Test Payload")]
        [SerializeField] private Vector2Int _debugPosition = Vector2Int.Zero;

        [ContextMenu("Raise with Debug Payload")]
        private void RaiseDebug()
        {
            if (!Application.isPlaying) return;
            Raise(new SelectionRejectedPayload(_debugPosition));
        }
    }
}
