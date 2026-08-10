using NUnit.Framework;
using PrimeTween;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.View;

namespace ChessTheBetrayal.Tests.EditMode.View
{
    /// <summary>
    /// A duration on its own says nothing about how fast a piece gets. The easing decides that: the
    /// same half-second is a smooth slide under one curve and a stutter under another, because what
    /// breaks a move up on screen is its quickest instant, not its average.
    ///
    /// So MoveTravelTiming carries a number describing how much quicker than average the board's
    /// easing runs, and paces every glide against it — which only holds while that number and the
    /// easing actually agree. They live in different assemblies and nothing else connects them, so
    /// this is the join.
    /// </summary>
    [TestFixture]
    public class BoardGlideEasingTests
    {
        [Test]
        public void ThePacingCurveDescribesTheEasingTheBoardActuallyGlidesOn()
        {
            // Changing one of these two without the other is the failure this exists to catch: the
            // durations would go on being tuned for a curve the board had stopped using, and every
            // long move would quietly get faster than anything measured it.
            Assert.That(PrimeTweenPieceAnimator.BoardGlideEase, Is.EqualTo(Ease.InOutSine),
                "The pacing curve's peak factor below is the one for a sine ease in and out. " +
                "Changing the easing means recomputing MoveTravelTiming.GlideEasePeakFactor to match.");

            // A sine ease in and out runs its middle at pi/2 times its average speed.
            Assert.That(MoveTravelTiming.GlideEasePeakFactor, Is.EqualTo(1.5707964f).Within(0.0001f));
        }

        [Test]
        public void AGlidesQuickestInstantIsFasterThanItsAverageButNotByMuch()
        {
            // The property that matters, stated without either constant: whatever easing is chosen,
            // a glide has to spend most of itself near its average pace. A curve that loiters at
            // each end and sprints through the middle covers the same ground in the same time and
            // still comes apart in the middle, which is exactly what a cubic ease was doing here.
            const float tiles = 7f;
            float averageTilesPerSecond = tiles / MoveTravelTiming.SecondsForTiles(tiles);

            Assert.That(MoveTravelTiming.PeakTilesPerSecond(tiles), Is.GreaterThan(averageTilesPerSecond));
            Assert.That(MoveTravelTiming.PeakTilesPerSecond(tiles), Is.LessThan(averageTilesPerSecond * 2f));
        }
    }
}
