using ChessTheBetrayal.Events.Payloads;
using UnityEngine;

namespace ChessTheBetrayal.Events.Channels
{
    /// <summary>
    /// Announces a single ply coming back off the board, so the view can play that move in reverse.
    ///
    /// Deliberately separate from the board-resync signal: a resync says "the position changed,
    /// catch up", which a reconnecting client wants and a takeback does not. A takeback is
    /// something the player asked for and should be able to watch happen.
    /// </summary>
    [CreateAssetMenu(menuName = "Chess/Events/Move Undone", fileName = "MoveUndoneEvent")]
    public sealed class MoveUndoneEventChannel : GameEventChannel<MoveUndonePayload>
    {
    }
}
