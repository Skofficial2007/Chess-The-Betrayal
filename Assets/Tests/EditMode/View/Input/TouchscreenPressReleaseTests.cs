using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ChessTheBetrayal.Tests.EditMode.View.Input
{
    /// <summary>
    /// What a touchscreen actually reports when more than one finger is involved.
    ///
    /// Written to settle a device report by experiment rather than by reading the input package and
    /// reasoning about it: the board stopped accepting taps after the same square was tapped with
    /// several fingers at once, and every explanation offered for that so far has been inferred.
    /// These drive a real Touchscreen through the input system's own test harness and read the
    /// controls the game reads.
    /// </summary>
    [TestFixture]
    public class TouchscreenPressReleaseTests : UnityEngine.InputSystem.InputTestFixture
    {
        private Touchscreen _screen;

        private static readonly Vector2 Somewhere = new Vector2(120f, 240f);
        private static readonly Vector2 SomewhereElse = new Vector2(300f, 400f);

        public override void Setup()
        {
            base.Setup();
            _screen = InputSystem.AddDevice<Touchscreen>();
        }

        /// <summary>What the game's pointer wrapper reads, named the way the wrapper names it.</summary>
        private bool PrimaryIsPressed => _screen.primaryTouch.press.isPressed;
        private bool PrimaryWasReleasedThisFrame => _screen.primaryTouch.press.wasReleasedThisFrame;
        private Vector2 PrimaryPosition => _screen.primaryTouch.position.ReadValue();

        private int FingersInProgress
        {
            get
            {
                int count = 0;
                foreach (TouchControl touch in _screen.touches)
                {
                    if (touch.isInProgress) count++;
                }
                return count;
            }
        }

        [Test]
        public void OneFingerDownAndUpReportsAPressAndARelease()
        {
            BeginTouch(1, Somewhere);
            Assert.That(PrimaryIsPressed, Is.True);

            EndTouch(1, Somewhere);
            Assert.That(PrimaryWasReleasedThisFrame, Is.True, "The one gesture the whole two-tap model is built on.");
            Assert.That(PrimaryIsPressed, Is.False);
        }

        /// <summary>
        /// The documented behaviour, confirmed rather than taken on trust: the primary touch is not
        /// a finger, it is a stand-in that lasts as long as ANY finger is on the glass. So a second
        /// finger tapping while a first one rests produces no release at all, and the game - which
        /// completes a tap only on a release - never sees that tap.
        /// </summary>
        [Test]
        public void ASecondFingerTappingWhileAFirstRestsIsNeverReportedAsARelease()
        {
            BeginTouch(1, Somewhere);

            BeginTouch(2, SomewhereElse);
            EndTouch(2, SomewhereElse);

            Assert.That(PrimaryWasReleasedThisFrame, Is.False,
                "The second finger's tap is invisible to anything watching the primary touch.");
            Assert.That(PrimaryIsPressed, Is.True, "and the primary touch is still held by the finger that is resting.");
        }

        /// <summary>
        /// The first finger lifting while a second is still down does not end the primary touch
        /// either. It is kept alive against the finger that remains, which means a player who rests
        /// one finger and taps with another gets no taps for as long as the resting finger stays.
        /// </summary>
        [Test]
        public void LiftingTheFirstFingerWhileASecondIsDownDoesNotEndThePrimaryTouch()
        {
            BeginTouch(1, Somewhere);
            BeginTouch(2, SomewhereElse);

            EndTouch(1, Somewhere);

            Assert.That(PrimaryIsPressed, Is.True);
            Assert.That(PrimaryWasReleasedThisFrame, Is.False);
        }

        /// <summary>
        /// And the question the whole report turns on: once every finger is off the glass, does it
        /// come back? Whether the board recovers on its own or stays dead is the difference between
        /// a gesture that is merely swallowed and one that ends the match's input for good.
        /// </summary>
        [Test]
        public void LiftingEveryFingerEndsThePrimaryTouchAgain()
        {
            BeginTouch(1, Somewhere);
            BeginTouch(2, SomewhereElse);
            EndTouch(1, Somewhere);
            EndTouch(2, SomewhereElse);

            Assert.That(FingersInProgress, Is.Zero, "Nothing is on the glass.");
            Assert.That(PrimaryIsPressed, Is.False,
                "If this is still pressed with no finger down, the board can never read another tap.");
        }

        /// <summary>
        /// Several fingers arriving and leaving out of order, which is what tapping one square
        /// repeatedly with a whole hand actually produces.
        /// </summary>
        [Test]
        public void ManyFingersArrivingAndLeavingOutOfOrderStillEndThePrimaryTouch()
        {
            BeginTouch(1, Somewhere);
            BeginTouch(2, Somewhere);
            BeginTouch(3, Somewhere);

            EndTouch(2, Somewhere);
            EndTouch(1, Somewhere);
            EndTouch(3, Somewhere);

            Assert.That(FingersInProgress, Is.Zero);
            Assert.That(PrimaryIsPressed, Is.False);
        }

        /// <summary>
        /// A finger whose lift never arrives - the app losing focus mid-touch, the platform
        /// dropping the event, a system gesture swallowing it. This is the state the device report
        /// describes, and it is the only one of these that does not come back on its own.
        ///
        /// Both halves of the symptom are pinned here. The primary touch stays held, so no tap can
        /// ever complete: a tap is finished by a release and no release will come. And its position
        /// stays frozen where the FIRST finger was - it does not follow the finger still on the
        /// glass, even while that finger moves - which is the square a hover highlight goes on
        /// being redrawn on, for the rest of the session.
        /// </summary>
        [Test]
        public void AFingerWhoseLiftNeverArrivesLeavesThePointerHeldAtAFrozenPosition()
        {
            BeginTouch(1, Somewhere);
            BeginTouch(2, SomewhereElse);
            EndTouch(1, Somewhere);

            // The finger that is still down goes on moving; nothing about that reaches the control
            // the game reads.
            MoveTouch(2, new Vector2(500f, 500f));

            Assert.That(PrimaryIsPressed, Is.True, "Held, with nothing that will ever release it.");
            Assert.That(PrimaryWasReleasedThisFrame, Is.False);
            Assert.That(PrimaryPosition, Is.EqualTo(Somewhere).Using<Vector2>(Near),
                "Frozen where the first finger was, which is the square that stays lit.");
        }

        /// <summary>
        /// And the consequence, stated as the game would experience it: once that has happened,
        /// a whole further tap - down and up, cleanly, on a fresh finger - produces no release at
        /// all. This is the difference between a gesture being swallowed and the board being over.
        /// </summary>
        [Test]
        public void OnceHeldByAStuckFingerAFurtherCleanTapIsNeverSeen()
        {
            BeginTouch(1, Somewhere);
            BeginTouch(2, SomewhereElse);
            EndTouch(1, Somewhere);

            BeginTouch(3, Somewhere);
            EndTouch(3, Somewhere);

            Assert.That(PrimaryWasReleasedThisFrame, Is.False,
                "A complete, ordinary tap, and the control the game completes taps on never moved.");
            Assert.That(PrimaryIsPressed, Is.True);
        }

        private static int Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 0.01f ? 0 : 1;
    }
}
