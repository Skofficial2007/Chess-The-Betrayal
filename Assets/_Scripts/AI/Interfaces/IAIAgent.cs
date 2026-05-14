using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;
using System;
using System.Threading;

namespace ChessTheMasterPiece.AI
{
    /// <summary>
    /// Defines what any AI player needs to be able to do. Implement this if you want to add a new AI difficulty or strategy.
    /// </summary>
    public interface IAIAgent
    {
        /// <summary>
        /// Starts an async best-move search. Pass a CancellationToken so the
        /// search can be aborted on game reset or scene change.
        /// Result fires via OnMoveDecided on the main thread.
        /// </summary>
        void RequestBestMove(BoardState board, Team team, CancellationToken cancellation = default);

        event Action<MoveCommand> OnMoveDecided;
    }
}
