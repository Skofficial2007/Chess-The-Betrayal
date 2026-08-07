using System;
using UnityEngine;

namespace ChessTheBetrayal.View
{
    /// <summary>
    /// Every colour and dimension the board's square markers use, in one asset you can edit while
    /// the game runs. Nothing else in the project decides what a highlight looks like.
    ///
    /// Three rules shaped these defaults, and all three are worth keeping if you retune them:
    ///
    /// Meaning is carried by SHAPE first and colour second — a dot for a quiet move, a bracketed
    /// ring for a capture, a ringed diamond for a betrayal, a frame for check. Roughly one man in
    /// twelve cannot separate the green from the red, so a board that encodes "move" and "capture"
    /// as hue alone is unreadable to them. Every state here stays distinguishable in greyscale.
    /// That rule is why three different states can share red: check, capture and a betrayer at
    /// large all mean "this piece dies", and they are told apart by silhouette and motion. Check is
    /// a pulsing square frame, a capture is a still bracketed ring, a betrayer is a ring whose
    /// chevrons turn. They also cannot appear together — while a Retribution is pending the only
    /// legal moves target the betrayer's own square, and that square shows the betrayer.
    ///
    /// Anything marking a square that HAS A PIECE ON IT must put its meaning on the tile's edges and
    /// corners, never its middle. The pieces are two to three times taller than a square is wide, so
    /// from a tilted camera the piece hides the whole far half of anything drawn flat around it. The
    /// middle of a tile is only safe for states whose square is empty by definition, like the
    /// quiet-move dot.
    ///
    /// Glow is spent only where it means danger. Bloom in the scene's volume profile starts lifting
    /// at a threshold of 1.0, so a state only blooms once its colour times (1 + glow) clears that.
    /// Calm states are deliberately left below it; if everything glows, nothing reads as urgent.
    /// Watch the individual channels, not the swatch: violet at glow 1.4 takes its blue to 2.3x the
    /// threshold while its green stays under, which is why it reads on screen as magenta-pink rather
    /// than the purple the colour picker shows.
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

        [Tooltip("The square a Betrayer is currently standing on, while Retribution is still pending. Red rather than the betrayal violet, because hue here means intent, not mechanic: violet offers you a choice, red says kill this. Deeper and colder than the capture red so the two stay apart.")]
        [SerializeField] private Look betrayerAtLarge = new Look { Colour = new Color(1f, 0.13f, 0.22f, 0.92f), Glow = 1.8f };

        [Tooltip("The square of a king in check. Deliberately the loudest state on the board.")]
        [SerializeField] private Look check = new Look { Colour = new Color(1f, 0.22f, 0.16f, 0.9f), Glow = 2f };

        [Tooltip("Corner ticks on the square whose piece is currently picked up.")]
        [SerializeField] private Look selectedMarker = new Look { Colour = new Color(1f, 0.78f, 0.25f, 0.95f), Glow = 0.2f };

        [Header("Tints — the whole square, drawn under any marker")]
        [Tooltip("Wash behind the picked-up piece's own square.")]
        [SerializeField] private Look selectedTint = new Look { Colour = new Color(1f, 0.78f, 0.25f, 0.22f), Glow = 0f };

        [Tooltip("Wash under the pointer. Kept very faint — it follows the cursor, so anything stronger reads as noise.")]
        [SerializeField] private Look hoverTint = new Look { Colour = new Color(1f, 1f, 1f, 0.1f), Glow = 0f };

        [Tooltip("Thin outline on the square the last-moved piece left. Blue is the one hue nothing else here claims — amber is selection, red is a kill, violet is betrayal, green is a quiet move — so the last move can never be mistaken for something you can act on.")]
        [SerializeField] private Look lastMoveFromTint = new Look { Colour = new Color(0.35f, 0.65f, 1f, 0.55f), Glow = 0f };

        [Tooltip("Thicker, brighter outline on the square the last-moved piece landed on. Same blue as the origin, heavier weight — that difference alone tells you which way the piece travelled, with no arrow needed.")]
        [SerializeField] private Look lastMoveToTint = new Look { Colour = new Color(0.35f, 0.65f, 1f, 0.85f), Glow = 0f };

        [Header("Shape sizes — fractions of one square")]
        [Tooltip("Radius of the quiet-move dot. Kept large because a tall 3D piece standing on a neighbouring square can occlude most of a small one.")]
        [SerializeField, Range(0.05f, 0.5f)] private float dotRadiusRatio = 0.48f;

        [Tooltip("Outer radius of the capture ring. Budget for the ticks too — they straddle this edge, so the shape actually reaches this plus half a tick, and that total has to stay inside the corner brackets or the two cross each other.")]
        [SerializeField, Range(0.2f, 0.5f)] private float captureRingRadiusRatio = 0.43f;

        [Tooltip("Thickness of the capture ring, as a fraction of one square.")]
        [SerializeField, Range(0.02f, 0.2f)] private float captureRingThicknessRatio = 0.07f;

        [Tooltip("How far the capture ring's four cardinal ticks extend past it. The ring turns, and a circle looks identical at every angle — these are the only part of it that shows the motion.")]
        [SerializeField, Range(0.02f, 0.2f)] private float captureRingTickLengthRatio = 0.07f;

        [Tooltip("Thickness of a capture ring tick.")]
        [SerializeField, Range(0.01f, 0.08f)] private float captureRingTickThicknessRatio = 0.025f;

        [Tooltip("Footprint of the corner brackets shared by the capture, betrayal and betrayer markers, as a fraction of one square. Slightly under 1 keeps them off the tile edge.")]
        [SerializeField, Range(0.5f, 1f)] private float markerBracketSpanRatio = 0.97f;

        [Tooltip("How far each corner bracket runs along its edge, as a fraction of one square. These carry the state when a tall piece hides the ring, so do not shrink them much. Also sets the gap the last-move origin's edge bars leave at each corner, so the two marks interlock.")]
        [SerializeField, Range(0.05f, 0.4f)] private float markerBracketLengthRatio = 0.16f;

        [Tooltip("Thickness of a corner bracket. Deliberately heavier than the selection ticks so the two read apart at a glance.")]
        [SerializeField, Range(0.02f, 0.15f)] private float markerBracketThicknessRatio = 0.07f;

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
        [SerializeField, Range(1f, 3f)] private float cornerTickClampStartRatio = 1.45f;

        [Tooltip("Size of a square tint. Slightly under 1 leaves a hairline of board showing, which stops neighbouring tinted squares merging into one block.")]
        [SerializeField, Range(0.5f, 1f)] private float tintSizeRatio = 0.97f;

        [Tooltip("Border thickness of the last-move origin's outline, as a fraction of the tint footprint.")]
        [SerializeField, Range(0.02f, 0.2f)] private float lastMoveFromOutlineThicknessRatio = 0.05f;

        [Tooltip("Border thickness of the last-move destination's outline. Heavier than the origin's — the weight difference is what encodes the direction of travel.")]
        [SerializeField, Range(0.02f, 0.25f)] private float lastMoveToOutlineThicknessRatio = 0.11f;

        [Tooltip("Segments in the round shapes. 24 is smooth at any sane board size; lower it only if you are chasing vertices.")]
        [SerializeField, Range(8, 64)] private int circleSegments = 24;

        [Header("Motion")]
        [Tooltip("How long a marker takes to scale up when it appears.")]
        [SerializeField, Range(0.05f, 0.6f)] private float appearDuration = 0.18f;

        [Tooltip("Extra delay per square of distance from the selected piece, so the markers ripple outward instead of all snapping on together. 0 disables the ripple.")]
        [SerializeField, Range(0f, 0.05f)] private float appearStaggerPerSquare = 0.012f;

        [Tooltip("Seconds for a selection corner tick to clamp in from outside the square to its resting corner. Under about 0.2 the eye reads it as a snap rather than a movement and the travel is wasted.")]
        [SerializeField, Range(0.05f, 0.5f)] private float cornerTickClampDuration = 0.24f;

        [Tooltip("Overshoot on a corner tick's clamp-in. Kept gentler than the general marker appear — this is a grip settling into place, not a pop.")]
        [SerializeField, Range(0f, 3f)] private float cornerTickClampOvershoot = 1.15f;

        [Tooltip("Extra delay per corner on the clamp-in, so the four ticks close in sequence rather than in lockstep. 0 fires them all together.")]
        [SerializeField, Range(0f, 0.08f)] private float cornerTickClampStagger = 0.03f;

        [Tooltip("Seconds for one full pulse of the check frame's glow.")]
        [SerializeField, Range(0.2f, 3f)] private float checkPulsePeriod = 1.1f;

        [Tooltip("How much glow the check pulse adds at its peak, on top of the check state's own glow.")]
        [SerializeField, Range(0f, 3f)] private float checkPulseGlowAmount = 1.2f;

        [Tooltip("Seconds for one full breathe of the capture reticle's glow. Slower than the check pulse — a capture is an option, not a warning.")]
        [SerializeField, Range(0.5f, 5f)] private float captureBreathePeriod = 2.2f;

        [Tooltip("How much glow the capture breathe adds at its peak, on top of the capture state's own glow.")]
        [SerializeField, Range(0f, 2f)] private float captureBreatheGlowAmount = 0.35f;

        [Tooltip("Degrees per second the capture ring turns. Only the ring moves — its corner brackets are on their own layer precisely so they can stay nailed to the corners while it does.")]
        [SerializeField, Range(0f, 60f)] private float captureRingSpinSpeed = 18f;

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
        public Look SelectedTint => selectedTint;
        public Look HoverTint => hoverTint;
        public Look LastMoveFromTint => lastMoveFromTint;
        public Look LastMoveToTint => lastMoveToTint;

        public float DotRadiusRatio => dotRadiusRatio;
        public float CaptureRingRadiusRatio => captureRingRadiusRatio;
        public float CaptureRingThicknessRatio => captureRingThicknessRatio;
        public float CaptureRingTickLengthRatio => captureRingTickLengthRatio;
        public float CaptureRingTickThicknessRatio => captureRingTickThicknessRatio;
        public float MarkerBracketSpanRatio => markerBracketSpanRatio;
        public float MarkerBracketLengthRatio => markerBracketLengthRatio;
        public float MarkerBracketThicknessRatio => markerBracketThicknessRatio;
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
        public float LastMoveToOutlineThicknessRatio => lastMoveToOutlineThicknessRatio;
        public int CircleSegments => circleSegments;

        public float AppearDuration => appearDuration;
        public float AppearStaggerPerSquare => appearStaggerPerSquare;
        public float CornerTickClampDuration => cornerTickClampDuration;
        public float CornerTickClampOvershoot => cornerTickClampOvershoot;
        public float CornerTickClampStagger => cornerTickClampStagger;
        public float CheckPulsePeriod => checkPulsePeriod;
        public float CheckPulseGlowAmount => checkPulseGlowAmount;
        public float CaptureBreathePeriod => captureBreathePeriod;
        public float CaptureBreatheGlowAmount => captureBreatheGlowAmount;
        public float CaptureRingSpinSpeed => captureRingSpinSpeed;
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
