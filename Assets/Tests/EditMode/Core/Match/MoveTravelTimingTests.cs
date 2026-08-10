using NUnit.Framework;
using ChessTheBetrayal.Core.Match;

namespace ChessTheBetrayal.Tests.EditMode.Core.Match
{
    /// <summary>
    /// The curve that decides how long a piece spends crossing the board.
    ///
    /// This fixture used to assert only in seconds, which is the wrong quantity: it happily passed
    /// while a rook's glide ran at nearly five times the speed of a king's step, because both
    /// durations sat inside the same narrow band. What a move needs is a bound on how fast it gets,
    /// so that is what most of these assert, and the one below that measured a duration ceiling is
    /// gone — capping the time was what forced the speed up in the first place.
    /// </summary>
    [TestFixture]
    public class MoveTravelTimingTests
    {
        // The duration every board glide used before distance was taken into account. A single
        // tile must still land on it exactly, or the curve has quietly retuned moves that were
        // never the problem.
        private const float ShippedSingleTileSeconds = 0.22f;

        /// <summary>
        /// The frame rate the board is animated against (see DisplayFrameRate). Speeds only mean
        /// something once they are expressed as ground covered between two drawn frames.
        /// </summary>
        private const float FramesPerSecond = 60f;

        /// <summary>
        /// How far a piece may jump between two frames before it stops reading as a moving thing.
        /// Anchored to the moves that already read well: a single-tile glide comes in around a
        /// tenth of a tile per frame and the pounce of a capture around a fifth, so a whole
        /// board-length is allowed roughly double the busiest of those and no more.
        /// </summary>
        private const float MaxTilesPerFrame = 0.45f;

        /// <summary>The longest move a chessboard allows: a queen's seven diagonal steps.</summary>
        private const float LongestMoveTiles = 9.8995f;

        [Test]
        public void ASingleTileTakesExactlyAsLongAsItAlwaysHas()
        {
            Assert.That(MoveTravelTiming.SecondsForTiles(1f), Is.EqualTo(ShippedSingleTileSeconds).Within(0.0001f));
        }

        [Test]
        public void TheCurveOnlyEverAddsTime()
        {
            for (float tiles = 1f; tiles <= LongestMoveTiles; tiles += 0.25f)
            {
                Assert.That(MoveTravelTiming.SecondsForTiles(tiles),
                    Is.GreaterThanOrEqualTo(ShippedSingleTileSeconds),
                    $"Travelling {tiles} tiles must not undercut a single-tile glide.");
            }
        }

        [Test]
        public void FurtherAlwaysTakesLonger()
        {
            float previous = 0f;

            for (float tiles = 1f; tiles <= LongestMoveTiles; tiles += 0.25f)
            {
                float seconds = MoveTravelTiming.SecondsForTiles(tiles);
                Assert.That(seconds, Is.GreaterThan(previous), $"{tiles} tiles did not take longer than the distance before it.");
                previous = seconds;
            }
        }

        [Test]
        public void NoMoveOnTheBoardOutrunsTheFramesDrawingIt()
        {
            // The defect this fixture exists for. Every glide used to be squeezed into at most 0.4
            // seconds however far it went, so a queen taking the long diagonal covered nearly ten
            // tiles in that time and jumped more than a tile between frames — which is not a fast
            // move, it is a piece that disappears and reappears.
            for (float tiles = 1f; tiles <= LongestMoveTiles; tiles += 0.25f)
            {
                float tilesPerFrame = MoveTravelTiming.PeakTilesPerSecond(tiles) / FramesPerSecond;

                Assert.That(tilesPerFrame, Is.LessThanOrEqualTo(MaxTilesPerFrame),
                    $"A {tiles}-tile glide jumps {tilesPerFrame:F3} tiles between frames at its quickest.");
            }
        }

        [Test]
        public void TheLongestMoveOnTheBoardIsTheOneToBeatAndItPasses()
        {
            // Named on its own because the sweep above steps past it: a queen's seven diagonal
            // steps is the worst case that can actually occur, and it is the one that failed.
            float tilesPerFrame = MoveTravelTiming.PeakTilesPerSecond(LongestMoveTiles) / FramesPerSecond;

            Assert.That(tilesPerFrame, Is.LessThanOrEqualTo(MaxTilesPerFrame));
        }

        [Test]
        public void AMoveShorterThanATileIsPacedAsOne()
        {
            // A promotion swaps a piece where it stands, so nothing travels. It still needs a
            // duration rather than a zero-length tween.
            Assert.That(MoveTravelTiming.SecondsForTiles(0f), Is.EqualTo(ShippedSingleTileSeconds).Within(0.0001f));
            Assert.That(MoveTravelTiming.SecondsForTiles(-3f), Is.EqualTo(ShippedSingleTileSeconds).Within(0.0001f));
            Assert.That(MoveTravelTiming.PeakTilesPerSecond(0f),
                Is.EqualTo(MoveTravelTiming.PeakTilesPerSecond(1f)).Within(0.0001f));
        }

        [Test]
        public void ADiagonalStepIsLongerGroundThanAStepAlongARank()
        {
            // The bug this replaced a square count to fix: both of these are "three squares" to a
            // player, but the diagonal is half again as much floor, and pacing them identically
            // made every diagonal the faster move.
            float straight = MoveTravelTiming.TilesApart(0, 0, 3, 0);
            float diagonal = MoveTravelTiming.TilesApart(0, 0, 3, 3);

            Assert.That(straight, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(diagonal, Is.EqualTo(4.2426f).Within(0.001f));
            Assert.That(MoveTravelTiming.SecondsForTiles(diagonal),
                Is.GreaterThan(MoveTravelTiming.SecondsForTiles(straight)),
                "A diagonal covers more ground, so it must be given more time to cover it.");
        }

        [Test]
        public void GroundReadsTheSameInEitherDirection()
        {
            Assert.That(MoveTravelTiming.TilesApart(2, 6, 5, 1),
                Is.EqualTo(MoveTravelTiming.TilesApart(5, 1, 2, 6)).Within(0.0001f));
        }

        [Test]
        public void ADiagonalStepCountsTheSameAsAStepAlongARankWhenCountingSquares()
        {
            // Squares are still the right measure for "are these two next to each other", which is
            // what decides whether a capture gets walked into.
            Assert.That(MoveTravelTiming.SquaresApart(0, 0, 3, 3), Is.EqualTo(3));
            Assert.That(MoveTravelTiming.SquaresApart(0, 0, 3, 0), Is.EqualTo(3));
            Assert.That(MoveTravelTiming.SquaresApart(0, 0, 0, 3), Is.EqualTo(3));
        }

        [Test]
        public void AKnightsMoveIsTwoSquares()
        {
            Assert.That(MoveTravelTiming.SquaresApart(1, 0, 2, 2), Is.EqualTo(2));
        }

        [Test]
        public void APieceThatDidNotMoveIsNoGroundAway()
        {
            Assert.That(MoveTravelTiming.SquaresApart(4, 4, 4, 4), Is.EqualTo(0));
            Assert.That(MoveTravelTiming.TilesApart(4, 4, 4, 4), Is.EqualTo(0f).Within(0.0001f));
        }
    }
}
