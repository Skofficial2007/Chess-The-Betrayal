using NUnit.Framework;
using PrimeTween;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.View.Pieces;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

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
        public void AStrikeCutShortLeavesThePieceUpright()
        {
            // A charge tips the piece over as it runs and only puts it back at the very end. Cut one
            // short — a takeback, a rebuild, a piece moved out from under it — and whatever ends the
            // strike has to straighten the piece, or it leans for the rest of the game.
            //
            // Nothing ticks a tween in here, so the tilt a running lean would have applied is set by
            // hand. What is being tested is the putting back, not the leaning.
            PrimeTweenPieceAnimator animator = Animator();
            Quaternion upright = _pieceObject.transform.rotation;

            animator.PlayCaptureStamp(new Vector3(7f, 0.5f, 7f), new CaptureRunUp(new Vector3(6f, 0.5f, 6f), 2.83f));
            _pieceObject.transform.rotation = Quaternion.AngleAxis(9f, Vector3.right) * upright;

            animator.MoveTo(new Vector3(1f, 0.5f, 1f));

            Assert.That(Quaternion.Angle(_pieceObject.transform.rotation, upright), Is.LessThan(0.01f),
                "The piece was left tilted after its strike was interrupted.");
        }

        [Test]
        public void AFlinchCutShortLeavesThePieceUpright()
        {
            // The stomp that follows is built against an upright victim, so a flinch that does not
            // undo itself would rotate the piece a little further on every capture it survives.
            PrimeTweenPieceAnimator animator = Animator();
            Quaternion upright = _pieceObject.transform.rotation;

            animator.PlayBrace(new Vector3(1f, 0f, 1f), 0.4f);
            _pieceObject.transform.rotation = Quaternion.AngleAxis(6f, Vector3.forward) * upright;

            animator.PlayStompedDeath(onVanished: null);

            Assert.That(Quaternion.Angle(_pieceObject.transform.rotation, upright), Is.LessThan(0.01f),
                "The victim was still leaning when the crush landed on it.");
        }

        [Test]
        public void FellingSomethingBigNeverOutlastsWhatThePacingBudgetedForIt()
        {
            // The strike stretches with the size of what it takes, and the move pacing releases the
            // next move on a fixed estimate. If the heaviest capture ran past that estimate, two
            // moves would animate on top of each other — the exact fault the pacing gate exists to
            // prevent, arriving quietly through a feature that has nothing to do with pacing.
            //
            // Read off PrimeTweenPieceAnimator's own constants at full heft: anticipation 0.09,
            // leap 0.30, impact squash 0.06, hold 0.05 + 0.05, recover 0.24, settle bob 0.10.
            const float heaviestStrikeSeconds = 0.09f + 0.30f + 0.06f + 0.05f + 0.05f + 0.24f + 0.10f;

            MoveCommand strikeOnANeighbour = MoveCommand.CreateStandardMove(
                new Vector2Int(4, 3), new Vector2Int(5, 4),
                new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1, hasMoved: true),
                new PieceData(Team.Black, ChessPieceType.Queen, moveDirection: -1, startRow: 7, hasMoved: true));

            Assert.That(MoveVisualDurationEstimator.EstimateSeconds(strikeOnANeighbour),
                Is.GreaterThanOrEqualTo(heaviestStrikeSeconds),
                "The heaviest capture plays for longer than the pacing gate holds the next move back.");
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
