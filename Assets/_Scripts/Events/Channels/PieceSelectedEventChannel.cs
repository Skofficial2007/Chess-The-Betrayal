using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Core.Data;
using UnityEngine;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Events
{
    [CreateAssetMenu(menuName = "Chess/Events/Piece Selected", fileName = "PieceSelectedEvent")]
    public sealed class PieceSelectedEventChannel : GameEventChannel<PieceSelectedPayload>
    {
        [Header("Debug Test Payload")]
        [SerializeField] private Vector2Int _debugPosition = Vector2Int.Zero;

        [ContextMenu("Raise with Debug Payload")]
        private void RaiseDebug()
        {
            if (!Application.isPlaying) return;
            Raise(new PieceSelectedPayload(_debugPosition));
        }
    }
}
