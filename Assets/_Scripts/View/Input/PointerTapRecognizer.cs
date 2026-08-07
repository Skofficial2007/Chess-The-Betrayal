using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.View
{
    /// <summary>
    /// Turns a stream of per-frame pointer facts into tap events. A tap is a press and a release
    /// on the same tile; dragging away before letting go is not one, because the two-tap model has
    /// no use for drag gestures.
    ///
    /// No Unity types and no device access, so the rules below can be exercised frame by frame in
    /// a plain test — including the awkward frames real hardware produces and a play-test never
    /// thinks to try. That matters more than it sounds: the release frame is the only frame that
    /// can complete a tap, and it is also the frame where a finger already reports as not pressed.
    /// </summary>
    public sealed class PointerTapRecognizer
    {
        private readonly float _minSecondsBetweenActivations;

        private bool _isPressed;
        private Vector2Int _pressStartTile = Vector2Int.Invalid;
        private float _lastActivationSeconds = float.NegativeInfinity;

        public PointerTapRecognizer(float minSecondsBetweenActivations)
        {
            _minSecondsBetweenActivations = minSecondsBetweenActivations;
        }

        /// <summary>
        /// Whether this frame's pointer position is worth resolving to a tile at all.
        ///
        /// A mouse always has somewhere it is pointing. A finger only has a position while it is
        /// touching the glass — plus the frame it lifts, which reports itself as not pressed and
        /// still holds the place it left from. Leaving that frame out is what makes a tap
        /// impossible to complete by touch: the release would never be looked at.
        /// </summary>
        public static bool HasUsablePosition(bool reportsPositionWhileIdle, bool isPressed, bool wasReleased)
        {
            return reportsPositionWhileIdle || isPressed || wasReleased;
        }

        /// <summary>
        /// Feeds one frame in and reports whether it completed a tap on <paramref name="tile"/>.
        /// Pass Vector2Int.Invalid when the pointer is off the board. realtimeSeconds should come
        /// from an unscaled clock, so a paused or slowed game does not stretch the debounce.
        /// </summary>
        public bool Observe(bool wasPressed, bool wasReleased, Vector2Int tile, float realtimeSeconds)
        {
            if (wasPressed)
            {
                _isPressed = true;
                _pressStartTile = tile;
            }

            if (!wasReleased) return false;

            bool landedWhereItStarted = _isPressed && tile != Vector2Int.Invalid && tile == _pressStartTile;
            _isPressed = false;
            _pressStartTile = Vector2Int.Invalid;

            if (!landedWhereItStarted) return false;

            // A single physical tap can only ever produce one release, so this is not guarding
            // against double-counting one gesture. It guards a fast player mashing two real taps
            // before the first one's slide, capture stamp or promotion swap has visibly settled.
            if (realtimeSeconds - _lastActivationSeconds < _minSecondsBetweenActivations) return false;

            _lastActivationSeconds = realtimeSeconds;
            return true;
        }
    }
}
