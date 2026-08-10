using System;

namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// How long a piece should spend crossing a given distance.
    ///
    /// The number that decides whether a move reads at all is not its duration, it is the speed that
    /// duration implies. Past roughly half a tile per rendered frame the eye stops following the
    /// piece and starts seeing a row of stills instead, and the move looks like a stutter rather
    /// than a slide. So the curve is tuned against speed, and <see cref="PeakTilesPerSecond"/>
    /// exists so a test can hold it there.
    ///
    /// Distance is measured in tile widths, not in squares. Counting a diagonal step as one square
    /// is how a player describes a move, but a diagonal step is 1.414 tiles of actual ground, so
    /// pacing on squares quietly made every diagonal 41% faster than the straight move it was
    /// supposed to match — worst of all for a queen, which has the longest diagonal on the board.
    ///
    /// A single tile deliberately lands on exactly the duration short moves have always had, so the
    /// feel of the moves that were never the problem is unchanged; the curve only ever adds time.
    ///
    /// Lives beside MoveVisualDurationEstimator, and for the same reason: the view plays these
    /// durations and the move pacing has to predict them, and those two live in assemblies that
    /// cannot reference each other. One definition here keeps them from drifting apart.
    /// </summary>
    public static class MoveTravelTiming
    {
        private const float OneTileSeconds = 0.22f;
        private const float ExtraSecondsPerTile = 0.045f;

        /// <summary>
        /// How much faster a glide's quickest instant is than its average, for the easing the board
        /// actually glides on — see PrimeTweenPieceAnimator.BoardGlideEase. A sine ease in and out
        /// peaks at pi/2 times its average. A cubic one peaks at three times, which is why the same
        /// durations used to break up on screen: the middle of every long move was running at
        /// triple the speed the duration suggested.
        ///
        /// This is the bridge between a duration and the speed it implies, which is why it lives
        /// with the durations rather than with the easing itself. Anything that changes
        /// BoardGlideEase has to change this to match.
        /// </summary>
        public const float GlideEasePeakFactor = 1.5707964f;

        /// <summary>
        /// Seconds to travel the given distance in tile widths. Anything shorter than a full tile
        /// is paced as one, so a piece that swaps in place still gets a real duration rather than a
        /// zero-length tween.
        /// </summary>
        public static float SecondsForTiles(float tiles)
        {
            if (tiles < 1f) tiles = 1f;

            return OneTileSeconds + (tiles - 1f) * ExtraSecondsPerTile;
        }

        /// <summary>
        /// The fastest the piece is ever moving on that glide, in tile widths per second. Divide by
        /// the frame rate to get how far it jumps between two drawn frames, which is the figure that
        /// actually decides whether the move is watchable.
        /// </summary>
        public static float PeakTilesPerSecond(float tiles)
        {
            if (tiles < 1f) tiles = 1f;

            return tiles * GlideEasePeakFactor / SecondsForTiles(tiles);
        }

        /// <summary>
        /// The ground between two squares in tile widths — the distance the piece physically
        /// covers, so a diagonal is correctly longer than the straight move of the same square
        /// count.
        /// </summary>
        public static float TilesApart(int fromX, int fromY, int toX, int toY)
        {
            float dx = toX - fromX;
            float dy = toY - fromY;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// How many squares apart two tiles are, counting a diagonal step as one — the way a player
        /// counts a move, and the only measure under which "next to it" means the same thing along
        /// a rank as along a diagonal. This is the one to reach for when deciding whether two pieces
        /// are neighbours; reach for <see cref="TilesApart"/> when pacing the ground between them.
        /// </summary>
        public static int SquaresApart(int fromX, int fromY, int toX, int toY)
        {
            int dx = toX - fromX;
            int dy = toY - fromY;
            if (dx < 0) dx = -dx;
            if (dy < 0) dy = -dy;
            return dx > dy ? dx : dy;
        }
    }
}
