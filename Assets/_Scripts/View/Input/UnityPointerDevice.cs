using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ChessTheBetrayal.View.Input
{
    /// <summary>
    /// Reads the engine's mouse and touchscreen. Nothing here can run in a test — there is no
    /// hardware in EditMode — so it is kept to mirroring the input API and nothing else. Which of
    /// the two attached devices is speaking, and which finger of several is the one being followed,
    /// are both decided elsewhere: the finger by <see cref="SingleFingerFollow"/>, which can be
    /// tested, and the tap itself by <see cref="PointerTapRecognizer"/>.
    ///
    /// It reads the per-finger touches rather than the touchscreen's primaryTouch, and that is a
    /// deliberate correction rather than a preference. primaryTouch is not a finger: it is a
    /// stand-in that Unity keeps alive for as long as ANY finger is on the screen. So a second
    /// finger tapping while a first one rests produced no release, and the game — which finishes a
    /// tap on a release — never saw that tap at all. Worse, a finger whose lift the platform never
    /// delivers leaves it held at a frozen position with no release ever coming, which is a board
    /// that ignores every tap for the rest of the session and a hover highlight stuck on whichever
    /// square was touched last.
    /// </summary>
    public sealed class UnityPointerDevice : IPointerDevice
    {
        private readonly SingleFingerFollow _follow = new SingleFingerFollow();

        // Refilled every frame rather than rebuilt, since this runs in Update. Grown to whatever
        // the touchscreen offers the first time one is seen — ten on every platform so far.
        private TouchFingerReading[] _fingers = System.Array.Empty<TouchFingerReading>();

        private PointerFrame _touch = PointerFrame.Idle;
        private bool _touchIsDriving;

        /// <summary>
        /// Takes this frame's reading once, so every property below answers about the same moment.
        ///
        /// Deciding which finger is being followed is a judgement that has to be made exactly once
        /// per frame — made again inside each property it would depend on which order the caller
        /// happened to read them in.
        /// </summary>
        public void ReadThisFrame()
        {
            Touchscreen touchscreen = Touchscreen.current;

            if (touchscreen == null)
            {
                _follow.Forget();
                _touch = PointerFrame.Idle;
                _touchIsDriving = false;
                return;
            }

            int count = touchscreen.touches.Count;
            if (_fingers.Length < count)
            {
                _fingers = new TouchFingerReading[count];
            }

            for (int i = 0; i < count; i++)
            {
                TouchControl finger = touchscreen.touches[i];
                _fingers[i] = new TouchFingerReading(
                    finger.touchId.ReadValue(),
                    finger.isInProgress,
                    finger.press.wasPressedThisFrame,
                    finger.press.wasReleasedThisFrame,
                    finger.position.ReadValue());
            }

            _touch = _follow.Advance(_fingers, count);

            // A finger wins whenever it is involved, and "involved" has to include the frame it
            // lifts. That frame is the whole point of a tap, and it is the one frame where the
            // finger already reads as not pressed — a check written only against "is it held"
            // hands that frame to a mouse which, on a phone, does not exist.
            _touchIsDriving = _touch.AnythingHappening;
        }

        public bool ReportsPositionWhileIdle => !_touchIsDriving && Mouse.current != null;

        public bool IsPressed => _touchIsDriving
            ? _touch.IsPressed
            : Mouse.current != null && Mouse.current.leftButton.isPressed;

        public bool WasPressed => _touchIsDriving
            ? _touch.WasPressed
            : Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        public bool WasReleased => _touchIsDriving
            ? _touch.WasReleased
            : Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;

        public Vector2 Position => _touchIsDriving
            ? _touch.Position
            : Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    }
}
