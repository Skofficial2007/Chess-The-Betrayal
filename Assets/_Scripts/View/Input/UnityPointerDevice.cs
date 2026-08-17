using UnityEngine;
using UnityEngine.InputSystem;

namespace ChessTheBetrayal.View.Input
{
    /// <summary>
    /// Reads the engine's mouse and touchscreen. Nothing here can run in a test — there is no
    /// hardware in EditMode — so it is kept to a straight mirror of the input API with one rule:
    /// which of the two attached devices is speaking this frame.
    ///
    /// A finger wins whenever it is involved, and "involved" has to include the frame it lifts.
    /// That frame is the whole point of a tap, and it is the one frame where isPressed already
    /// reads false, so a check written only against isPressed hands the frame to a mouse that on a
    /// phone does not exist — and the tap is never completed. The touch position survives the lift
    /// and still names the square the finger left from, which is what a tap is measured against.
    /// </summary>
    public sealed class UnityPointerDevice : IPointerDevice
    {
        public bool ReportsPositionWhileIdle => !TouchIsDriving && Mouse.current != null;

        public bool IsPressed => TouchIsDriving
            ? Touchscreen.current.primaryTouch.press.isPressed
            : Mouse.current != null && Mouse.current.leftButton.isPressed;

        public bool WasPressed => TouchIsDriving
            ? Touchscreen.current.primaryTouch.press.wasPressedThisFrame
            : Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        public bool WasReleased => TouchIsDriving
            ? Touchscreen.current.primaryTouch.press.wasReleasedThisFrame
            : Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;

        public Vector2 Position => TouchIsDriving
            ? Touchscreen.current.primaryTouch.position.ReadValue()
            : Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        private static bool TouchIsDriving
        {
            get
            {
                Touchscreen touchscreen = Touchscreen.current;
                return touchscreen != null &&
                       (touchscreen.primaryTouch.press.isPressed ||
                        touchscreen.primaryTouch.press.wasReleasedThisFrame);
            }
        }
    }
}
