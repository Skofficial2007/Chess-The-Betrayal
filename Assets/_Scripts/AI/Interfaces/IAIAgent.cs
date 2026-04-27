using System;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;

namespace ChessTheMasterPiece.AI
{
    /// <summary>
    /// Contract for any AI opponent (Minimax, Random, Neural Net).
    /// </summary>
    public interface IAIAgent
    {
        /// <summary>
        /// Requests the best move for the current position.
        /// Async-ready: result must be fired via OnMoveDecided event.
        /// </summary>
        void RequestBestMove(BoardState board, Team team);
        
        event Action<MoveCommand> OnMoveDecided;
    }
}
