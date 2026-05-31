using UnityEngine;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events
{
    /// <summary>
    /// Holds a reference to the current live BoardState.
    /// This acts as a data bridge: the gameplay orchestration layer writes this upon game start,
    /// and the visual layer reads it to instantiate pieces.
    /// </summary>
    [CreateAssetMenu(menuName = "Chess/Shared/Board State Reference", fileName = "SharedBoardState")]
    public sealed class SharedBoardStateSO : ScriptableObject
    {
        public BoardState Value { get; private set; }

        /// <summary>
        /// Sets the board reference. Call before raising the GameStarted event channel.
        /// </summary>
        public void Set(BoardState board) => Value = board;

        /// <summary>
        /// Clears the reference on game reset to prevent stale reads by the UI.
        /// </summary>
        public void Clear() => Value = null;
    }
}
