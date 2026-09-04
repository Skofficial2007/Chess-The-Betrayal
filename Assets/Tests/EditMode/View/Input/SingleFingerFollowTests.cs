using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.View.Input;

namespace ChessTheBetrayal.Tests.EditMode.View.Input
{
    /// <summary>
    /// Which finger the board is being driven by, frame by frame.
    ///
    /// Nothing here touches hardware — the readings are written by hand, which is the point: the
    /// sequences that broke the board are ones several fingers produce together, and no play-test
    /// would think to try them. What a real touchscreen actually reports is pinned separately, in
    /// TouchscreenPressReleaseTests, so these two together say both "this is what the hardware
    /// does" and "this is what we do about it".
    /// </summary>
    [TestFixture]
    public class SingleFingerFollowTests
    {
        private static readonly Vector2 Somewhere = new Vector2(120f, 240f);
        private static readonly Vector2 SomewhereElse = new Vector2(300f, 400f);

        private SingleFingerFollow _follow;
        private readonly TouchFingerReading[] _fingers = new TouchFingerReading[10];

        [SetUp]
        public void Setup() => _follow = new SingleFingerFollow();

        private static TouchFingerReading Down(int id, Vector2 at) => new TouchFingerReading(id, true, true, false, at);
        private static TouchFingerReading Held(int id, Vector2 at) => new TouchFingerReading(id, true, false, false, at);
        private static TouchFingerReading Lifting(int id, Vector2 at) => new TouchFingerReading(id, false, false, true, at);
        private static TouchFingerReading Gone(int id, Vector2 at) => new TouchFingerReading(id, false, false, false, at);

        private PointerFrame Frame(params TouchFingerReading[] fingers)
        {
            for (int i = 0; i < fingers.Length; i++) _fingers[i] = fingers[i];
            return _follow.Advance(_fingers, fingers.Length);
        }

        [Test]
        public void NothingOnTheGlassIsNoPointerAtAll()
        {
            PointerFrame frame = Frame();

            Assert.That(frame.IsPressed, Is.False);
            Assert.That(frame.WasPressed, Is.False);
            Assert.That(frame.WasReleased, Is.False);
            Assert.That(_follow.FollowedTouchId, Is.EqualTo(SingleFingerFollow.NoFinger));
        }

        [Test]
        public void OneFingerDownHeldAndLiftedIsAPressAHoldAndARelease()
        {
            PointerFrame press = Frame(Down(1, Somewhere));
            Assert.That(press.WasPressed, Is.True);
            Assert.That(press.IsPressed, Is.True);
            Assert.That(press.Position, Is.EqualTo(Somewhere));

            PointerFrame held = Frame(Held(1, Somewhere));
            Assert.That(held.IsPressed, Is.True);
            Assert.That(held.WasPressed, Is.False, "A press is one frame, not every frame it stays down.");

            PointerFrame release = Frame(Lifting(1, Somewhere));
            Assert.That(release.WasReleased, Is.True);
            Assert.That(release.IsPressed, Is.False);
            Assert.That(release.Position, Is.EqualTo(Somewhere), "A tap is measured where the finger left.");
            Assert.That(_follow.FollowedTouchId, Is.EqualTo(SingleFingerFollow.NoFinger));
        }

        /// <summary>
        /// The case Unity's own primary touch gets wrong. A finger resting on the glass must not
        /// stop a different finger's tap from being read.
        /// </summary>
        [Test]
        public void ASecondFingersTapIsReadWhileAFirstFingerRests()
        {
            Frame(Down(1, Somewhere));                                   // finger 1 lands and is followed
            Frame(Lifting(1, Somewhere));                                // and lifts, so nobody is followed

            PointerFrame press = Frame(Held(1, Somewhere), Down(2, SomewhereElse));
            Assert.That(press.WasPressed, Is.True, "A resting finger must not swallow another one's tap.");
            Assert.That(press.Position, Is.EqualTo(SomewhereElse));

            PointerFrame release = Frame(Held(1, Somewhere), Lifting(2, SomewhereElse));
            Assert.That(release.WasReleased, Is.True);
            Assert.That(release.Position, Is.EqualTo(SomewhereElse));
        }

        [Test]
        public void AFingerLandingWhileAnotherIsAlreadyFollowedDoesNotStealThePointer()
        {
            Frame(Down(1, Somewhere));

            PointerFrame frame = Frame(Held(1, Somewhere), Down(2, SomewhereElse));
            Assert.That(frame.Position, Is.EqualTo(Somewhere), "The gesture already under way is the one being made.");
            Assert.That(frame.WasPressed, Is.False);
            Assert.That(_follow.FollowedTouchId, Is.EqualTo(1));
        }

        /// <summary>
        /// The rule the whole class exists for. A finger the platform never delivers a lift for
        /// stays down forever; following it would mean waiting forever for a release that is not
        /// coming, which is how the board came to ignore every tap for a whole session. It is never
        /// adopted, because its press was never seen.
        /// </summary>
        [Test]
        public void AFingerThatWasAlreadyDownIsNeverAdopted()
        {
            PointerFrame first = Frame(Held(99, Somewhere));
            PointerFrame second = Frame(Held(99, Somewhere));
            PointerFrame third = Frame(Held(99, Somewhere));

            Assert.That(first.IsPressed, Is.False);
            Assert.That(second.IsPressed, Is.False);
            Assert.That(third.IsPressed, Is.False);
            Assert.That(_follow.FollowedTouchId, Is.EqualTo(SingleFingerFollow.NoFinger),
                "Waiting on a finger whose press was never seen is exactly the wedge this avoids.");
        }

        [Test]
        public void ATapStillWorksWithAStuckFingerOnTheGlass()
        {
            Frame(Held(99, Somewhere));   // stuck, forever, and ignored

            PointerFrame press = Frame(Held(99, Somewhere), Down(2, SomewhereElse));
            Assert.That(press.WasPressed, Is.True);

            PointerFrame release = Frame(Held(99, Somewhere), Lifting(2, SomewhereElse));
            Assert.That(release.WasReleased, Is.True, "The board goes on working around a finger that never lifts.");
            Assert.That(release.Position, Is.EqualTo(SomewhereElse));
        }

        /// <summary>
        /// A followed finger can disappear without a lift ever being reported — its slot reused, or
        /// the device reset under it. Letting go is the only safe answer; holding on is the wedge.
        /// </summary>
        [Test]
        public void AFollowedFingerThatVanishesIsLetGoOfRatherThanWaitedOn()
        {
            Frame(Down(1, Somewhere));

            PointerFrame frame = Frame(Down(2, SomewhereElse));

            Assert.That(frame.IsPressed, Is.False, "The finger being followed is not in this frame at all.");
            Assert.That(_follow.FollowedTouchId, Is.EqualTo(SingleFingerFollow.NoFinger));

            PointerFrame next = Frame(Down(3, Somewhere));
            Assert.That(next.WasPressed, Is.True, "and the next real press is followed normally.");
        }

        /// <summary>
        /// A finger still sitting in its slot having ended on some earlier frame. Not a release —
        /// that frame has been and gone — so there is nothing to report, but it must not be waited
        /// on either.
        /// </summary>
        [Test]
        public void AFollowedFingerFoundAlreadyEndedIsLetGoOfQuietly()
        {
            Frame(Down(1, Somewhere));

            PointerFrame frame = Frame(Gone(1, Somewhere));

            Assert.That(frame.WasReleased, Is.False, "The release frame was missed; inventing one would fake a tap.");
            Assert.That(frame.IsPressed, Is.False);
            Assert.That(_follow.FollowedTouchId, Is.EqualTo(SingleFingerFollow.NoFinger));
        }

        [Test]
        public void APlatformReusingATouchIdAfterALiftStartsAFreshGesture()
        {
            Frame(Down(1, Somewhere));
            Frame(Lifting(1, Somewhere));

            PointerFrame press = Frame(Down(1, SomewhereElse));

            Assert.That(press.WasPressed, Is.True, "Ids are only unique among fingers currently down.");
            Assert.That(press.Position, Is.EqualTo(SomewhereElse));
            Assert.That(_follow.FollowedTouchId, Is.EqualTo(1));
        }

        [Test]
        public void ForgettingStopsFollowingWithoutAnyReadingToSaySo()
        {
            Frame(Down(1, Somewhere));
            Assert.That(_follow.FollowedTouchId, Is.EqualTo(1));

            _follow.Forget();

            Assert.That(_follow.FollowedTouchId, Is.EqualTo(SingleFingerFollow.NoFinger));
        }

        /// <summary>
        /// Only the part of the buffer the caller says is filled gets read. The buffer is reused
        /// every frame, so whatever is left in it from the last one is not this frame's news.
        /// </summary>
        [Test]
        public void OnlyTheFingersTheCallerCountsAreRead()
        {
            _fingers[0] = Down(1, Somewhere);
            _fingers[1] = Down(2, SomewhereElse);

            PointerFrame frame = _follow.Advance(_fingers, 1);

            Assert.That(_follow.FollowedTouchId, Is.EqualTo(1));
            Assert.That(frame.Position, Is.EqualTo(Somewhere));
        }
    }
}
