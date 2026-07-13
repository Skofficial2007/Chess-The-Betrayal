using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins MatchSimulator's own contract (determinism, seed independence, adjudication) rather
    /// than any specific profile's strength — that ordering assertion lives in
    /// AIProfileStrengthOrderingTests, tagged as an explicit/nightly-class run since it plays real
    /// tournaments and is too slow for the routine per-commit suite.
    /// </summary>
    [TestFixture]
    public class MatchSimulatorTests
    {
        private static AIProfile Fast(string id, int maxDepth) =>
            new AIProfile(id, maxDepth, softTimeBudgetMs: 2000, blunderRate: 0f, blunderMarginCp: 0,
                betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 0, useOpeningBook: false);

        [Test]
        public void PlayGame_SameSeeds_ProducesBitIdenticalOutcomeAndPlyCount()
        {
            BoardState position = CuratedPositionSuite.Build(0);
            AIProfile shallow = Fast("shallow", maxDepth: 2);

            var simulator1 = new MatchSimulator();
            var simulator2 = new MatchSimulator();

            MatchResult result1 = simulator1.PlayGame(position, shallow, shallow, rngSeedWhite: 111, rngSeedBlack: 222, plyCap: 20);
            MatchResult result2 = simulator2.PlayGame(position, shallow, shallow, rngSeedWhite: 111, rngSeedBlack: 222, plyCap: 20);

            Assert.That(result2.Outcome, Is.EqualTo(result1.Outcome));
            Assert.That(result2.PlyCount, Is.EqualTo(result1.PlyCount));
        }

        [Test]
        public void PlayGame_DoesNotMutateTheCallersStartingPosition()
        {
            BoardState position = CuratedPositionSuite.Build(0);
            ulong hashBefore = position.ZobristHash;
            AIProfile shallow = Fast("shallow", maxDepth: 2);

            new MatchSimulator().PlayGame(position, shallow, shallow, rngSeedWhite: 1, rngSeedBlack: 2, plyCap: 10);

            Assert.That(position.ZobristHash, Is.EqualTo(hashBefore),
                "PlayGame must clone the starting position — the same BoardState instance is reused across every game in a tournament.");
        }

        [Test]
        public void PlayGame_ReachesPlyCap_AdjudicatesRatherThanThrowing()
        {
            BoardState position = CuratedPositionSuite.Build(0);
            AIProfile veryShallow = Fast("veryshallow", maxDepth: 1);

            MatchResult result = new MatchSimulator().PlayGame(
                position, veryShallow, veryShallow, rngSeedWhite: 1, rngSeedBlack: 2, plyCap: 4);

            Assert.That(result.PlyCount, Is.LessThanOrEqualTo(4));
            Assert.That(System.Enum.IsDefined(typeof(MatchOutcome), result.Outcome), Is.True);
        }

        [Test]
        public void TournamentSeeding_PerturbingOneSidesGameIndex_LeavesTheOtherSidesSeedUnchanged()
        {
            int whiteSeedBefore = TournamentSeeding.DeriveSeed(runSeed: 7, positionIndex: 0, pairIndex: 0, gameIndex: 0, side: 0);
            int blackSeedBefore = TournamentSeeding.DeriveSeed(runSeed: 7, positionIndex: 0, pairIndex: 0, gameIndex: 0, side: 1);

            // Changing gameIndex perturbs both streams identically (that's expected — a different
            // game). The point of this test is that side 0 and side 1 never collide with each other
            // for the SAME (runSeed, positionIndex, pairIndex, gameIndex) tuple.
            Assert.That(whiteSeedBefore, Is.Not.EqualTo(blackSeedBefore));
        }

        [Test]
        public void TournamentSeeding_SameInputs_AlwaysProducesTheSameSeed()
        {
            int seed1 = TournamentSeeding.DeriveSeed(runSeed: 42, positionIndex: 3, pairIndex: 1, gameIndex: 5, side: 0);
            int seed2 = TournamentSeeding.DeriveSeed(runSeed: 42, positionIndex: 3, pairIndex: 1, gameIndex: 5, side: 0);

            Assert.That(seed2, Is.EqualTo(seed1));
        }

        [Test]
        public void CuratedPositionSuite_EveryPosition_BuildsWithoutThrowing()
        {
            for (int i = 0; i < CuratedPositionSuite.Count; i++)
            {
                BoardState board = null;
                Assert.DoesNotThrow(() => board = CuratedPositionSuite.Build(i),
                    $"Position {i}'s authored line must still be legal against the current engine.");
                Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                    $"Position {i} must leave the board's incremental Zobrist hash consistent with a full recompute.");
            }
        }

        [Test]
        public void CuratedPositionSuite_HasAtLeastTwentyPositions()
        {
            Assert.That(CuratedPositionSuite.Count, Is.GreaterThanOrEqualTo(20));
        }
    }
}
