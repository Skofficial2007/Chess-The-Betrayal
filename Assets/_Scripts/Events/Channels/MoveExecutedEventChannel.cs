using ChessTheBetrayal.Events.Payloads;
using UnityEngine;

namespace ChessTheBetrayal.Events
{
    [CreateAssetMenu(menuName = "Chess/Events/Move Executed", fileName = "MoveExecutedEvent")]
    public sealed class MoveExecutedEventChannel : GameEventChannel<MoveExecutedPayload>
    {
        [Header("Debug Test Payload")]
        [SerializeField] private bool _debugIsCheck = false;

        [ContextMenu("Raise with Debug Payload")]
        private void RaiseDebug()
        {
            if (!Application.isPlaying) return;
            Raise(new MoveExecutedPayload(default, 0, _debugIsCheck, 0));
        }
    }
}
