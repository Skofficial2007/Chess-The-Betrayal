using System;
using System.Collections.Generic;
using NUnit.Framework;
using PrimeTween;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.View.Pieces;

namespace ChessTheBetrayal.Tests.EditMode.View.Pieces
{
    /// <summary>
    /// Only one animation moves a piece at a time.
    ///
    /// Every animation that travels sets the piece's position outright rather than adding to it, so
    /// two running together overwrite each other every frame and the piece finishes wherever the one
    /// that lasted longest left it. That has cost real bugs twice — a king that would not go back to
    /// the square a takeback sent it to, and a king put down a little beside its own square after
    /// being picked up mid-warning — and both were one animation quietly running under another.
    ///
    /// The rule is checked by asking the piece who is moving it, rather than by watching it move.
    /// Which animation is allowed to write is a decision, already made by the time either of them
    /// has drawn a frame, so there is nothing here to drive and nothing to wait for. Watching
    /// instead would mean advancing real animations frame by frame, and the ways of doing that from
    /// a test all lie: stopping everything cannot reach a tween inside a sequence, completing
    /// everything aimed at the piece cannot reach a sequence that was built without a target, and a
    /// count of what is running does not drop when the thing it was running on is destroyed.
    /// </summary>
    [TestFixture]
    public class PositionOwnershipTests
    {
        private GameObject _pieceObject;

        private static readonly Vector3 Square = new Vector3(4f, 0.5f, 0f);
        private static readonly Vector3 Elsewhere = new Vector3(3f, 0.5f, 1f);
        private static readonly Vector3 Graveyard = new Vector3(9f, 0.5f, 9f);

        /// <summary>Where a lift would have left the piece. Nothing ticks here, so it is put in by hand.</summary>
        private static readonly Vector3 Raised = new Vector3(0f, 0.3f, 0f);

        [SetUp]
        public void Setup()
        {
            _pieceObject = new GameObject("PositionOwnershipTestPiece");
            _pieceObject.transform.position = Square;
        }

        [TearDown]
        public void TearDown()
        {
            Tween.StopAll();
            if (_pieceObject != null) UnityEngine.Object.DestroyImmediate(_pieceObject);
        }

        private PrimeTweenPieceAnimator Animator()
        {
            return new PrimeTweenPieceAnimator(_pieceObject.transform, null, () => ChessPieceType.Queen);
        }

        /// <summary>
        /// Every way this piece can be set travelling, and who that makes responsible for it.
        /// Kept as a plain list walked inside the tests rather than fed in as test cases: the
        /// answer is an internal type, and a test method cannot take one as a parameter without
        /// widening the animation layer's surface for nobody's benefit but this file's.
        /// </summary>
        private static List<(string what, Action<PrimeTweenPieceAnimator> start, PositionWriter who)> Travellers()
        {
            return new List<(string, Action<PrimeTweenPieceAnimator>, PositionWriter)>
            {
                ("a board move",                      a => a.MoveTo(Elsewhere, MoveStyle.Quiet, 1.414f),            PositionWriter.Move),
                ("a castling rook",                   a => a.MoveToForCastle(Elsewhere, 0f),                        PositionWriter.Castle),
                ("a pawn walking onto the last rank", a => a.PlayPromotionApproach(Elsewhere, 1f, null),            PositionWriter.Promotion),
                ("a capture",                         a => a.PlayCaptureStamp(Elsewhere, new CaptureRunUp(Square, 1.4f)), PositionWriter.Stamp),
                ("a piece leaving for the pile",      a => a.PlayEnPassantDeath(Graveyard, 6f, null),               PositionWriter.Death),
                ("a piece coming back off the pile",  a => a.PlayGraveyardReturn(Elsewhere, Vector3.one, 6f, null), PositionWriter.Death),
                ("being picked up",                   a => a.LiftSelect(),                                          PositionWriter.Lift),
                ("a check warning",                   a => a.Shake(),                                               PositionWriter.Shake),
            };
        }

        [Test]
        public void APieceIsNotBeingMovedByAnythingToBeginWith()
        {
            Assert.That(Animator().PositionHolder, Is.EqualTo(PositionWriter.None));
        }

        [Test]
        public void StartingATravelMakesThatAnimationResponsibleForThePiece()
        {
            foreach (var traveller in Travellers())
            {
                _pieceObject.transform.position = Square;
                PrimeTweenPieceAnimator animator = Animator();

                traveller.start(animator);

                Assert.That(animator.PositionHolder, Is.EqualTo(traveller.who),
                    $"{traveller.what} moves the piece without saying so, which is how one animation ends up running underneath another.");

                animator.StopAllAnimations();
            }
        }

        [Test]
        public void AnAnimationStartedOnTopOfAnotherTakesThePieceOverCompletely()
        {
            // Every ordered pair, so no single handover is the one nobody looked at.
            foreach (var first in Travellers())
            foreach (var second in Travellers())
            {
                _pieceObject.transform.position = Square;
                PrimeTweenPieceAnimator animator = Animator();

                first.start(animator);
                second.start(animator);

                // A check warning is the one animation that does not barge in. It waits for a board
                // move to land and rattles the piece where it arrives, which is the case that
                // matters: only a king is ever warned, and a king's travel is always a board move.
                // It does not wait behind the others, and deliberately — a castling rook, a
                // promoting pawn and a piece on its way to the pile are none of them ever warned,
                // and each of those carries work that runs when it finishes, which cutting it short
                // to hand the piece over would fire early.
                bool warningWaitsItsTurn =
                    second.who == PositionWriter.Shake && first.who == PositionWriter.Move;

                PositionWriter expected = warningWaitsItsTurn ? first.who : second.who;

                Assert.That(animator.PositionHolder, Is.EqualTo(expected),
                    $"Starting {second.what} on top of {first.what} left the wrong animation holding the piece.");

                animator.StopAllAnimations();
            }
        }

        [Test]
        public void TakingAPieceOverStopsWhateverWasMovingItBefore()
        {
            // Recording the new owner and stopping the old animation are two separate things done
            // by the same method, and reading the owner only ever proved the first. Deleting one of
            // the stops failed nothing: the piece came away held by the right animation with the
            // wrong one still writing its position underneath, which is both bugs above.
            //
            // Counting what is still running says the other half. There is nothing to advance and
            // nothing to wait for - stopping a tween takes it off the list there and then.
            foreach (var second in Travellers())
            {
                // A pick-up and a check warning claim the piece without going through the shared
                // take-over, and deliberately so for the warning. Neither is asserted here, and
                // that gap is real rather than overlooked.
                if (second.who == PositionWriter.Lift || second.who == PositionWriter.Shake) continue;

                int alone = LiveTweensAfter(a => second.start(a));

                foreach (var first in Travellers())
                {
                    int onTopOfFirst = LiveTweensAfter(a => { first.start(a); second.start(a); });

                    Assert.That(onTopOfFirst, Is.EqualTo(alone),
                        $"Starting {second.what} on top of {first.what} left {onTopOfFirst - alone} more " +
                        $"animation(s) running than {second.what} starts on its own, so something from " +
                        $"{first.what} is still writing the piece's position underneath it.");
                }
            }
        }

        /// <summary>
        /// How much is still moving this piece once the given animations have been started. Counted
        /// against both targets the animator tweens against, since a sequence built around one is
        /// invisible to a count of the other.
        /// </summary>
        private int LiveTweensAfter(Action<PrimeTweenPieceAnimator> arrange)
        {
            Assert.That(Tween.GetTweensCount(_pieceObject.transform), Is.Zero,
                "Something was left running by the previous case, so this count would not mean what it says.");

            _pieceObject.transform.position = Square;
            PrimeTweenPieceAnimator animator = Animator();

            arrange(animator);
            int live = Tween.GetTweensCount(_pieceObject.transform) + Tween.GetTweensCount(animator);

            animator.StopAllAnimations();
            return live;
        }

        [Test]
        public void PuttingAPieceDownLeavesNothingMovingIt()
        {
            // Nothing ticks in here, so the piece never actually rose and is already standing where
            // it would be set down: there is no descent to run and nobody to hold it. The case
            // where one does run is below, and needs the raise put in by hand to reach.
            PrimeTweenPieceAnimator animator = Animator();

            animator.LiftSelect();
            animator.LowerDeselect();

            Assert.That(animator.PositionHolder, Is.EqualTo(PositionWriter.None));
        }

        [Test]
        public void APieceOnItsWayBackDownSaysSoWhileItIsStillGoing()
        {
            // Letting go animates the piece back onto its square, and that writes its position for
            // a tenth of a second afterwards. Reporting nobody as holding it through that window is
            // what let it be left out of every list of things to stop.
            PrimeTweenPieceAnimator animator = Animator();

            animator.LiftSelect();
            _pieceObject.transform.position = Square + Raised;
            animator.LowerDeselect();

            Assert.That(animator.PositionHolder, Is.EqualTo(PositionWriter.Lower));
        }

        [Test]
        public void PickingAPieceStraightBackUpSetsItDownOnItsSquareFirst()
        {
            // Two taps inside a tenth of a second: the piece is still on its way down when it is
            // picked up again, and a pick-up reads the transform to learn where to put the piece
            // when the player next lets go. Read mid-descent, that is a point in mid-air, and it is
            // never recovered — the next lift is measured from there, and the one after that from
            // wherever that left it, so the piece climbs away from its own square as it is played.
            PrimeTweenPieceAnimator animator = Animator();

            animator.LiftSelect();
            _pieceObject.transform.position = Square + Raised;
            animator.LowerDeselect();
            _pieceObject.transform.position = Square + Raised * 0.6f;   // part of the way back down

            animator.LiftSelect();

            Assert.That(Vector3.Distance(_pieceObject.transform.position, Square), Is.LessThan(0.001f),
                "The piece was picked back up in mid-air, so mid-air is where it will be put down.");
        }

        [Test]
        public void ATravelTakesThePieceOverFromADescentToo()
        {
            // The matrix above starts from the things that travel. Being set back down is not one
            // of them — it only ever follows a pick-up, so it cannot be arranged alongside the
            // rest — and it was the one writer nothing in the animator could stop.
            int moveAlone = LiveTweensAfter(a => a.MoveTo(Elsewhere, MoveStyle.Quiet, 1.414f));

            int moveOverADescent = LiveTweensAfter(a =>
            {
                a.LiftSelect();
                _pieceObject.transform.position = Square + Raised;
                a.LowerDeselect();
                a.MoveTo(Elsewhere, MoveStyle.Quiet, 1.414f);
            });

            Assert.That(moveOverADescent, Is.EqualTo(moveAlone),
                "Setting the piece down is still writing its position underneath the move that took it over.");
        }

        [Test]
        public void TearingAPieceDownLeavesNothingMovingIt()
        {
            PrimeTweenPieceAnimator animator = Animator();

            animator.MoveTo(Elsewhere, MoveStyle.Quiet, 1.414f);
            animator.StopAllAnimations();

            Assert.That(animator.PositionHolder, Is.EqualTo(PositionWriter.None));
        }
    }
}
