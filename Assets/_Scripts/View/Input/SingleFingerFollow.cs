using UnityEngine;

namespace ChessTheBetrayal.View.Input
{
    /// <summary>What one finger is doing this frame, as the hardware reports it.</summary>
    public readonly struct TouchFingerReading
    {
        public readonly int TouchId;
        public readonly bool IsInProgress;
        public readonly bool WasPressedThisFrame;
        public readonly bool WasReleasedThisFrame;
        public readonly Vector2 Position;

        public TouchFingerReading(int touchId, bool isInProgress, bool wasPressedThisFrame,
            bool wasReleasedThisFrame, Vector2 position)
        {
            TouchId = touchId;
            IsInProgress = isInProgress;
            WasPressedThisFrame = wasPressedThisFrame;
            WasReleasedThisFrame = wasReleasedThisFrame;
            Position = position;
        }
    }

    /// <summary>
    /// One frame's answer for the single pointer the board is driven by, whatever hardware and
    /// however many fingers produced it.
    /// </summary>
    public readonly struct PointerFrame
    {
        public readonly bool IsPressed;
        public readonly bool WasPressed;
        public readonly bool WasReleased;
        public readonly Vector2 Position;

        public PointerFrame(bool isPressed, bool wasPressed, bool wasReleased, Vector2 position)
        {
            IsPressed = isPressed;
            WasPressed = wasPressed;
            WasReleased = wasReleased;
            Position = position;
        }

        /// <summary>Nothing is touching the glass.</summary>
        public static PointerFrame Idle => new PointerFrame(false, false, false, Vector2.zero);

        /// <summary>Whether a finger is doing anything this frame worth answering with.</summary>
        public bool AnythingHappening => IsPressed || WasReleased;
    }

    /// <summary>
    /// Picks one finger out of everything on the glass and follows it until it lifts.
    ///
    /// The board is a two-tap game: press and release on the same square, no dragging and no
    /// gestures. So it needs exactly one pointer, and the question worth answering carefully is
    /// which finger that should be when several are involved.
    ///
    /// The rule the whole class exists for: <b>only ever follow a finger whose press was actually
    /// seen.</b> A finger already down when we start looking is never adopted, however long it
    /// stays. That one restriction is what makes a lost touch survivable. If the platform never
    /// delivers a finger's lift — the app losing focus mid-touch, a system gesture swallowing it —
    /// that finger stays down as far as the input system is concerned, forever. Following it would
    /// mean waiting forever for a release that is not coming, which is exactly how the board came
    /// to stop accepting taps for the rest of a session, through a match exit and into a new game.
    /// Refusing to adopt it costs nothing: the next real tap is a fresh press, and that one is
    /// followed normally.
    ///
    /// It also answers the ordinary case that was silently broken. Unity's primaryTouch is not a
    /// finger but a stand-in kept alive for as long as ANY finger is on the screen, so a second
    /// finger tapping while a first one rests produced no release at all and its tap was never
    /// seen. Following a finger of our own choosing makes that tap ordinary.
    ///
    /// No Unity device here, so the rule can be exercised frame by frame in a plain test against
    /// readings written by hand — including the sequences real hardware produces under a whole hand
    /// of fingers, which no play-test would think to try.
    /// </summary>
    public sealed class SingleFingerFollow
    {
        /// <summary>No finger is being followed. Not a touch id any platform hands out.</summary>
        public const int NoFinger = int.MinValue;

        private int _followedTouchId = NoFinger;

        /// <summary>Which finger is currently being followed, or <see cref="NoFinger"/>.</summary>
        public int FollowedTouchId => _followedTouchId;

        /// <summary>
        /// Reads one frame of finger activity and answers for the single pointer.
        ///
        /// Takes the array and how much of it to read rather than a collection, so the caller can
        /// refill one buffer every frame instead of building a new one.
        /// </summary>
        public PointerFrame Advance(TouchFingerReading[] fingers, int count)
        {
            if (_followedTouchId != NoFinger)
            {
                for (int i = 0; i < count; i++)
                {
                    if (fingers[i].TouchId != _followedTouchId) continue;

                    // Checked before the in-progress test, and that ordering matters: a finger is
                    // no longer "in progress" on the very frame it lifts, so a release looked for
                    // only among ongoing touches is a release never found.
                    if (fingers[i].WasReleasedThisFrame)
                    {
                        _followedTouchId = NoFinger;
                        return new PointerFrame(false, false, true, fingers[i].Position);
                    }

                    if (fingers[i].IsInProgress)
                    {
                        return new PointerFrame(true, false, false, fingers[i].Position);
                    }

                    // Still in its slot, but neither held nor lifting: it ended on some earlier
                    // frame and nothing said so. Let it go rather than wait on it.
                    break;
                }

                // The finger has gone without a lift ever being seen — its slot reused, or the
                // whole device reset. Waiting on it is the one thing that must never happen.
                _followedTouchId = NoFinger;
                return PointerFrame.Idle;
            }

            for (int i = 0; i < count; i++)
            {
                if (!fingers[i].WasPressedThisFrame) continue;

                _followedTouchId = fingers[i].TouchId;
                return new PointerFrame(true, true, false, fingers[i].Position);
            }

            return PointerFrame.Idle;
        }

        /// <summary>
        /// Stops following whatever was being followed. For a pointer that has gone away entirely
        /// — no touchscreen at all — where there are no readings to reach the conclusion from.
        /// </summary>
        public void Forget() => _followedTouchId = NoFinger;
    }
}
