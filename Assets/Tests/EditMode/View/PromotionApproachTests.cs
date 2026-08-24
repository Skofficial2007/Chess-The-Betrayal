using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.View;

namespace ChessTheBetrayal.Tests.EditMode.View
{
    /// <summary>
    /// The walk a promoting pawn takes onto the last rank before it changes into anything.
    ///
    /// Two callers with opposite needs share it. A human's pawn has already walked there while the
    /// promotion prompt was open, so it must report back at once or the player waits through a
    /// second walk they already watched. The AI is never prompted, so its pawn is still a rank
    /// short and the morph has to wait. Both hinge on the same question — is it there yet — which
    /// is why that answer is worth pinning rather than trusting.
    /// </summary>
    [TestFixture]
    public class PromotionApproachTests
    {
        private GameObject _pieceObject;

        [SetUp]
        public void Setup()
        {
            _pieceObject = new GameObject("PromotionApproachTestPawn");
        }

        [TearDown]
        public void TearDown()
        {
            if (_pieceObject != null) Object.DestroyImmediate(_pieceObject);
        }

        private PrimeTweenPieceAnimator AnimatorAt(Vector3 position)
        {
            _pieceObject.transform.position = position;
            return new PrimeTweenPieceAnimator(_pieceObject.transform, null, () => ChessPieceType.Pawn);
        }

        [Test]
        public void APawnAlreadyStandingOnTheSquareReportsWithoutWaiting()
        {
            // The human's pawn. Anything other than an immediate answer here adds a walk to a
            // promotion that was already on its square before the player finished choosing.
            var square = new Vector3(3f, 0.5f, 7f);
            PrimeTweenPieceAnimator animator = AnimatorAt(square);

            bool arrived = false;
            animator.PlayPromotionApproach(square, tilesTravelled: 1f, onArrived: () => arrived = true);

            Assert.That(arrived, Is.True);
        }

        [Test]
        public void APawnStillARankShortDoesNotReportUntilItHasTravelled()
        {
            // The AI's pawn. Reporting straight away would put the morph back where it was, with
            // the pawn dissolving on one square and its replacement appearing on another.
            PrimeTweenPieceAnimator animator = AnimatorAt(new Vector3(3f, 0.5f, 5.5f));

            bool arrived = false;
            animator.PlayPromotionApproach(new Vector3(3f, 0.5f, 7f), tilesTravelled: 1f, onArrived: () => arrived = true);

            Assert.That(arrived, Is.False);
        }

        [Test]
        public void ANonFiniteTargetStillReportsSoThePromotionCannotHang()
        {
            // The morph only ever runs from this callback. A refused walk that stayed silent would
            // leave a pawn on the board that never becomes anything.
            PrimeTweenPieceAnimator animator = AnimatorAt(Vector3.zero);

            bool arrived = false;
            animator.PlayPromotionApproach(new Vector3(float.NaN, 0f, 0f), tilesTravelled: 1f, onArrived: () => arrived = true);

            Assert.That(arrived, Is.True);
        }
    }
}
