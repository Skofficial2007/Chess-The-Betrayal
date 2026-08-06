using System;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Sits between every move source (human input, the AI, and eventually a network peer) and
    /// <see cref="MatchDriver.PlayMove"/>: instead of calling PlayMove directly, a caller enqueues
    /// a move here, and this class plays it the instant the PREVIOUS move's on-board animation has
    /// had time to finish. Domain logic (MatchDriver) stays untouched and fully synchronous — this
    /// only paces WHEN the next move is allowed to reach it, never how it's resolved.
    ///
    /// Why this exists: MatchDriver applies a move and advances the domain instantly, but the view
    /// (BoardVisuals) animates it over real time via PrimeTween. A fast decision-maker (the AI,
    /// especially post-search-performance-work) can enqueue its next move before the previous
    /// move's capture/castle/promotion animation has actually finished playing, so two pieces can
    /// end up visually overlapping mid-animation. This gate is the fix: every move source funnels
    /// through one queue, and the queue only ever drains at the pace the board can actually show.
    ///
    /// Single-threaded by contract: Enqueue/Tick are both expected to run on the main thread (the
    /// same thread MatchDriver.PlayMove itself requires — see its own doc comment). An AI move
    /// already crosses that boundary before it reaches here (AIMatchCoordinator.HandleMoveDecided
    /// only ever runs from Tick()), so this class never needs to know about background threads.
    /// </summary>
    public sealed class MoveVisualPacingGate
    {
        private readonly Action<MoveCommand> _playMove;
        private readonly Func<MoveCommand, float> _estimateAnimationSeconds;

        // Pre-sized once and reused for the life of the match — a queue this shallow (a handful of
        // pending moves at most; in practice almost always 0 or 1) never needs to grow, so no
        // per-move allocation happens here regardless of match length.
        private readonly Queue<MoveCommand> _pending = new Queue<MoveCommand>(8);

        // Counts down in Tick(); a move is playable again once this reaches zero. Time-based
        // rather than frame-based so pacing stays correct regardless of framerate.
        private float _remainingPaceSeconds;

        public MoveVisualPacingGate(Action<MoveCommand> playMove, Func<MoveCommand, float> estimateAnimationSeconds)
        {
            _playMove = playMove;
            _estimateAnimationSeconds = estimateAnimationSeconds;
        }

        /// <summary>True while a move is still pacing out its animation window or waiting behind one that is.</summary>
        public bool IsPacing => _remainingPaceSeconds > 0f || _pending.Count > 0;

        /// <summary>
        /// Accepts a move from any source (human, AI, future network). Plays it immediately if the
        /// gate is idle; otherwise holds it until every move ahead of it has finished pacing, then
        /// plays it in the order it arrived. A move is never dropped to make room for another one —
        /// the queue only ever empties by playing, or by an explicit Clear().
        /// </summary>
        public void Enqueue(MoveCommand move)
        {
            _pending.Enqueue(move);
            if (_pending.Count == 1 && _remainingPaceSeconds <= 0f)
            {
                PlayNext();
            }
        }

        /// <summary>
        /// Throws away every queued move and reopens the gate immediately.
        ///
        /// Undo is the caller this exists for. A move waiting in here has been decided but has NOT
        /// reached the board yet, so it was chosen against a position that an undo is about to
        /// destroy — letting the timer expire afterwards would apply it to a board that has already
        /// been rewound underneath it. Abandoning the queue is the only correct answer: there is no
        /// position left for those moves to be legal in.
        /// </summary>
        public void Clear()
        {
            _pending.Clear();
            _remainingPaceSeconds = 0f;
        }

        /// <summary>Advances the pacing timer. Call once per frame from a MonoBehaviour Update(), same as AIMatchCoordinator.Tick().</summary>
        public void Tick(float deltaSeconds)
        {
            if (_remainingPaceSeconds <= 0f) return;

            _remainingPaceSeconds -= deltaSeconds;
            if (_remainingPaceSeconds <= 0f)
            {
                _remainingPaceSeconds = 0f;
                PlayNext();
            }
        }

        private void PlayNext()
        {
            if (_pending.Count == 0) return;

            MoveCommand move = _pending.Dequeue();
            _remainingPaceSeconds = _estimateAnimationSeconds(move);
            _playMove(move);
        }
    }
}
