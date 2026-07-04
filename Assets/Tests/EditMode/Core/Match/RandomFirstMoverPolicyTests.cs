using NUnit.Framework;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Core.Utils;

namespace ChessTheBetrayal.Tests.EditMode.Core.Match
{
    /// <summary>
    /// Test suite for RandomFirstMoverPolicy, validating that seat-to-White assignment follows
    /// the injected IRandomSource deterministically and covers both possible outcomes.
    /// </summary>
    [TestFixture]
    public class RandomFirstMoverPolicyTests
    {
        // ── Fake Random Source ─────────────────────────────────────────────────

        private class FakeRandomSource : IRandomSource
        {
            private readonly bool _nextBool;

            public FakeRandomSource(bool nextBool)
            {
                _nextBool = nextBool;
            }

            public bool NextBool() => _nextBool;

            public int NextInt(int maxExclusive) => 0;
        }

        private RandomFirstMoverPolicy _policy;

        [SetUp]
        public void Setup()
        {
            _policy = new RandomFirstMoverPolicy();
        }

        // ── Assignment Tests ───────────────────────────────────────────────────

        [Test]
        public void Assign_RngReturnsTrue_PlayerAIsWhite()
        {
            // Arrange
            var rng = new FakeRandomSource(nextBool: true);

            // Act
            SideAssignment assignment = _policy.Assign(rng);

            // Assert
            Assert.AreEqual(Seat.PlayerA, assignment.White, "PlayerA should be White when the RNG returns true.");
            Assert.AreEqual(Seat.PlayerB, assignment.Black, "PlayerB should be Black when the RNG returns true.");
        }

        [Test]
        public void Assign_RngReturnsFalse_PlayerBIsWhite()
        {
            // Arrange
            var rng = new FakeRandomSource(nextBool: false);

            // Act
            SideAssignment assignment = _policy.Assign(rng);

            // Assert
            Assert.AreEqual(Seat.PlayerB, assignment.White, "PlayerB should be White when the RNG returns false.");
            Assert.AreEqual(Seat.PlayerA, assignment.Black, "PlayerA should be Black when the RNG returns false.");
        }

        [Test]
        public void Assign_AlwaysProducesOppositeSeatsForWhiteAndBlack()
        {
            // Arrange
            var trueRng = new FakeRandomSource(nextBool: true);
            var falseRng = new FakeRandomSource(nextBool: false);

            // Act
            SideAssignment fromTrue = _policy.Assign(trueRng);
            SideAssignment fromFalse = _policy.Assign(falseRng);

            // Assert
            Assert.AreNotEqual(fromTrue.White, fromTrue.Black, "White and Black must never be the same seat.");
            Assert.AreNotEqual(fromFalse.White, fromFalse.Black, "White and Black must never be the same seat.");
        }

        [Test]
        public void Assign_IsDeterministic_GivenSameRngResult()
        {
            // Arrange
            var rngA = new FakeRandomSource(nextBool: true);
            var rngB = new FakeRandomSource(nextBool: true);

            // Act
            SideAssignment first = _policy.Assign(rngA);
            SideAssignment second = _policy.Assign(rngB);

            // Assert
            Assert.AreEqual(first.White, second.White, "Same RNG outcome must always produce the same seat assignment.");
            Assert.AreEqual(first.Black, second.Black, "Same RNG outcome must always produce the same seat assignment.");
        }
    }
}
