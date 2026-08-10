using NUnit.Framework;
using PrimeTween;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.View;

namespace ChessTheBetrayal.Tests.EditMode.View
{
    /// <summary>
    /// Tearing a piece down while it is still animating.
    ///
    /// Callers must leave nothing running before they destroy a piece's GameObject: Unity defers
    /// destruction to the end of the frame, so a tween that is still live can write to a Transform
    /// that is already gone and throw. That makes "did it actually stop everything" the whole
    /// contract, and it is worth asserting rather than assuming — the obvious way of writing it
    /// silently did not hold for anything inside a sequence, which is most of what this piece plays.
    /// </summary>
    [TestFixture]
    public class StopAllAnimationsTests
    {
        private GameObject _pieceObject;

        [SetUp]
        public void Setup()
        {
            _pieceObject = new GameObject("StopAllTestPiece");
            _pieceObject.transform.position = new Vector3(4f, 0.5f, 4f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_pieceObject != null) Object.DestroyImmediate(_pieceObject);
        }

        private PrimeTweenPieceAnimator Animator()
        {
            return new PrimeTweenPieceAnimator(_pieceObject.transform, null, () => ChessPieceType.Queen);
        }

        [Test]
        public void AStrikeCutShortLeavesNothingStillWriting()
        {
            // A capture is the longest thing a piece plays and the likeliest to be interrupted — a
            // takeback, a rebuild, a piece destroyed mid-stamp. Every beat of it lives inside a
            // sequence, which is exactly the shape that was being missed.
            PrimeTweenPieceAnimator animator = Animator();
            animator.PlayCaptureStamp(new Vector3(7f, 0.5f, 7f), new CaptureRunUp(new Vector3(6f, 0.5f, 6f), 2.83f));

            Assume.That(Tween.GetTweensCount(_pieceObject.transform), Is.GreaterThan(0),
                "The strike did not start, so there is nothing here to stop.");

            animator.StopAllAnimations();

            Assert.That(Tween.GetTweensCount(_pieceObject.transform), Is.Zero,
                "Something is still moving this piece after everything was supposed to stop.");
            Assert.That(Tween.GetTweensCount(animator), Is.Zero,
                "Something is still driving this piece's own values after everything was supposed to stop.");
        }

        [Test]
        public void ASwapCutShortLeavesNothingStillWriting()
        {
            // The one seen in play: a defection being taken back destroys the outgoing piece from a
            // callback chained onto that piece's own spin, so the teardown runs while the spin is
            // still live and holding a tween of its own.
            PrimeTweenPieceAnimator animator = Animator();
            animator.PlayTransitionOut(PieceTransitionStyle.Spin, onComplete: null);

            Assume.That(Tween.GetTweensCount(animator), Is.GreaterThan(0),
                "The spin did not start, so there is nothing here to stop.");

            animator.StopAllAnimations();

            Assert.That(Tween.GetTweensCount(animator), Is.Zero);
            Assert.That(Tween.GetTweensCount(_pieceObject.transform), Is.Zero);
        }

        [Test]
        public void APieceStoppedWhileDoingNothingIsFine()
        {
            // Teardown runs on pieces that were never animating at all, and it must not complain.
            PrimeTweenPieceAnimator animator = Animator();

            animator.StopAllAnimations();

            Assert.That(Tween.GetTweensCount(_pieceObject.transform), Is.Zero);
            Assert.That(Tween.GetTweensCount(animator), Is.Zero);
        }
    }
}
