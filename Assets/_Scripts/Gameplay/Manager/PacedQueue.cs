using System;
using System.Collections.Generic;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Hands items out one at a time, waiting between them for however long the last one needs.
    ///
    /// The domain resolves instantly while the board animates over real time, so anything that
    /// drives the board has to be spaced out or it stacks animations on top of each other. Both
    /// places that need this - moves going onto the board, and plies coming back off it - want the
    /// same timer and the same drain, so it lives here once.
    ///
    /// Single-threaded by contract: Enqueue and Tick both belong on the main thread.
    /// </summary>
    public sealed class PacedQueue<T>
    {
        private readonly Action<T> _emit;
        private readonly Func<T, float> _durationOf;

        // Sized once for the handful of items that can realistically be waiting (in practice zero
        // or one), so an ordinary match never grows it.
        private readonly Queue<T> _pending = new Queue<T>(8);

        // Counts down in Tick(). Time-based rather than frame-based so the spacing holds whatever
        // the framerate is doing.
        private float _remainingSeconds;

        public PacedQueue(Action<T> emit, Func<T, float> durationOf)
        {
            _emit = emit;
            _durationOf = durationOf;
        }

        /// <summary>True while an item is still spending its time, or waiting behind one that is.</summary>
        public bool IsBusy => _remainingSeconds > 0f || _pending.Count > 0;

        /// <summary>
        /// Takes an item, releasing it straight away if nothing is in the way and otherwise holding
        /// it until everything ahead of it has had its time. Order is always preserved, and nothing
        /// is ever dropped to make room — the queue only empties by draining, or by Clear().
        /// </summary>
        public void Enqueue(T item)
        {
            _pending.Enqueue(item);
            if (_pending.Count == 1 && _remainingSeconds <= 0f)
            {
                EmitNext();
            }
        }

        /// <summary>Advances the timer. Call once per frame.</summary>
        public void Tick(float deltaSeconds)
        {
            if (_remainingSeconds <= 0f) return;

            _remainingSeconds -= deltaSeconds;
            if (_remainingSeconds <= 0f)
            {
                _remainingSeconds = 0f;
                EmitNext();
            }
        }

        /// <summary>Abandons everything still waiting and reopens immediately.</summary>
        public void Clear()
        {
            _pending.Clear();
            _remainingSeconds = 0f;
        }

        private void EmitNext()
        {
            if (_pending.Count == 0) return;

            T item = _pending.Dequeue();

            // The timer is armed before the item goes out, not after: emitting can lead straight
            // back here (a move reaching the board can produce the opponent's reply in the same
            // call), and that arrival has to find the queue busy so it waits its turn.
            _remainingSeconds = _durationOf(item);
            _emit(item);
        }
    }
}
