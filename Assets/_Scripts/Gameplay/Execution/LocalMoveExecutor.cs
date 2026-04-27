using System;
using System.Collections.Generic;
using UnityEngine;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;

namespace ChessTheMasterPiece.Controllers
{
    /// <summary>
    /// Handles move validation for local, offline play.
    /// All the pure chess logic that was previously in GameManager now lives here.
    /// In the future, NetworkMoveExecutor will implement the same interface for multiplayer.
    /// </summary>
    public class LocalMoveExecutor : IMoveExecutor
    {
        public event Action<MoveCommand> OnMoveConfirmed;
        public event Action<ChessTheMasterPiece.Data.Vector2Int, ChessTheMasterPiece.Data.Vector2Int> OnMoveRejected;
        public event Action<ChessTheMasterPiece.Data.Vector2Int> OnPromotionRequired;

        private readonly BoardState _board;
        private readonly List<MoveCommand> _legalMoves = new List<MoveCommand>(32);
        private readonly List<MoveCommand> _movesToTarget = new List<MoveCommand>(4);
        
        private MoveCommand _pendingPromotionMove;
        private bool _isAwaitingPromotion;
        private bool _logMoves;

        /// <summary>
        /// Note: We take a direct reference to the live board here. A network executor must NOT do this — it should validate against a server snapshot, not the client's version.
        /// </summary>
        public LocalMoveExecutor(BoardState board, bool logMoves = true)
        {
            _board = board;
            _logMoves = logMoves;
        }

        /// <summary>
        /// Validates a move request and either fires OnMoveConfirmed (legal) or OnMoveRejected (illegal). If it's a promotion, we pause and fire OnPromotionRequired instead.
        /// </summary>
        public void RequestMove(ChessTheMasterPiece.Data.Vector2Int from, ChessTheMasterPiece.Data.Vector2Int to)
        {
            // Block new moves if waiting for a UI decision
            if (_isAwaitingPromotion)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Move rejected: awaiting promotion choice");
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            // Validate piece ownership
            PieceData piece = _board.GetPiece(from);
            if (piece == null || piece.Team != _board.CurrentTurn)
            {
                if (_logMoves) Debug.Log($"[LocalMoveExecutor] Move rejected: wrong piece or turn");
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            // Handle King-on-Rook castling shortcut
            // If the player dragged the King onto a friendly Rook, remap the 'to' target to the standard 2-step castling square.
            PieceData targetPiece = _board.GetPiece(to);
            if (piece.Type == ChessPieceType.King && targetPiece != null && 
                targetPiece.Type == ChessPieceType.Rook && targetPiece.Team == piece.Team)
            {
                // If Rook is at X=7 (Kingside), Target is X=6. If Rook is at X=0 (Queenside), Target is X=2.
                int castlingX = to.x > from.x ? 6 : 2;
                to = new ChessTheMasterPiece.Data.Vector2Int(castlingX, from.y);
            }

            _legalMoves.Clear();
            ChessEngine.GetLegalMoves(_board, from, _legalMoves);

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
                OnPromotionRequired?.Invoke(to);
                return;
            }

            MoveCommand validMove = _movesToTarget[0];
            
            if (_logMoves) Debug.Log($"[LocalMoveExecutor] Move confirmed: {validMove.StartPosition} -> {validMove.EndPosition}");
            
            // Fire the confirmation event - GameManager will execute it
            OnMoveConfirmed?.Invoke(validMove);
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
            ChessEngine.GetLegalMoves(_board, _pendingPromotionMove.StartPosition, _legalMoves);

            bool found = false;
            for (int i = 0; i < _legalMoves.Count; i++)
            {
                MoveCommand cmd = _legalMoves[i];
                if (cmd.EndPosition == _pendingPromotionMove.EndPosition && cmd.PromotedTo == type)
                {
                    _isAwaitingPromotion = false;
                    found = true;
                    
                    if (_logMoves) Debug.Log($"[LocalMoveExecutor] Promotion confirmed: {type}");
                    
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
