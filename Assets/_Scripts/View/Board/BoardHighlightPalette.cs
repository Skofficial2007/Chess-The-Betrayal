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

        [Tooltip("The square of a king in check. Deliberately the loudest state on the board.")]
        [SerializeField] private Look check = new Look { Colour = new Color(1f, 0.22f, 0.16f, 0.9f), Glow = 2f };

        [Tooltip("Corner ticks on the square whose piece is currently picked up.")]
        [SerializeField] private Look selectedMarker = new Look { Colour = new Color(1f, 0.78f, 0.25f, 0.95f), Glow = 0.2f };

        [Header("Tints — the whole square, drawn under any marker")]
        [Tooltip("Wash behind the picked-up piece's own square.")]
        [SerializeField] private Look selectedTint = new Look { Colour = new Color(1f, 0.78f, 0.25f, 0.22f), Glow = 0f };

        [Tooltip("Wash under the pointer. Kept very faint — it follows the cursor, so anything stronger reads as noise.")]
        [SerializeField] private Look hoverTint = new Look { Colour = new Color(1f, 1f, 1f, 0.1f), Glow = 0f };

        [Tooltip("The two squares of the move just played. Easy to miss how much this helps until it is gone — it answers \"what just happened?\" without the player having to watch the animation.")]
        [SerializeField] private Look lastMoveTint = new Look { Colour = new Color(1f, 0.85f, 0.45f, 0.14f), Glow = 0f };

        [Header("Shape sizes — fractions of one square")]
        [Tooltip("Radius of the quiet-move dot. Kept large because a tall 3D piece standing on a neighbouring square can occlude most of a small one.")]
        [SerializeField, Range(0.05f, 0.5f)] private float dotRadiusRatio = 0.48f;

        [Tooltip("Outer radius of the capture ring. Large enough to frame a piece rather than sit under it.")]
        [SerializeField, Range(0.2f, 0.5f)] private float captureRingRadiusRatio = 0.46f;

        [Tooltip("Thickness of the capture ring, as a fraction of one square.")]
        [SerializeField, Range(0.02f, 0.2f)] private float captureRingThicknessRatio = 0.07f;

        [Tooltip("Outer radius of the betrayal marker's ring.")]
        [SerializeField, Range(0.2f, 0.5f)] private float betrayalRingRadiusRatio = 0.46f;

        [Tooltip("Half-width of the diamond inside the betrayal marker's ring.")]
        [SerializeField, Range(0.05f, 0.4f)] private float betrayalDiamondRatio = 0.16f;

        [Tooltip("Outer size of the check frame. Under 1 insets it, so the king reads as sitting inside the frame.")]
        [SerializeField, Range(0.5f, 1f)] private float checkFrameSizeRatio = 0.92f;

        [Tooltip("Border thickness of the check frame, as a fraction of its outer size.")]
        [SerializeField, Range(0.02f, 0.3f)] private float checkFrameThicknessRatio = 0.09f;

        [Tooltip("How far each selection corner tick runs along its edge, as a fraction of one square.")]
        [SerializeField, Range(0.05f, 0.5f)] private float cornerTickLengthRatio = 0.22f;

        [Tooltip("Thickness of a selection corner tick.")]
        [SerializeField, Range(0.02f, 0.15f)] private float cornerTickThicknessRatio = 0.055f;

        [Tooltip("Size of a square tint. Slightly under 1 leaves a hairline of board showing, which stops neighbouring tinted squares merging into one block.")]
        [SerializeField, Range(0.5f, 1f)] private float tintSizeRatio = 0.97f;

        [Tooltip("Segments in the round shapes. 24 is smooth at any sane board size; lower it only if you are chasing vertices.")]
        [SerializeField, Range(8, 64)] private int circleSegments = 24;

        [Header("Motion")]
        [Tooltip("How long a marker takes to scale up when it appears.")]
        [SerializeField, Range(0.05f, 0.6f)] private float appearDuration = 0.18f;

        [Tooltip("Extra delay per square of distance from the selected piece, so the markers ripple outward instead of all snapping on together. 0 disables the ripple.")]
        [SerializeField, Range(0f, 0.05f)] private float appearStaggerPerSquare = 0.012f;

        [Tooltip("Seconds for one full pulse of the check frame's glow.")]
        [SerializeField, Range(0.2f, 3f)] private float checkPulsePeriod = 1.1f;

        [Tooltip("How much glow the check pulse adds at its peak, on top of the check state's own glow.")]
        [SerializeField, Range(0f, 3f)] private float checkPulseGlowAmount = 1.2f;

        public Look QuietMove => quietMove;
        public Look Capture => capture;
        public Look BetrayalTarget => betrayalTarget;
        public Look Check => check;
        public Look SelectedMarker => selectedMarker;
        public Look SelectedTint => selectedTint;
        public Look HoverTint => hoverTint;
        public Look LastMoveTint => lastMoveTint;

        public float DotRadiusRatio => dotRadiusRatio;
        public float CaptureRingRadiusRatio => captureRingRadiusRatio;
        public float CaptureRingThicknessRatio => captureRingThicknessRatio;
        public float BetrayalRingRadiusRatio => betrayalRingRadiusRatio;
        public float BetrayalDiamondRatio => betrayalDiamondRatio;
        public float CheckFrameSizeRatio => checkFrameSizeRatio;
        public float CheckFrameThicknessRatio => checkFrameThicknessRatio;
        public float CornerTickLengthRatio => cornerTickLengthRatio;
        public float CornerTickThicknessRatio => cornerTickThicknessRatio;
        public float TintSizeRatio => tintSizeRatio;
        public int CircleSegments => circleSegments;

        public float AppearDuration => appearDuration;
        public float AppearStaggerPerSquare => appearStaggerPerSquare;
        public float CheckPulsePeriod => checkPulsePeriod;
        public float CheckPulseGlowAmount => checkPulseGlowAmount;

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
                case SquareTint.LastMove: return lastMoveTint;
                default: return default;
            }
        }
    }
}
