using NUnit.Framework;
using PrimeTween;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.View.Pieces;

namespace ChessTheBetrayal.Tests.EditMode.View.Pieces
{
    /// <summary>
    /// Picking a piece up while it is rattling.
    ///
    /// A king is warned of check on the turn its own side has to answer, so the rattle plays while
    /// input is open, and the most natural thing a player can do — reach for the king — lands
    /// inside it. Picking a piece up also records where it was standing, so it can be set back down
    /// there when the player lets go. Recorded mid-rattle that is not the piece's square, and the
    /// piece is put down somewhere it never stood — for good, and a little further along after
    /// every check answered the same way.
    ///
    /// A control with no rattle runs the same journey, so the numbers here are read against what
    /// picking a piece up is supposed to cost rather than against zero on faith.
    ///
    /// Nothing waits for real time; the tweens are driven straight to their end. Note the bare
    /// <see cref="Tween.CompleteAll()"/>: the lift is a Sequence built with no target, so
    /// completing "everything on the transform" cannot reach that sequence's root, refuses to touch
    /// the children it owns, and logs an error rather than doing anything visible. Measured that
    /// way the piece appears never to leave the board at all, which says nothing about the piece
    /// and everything about how it was measured.
    /// </summary>
    [TestFixture]
    public class LiftDuringCheckShakeTests
    {
        private GameObject _pieceObject;

        private static readonly Vector3 Square = new Vector3(4f, 0.5f, 0f);

        // Where a rattle in progress holds the piece — off to one side and slightly up, well inside
        // the peak offsets the shake is built from.
        private static readonly Vector3 RattleOffset = new Vector3(0.05f, 0.03f, 0f);

        private const float OnItsSquare = 0.001f;

        [SetUp]
        public void Setup()
        {
            _pieceObject = new GameObject("LiftDuringShakeTestKing");
            _pieceObject.transform.position = Square;
        }

        [TearDown]
        public void TearDown()
        {
            Tween.StopAll();
            if (_pieceObject != null) Object.DestroyImmediate(_pieceObject);
        }

        private PrimeTweenPieceAnimator Animator()
        {
            return new PrimeTweenPieceAnimator(_pieceObject.transform, null, () => ChessPieceType.King);
        }

        /// <summary>Runs every animation out, whoever owns it — see the note on the fixture.</summary>
        private static void RunEverythingOut()
        {
            for (int i = 0; i < 6; i++)
            {
                Tween.CompleteAll();
            }
        }

        private void PickUpAndPutDown(PrimeTweenPieceAnimator animator)
        {
            animator.LiftSelect();
            RunEverythingOut();

            animator.LowerDeselect();
            RunEverythingOut();
        }

        private float DistanceFromSquare() => Vector3.Distance(_pieceObject.transform.position, Square);

        [Test]
        public void PickingAPieceUpAndPuttingItDownLeavesItWhereItWas()
        {
            // The control. Everything below is only meaningful if this is exactly zero.
            PrimeTweenPieceAnimator animator = Animator();

            PickUpAndPutDown(animator);

            Assert.That(DistanceFromSquare(), Is.LessThan(OnItsSquare),
                $"Picking a piece up and putting it down moved it: {_pieceObject.transform.position}.");
        }

        [Test]
        public void AKingPickedUpMidRattleIsStillPutBackOnItsSquare()
        {
            PrimeTweenPieceAnimator animator = Animator();

            animator.Shake();
            _pieceObject.transform.position = Square + RattleOffset;

            PickUpAndPutDown(animator);

            Assert.That(DistanceFromSquare(), Is.LessThan(OnItsSquare),
                $"The king was set down at {_pieceObject.transform.position} instead of on {Square}.");
        }

        [Test]
        public void AKingPickedUpThroughSeveralChecksDoesNotWalkOffItsSquare()
        {
            // Each pick-up reads the piece's place afresh, so an error the last one left behind is
            // taken for the truth by the next. Three checks answered by reaching for the king is an
            // ordinary sequence, not a contrived one, and the drift adds up once per check.
            PrimeTweenPieceAnimator animator = Animator();

            for (int i = 0; i < 3; i++)
            {
                animator.Shake();
                _pieceObject.transform.position += RattleOffset;

                PickUpAndPutDown(animator);
            }

            Assert.That(DistanceFromSquare(), Is.LessThan(OnItsSquare),
                $"After three checks the king stands at {_pieceObject.transform.position}, not on {Square}.");
        }
    }
}
