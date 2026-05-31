using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Core.Data;
using UnityEngine;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Events
{
    [CreateAssetMenu(menuName = "Chess/Events/Betrayal Phase Changed", fileName = "BetrayalEvent")]
    public sealed class BetrayalEventChannel : GameEventChannel<BetrayalPayload>
    {
        [Header("Debug Test Payload")]
        [SerializeField] private BetrayalPhase _debugPhase = BetrayalPhase.Initiated;

        [ContextMenu("Raise with Debug Payload")]
        private void RaiseDebug()
        {
            if (!Application.isPlaying) return;
            Raise(new BetrayalPayload(Team.White, Vector2Int.Zero, _debugPhase));
        }
    }
}
