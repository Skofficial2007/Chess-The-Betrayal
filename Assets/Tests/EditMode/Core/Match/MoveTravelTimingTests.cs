using NUnit.Framework;
using ChessTheBetrayal.Core.Match;

namespace ChessTheBetrayal.Tests.EditMode.Core.Match
{
    /// <summary>
    /// The curve that decides how long a piece spends crossing the board. The two properties worth
    /// protecting are that it never makes an existing move faster, and that it cannot run away with
    /// itself on a long one — a queen crossing seven squares still has to feel decisive.
    /// </summary>
    [TestFixture]
    public class MoveTravelTimingTests
    {
        // The duration every board glide used before distance was taken into account. A single
        // square must still land on it exactly, or the curve has quietly retuned moves that were
        // never the problem.
        private const float ShippedSingleSquareSeconds = 0.22f;

        [Test]
        public void ASingleSquareTakesExactlyAsLongAsItAlwaysHas()
        {
            Assert.That(MoveTravelTiming.SecondsForSquares(1), Is.EqualTo(ShippedSingleSquareSeconds).Within(0.0001f));
        }

        [Test]
        public void TheCurveOnlyEverAddsTime()
        {
            for (int squares = 1; squares <= 7; squares++)
            {
                Assert.That(MoveTravelTiming.SecondsForSquares(squares),
                    Is.GreaterThanOrEqualTo(ShippedSingleSquareSeconds),
                    $"Travelling {squares} squares must not undercut a single-square glide.");
            }
        }

        [Test]
        public void FurtherAlwaysTakesLongerUntilTheCeiling()
        {
            float previous = 0f;
            bool sawAnIncrease = false;

            for (int squares = 1; squares <= 7; squares++)
            {
                float seconds = MoveTravelTiming.SecondsForSquares(squares);
                Assert.That(seconds, Is.GreaterThanOrEqualTo(previous));
                if (seconds > previous) sawAnIncrease = true;
                previous = seconds;
            }

            Assert.That(sawAnIncrease, Is.True, "A flat curve would mean distance is still being ignored.");
        }

        [Test]
        public void CrossingTheWholeBoardStaysInsideTheCeiling()
        {
            // Seven squares is the longest move a chessboard allows. Without a cap the curve would
            // keep growing and a rook's slide would start to feel like waiting.
            Assert.That(MoveTravelTiming.SecondsForSquares(7), Is.LessThanOrEqualTo(0.4f));
        }

        [Test]
        public void AMoveShorterThanASquareIsPacedAsOne()
        {
            // A promotion swaps a piece where it stands, so nothing travels. It still needs a
            // duration rather than a zero-length tween.
            Assert.That(MoveTravelTiming.SecondsForSquares(0), Is.EqualTo(ShippedSingleSquareSeconds).Within(0.0001f));
            Assert.That(MoveTravelTiming.SecondsForSquares(-3), Is.EqualTo(ShippedSingleSquareSeconds).Within(0.0001f));
        }

        [Test]
        public void ChargingIsQuickerThanStrollingTheSameDistance()
        {
            for (int squares = 1; squares <= 7; squares++)
            {
                Assert.That(MoveTravelTiming.ChargeSecondsForSquares(squares),
                    Is.LessThan(MoveTravelTiming.SecondsForSquares(squares)),
                    $"A run-up across {squares} squares should outpace an ordinary glide.");
            }
        }

        [Test]
        public void AChargeStillTakesVisibleTime()
        {
            // The run-up is meant to be seen. Shrinking it to nothing would put the strike back
            // where it started — arriving out of nowhere.
            Assert.That(MoveTravelTiming.ChargeSecondsForSquares(1), Is.GreaterThan(0.1f));
        }

        [Test]
        public void ADiagonalStepCountsTheSameAsAStepAlongARank()
        {
            Assert.That(MoveTravelTiming.SquaresApart(0, 0, 3, 3), Is.EqualTo(3));
            Assert.That(MoveTravelTiming.SquaresApart(0, 0, 3, 0), Is.EqualTo(3));
            Assert.That(MoveTravelTiming.SquaresApart(0, 0, 0, 3), Is.EqualTo(3));
        }

        [Test]
        public void DistanceReadsTheSameInEitherDirection()
        {
            Assert.That(MoveTravelTiming.SquaresApart(2, 6, 5, 1),
                Is.EqualTo(MoveTravelTiming.SquaresApart(5, 1, 2, 6)));
        }

        [Test]
        public void AKnightsMoveIsTwoSquares()
        {
            Assert.That(MoveTravelTiming.SquaresApart(1, 0, 2, 2), Is.EqualTo(2));
        }

        [Test]
        public void APieceThatDidNotMoveIsZeroSquaresAway()
        {
            Assert.That(MoveTravelTiming.SquaresApart(4, 4, 4, 4), Is.EqualTo(0));
        }
    }
}
