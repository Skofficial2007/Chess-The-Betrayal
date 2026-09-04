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

        // Somewhere different for each journey to be going. They used to share one target, which
        // quietly hid the two animations that decline to move a piece already standing on the
        // square asked for: started on top of a hand-over that had just put the piece there, those
        // two created no tween at all and the pair read as a clean take-over of nothing.
        private static readonly Vector3 CastleSquare = new Vector3(2f, 0.5f, 0f);
        private static readonly Vector3 LastRank = new Vector3(4f, 0.5f, 3f);
        private static readonly Vector3 VictimSquare = new Vector3(5f, 0.5f, 1f);
        private static readonly Vector3 HomeSquare = new Vector3(1f, 0.5f, 2f);

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
        private static List<(string what, Action<PrimeTweenPieceAnimator> start, PositionWriter who, Vector3? lands)> Travellers()
        {
            return new List<(string, Action<PrimeTweenPieceAnimator>, PositionWriter, Vector3?)>
            {
                ("a board move",                      a => a.MoveTo(Elsewhere, MoveStyle.Quiet, 1.414f),            PositionWriter.Move,      Elsewhere),
                ("a castling rook",                   a => a.MoveToForCastle(CastleSquare, 0f),                     PositionWriter.Castle,    CastleSquare),
                ("a pawn walking onto the last rank", a => a.PlayPromotionApproach(LastRank, 1f, null),             PositionWriter.Promotion, LastRank),
                ("a capture",                         a => a.PlayCaptureStamp(VictimSquare, new CaptureRunUp(Square, 1.4f)), PositionWriter.Stamp, VictimSquare),
                ("a piece leaving for the pile",      a => a.PlayEnPassantDeath(Graveyard, 6f, null),               PositionWriter.Death,     Graveyard),
                ("a piece coming back off the pile",  a => a.PlayGraveyardReturn(HomeSquare, Vector3.one, 6f, null), PositionWriter.Death,    HomeSquare),

                // Neither of these is a journey: a piece being held and a piece being rattled are
                // both going nowhere, so there is no square for either to be cut short on to.
                ("being picked up",                   a => a.LiftSelect(),                                          PositionWriter.Lift,      null),
                ("a check warning",                   a => a.Shake(),                                               PositionWriter.Shake,     null),
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

        /// <summary>
        /// A journey that is taken over still finishes where it said it was going.
        ///
        /// This is the half of a hand-over that ownership alone never said anything about. Stopping
        /// the old animation leaves the piece exactly where the interruption caught it, and nothing
        /// afterwards ever puts it right: the game applied the move long before the glide started,
        /// so no later event has any reason to write that piece's position again, and only a full
        /// rebuild of the position does. A piece left standing between two squares stays there for
        /// the rest of the match.
        ///
        /// Every ordered pair again, because the pair that matters is a player tapping a piece that
        /// is still sliding, and that is one entry in a grid nobody would think to try by hand.
        /// </summary>
        [Test]
        public void ATravelCutShortStillLeavesThePieceOnItsSquare()
        {
            foreach (var first in Travellers())
            {
                if (!first.lands.HasValue) continue;

                foreach (var second in Travellers())
                {
                    // The warning does not take the piece over — it waits for the glide to land, so
                    // there is nothing cut short and the piece is still on its way.
                    if (second.who == PositionWriter.Shake) continue;

                    _pieceObject.transform.position = Square;
                    PrimeTweenPieceAnimator animator = Animator();

                    first.start(animator);
                    second.start(animator);

                    Assert.That(_pieceObject.transform.position, Is.EqualTo(first.lands.Value).Using(PositionComparer),
                        $"Starting {second.what} cut {first.what} short and left the piece at " +
                        $"{_pieceObject.transform.position} rather than on the square it was going to.");

                    animator.StopAllAnimations();
                }
            }
        }

        /// <summary>
        /// Picking up a piece that is still sliding measures the lift from the square it was going
        /// to, not from wherever it had reached.
        ///
        /// Worth its own test rather than being left to the sweep above, because of how long this
        /// one lasts. The place a lift is measured from becomes the place the piece is set back
        /// down, and every lift after it is measured from there in turn — so a rest position taken
        /// mid-glide is not a piece that looks wrong for a moment, it is a piece permanently a
        /// little off its square, drifting further with each tap. The transform is what that
        /// position is read from on the next line, so pinning the transform pins it.
        /// </summary>
        [Test]
        public void PickingUpAPieceThatIsStillSlidingMeasuresItFromTheSquareItWasGoingTo()
        {
            PrimeTweenPieceAnimator animator = Animator();

            animator.MoveTo(Elsewhere, MoveStyle.Quiet, 1.414f);
            animator.LiftSelect();

            Assert.That(_pieceObject.transform.position, Is.EqualTo(Elsewhere).Using(PositionComparer));
            Assert.That(animator.PositionHolder, Is.EqualTo(PositionWriter.Lift));
        }

        /// <summary>
        /// Positions come off eased tweens and a snapped hand-over, so they are compared to within
        /// a fraction of a millimetre rather than exactly. A piece parked between two squares is
        /// out by a good part of a tile; nothing this is meant to catch hides under this margin.
        /// </summary>
        private static readonly IComparer<Vector3> PositionComparer = new WithinAMillimetre();

        private sealed class WithinAMillimetre : IComparer<Vector3>
        {
            public int Compare(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= 0.000001f ? 0 : 1;
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
                // A check warning claims the piece without going through the shared take-over,
                // and deliberately: it waits for a board move to land rather than barging in. It
                // is the only one left out, and a king is the only piece ever warned, so the case
                // it skips is a king mid-glide - which is the case it is built to wait for.
                if (second.who == PositionWriter.Shake) continue;

                foreach (var first in Travellers())
                {
                    // A hand-over puts the piece on the square the journey it cut short was going
                    // to, so by the time `second` starts the piece is standing there rather than
                    // back where this began. The lone count has to start from the same place: two
                    // of these animations decline to move a piece that is already on the square
                    // they were asked for, and measured from anywhere else that reads as an
                    // animation which was never started at all.
                    Vector3 handedOverAt = first.lands ?? Square;

                    int alone = LiveTweensAfter(handedOverAt, a => second.start(a));
                    int onTopOfFirst = LiveTweensAfter(Square, a => { first.start(a); second.start(a); });

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
        private int LiveTweensAfter(Vector3 startingAt, Action<PrimeTweenPieceAnimator> arrange)
        {
            Assert.That(Tween.GetTweensCount(_pieceObject.transform), Is.Zero,
                "Something was left running by the previous case, so this count would not mean what it says.");

            _pieceObject.transform.position = startingAt;
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
            int moveAlone = LiveTweensAfter(Square, a => a.MoveTo(Elsewhere, MoveStyle.Quiet, 1.414f));

            int moveOverADescent = LiveTweensAfter(Square, a =>
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
