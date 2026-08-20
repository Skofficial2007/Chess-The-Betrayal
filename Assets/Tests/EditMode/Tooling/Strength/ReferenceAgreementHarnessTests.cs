using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tooling.Agreement;
using ChessTheBetrayal.Tooling.Strength;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.Strength
{
    /// <summary>
    /// Proves the agreement harness measures what it claims to, before anything is measured with
    /// it. An instrument that silently reports a plausible number is worse than no instrument, so
    /// these check the properties that would make its output meaningless if they failed: that a
    /// profile compared against itself agrees completely, that repeated runs give the same answer,
    /// and that a cached reference answer is identical to a freshly searched one and is thrown away
    /// rather than trusted once it can no longer be about the position it was stored under.
    ///
    /// Runs at a shallow reference depth over two positions so it stays a per-commit test. The real
    /// measuring depth is far higher and is not what these are checking — they check the mechanism.
    /// </summary>
    [TestFixture]
    public class ReferenceAgreementHarnessTests
    {
        // Two positions and a shallow reference keep this in seconds. The properties under test are
        // structural and hold at any depth.
        private const int TestReferenceDepth = 4;
        private static readonly int[] TwoPositions = { 0, 1 };

        private static AIProfile TopTier => AIProfileTable.BuiltIn.Single(p => p.Id == "impossible");

        /// <summary>
        /// A profile measured against a reference that thinks exactly as it does, searching just as
        /// deep, must agree everywhere. Any shortfall means the harness introduces a difference the
        /// two searches never had — which would make every later number it reports untrustworthy.
        /// </summary>
        [Test]
        public void SubjectIdenticalToReference_AgreesOnEveryPosition()
        {
            AIProfile subject = ProfileAtDepth(TopTier, TestReferenceDepth);
            var oracle = new ReferenceMoveOracle(TopTier, TestReferenceDepth);

            AgreementReport report = ReferenceAgreementRunner.Run(subject, oracle, TwoPositions);

            Assert.That(report.RawAgreement, Is.EqualTo(1.0), report.Describe());
        }

        /// <summary>
        /// The same question asked twice must give the same answer. The reference search takes no
        /// clock and no random input, so this is a property of the code path rather than of a seed —
        /// a reference that drifted between runs could not serve as a fixed point to measure
        /// anything against.
        /// </summary>
        [Test]
        public void RepeatedRuns_ProduceIdenticalAgreement()
        {
            AIProfile subject = ProfileAtDepth(TopTier, TestReferenceDepth);

            AgreementReport first = ReferenceAgreementRunner.Run(
                subject, new ReferenceMoveOracle(TopTier, TestReferenceDepth), TwoPositions);
            AgreementReport second = ReferenceAgreementRunner.Run(
                subject, new ReferenceMoveOracle(TopTier, TestReferenceDepth), TwoPositions);

            Assert.That(second.RawAgreement, Is.EqualTo(first.RawAgreement), "agreement fraction drifted between runs");
            for (int i = 0; i < first.Results.Count; i++)
            {
                Assert.That(second.Results[i].ReferenceMove.StartPosition, Is.EqualTo(first.Results[i].ReferenceMove.StartPosition));
                Assert.That(second.Results[i].ReferenceMove.EndPosition, Is.EqualTo(first.Results[i].ReferenceMove.EndPosition));
                Assert.That(second.Results[i].ReferenceMove.Stage, Is.EqualTo(first.Results[i].ReferenceMove.Stage));
            }
        }

        /// <summary>
        /// Reusing a cached answer must give exactly what searching again would have given. If the
        /// cache returned anything else, every run after the first would be measuring against a
        /// different oracle than the one the first run used.
        /// </summary>
        [Test]
        public void CachedReference_MatchesFreshlySearchedReference()
        {
            var oracle = new ReferenceMoveOracle(TopTier, TestReferenceDepth);
            BoardState board = CuratedPositionSuite.Build(0);

            ReferenceMove fresh = oracle.Answer(board);
            ReferenceMove cached = oracle.Answer(board);

            Assert.That(oracle.CacheMisses, Is.EqualTo(1), "the first call should have searched");
            Assert.That(oracle.CacheHits, Is.EqualTo(1), "the second call should have been served from cache");
            Assert.That(cached.Move.StartPosition, Is.EqualTo(fresh.Move.StartPosition));
            Assert.That(cached.Move.EndPosition, Is.EqualTo(fresh.Move.EndPosition));
            Assert.That(cached.Move.Stage, Is.EqualTo(fresh.Move.Stage));
            Assert.That(cached.ScoreCp, Is.EqualTo(fresh.ScoreCp));
            Assert.That(cached.Depth, Is.EqualTo(fresh.Depth));
        }

        /// <summary>
        /// A cached answer stamped with a different key scheme describes a position its hash no
        /// longer identifies. It must be rejected rather than reused — a stale oracle is the one
        /// failure that produces confident numbers measuring nothing at all.
        /// </summary>
        [Test]
        public void ReferenceStampedWithAnotherKeyScheme_IsNotTrusted()
        {
            BoardState board = CuratedPositionSuite.Build(0);
            var answer = new ReferenceMove(board.ZobristHash, BoardState.ZobristSchemeVersion,
                default, 0, TestReferenceDepth, 0.0);

            Assert.That(answer.IsValidFor(board.ZobristHash, BoardState.ZobristSchemeVersion), Is.True,
                "an answer should be valid for the position and scheme it was stored under");
            Assert.That(answer.IsValidFor(board.ZobristHash, BoardState.ZobristSchemeVersion + 1), Is.False,
                "an answer must not be trusted once the key scheme has changed");
            Assert.That(answer.IsValidFor(board.ZobristHash + 1, BoardState.ZobristSchemeVersion), Is.False,
                "an answer must not be trusted for a different position");
        }

        /// <summary>
        /// The as-played figure counts the profile's real move, so a profile that deliberately
        /// discards good moves cannot score above its own raw figure. This is what keeps the two
        /// numbers meaningful side by side.
        /// </summary>
        [Test]
        public void TopTier_PlaysItsOwnBestMove_SoBothFiguresMatch()
        {
            AIProfile subject = ProfileAtDepth(TopTier, TestReferenceDepth);
            var oracle = new ReferenceMoveOracle(TopTier, TestReferenceDepth);

            AgreementReport report = ReferenceAgreementRunner.Run(subject, oracle, TwoPositions);

            Assert.That(report.SelectedAgreement, Is.EqualTo(report.RawAgreement),
                "the top tier has no blunder roll and no tie-break window, so it always plays its own best move");
        }

        /// <summary>
        /// A position whose best move is the same at the reference depth and the two plies below it
        /// is reported stable; one that changes its mind is not. This matters because some positions
        /// genuinely alternate their answer from one ply to the next, and on those the reference
        /// says more about the depth that was chosen than about the position — agreement measured
        /// there is parity noise dressed up as a strength signal.
        /// </summary>
        [Test]
        public void StabilityCheck_AgreesWithSearchingEachDepthDirectly()
        {
            var oracle = new ReferenceMoveOracle(TopTier, TestReferenceDepth);
            BoardState board = CuratedPositionSuite.Build(0);

            bool reportedStable = oracle.IsStableAcrossDepths(board);

            // Independently derive the same verdict by asking a separate oracle at each depth in
            // the window, so the check is verified against something other than itself.
            bool independentlyStable = true;
            ReferenceMove deep = new ReferenceMoveOracle(TopTier, TestReferenceDepth).Answer(board);
            for (int depth = TestReferenceDepth - 2; depth < TestReferenceDepth; depth++)
            {
                ReferenceMove shallow = new ReferenceMoveOracle(TopTier, depth).Answer(board);
                if (shallow.Move.StartPosition != deep.Move.StartPosition
                    || shallow.Move.EndPosition != deep.Move.EndPosition
                    || shallow.Move.Stage != deep.Move.Stage)
                {
                    independentlyStable = false;
                }
            }

            Assert.That(reportedStable, Is.EqualTo(independentlyStable),
                "the stability check must agree with searching each depth in the window directly");
        }

        /// <summary>Rebuilds a profile at a different search depth, leaving every personality dial
        /// untouched — the depth is the only thing these tests need to vary.</summary>
        private static AIProfile ProfileAtDepth(AIProfile profile, int maxDepth) =>
            new AIProfile(profile.Id, maxDepth, profile.TimeBudget, profile.BlunderRate,
                profile.BlunderMarginCp, profile.BetrayalAggression, profile.AttackDefenseBias,
                profile.TieBreakWindowCp, profile.UseOpeningBook);
    }
}
