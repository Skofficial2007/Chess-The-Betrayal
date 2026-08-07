using System;
using UnityEngine;

namespace ChessTheBetrayal.View
{
    /// <summary>
    /// Every colour and dimension the board's square markers use, in one asset you can edit while
    /// the game runs. Nothing else in the project decides what a highlight looks like.
    ///
    /// Two rules shaped these defaults, and both are worth keeping if you retune them:
    ///
    /// Meaning is carried by SHAPE first and colour second — a dot for a quiet move, a ring for a
    /// capture, a ringed diamond for a betrayal, a frame for check. Roughly one man in twelve cannot
    /// separate the green from the red, so a board that encodes "move" and "capture" as hue alone is
    /// unreadable to them. Every state here stays distinguishable in greyscale.
    ///
    /// Glow is spent only where it means danger. Bloom in the scene's volume profile starts lifting
    /// at a threshold of 1.0, so a state only blooms once its colour times (1 + glow) clears that.
    /// Calm states are deliberately left below it; if everything glows, nothing reads as urgent.
    /// </summary>
    [CreateAssetMenu(menuName = "Chess/Board Highlight Palette", fileName = "BoardHighlightPalette")]
    public class BoardHighlightPalette : ScriptableObject
    {
        /// <summary>
        /// One state's appearance: the colour you pick, and how far past the bloom threshold it is
        /// pushed. Glow multiplies the colour rather than adding to it, so a dim colour with a big
        /// glow stays dim-hued instead of washing toward white.
        /// </summary>
        [Serializable]
        public struct Look
        {
            [Tooltip("Colour and opacity, picked in ordinary sRGB. Converted to linear when applied.")]
            public Color Colour;

            [Tooltip("0 keeps the state matte. Above 0 multiplies the colour, and once colour x (1 + glow) passes 1.0 the state starts to bloom.")]
            [Range(0f, 4f)]
            public float Glow;
        }

        [Header("Markers — the shape drawn on a square")]
        [Tooltip("A square this piece can move to with nothing to take. The quietest thing on the board.")]
        [SerializeField] private Look quietMove = new Look { Colour = new Color(0.35f, 0.85f, 0.45f, 0.72f), Glow = 0f };

        [Tooltip("A square holding a piece this move would capture. Drawn as a ring so the piece underneath stays visible.")]
        [SerializeField] private Look capture = new Look { Colour = new Color(0.95f, 0.25f, 0.18f, 0.85f), Glow = 0.4f };

        [Tooltip("A square where this move would begin a Betrayal. Its own hue AND its own shape, so it can never be mistaken for an ordinary capture.")]
        [SerializeField] private Look betrayalTarget = new Look { Colour = new Color(0.72f, 0.36f, 0.98f, 0.85f), Glow = 1.4f };

        [Tooltip("The square a Betrayer is currently standing on, while Retribution is still pending. Same hue as betrayalTarget so the mechanic keeps one colour end to end, but louder — this is the live hazard, not a move you could choose to make.")]
        [SerializeField] private Look betrayerAtLarge = new Look { Colour = new Color(0.72f, 0.36f, 0.98f, 0.9f), Glow = 1.6f };

        [Tooltip("The square of a king in check. Deliberately the loudest state on the board.")]
        [SerializeField] private Look check = new Look { Colour = new Color(1f, 0.22f, 0.16f, 0.9f), Glow = 2f };

        [Tooltip("Corner ticks on the square whose piece is currently picked up.")]
        [SerializeField] private Look selectedMarker = new Look { Colour = new Color(1f, 0.78f, 0.25f, 0.95f), Glow = 0.2f };

        [Tooltip("The faint dark disc grounding the capture reticle. At markerYOffset the ring alone can read as floating beside the piece rather than framing it, on a tilted camera.")]
        [SerializeField] private Look captureShadow = new Look { Colour = new Color(0f, 0f, 0f, 0.25f), Glow = 0f };

        [Header("Tints — the whole square, drawn under any marker")]
        [Tooltip("Wash behind the picked-up piece's own square.")]
        [SerializeField] private Look selectedTint = new Look { Colour = new Color(1f, 0.78f, 0.25f, 0.22f), Glow = 0f };

        [Tooltip("Wash under the pointer. Kept very faint — it follows the cursor, so anything stronger reads as noise.")]
        [SerializeField] private Look hoverTint = new Look { Colour = new Color(1f, 1f, 1f, 0.1f), Glow = 0f };

        [Tooltip("Hollow outline on the square the last-moved piece left. A cool, desaturated hue deliberately far from selection's amber — the two used to sit close enough to read as the same state failing to clear.")]
        [SerializeField] private Look lastMoveFromTint = new Look { Colour = new Color(0.4f, 0.58f, 0.62f, 0.32f), Glow = 0f };

        [Tooltip("Soft fill on the square the last-moved piece landed on. Same cool hue as the origin outline, so the pair reads as one story — where it came from, where it went — without needing an arrow.")]
        [SerializeField] private Look lastMoveToTint = new Look { Colour = new Color(0.4f, 0.58f, 0.62f, 0.16f), Glow = 0f };

        [Header("Shape sizes — fractions of one square")]
        [Tooltip("Radius of the quiet-move dot. Kept large because a tall 3D piece standing on a neighbouring square can occlude most of a small one.")]
        [SerializeField, Range(0.05f, 0.5f)] private float dotRadiusRatio = 0.48f;

        [Tooltip("Outer radius of the capture ring. Large enough to frame a piece rather than sit under it.")]
        [SerializeField, Range(0.2f, 0.5f)] private float captureRingRadiusRatio = 0.5f;

        [Tooltip("Thickness of the capture ring, as a fraction of one square.")]
        [SerializeField, Range(0.02f, 0.2f)] private float captureRingThicknessRatio = 0.07f;

        [Tooltip("Outer radius of the capture reticle's grounding shadow, as a fraction of one square. Must stay under 0.5 — at exactly 0.5 it reaches the tile edge, and beyond that it spills onto the neighbouring squares.")]
        [SerializeField, Range(0.2f, 0.5f)] private float captureShadowRadiusRatio = 0.49f;

        [Tooltip("How far the capture reticle's four cardinal ticks extend beyond the ring, as a fraction of one square.")]
        [SerializeField, Range(0.02f, 0.2f)] private float captureTickLengthRatio = 0.08f;

        [Tooltip("Thickness of a capture reticle tick.")]
        [SerializeField, Range(0.01f, 0.08f)] private float captureTickThicknessRatio = 0.025f;

        [Tooltip("Outer radius of the betrayal marker's ring.")]
        [SerializeField, Range(0.2f, 0.5f)] private float betrayalRingRadiusRatio = 0.46f;

        [Tooltip("Half-width of the diamond inside the betrayal marker's ring.")]
        [SerializeField, Range(0.05f, 0.4f)] private float betrayalDiamondRatio = 0.16f;

        [Tooltip("How far each of the Betrayer marker's four chevrons reaches inward from the ring's edge, as a fraction of one square.")]
        [SerializeField, Range(0.02f, 0.3f)] private float betrayerChevronLengthRatio = 0.14f;

        [Tooltip("Half-width of a Betrayer marker chevron's base.")]
        [SerializeField, Range(0.02f, 0.2f)] private float betrayerChevronHalfWidthRatio = 0.05f;

        [Tooltip("Outer size of the check frame. Under 1 insets it, so the king reads as sitting inside the frame.")]
        [SerializeField, Range(0.5f, 1f)] private float checkFrameSizeRatio = 0.92f;

        [Tooltip("Border thickness of the check frame, as a fraction of its outer size.")]
        [SerializeField, Range(0.02f, 0.3f)] private float checkFrameThicknessRatio = 0.09f;

        [Tooltip("How far each selection corner tick runs along its edge, as a fraction of one square.")]
        [SerializeField, Range(0.05f, 0.5f)] private float cornerTickLengthRatio = 0.22f;

        [Tooltip("Thickness of a selection corner tick.")]
        [SerializeField, Range(0.02f, 0.15f)] private float cornerTickThicknessRatio = 0.055f;

        [Tooltip("How far outside the tile a corner tick starts before clamping in, as a multiple of its resting distance from centre. 1 would start it already at rest; higher values start it further outside the square.")]
        [SerializeField, Range(1f, 3f)] private float cornerTickClampStartRatio = 1.6f;

        [Tooltip("Size of a square tint. Slightly under 1 leaves a hairline of board showing, which stops neighbouring tinted squares merging into one block.")]
        [SerializeField, Range(0.5f, 1f)] private float tintSizeRatio = 0.97f;

        [Tooltip("Border thickness of the last-move origin's hollow outline, as a fraction of the tint footprint.")]
        [SerializeField, Range(0.02f, 0.2f)] private float lastMoveFromOutlineThicknessRatio = 0.06f;

        [Tooltip("Segments in the round shapes. 24 is smooth at any sane board size; lower it only if you are chasing vertices.")]
        [SerializeField, Range(8, 64)] private int circleSegments = 24;

        [Header("Motion")]
        [Tooltip("How long a marker takes to scale up when it appears.")]
        [SerializeField, Range(0.05f, 0.6f)] private float appearDuration = 0.18f;

        [Tooltip("Extra delay per square of distance from the selected piece, so the markers ripple outward instead of all snapping on together. 0 disables the ripple.")]
        [SerializeField, Range(0f, 0.05f)] private float appearStaggerPerSquare = 0.012f;

        [Tooltip("Seconds for a selection corner tick to clamp in from outside the square to its resting corner.")]
        [SerializeField, Range(0.05f, 0.4f)] private float cornerTickClampDuration = 0.14f;

        [Tooltip("Overshoot on a corner tick's clamp-in. Kept gentler than the general marker appear — this is a grip settling into place, not a pop.")]
        [SerializeField, Range(0f, 3f)] private float cornerTickClampOvershoot = 1.3f;

        [Tooltip("Seconds for one full pulse of the check frame's glow.")]
        [SerializeField, Range(0.2f, 3f)] private float checkPulsePeriod = 1.1f;

        [Tooltip("How much glow the check pulse adds at its peak, on top of the check state's own glow.")]
        [SerializeField, Range(0f, 3f)] private float checkPulseGlowAmount = 1.2f;

        [Tooltip("Seconds for one full breathe of the capture reticle's glow. Slower than the check pulse — a capture is an option, not a warning.")]
        [SerializeField, Range(0.5f, 5f)] private float captureBreathePeriod = 2.2f;

        [Tooltip("How much glow the capture breathe adds at its peak, on top of the capture state's own glow.")]
        [SerializeField, Range(0f, 2f)] private float captureBreatheGlowAmount = 0.35f;

        [Tooltip("Degrees per second the capture reticle's four ticks rotate. Slow and constant, independent of the breathe.")]
        [SerializeField, Range(0f, 60f)] private float captureTickRotationSpeed = 12f;

        [Tooltip("Seconds for one full pulse of the Betrayer marker's glow. Only one ever exists at a time, so this can be tuned freely without worrying about squares falling out of sync with each other.")]
        [SerializeField, Range(0.2f, 3f)] private float betrayerPulsePeriod = 0.9f;

        [Tooltip("How much glow the Betrayer marker's pulse adds at its peak, on top of its own base glow.")]
        [SerializeField, Range(0f, 3f)] private float betrayerPulseGlowAmount = 1.4f;

        [Tooltip("Degrees per second the Betrayer marker's four chevrons rotate.")]
        [SerializeField, Range(0f, 90f)] private float betrayerChevronRotationSpeed = 25f;

        public Look QuietMove => quietMove;
        public Look Capture => capture;
        public Look BetrayalTarget => betrayalTarget;
        public Look BetrayerAtLarge => betrayerAtLarge;
        public Look Check => check;
        public Look SelectedMarker => selectedMarker;
        public Look CaptureShadow => captureShadow;
        public Look SelectedTint => selectedTint;
        public Look HoverTint => hoverTint;
        public Look LastMoveFromTint => lastMoveFromTint;
        public Look LastMoveToTint => lastMoveToTint;

        public float DotRadiusRatio => dotRadiusRatio;
        public float CaptureRingRadiusRatio => captureRingRadiusRatio;
        public float CaptureRingThicknessRatio => captureRingThicknessRatio;
        public float CaptureShadowRadiusRatio => captureShadowRadiusRatio;
        public float CaptureTickLengthRatio => captureTickLengthRatio;
        public float CaptureTickThicknessRatio => captureTickThicknessRatio;
        public float BetrayalRingRadiusRatio => betrayalRingRadiusRatio;
        public float BetrayalDiamondRatio => betrayalDiamondRatio;
        public float BetrayerChevronLengthRatio => betrayerChevronLengthRatio;
        public float BetrayerChevronHalfWidthRatio => betrayerChevronHalfWidthRatio;
        public float CheckFrameSizeRatio => checkFrameSizeRatio;
        public float CheckFrameThicknessRatio => checkFrameThicknessRatio;
        public float CornerTickLengthRatio => cornerTickLengthRatio;
        public float CornerTickThicknessRatio => cornerTickThicknessRatio;
        public float CornerTickClampStartRatio => cornerTickClampStartRatio;
        public float TintSizeRatio => tintSizeRatio;
        public float LastMoveFromOutlineThicknessRatio => lastMoveFromOutlineThicknessRatio;
        public int CircleSegments => circleSegments;

        public float AppearDuration => appearDuration;
        public float AppearStaggerPerSquare => appearStaggerPerSquare;
        public float CornerTickClampDuration => cornerTickClampDuration;
        public float CornerTickClampOvershoot => cornerTickClampOvershoot;
        public float CheckPulsePeriod => checkPulsePeriod;
        public float CheckPulseGlowAmount => checkPulseGlowAmount;
        public float CaptureBreathePeriod => captureBreathePeriod;
        public float CaptureBreatheGlowAmount => captureBreatheGlowAmount;
        public float CaptureTickRotationSpeed => captureTickRotationSpeed;
        public float BetrayerPulsePeriod => betrayerPulsePeriod;
        public float BetrayerPulseGlowAmount => betrayerPulseGlowAmount;
        public float BetrayerChevronRotationSpeed => betrayerChevronRotationSpeed;

        /// <summary>
        /// Returns the look for one marker state, so callers never need a switch of their own.
        /// </summary>
        public Look LookFor(SquareMarker marker)
        {
            switch (marker)
            {
                case SquareMarker.QuietMove: return quietMove;
                case SquareMarker.Capture: return capture;
                case SquareMarker.BetrayalTarget: return betrayalTarget;
                case SquareMarker.BetrayerAtLarge: return betrayerAtLarge;
                case SquareMarker.Check: return check;
                case SquareMarker.Selected: return selectedMarker;
                default: return default;
            }
        }

        /// <summary>
        /// Returns the look for one square tint.
        /// </summary>
        public Look LookFor(SquareTint tint)
        {
            switch (tint)
            {
                case SquareTint.Selected: return selectedTint;
                case SquareTint.Hover: return hoverTint;
                case SquareTint.LastMoveFrom: return lastMoveFromTint;
                case SquareTint.LastMoveTo: return lastMoveToTint;
                default: return default;
            }
        }
    }
}
