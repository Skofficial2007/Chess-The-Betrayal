using System;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Walks a takeback out one ply at a time so the player can watch it happen.
    ///
    /// The board is already back where it belongs before this starts — unmaking moves is instant,
    /// and correctness never waits on an animation. What is left is purely how the change is shown,
    /// which is why this decides only two things: which ply is announced next, and how long to
    /// leave before the one after it. Each ply is given the same room its forward animation would
    /// have taken, so a capture coming back gets the time a capture needs.
    ///
    /// Announcing the whole takeback at once would put every ply on the board in the same frame,
    /// which is the snap this exists to replace.
    /// </summary>
    public sealed class UndoPlaybackSequencer
    {
        private readonly PacedQueue<MoveUndonePayload> _queue;

        /// <param name="announcePly">Where each ply goes once it is that ply's turn to be shown.</param>
        /// <param name="estimateSeconds">How long that move's animation is worth — the same estimate the forward direction is paced against.</param>
        public UndoPlaybackSequencer(Action<MoveUndonePayload> announcePly, Func<MoveCommand, float> estimateSeconds)
        {
            _queue = new PacedQueue<MoveUndonePayload>(announcePly, payload => estimateSeconds(payload.Move));
        }

        /// <summary>
        /// True from the moment a takeback starts being shown until its last ply has been announced.
        /// The position on screen disagrees with the board while this is true, so input has to stay
        /// shut — a player who could grab a piece mid-rewind would be grabbing at where it used to
        /// be.
        /// </summary>
        public bool IsPlayingBack => _queue.IsBusy;

        /// <summary>
        /// Starts showing a takeback. <paramref name="unmadePliesNewestFirst"/> must be in the order
        /// the plies actually came off the board — see UndoService.RequestUndo, which reports it
        /// that way precisely so this does not have to guess.
        /// </summary>
        /// <param name="landsInCheck">Whether the position being landed on leaves the side to move in check.</param>
        public void Begin(IReadOnlyList<MoveCommand> unmadePliesNewestFirst, bool landsInCheck)
        {
            if (unmadePliesNewestFirst == null) return;

            for (int i = 0; i < unmadePliesNewestFirst.Count; i++)
            {
                // Only the last ply arrives anywhere; the rest are passed through on the way, so
                // they carry no claim about the position they leave behind.
                bool isFinalPly = i == unmadePliesNewestFirst.Count - 1;

                _queue.Enqueue(new MoveUndonePayload(
                    unmadePliesNewestFirst[i],
                    isFinalPly,
                    isFinalPly && landsInCheck));
            }
        }

        /// <summary>Advances the playback timer. Call once per frame.</summary>
        public void Tick(float deltaSeconds) => _queue.Tick(deltaSeconds);

        /// <summary>Abandons a takeback still being shown — for a new match, or leaving the current one.</summary>
        public void Clear() => _queue.Clear();
    }
}
