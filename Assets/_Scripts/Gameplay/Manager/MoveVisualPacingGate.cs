using System;
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
        private readonly PacedQueue<MoveCommand> _queue;

        public MoveVisualPacingGate(Action<MoveCommand> playMove, Func<MoveCommand, float> estimateAnimationSeconds)
        {
            _queue = new PacedQueue<MoveCommand>(playMove, estimateAnimationSeconds);
        }

        /// <summary>True while a move is still pacing out its animation window or waiting behind one that is.</summary>
        public bool IsPacing => _queue.IsBusy;

        /// <summary>
        /// Accepts a move from any source (human, AI, future network). Plays it immediately if the
        /// gate is idle; otherwise holds it until every move ahead of it has finished pacing, then
        /// plays it in the order it arrived. A move is never dropped to make room for another one —
        /// the queue only ever empties by playing, or by an explicit Clear().
        /// </summary>
        public void Enqueue(MoveCommand move) => _queue.Enqueue(move);

        /// <summary>Advances the pacing timer. Call once per frame from a MonoBehaviour Update(), same as AIMatchCoordinator.Tick().</summary>
        public void Tick(float deltaSeconds) => _queue.Tick(deltaSeconds);

        /// <summary>
        /// Holds the gate shut for <paramref name="extraSeconds"/> longer than the move in front
        /// of it asked for.
        ///
        /// A move's estimate covers the move. A Betrayal Act that ends in a Defection also has the
        /// Betrayer turning into the other side's piece to play afterwards, and no move command
        /// carries the fact that it is coming - the driver resolves it while applying the Act. So
        /// whoever learns of it says so here, rather than every estimate pretending to know.
        /// </summary>
        public void HoldFor(float extraSeconds) => _queue.ExtendHold(extraSeconds);

        /// <summary>
        /// Throws away every queued move and reopens the gate immediately.
        ///
        /// Undo is the caller this exists for. A move waiting in here has been decided but has NOT
        /// reached the board yet, so it was chosen against a position that an undo is about to
        /// destroy — letting the timer expire afterwards would apply it to a board that has already
        /// been rewound underneath it. Abandoning the queue is the only correct answer: there is no
        /// position left for those moves to be legal in.
        /// </summary>
        public void Clear() => _queue.Clear();
    }
}
