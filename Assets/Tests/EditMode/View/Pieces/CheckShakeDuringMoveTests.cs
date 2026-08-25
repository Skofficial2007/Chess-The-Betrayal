using NUnit.Framework;
using PrimeTween;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.View.Pieces;

namespace ChessTheBetrayal.Tests.EditMode.View.Pieces
{
    /// <summary>
    /// A piece told to be somewhere else and told it is in check at the same moment.
    ///
    /// The two are built differently: a move interpolates toward a destination, while a shake
    /// writes an absolute pose built from wherever the piece was standing when it began. Left to
    /// share a transform, the shake outlasts any king-length move and gets the last word, and the
    /// piece finishes standing exactly where it set off from.
    ///
    /// It is not a rare interleaving. Taking back a king's escape from check hits it every time:
    /// the king is sent home and the position it lands on is in check, so the same piece is the one
    /// moving and the one being warned. What the player saw was a king that would not go back,
    /// with the check frame drawn on the empty square it should have returned to.
    ///
    /// Nothing here waits for real time. Tweens are driven straight to their end, and because a
    /// move is aimed at the transform while a shake is aimed at the animator, each can be finished
    /// on its own — so these pin down the outcome rather than whichever happened to run last.
    /// </summary>
    [TestFixture]
    public class CheckShakeDuringMoveTests
    {
        private GameObject _pieceObject;

        // One diagonal step, the move a king actually makes getting out of check.
        private static readonly Vector3 Origin = new Vector3(4f, 0.5f, 0f);
        private static readonly Vector3 Destination = new Vector3(3f, 0.5f, 1f);
        private const float DiagonalStepTiles = 1.414f;

        [SetUp]
        public void Setup()
        {
            _pieceObject = new GameObject("CheckShakeTestKing");
            _pieceObject.transform.position = Origin;
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

        /// <summary>
        /// Lands the move, then runs out everything the animator drives itself. The loop is for the
        /// wait and the rattle behind it, which only exist one at a time.
        /// </summary>
        private void RunEverythingOut(PrimeTweenPieceAnimator animator)
        {
            Tween.CompleteAll(_pieceObject.transform);

            for (int i = 0; i < 4 && Tween.GetTweensCount(animator) > 0; i++)
            {
                Tween.CompleteAll(animator);
            }
        }

        private void AssertStandsOn(Vector3 square, string whatWentWrong)
        {
            Assert.That(Vector3.Distance(_pieceObject.transform.position, square), Is.LessThan(0.001f),
                $"{whatWentWrong} Left at {_pieceObject.transform.position}, expected {square}.");
        }

        [Test]
        public void AKingWarnedWhileStillTravellingStillArrives()
        {
            // The one from the takeback: sent home and warned in the same breath.
            PrimeTweenPieceAnimator animator = Animator();

            animator.MoveTo(Destination, MoveStyle.Quiet, DiagonalStepTiles);
            animator.Shake();

            RunEverythingOut(animator);

            AssertStandsOn(Destination, "The check warning kept the king on the square it was leaving.");
        }

        [Test]
        public void AKingWarnedWhileStillTravellingIsStillAllowedToTravel()
        {
            // Arriving is not enough on its own: a warning that took the transform by ending the
            // glide outright would also land the king on the right square, having teleported it
            // there. The glide has to still be in the air afterwards, because that is the takeback
            // the player pressed the button to watch.
            PrimeTweenPieceAnimator animator = Animator();

            animator.MoveTo(Destination, MoveStyle.Quiet, DiagonalStepTiles);
            animator.Shake();

            Assert.That(Tween.GetTweensCount(_pieceObject.transform), Is.EqualTo(1),
                "The king was put on its square rather than left to walk there.");
        }

        [Test]
        public void AKingSentOnWhileAlreadyShakingStillArrives()
        {
            // The same collision the other way round, which a shake started a frame earlier gives:
            // the rattle is still writing an absolute pose while the move tries to leave it.
            PrimeTweenPieceAnimator animator = Animator();

            animator.Shake();
            animator.MoveTo(Destination, MoveStyle.Quiet, DiagonalStepTiles);

            RunEverythingOut(animator);

            AssertStandsOn(Destination, "The check warning held the king back from the square it was sent to.");
        }

        [Test]
        public void AKingSentOnMidShakeIsLeftUpright()
        {
            // A rattle leans the piece as well as sliding it, and only takes the lean out at the
            // very end. Cut one short and nothing else ever puts a rotation back — a move fixes
            // where the piece stands but not which way it is tipped — so the king would lean for
            // the rest of the game, a little further after every check it was interrupted out of.
            //
            // Nothing ticks a tween in here, so the lean a running rattle would have applied is set
            // by hand. What is being tested is the putting back, not the leaning.
            PrimeTweenPieceAnimator animator = Animator();
            Quaternion upright = _pieceObject.transform.localRotation;

            animator.Shake();
            _pieceObject.transform.localRotation = Quaternion.AngleAxis(7f, Vector3.forward) * upright;

            animator.MoveTo(Destination, MoveStyle.Quiet, DiagonalStepTiles);

            Assert.That(Quaternion.Angle(_pieceObject.transform.localRotation, upright), Is.LessThan(0.01f),
                "The king was left tipped over after its rattle was cut short by a move.");
        }

        [Test]
        public void AShakeOnAPieceThatIsNotGoingAnywhereStartsAtOnce()
        {
            // Tapping a piece with no legal move rattles it too, and that is the answer to a tap —
            // it has to be immediate. Waiting behind a move it is not making would make the board
            // feel unresponsive to the one press that most needs an answer.
            PrimeTweenPieceAnimator animator = Animator();

            animator.Shake();

            Assert.That(Tween.GetTweensCount(animator), Is.GreaterThan(0),
                "Nothing is rattling the piece, so the tap went unanswered.");

            _pieceObject.transform.position = Origin + new Vector3(0f, 0f, 0.03f);
            Tween.CompleteAll(animator);

            AssertStandsOn(Origin, "The rattle did not put the piece back where it started.");
        }

        [Test]
        public void APieceTornDownWhileWaitingToShakeLeavesNothingRunning()
        {
            // A shake now spends part of its life waiting rather than rattling, and a piece can be
            // destroyed during either. Unity defers destruction to the end of the frame, so a wait
            // left running would come back to a transform that is already gone.
            PrimeTweenPieceAnimator animator = Animator();

            animator.MoveTo(Destination, MoveStyle.Quiet, DiagonalStepTiles);
            animator.Shake();

            Assume.That(Tween.GetTweensCount(animator), Is.GreaterThan(0),
                "The shake never started waiting, so there is nothing here to tear down.");

            animator.StopAllAnimations();

            Assert.That(Tween.GetTweensCount(animator), Is.Zero,
                "Something is still driving this piece's own values after everything was supposed to stop.");
            Assert.That(Tween.GetTweensCount(_pieceObject.transform), Is.Zero,
                "Something is still moving this piece after everything was supposed to stop.");
        }
    }
}
