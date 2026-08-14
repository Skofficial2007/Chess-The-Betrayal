using System;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Gameplay.Interaction
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
        /// The player meant the Betrayal Act they asked for. The move is re-derived from the board
        /// as it stands now rather than replayed from the moment it was requested — see
        /// LocalMoveExecutor.ConfirmBetrayalAct for why that distinction matters.
        ///
        /// A network executor must authorize this exactly as it would any other move request: the
        /// server independently re-confirms the Act is still legal on its own board before applying
        /// it. A client saying "they pressed Continue" is not evidence the move is playable.
        /// </summary>
        void ConfirmBetrayalAct();

        /// <summary>
        /// The player backed out of the Betrayal Act. The parked move is dropped and nothing is
        /// played. Deliberately not reported as a rejection — a rejection means the move was
        /// illegal, and the visual layer answers one by snapping a piece back.
        /// </summary>
        void CancelBetrayalAct();

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
        /// Fired when a requested move turns out to be a legal Betrayal Act, carrying the squares it
        /// would run between. The move is held here, unplayed, until <see cref="ConfirmBetrayalAct"/>
        /// or <see cref="CancelBetrayalAct"/> answers for it — an Act spends a right neither side
        /// gets back, and one tap on a friendly piece is all it used to take.
        ///
        /// Whoever handles this must always answer. A subscriber that neither confirms nor cancels
        /// leaves the executor holding a move and refusing every new one, which reads to the player
        /// as a board that has stopped responding.
        /// </summary>
        event Action<Vector2Int, Vector2Int> OnBetrayalActConfirmationRequired;

        /// <summary>
        /// Fired when a pawn reaches the end and needs promotion.
        /// Passes (from, to, isCapture) so the visual layer can glide the correct piece onto an
        /// empty square — isCapture is included because the move's legality (and therefore whether
        /// it captures) is already fully resolved at this point, before the player even picks a
        /// promoted piece type, so the View shouldn't have to re-derive it.
        /// </summary>
        event Action<Vector2Int, Vector2Int, bool> OnPromotionRequired;
    }
}
