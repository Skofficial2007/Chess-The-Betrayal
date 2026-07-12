using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using ChessTheBetrayal.Core.Diagnostics;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Routes domain diagnostic events to the Unity console.
    /// Manages a thread-safe queue to capture errors emitted by background search threads
    /// and defers their string formatting to the main thread.
    /// </summary>
    public sealed class UnityDomainLogger : IDomainLogger
    {
        public bool IsVerbose { get; }

        private readonly ConcurrentQueue<DomainLogEvent> _errorBuffer = new ConcurrentQueue<DomainLogEvent>();
        private readonly Action<DomainLogEvent> _onFatalError;

        // Pre-built prefixes map domain event codes to human-readable context.
        // This defers string formatting to the presentation layer, keeping domain execution fast.
        private static readonly Dictionary<DomainEventCode, string> _prefixes = new Dictionary<DomainEventCode, string>
        {
            { DomainEventCode.Engine_PromotionPieceNotFound,   "[ChessEngine] Promotion: piece not found at square" },
            { DomainEventCode.Engine_KingNotFound,             "[ChessEngine] King not found on board for team" },
            { DomainEventCode.Engine_IllegalMoveRequested,     "[ChessEngine] Illegal move requested" },
            { DomainEventCode.Engine_MoveHistoryUnderflow,     "[ChessEngine] Move history underflow on undo" },
            { DomainEventCode.Board_PieceSetOutOfBounds,       "[BoardState] SetPiece called with out-of-bounds coordinates" },
            { DomainEventCode.Board_ZobristDesync,             "[BoardState] Zobrist hash desync — incremental hash does not match full recompute" },
            { DomainEventCode.Betrayal_RightAlreadyConsumed,   "[Betrayal] Betrayal right already consumed this match" },
            { DomainEventCode.Betrayal_KingTargetedAsVictim,   "[Betrayal] INVARIANT VIOLATED: King cannot be the Victim" },
            { DomainEventCode.Betrayal_KingTargetedAsBetrayer, "[Betrayal] INVARIANT VIOLATED: King cannot be the Betrayer" },
            { DomainEventCode.Betrayal_RetributionPieceNone,   "[Betrayal] No executioner piece can reach the Betrayer — Defection triggered" },
            { DomainEventCode.Betrayal_DefectionResolved,      "[Betrayal] Defection resolved — Betrayer joined opponent's army" },
            { DomainEventCode.Betrayal_ForcedSaveRequired,     "[Betrayal] Defection placed own King in check — ForcedSave phase active" },
            { DomainEventCode.AI_TranspositionHashCollision,   "[AI] Transposition table hash collision at ply" },
            { DomainEventCode.AI_SearchDepthExceeded,          "[AI] Search depth exceeded maximum budget at ply" },
            { DomainEventCode.AI_BetrayalBranchExpansion,      "[AI] Betrayal branch expansion factor at node" },
            { DomainEventCode.AI_SearchRequested,              "[AI] Search requested — depth" },
            { DomainEventCode.AI_MoveDecided,                  "[AI] Move decided — elapsed ms" },
            { DomainEventCode.AI_SearchCancelled,              "[AI] Search cancelled (undo)" },
            { DomainEventCode.AI_BookMovePlayed,               "[AI] Opening book move played" },
        };

        public UnityDomainLogger(bool verbose = false, Action<DomainLogEvent> onFatalError = null)
        {
            IsVerbose = verbose;
            _onFatalError = onFatalError;
        }

        public void LogInfo(DomainLogEvent evt)
        {
            if (!IsVerbose) return;
            Debug.Log(Format(evt));
        }

        public void LogWarning(DomainLogEvent evt)
        {
            Debug.LogWarning(Format(evt));
        }

        /// <summary>
        /// Enqueues an error for processing on the main thread.
        /// Thread-safe for use within background AI evaluations.
        /// </summary>
        public void LogError(DomainLogEvent evt)
        {
            _errorBuffer.Enqueue(evt);
        }

        /// <summary>
        /// Drains the error queue and dispatches messages to the Unity console.
        /// Must be called continuously from a MonoBehaviour Update loop.
        /// </summary>
        public void FlushToUnityConsole()
        {
            while (_errorBuffer.TryDequeue(out DomainLogEvent evt))
            {
                Debug.LogError(Format(evt));
                _onFatalError?.Invoke(evt);
            }
        }

        private static string Format(DomainLogEvent evt)
        {
            if (_prefixes.TryGetValue(evt.Code, out string prefix))
            {
                return evt.Message != null
                    ? $"{prefix} ({evt.AuxInt}): {evt.Message}"
                    : $"{prefix} ({evt.AuxInt})";
            }
            
            return evt.Message != null
                ? $"[Domain:{evt.Code}({evt.AuxInt})] {evt.Message}"
                : $"[Domain:{evt.Code}({evt.AuxInt})]";
        }
    }
}
