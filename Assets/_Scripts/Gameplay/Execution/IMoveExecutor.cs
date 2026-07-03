using System;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Gameplay
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
        /// Player has a legal Retribution move available in RetributionPending but chooses not to
        /// use it — a voluntary Defection (rulebook 5B allows "cannot or chooses not to"). Sends
        /// intent only; the executor validates the phase before forwarding. A network executor
        /// must authorize this identically to any other move request — the server independently
        /// confirms CurrentPhase == RetributionPending before applying the resolution.
        /// </summary>
        void RequestRetributionSkip();

        /// <summary>
        /// Fired when a move has been validated and should be executed.
        /// GameManager listens to this to update the board state.
        /// </summary>
        event Action<MoveCommand> OnMoveConfirmed;

        /// <summary>
        /// Fired when a voluntary Retribution skip has been validated and applied. Distinct from
        /// OnMoveConfirmed because a Defection isn't representable as a normal MoveCommand the
        /// caller submitted — MatchDriver has already resolved it internally by the time this fires.
        /// </summary>
        event Action OnRetributionSkipConfirmed;
        
        /// <summary>
        /// Fired when a move is rejected (illegal move, wrong turn, etc).
        /// Visual layer listens to this to snap pieces back.
        /// </summary>
        event Action<Vector2Int, Vector2Int> OnMoveRejected;

        /// <summary>
        /// Fired when a pawn reaches the end and needs promotion.
        /// Passes (from, to) so the visual layer can optimistically snap the correct piece.
        /// </summary>
        event Action<Vector2Int, Vector2Int> OnPromotionRequired;
    }
}
