namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// How long a piece should spend crossing a given number of squares.
    ///
    /// Every board glide used to take the same fixed time whatever the distance, so a king stepping
    /// one square and a rook running the length of the board finished together — which meant the
    /// rook was moving seven times faster and read as a jump cut rather than a move. Long travel
    /// now gets proportionally more time, with a ceiling so a queen crossing the whole board still
    /// feels decisive rather than ponderous.
    ///
    /// A single square deliberately lands on exactly the duration short moves already had, so the
    /// feel of the moves that were never the problem is unchanged; the curve only ever adds time.
    ///
    /// Lives beside MoveVisualDurationEstimator, and for the same reason: the view plays these
    /// durations and the move pacing has to predict them, and those two live in assemblies that
    /// cannot reference each other. One definition here keeps them from drifting apart.
    /// </summary>
    public static class MoveTravelTiming
    {
        private const float OneSquareSeconds = 0.22f;
        private const float ExtraSecondsPerSquare = 0.035f;
        private const float MaxSeconds = 0.4f;

        /// <summary>
        /// The pace of a piece closing on something it intends to take. Brisker than an ordinary
        /// glide — a run-up is a charge, and it is also only the first half of a longer beat, so it
        /// cannot afford an unhurried stroll before the strike lands.
        /// </summary>
        public const float ChargeFactor = 0.7f;

        /// <summary>
        /// Seconds to travel across the given number of squares, counting a diagonal step as one.
        /// Anything shorter than a full square is treated as a single square.
        /// </summary>
        public static float SecondsForSquares(int squares)
        {
            if (squares < 1) squares = 1;

            float seconds = OneSquareSeconds + (squares - 1) * ExtraSecondsPerSquare;
            return seconds < MaxSeconds ? seconds : MaxSeconds;
        }

        /// <summary>
        /// The same curve at charge pace, for the approach leg of a capture.
        /// </summary>
        public static float ChargeSecondsForSquares(int squares)
        {
            return SecondsForSquares(squares) * ChargeFactor;
        }

        /// <summary>
        /// How many squares apart two tiles are, counting a diagonal step as one — the way a player
        /// counts a move, and the only measure under which "next to it" means the same thing along
        /// a rank as along a diagonal.
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
