using System;
using System.Collections.Generic;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Logic;
using ChessTheBetrayal.Core.Diagnostics;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Gameplay.Interaction
{
    /// <summary>
    /// Handles move validation for local, offline play.
    /// All the pure chess logic that was previously in GameManager now lives here.
    /// In the future, NetworkMoveExecutor will implement the same interface for multiplayer.
    /// </summary>
    public class LocalMoveExecutor : IMoveExecutor
    {
        public event Action<MoveCommand> OnMoveConfirmed;
        public event Action<Vector2Int, Vector2Int> OnMoveRejected;
        public event Action<Vector2Int, Vector2Int, bool> OnPromotionRequired;
        public event Action OnRetributionSkipConfirmed;
        public event Action<Vector2Int, Vector2Int> OnBetrayalActConfirmationRequired;

        private readonly BoardState _board;
        private readonly IChessEngine _engine;
        private readonly IClockSnapshotSource _clockSource;
        private readonly List<MoveCommand> _legalMoves = new List<MoveCommand>(32);
        private readonly List<MoveCommand> _movesToTarget = new List<MoveCommand>(4);

        private readonly Func<TurnPhase> _phaseProvider;

        private MoveCommand _pendingPromotionMove;
        private bool _isAwaitingPromotion;
        private bool _logMoves;

        // The squares a Betrayal Act would run between, held while the player decides. Only the
        // squares, never the resolved MoveCommand: that command carries a snapshot of the board's
        // castling rights, en-passant file and Betrayal sub-state taken at the moment it was
        // requested, which is what an undo would put back. Playing it after the question has been
        // on screen for a while would restore a board that has moved on.
        private Vector2Int _pendingBetrayalFrom = Vector2Int.Invalid;
        private Vector2Int _pendingBetrayalTo = Vector2Int.Invalid;
        private bool _isAwaitingBetrayalConfirmation;

        /// <summary>
        /// Note: We take a direct reference to the live board here. A network executor must NOT do this — it should validate against a server snapshot, not the client's version.
        /// clockSource may be null, in which case moves are never stamped with a clock snapshot (untimed/AI mode).
        /// </summary>
        public LocalMoveExecutor(BoardState board, IChessEngine engine, Func<TurnPhase> phaseProvider, IClockSnapshotSource clockSource = null, bool logMoves = true)
        {
            _board = board;
            _engine = engine;
            _phaseProvider = phaseProvider;
            _clockSource = clockSource;
            _logMoves = logMoves;
        }

        /// <summary>
        /// Validates a move request and either fires OnMoveConfirmed (legal) or OnMoveRejected (illegal). If it's a promotion, we pause and fire OnPromotionRequired instead.
        /// </summary>
        public void RequestMove(Vector2Int from, Vector2Int to)
        {
            // Block new moves while a decision on the last one is still outstanding — a promotion
            // choice, or a Betrayal Act nobody has answered for yet. Each of those has a move parked
            // against it, so a second request would either replace it without a word or end up
            // playing both.
            if (_isAwaitingPromotion || _isAwaitingBetrayalConfirmation)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Move rejected: awaiting {(_isAwaitingPromotion ? "promotion choice" : "Betrayal confirmation")}");
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            if (_phaseProvider != null && _phaseProvider() == TurnPhase.RetributionPending)
            {
                if (!_board.PendingBetrayerSquare.HasValue || to != _board.PendingBetrayerSquare.Value)
                {
                    if (_logMoves) Debug.Log($"[LocalMoveExecutor] Move rejected: Phase 2 requires targeting the Betrayer at {_board.PendingBetrayerSquare}");
                    OnMoveRejected?.Invoke(from, to);
                    return;
                }

                _legalMoves.Clear();
                _engine.GetRetributionMoves(_board, _board.CurrentTurn, _board.PendingBetrayerSquare.Value, _legalMoves);

                _movesToTarget.Clear();
                for (int i = 0; i < _legalMoves.Count; i++)
                {
                    if (_legalMoves[i].StartPosition == from && _legalMoves[i].EndPosition == to)
                    {
                        _movesToTarget.Add(_legalMoves[i]);
                    }
                }

                if (_movesToTarget.Count == 0)
                {
                    if (_logMoves) Debug.Log($"[LocalMoveExecutor] Move rejected: piece at {from} cannot legally execute the Betrayer");
                    OnMoveRejected?.Invoke(from, to);
                    return;
                }

                // Handle standard promotion flow for Retribution (e.g., Pawn captures Betrayer on the 8th rank)
                bool isRetributionPromotion = false;
                for (int i = 0; i < _movesToTarget.Count; i++)
                {
                    if (_movesToTarget[i].IsPromotion) { isRetributionPromotion = true; break; }
                }

                if (isRetributionPromotion)
                {
                    _pendingPromotionMove = _movesToTarget[0];
                    _isAwaitingPromotion = true;
                    OnPromotionRequired?.Invoke(_pendingPromotionMove.StartPosition, to, _pendingPromotionMove.IsCapture);
                    return;
                }

                MoveCommand validRetribution = _movesToTarget[0];
                ClockState? clockSnap = _clockSource?.Current;
                if (clockSnap.HasValue) validRetribution = validRetribution.WithClockSnapshot(clockSnap.Value);

                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Retribution confirmed: {validRetribution}");
                OnMoveConfirmed?.Invoke(validRetribution);
                return;
            }

            // Forced Save (Defensive Override): the side under betrayal must resolve the check.
            if (_phaseProvider != null && _phaseProvider() == TurnPhase.ForcedSave)
            {
                _legalMoves.Clear();
                _engine.GetForcedSaveMoves(_board, _board.CurrentTurn, _legalMoves);

                _movesToTarget.Clear();
                for (int i = 0; i < _legalMoves.Count; i++)
                {
                    if (_legalMoves[i].StartPosition == from && _legalMoves[i].EndPosition == to)
                    {
                        _movesToTarget.Add(_legalMoves[i]);
                    }
                }

                if (_movesToTarget.Count == 0)
                {
                    if (_logMoves) Debug.Log($"[LocalMoveExecutor] Move rejected: piece at {from} cannot legally resolve the forced save check");
                    OnMoveRejected?.Invoke(from, to);
                    return;
                }

                // Handle standard promotion flow for Forced Save
                bool isSavePromotion = false;
                for (int i = 0; i < _movesToTarget.Count; i++)
                {
                    if (_movesToTarget[i].IsPromotion) { isSavePromotion = true; break; }
                }

                if (isSavePromotion)
                {
                    _pendingPromotionMove = _movesToTarget[0];
                    _isAwaitingPromotion = true;
                    OnPromotionRequired?.Invoke(_pendingPromotionMove.StartPosition, to, _pendingPromotionMove.IsCapture);
                    return;
                }

                MoveCommand validSave = _movesToTarget[0];
                ClockState? clockSnap = _clockSource?.Current;
                if (clockSnap.HasValue) validSave = validSave.WithClockSnapshot(clockSnap.Value);

                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Forced Save confirmed: {validSave}");
                OnMoveConfirmed?.Invoke(validSave);
                return;
            }

            // Validate piece ownership
            PieceData piece = _board.GetPiece(from);
            if (piece.IsEmpty || piece.Team != _board.CurrentTurn)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Move rejected: wrong piece or turn");
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            PieceData targetPiece = _board.GetPiece(to);

            MoveIntent intent = MoveClassifier.ClassifyMove(piece, targetPiece, _board.BetrayalRightAvailable);

            if (intent == MoveIntent.Castling)
            {
                // If Rook is at X=7 (Kingside), Target is X=6. If Rook is at X=0 (Queenside), Target is X=2.
                int castlingX = to.x > from.x ? 6 : 2;
                to = new Vector2Int(castlingX, from.y);
            }
            else if (intent == MoveIntent.Illegal)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Move rejected: {from} -> {to} is not a legal move or Betrayal");
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            _legalMoves.Clear();
            try
            {
                _engine.GetLegalMoves(_board, from, _legalMoves);
            }
            catch (DomainException ex)
            {
                if (_logMoves) Debug.LogException(ex);
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            _movesToTarget.Clear();
            for (int i = 0; i < _legalMoves.Count; i++)
            {
                if (_legalMoves[i].EndPosition == to)
                {
                    _movesToTarget.Add(_legalMoves[i]);
                }
            }

            // No legal moves to this square
            if (_movesToTarget.Count == 0)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Illegal move: {from} -> {to}");
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            // A Betrayal Act is one tap on a piece of your own, on a square that looks no different
            // from an enemy's, and it spends a right neither side gets back. Park it and ask.
            //
            // Checked after legality rather than off the classifier above, so an Act that would
            // leave the king in check is turned down as illegal instead of being offered. There is
            // never more than one candidate here either: a square holding a friendly piece is not
            // reachable by any ordinary move, and an Act never promotes.
            if (_movesToTarget[0].Stage == BetrayalStage.Act)
            {
                _pendingBetrayalFrom = from;
                _pendingBetrayalTo = to;
                _isAwaitingBetrayalConfirmation = true;

                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Betrayal Act {from} -> {to} awaiting confirmation.");

                // Nothing may follow this raise. A handler with confirmation switched off answers
                // straight back into ConfirmBetrayalAct, so by the time it returns the move has
                // already been played and anything written here would land on a finished sequence.
                OnBetrayalActConfirmationRequired?.Invoke(from, to);
                return;
            }

            // If the engine generated promotion variants for this square, we need the player to pick one.
            bool isPromotionChoice = false;
            for (int i = 0; i < _movesToTarget.Count; i++)
            {
                if (_movesToTarget[i].IsPromotion)
                {
                    isPromotionChoice = true;
                    break;
                }
            }

            if (isPromotionChoice)
            {
                // Store the first match as a template for position/capture data
                _pendingPromotionMove = _movesToTarget[0];
                _isAwaitingPromotion = true;

                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Promotion detected at {to}. Awaiting UI choice.");

                // Fire the promotion event - UI will show the dialog
                OnPromotionRequired?.Invoke(_pendingPromotionMove.StartPosition, to, _pendingPromotionMove.IsCapture);
                return;
            }

            MoveCommand validMove = _movesToTarget[0];
            
            // Capture the current clock state to stamp the move for network and replay validation.
            // GetCurrentClockSnapshot returns null when no clock is active (e.g., untimed or AI mode).
            ClockState? clockSnapshot = _clockSource?.Current;
            if (clockSnapshot.HasValue)
            {
                validMove = validMove.WithClockSnapshot(clockSnapshot.Value);
            }
            
            if (_logMoves) Debug.Log($"[LocalMoveExecutor] Move confirmed: {validMove.StartPosition} -> {validMove.EndPosition}");
            
            OnMoveConfirmed?.Invoke(validMove);
        }

        /// <summary>
        /// The player meant it. The Act is worked out again from the board as it stands now rather
        /// than replayed from the one that was parked, because the question was on screen for as
        /// long as the player wanted it there: the clock kept running, and in a timed game it can
        /// have run out. Anything that leaves the position where it was — the phase moving on, the
        /// victim no longer there — turns this into a rejection rather than a move played against a
        /// board that no longer matches it.
        /// </summary>
        public void ConfirmBetrayalAct()
        {
            if (!_isAwaitingBetrayalConfirmation)
            {
                if (_logMoves) Debug.Log("[LocalMoveExecutor] Betrayal confirmation ignored: no Act was waiting on one");
                return;
            }

            Vector2Int from = _pendingBetrayalFrom;
            Vector2Int to = _pendingBetrayalTo;
            ClearPendingBetrayal();

            if (_phaseProvider != null && _phaseProvider() != TurnPhase.Normal)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Betrayal Act rejected: the turn moved on while the question was up");
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            DomainResult<MoveCommand> resolved;
            try
            {
                resolved = MoveRequest.Resolve(
                    _engine, _board, from, to, BetrayalStage.Act, scratch: _legalMoves);
            }
            catch (DomainException ex)
            {
                if (_logMoves) Debug.LogException(ex);
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            if (!resolved.IsSuccess)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Betrayal Act rejected: {resolved.ErrorDetail}");
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            MoveCommand act = resolved.Value;

            // Stamped now, not when the question went up — the time spent deciding is the
            // player's own, and the move has to say so.
            ClockState? clockSnapshot = _clockSource?.Current;
            if (clockSnapshot.HasValue) act = act.WithClockSnapshot(clockSnapshot.Value);

            if (_logMoves) Debug.Log($"[LocalMoveExecutor] Betrayal Act confirmed: {act}");
            OnMoveConfirmed?.Invoke(act);
        }

        /// <summary>
        /// Player backed out. The parked Act is dropped and nothing reaches the board — and
        /// deliberately nothing is reported as rejected either, since a rejection means "that was
        /// illegal" and the visual layer answers one by snapping a piece back into place.
        /// </summary>
        public void CancelBetrayalAct()
        {
            if (!_isAwaitingBetrayalConfirmation) return;

            if (_logMoves) Debug.Log($"[LocalMoveExecutor] Betrayal Act {_pendingBetrayalFrom} -> {_pendingBetrayalTo} cancelled");
            ClearPendingBetrayal();
        }

        private void ClearPendingBetrayal()
        {
            _isAwaitingBetrayalConfirmation = false;
            _pendingBetrayalFrom = Vector2Int.Invalid;
            _pendingBetrayalTo = Vector2Int.Invalid;
        }

        /// <summary>
        /// Player chose not to execute a legal Retribution move. Only valid while resting in
        /// RetributionPending — sends intent only, MatchDriver owns the actual resolution
        /// (ResolveVoluntaryDefection routes through the same code as a forced Defection).
        /// </summary>
        public void RequestRetributionSkip()
        {
            if (_isAwaitingPromotion)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Retribution skip rejected: awaiting promotion choice");
                return;
            }

            if (_phaseProvider == null || _phaseProvider() != TurnPhase.RetributionPending)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Retribution skip rejected: not in RetributionPending");
                return;
            }

            if (_logMoves) Debug.Log($"[LocalMoveExecutor] Retribution skip confirmed");
            OnRetributionSkipConfirmed?.Invoke();
        }

        /// <summary>
        /// Confirms the player's promotion choice. We re-check legality here just to be safe before firing the final move event.
        /// </summary>
        public void RequestPromotion(ChessPieceType type)
        {
            if (!_isAwaitingPromotion)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Promotion rejected: not awaiting promotion");
                return;
            }

            // Re-validate to prevent memory injection / desyncs
            _legalMoves.Clear();
            try
            {
                if (_phaseProvider != null && _phaseProvider() == TurnPhase.RetributionPending)
                {
                    _engine.GetRetributionMoves(_board, _board.CurrentTurn, _board.PendingBetrayerSquare.Value, _legalMoves);
                }
                else if (_phaseProvider != null && _phaseProvider() == TurnPhase.ForcedSave)
                {
                    _engine.GetForcedSaveMoves(_board, _board.CurrentTurn, _legalMoves);
                }
                else
                {
                    _engine.GetLegalMoves(_board, _pendingPromotionMove.StartPosition, _legalMoves);
                }
            }
            catch (DomainException ex)
            {
                if (_logMoves) Debug.LogException(ex);
                OnMoveRejected?.Invoke(_pendingPromotionMove.StartPosition, _pendingPromotionMove.EndPosition);
                return;
            }

            bool found = false;
            for (int i = 0; i < _legalMoves.Count; i++)
            {
                MoveCommand cmd = _legalMoves[i];
                if (cmd.EndPosition == _pendingPromotionMove.EndPosition && cmd.PromotedTo == type)
                {
                    _isAwaitingPromotion = false;
                    found = true;
                    
                    if (_logMoves) Debug.Log($"[LocalMoveExecutor] Promotion confirmed: {type}");

                    // Stamp the promotion move with the current clock state.
                    ClockState? clockSnapshot = _clockSource?.Current;
                    if (clockSnapshot.HasValue)
                    {
                        cmd = cmd.WithClockSnapshot(clockSnapshot.Value);
                    }
                    
                    OnMoveConfirmed?.Invoke(cmd);
                    break;
                }
            }

            if (!found)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Promotion rejected: invalid choice");
                _isAwaitingPromotion = false;
                OnMoveRejected?.Invoke(_pendingPromotionMove.StartPosition, _pendingPromotionMove.EndPosition);
            }
        }
    }
}
