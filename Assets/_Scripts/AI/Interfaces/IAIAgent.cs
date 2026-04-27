using System;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;

namespace ChessTheMasterPiece.AI
{
    /// <summary>
    /// Defines what any AI player needs to be able to do. Implement this if you want to add a new AI difficulty or strategy.
    /// </summary>
    public interface IAIAgent
    {
        /// <summary>
        /// Ask the AI to pick a move. When it has an answer, it fires the <c>OnMoveDecided</c> event — it won't return the move directly.
        /// </summary>
        void RequestBestMove(BoardState board, Team team);
        
        event Action<MoveCommand> OnMoveDecided;
    }
}
