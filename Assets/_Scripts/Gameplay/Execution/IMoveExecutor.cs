using System;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;

namespace ChessTheMasterPiece.Controllers
{
    /// <summary>
    /// Defines how a move request travels from the player's input to the game board.
    /// The local offline version validates immediately; a future network version will ask the server first.
    /// </summary>
    public interface IMoveExecutor
    {
        /// <summary>
        /// Request a move from one position to another.
        /// This is async - the result is returned via events.
        /// </summary>
        void RequestMove(Vector2Int from, Vector2Int to);
        
        /// <summary>
        /// Request a promotion to a specific piece type.
        /// Called when player selects a promotion choice from UI.
        /// </summary>
        void RequestPromotion(ChessPieceType type);
        
        /// <summary>
        /// Fired when a move has been validated and should be executed.
        /// GameManager listens to this to update the board state.
        /// </summary>
        event Action<MoveCommand> OnMoveConfirmed;
        
        /// <summary>
        /// Fired when a move is rejected (illegal move, wrong turn, etc).
        /// Visual layer listens to this to snap pieces back.
        /// </summary>
        event Action<Vector2Int, Vector2Int> OnMoveRejected;
        
        /// <summary>
        /// Fired when a pawn reaches the end and needs promotion.
        /// UI layer listens to this to show the promotion dialog.
        /// </summary>
        event Action<Vector2Int> OnPromotionRequired;
    }
}
