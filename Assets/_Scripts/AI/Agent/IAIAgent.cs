using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using System;
using System.Threading;

namespace ChessTheBetrayal.AI.Agent
{
    /// <summary>
    /// What anything that answers for the AI side has to be able to do.
    ///
    /// A new difficulty tier does not go here — that is a row in AIProfileTable, and the one
    /// implementation reads it. Implement this only for a genuinely different kind of opponent,
    /// such as one whose move arrives over a network rather than out of a search.
    /// </summary>
    public interface IAIAgent
    {
        /// <summary>
        /// Starts a best-move search and returns immediately. Pass a CancellationToken so the search
        /// can be abandoned when a game resets or a scene unloads.
        ///
        /// OnMoveDecided must be raised on the main thread. An implementation that searches on a
        /// background thread has to marshal it back itself: what listens to it goes on to touch
        /// Unity objects, which is only legal on the main thread and fails in ways that do not look
        /// like a threading bug.
        /// </summary>
        void RequestBestMove(BoardState board, Team team, CancellationToken cancellation = default);

        event Action<MoveCommand> OnMoveDecided;
    }
}
