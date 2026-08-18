using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.UI.SafeArea;

namespace ChessTheBetrayal.Tests.EditMode.UI
{
    /// <summary>
    /// Pins the safe-area conversion against the inputs that used to break it. The whole calculation
    /// divides by the screen dimensions, and floating-point division by zero does not throw — it
    /// yields an infinity, or a NaN when the numerator is zero as well. Either one written into a
    /// RectTransform propagates through every child's layout and is reported much later, as corrupted
    /// bounds, from a call stack that names nothing useful. So the contract worth pinning is not what
    /// the maths produces on good input; it is that bad input produces no answer at all.
    /// </summary>
    [TestFixture]
    public class SafeAreaFitterTests
    {
        [Test]
        public void AFullScreenSafeArea_FillsTheParentExactly()
        {
            bool computed = SafeAreaFitter.TryComputeNormalizedSafeArea(
                new Rect(0f, 0f, 1080f, 1920f), 1080, 1920, out Vector2 min, out Vector2 max);

            Assert.That(computed, Is.True);
            Assert.That(min, Is.EqualTo(Vector2.zero));
            Assert.That(max, Is.EqualTo(Vector2.one));
        }

        [Test]
        public void AnInsetSafeArea_BecomesTheMatchingFractionOfTheScreen()
        {
            // A notch taking the top 10% and a gesture bar taking the bottom 5% of a 1000x2000
            // screen: the usable band runs from y=100 to y=1900.
            bool computed = SafeAreaFitter.TryComputeNormalizedSafeArea(
                new Rect(0f, 100f, 1000f, 1800f), 1000, 2000, out Vector2 min, out Vector2 max);

            Assert.That(computed, Is.True);
            Assert.That(min.x, Is.EqualTo(0f));
            Assert.That(min.y, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(max.x, Is.EqualTo(1f));
            Assert.That(max.y, Is.EqualTo(0.95f).Within(1e-6f));
        }

        [Test]
        public void AZeroWidthScreen_IsRefusedRatherThanDividedBy()
        {
            bool computed = SafeAreaFitter.TryComputeNormalizedSafeArea(
                new Rect(0f, 0f, 1080f, 1920f), 0, 1920, out Vector2 min, out Vector2 max);

            Assert.That(computed, Is.False,
                "A screen with no width has no fraction of itself to express — the only honest " +
                "answer is to decline, not to substitute a number.");
            AssertUsable(min, max);
        }

        [Test]
        public void AZeroHeightScreen_IsRefusedRatherThanDividedBy()
        {
            bool computed = SafeAreaFitter.TryComputeNormalizedSafeArea(
                new Rect(0f, 0f, 1080f, 1920f), 1080, 0, out Vector2 min, out Vector2 max);

            Assert.That(computed, Is.False);
            AssertUsable(min, max);
        }

        [Test]
        public void AZeroSizedScreenAndSafeArea_DoesNotProduceNaN()
        {
            // The exact combination the editor reports for a view that has not been laid out yet,
            // and the one that produced the original corrupted-bounds warnings: zero divided by zero
            // is a NaN rather than an infinity, so it passes any check that only looks for infinity.
            bool computed = SafeAreaFitter.TryComputeNormalizedSafeArea(
                new Rect(0f, 0f, 0f, 0f), 0, 0, out Vector2 min, out Vector2 max);

            Assert.That(computed, Is.False);
            AssertUsable(min, max);
        }

        [Test]
        public void ANegativeScreenSize_IsRefused()
        {
            bool computed = SafeAreaFitter.TryComputeNormalizedSafeArea(
                new Rect(0f, 0f, 1080f, 1920f), -1080, -1920, out Vector2 min, out Vector2 max);

            Assert.That(computed, Is.False);
            AssertUsable(min, max);
        }

        [Test]
        public void ACollapsedSafeArea_IsRefusedEvenOnAValidScreen()
        {
            // A safe area with no height would pin the rect to a line. That is a degenerate layout
            // rather than an inset one, and it is not what an empty reading from the OS means.
            bool computed = SafeAreaFitter.TryComputeNormalizedSafeArea(
                new Rect(0f, 960f, 1080f, 0f), 1080, 1920, out Vector2 min, out Vector2 max);

            Assert.That(computed, Is.False);
            AssertUsable(min, max);
        }

        [Test]
        public void AnAlreadyInfiniteSafeArea_DoesNotSurviveTheDivision()
        {
            // An infinity or NaN arriving in the input passes through the division unchanged, so the
            // dimension guards alone would not catch it. This is the case the final validation exists
            // for, and the reason it is a separate check rather than a comment saying it cannot happen.
            bool computed = SafeAreaFitter.TryComputeNormalizedSafeArea(
                new Rect(float.PositiveInfinity, 0f, 1080f, 1920f), 1080, 1920,
                out Vector2 min, out Vector2 max);

            Assert.That(computed, Is.False);
            AssertUsable(min, max);
        }

        /// <summary>A refused computation must still hand back anchors that are safe to assign —
        /// the caller skips the write, but nothing downstream should ever see a poisoned value even
        /// if a future caller forgets to check the return.</summary>
        private static void AssertUsable(Vector2 min, Vector2 max)
        {
            foreach (float component in new[] { min.x, min.y, max.x, max.y })
            {
                Assert.That(float.IsNaN(component), Is.False, "A refused computation produced a NaN.");
                Assert.That(float.IsInfinity(component), Is.False, "A refused computation produced an infinity.");
            }
        }
    }
}
