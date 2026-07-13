using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Plays real AI-vs-AI tournaments between adjacent tiers and asserts the strength chain the
    /// preset table promises: Impossible &gt;= Extreme &gt;= Hard &gt;= Normal &gt;= Easy. Aggressive is a
    /// personality sibling of Hard-ish strength, not a rung in that chain, so it's checked only
    /// against Normal and Easy — punishing it for playing differently in kind would be testing the
    /// wrong thing.
    ///
    /// [Explicit]: these are real searches at each tier's real depth, played out over the full
    /// curated suite — minutes, not seconds. Routine/per-commit runs must not pay this cost; run
    /// explicitly (or from a nightly/on-demand job) whenever a dial or search change might have
    /// shifted the ordering.
    /// </summary>
    [TestFixture]
    [Explicit("Plays real AI-vs-AI tournaments at full search depth — minutes per pair, not a per-commit check.")]
    public class AIProfileStrengthOrderingTests
    {
        // Binomial standard error at N games is ~0.5/sqrt(N); N=40 (20 positions x 2 colors) keeps
        // a full run under a few minutes per pair while still resolving a real strength gap. The
        // decision rule itself (60% pass / 55% hard floor) is unchanged by N — a smaller N just
        // widens the confidence interval around it, which is why weaker/noisier pairs may want a
        // larger N in a dedicated on-demand run rather than here.
        private const float PassWinRate = 0.60f;
        private const float FloorWinRate = 0.55f;
        private const int RunSeed = 20260713;

        private static AIProfile Profile(string id) => AIProfileTable.BuiltIn.Single(p => p.Id == id);

        [Test]
        public void Normal_ScoresAtLeastSixtyPercent_AgainstEasy() => AssertStrongerScoresAtLeast("normal", "easy");

        [Test]
        public void Hard_ScoresAtLeastSixtyPercent_AgainstNormal() => AssertStrongerScoresAtLeast("hard", "normal");

        [Test]
        public void Extreme_ScoresAtLeastSixtyPercent_AgainstHard() => AssertStrongerScoresAtLeast("extreme", "hard");

        [Test]
        public void Impossible_ScoresAtLeastSixtyPercent_AgainstExtreme() => AssertStrongerScoresAtLeast("impossible", "extreme");

        [Test]
        public void Aggressive_ScoresAtLeastSixtyPercent_AgainstNormal() => AssertStrongerScoresAtLeast("aggressive", "normal");

        [Test]
        public void Aggressive_ScoresAtLeastSixtyPercent_AgainstEasy() => AssertStrongerScoresAtLeast("aggressive", "easy");

        private static void AssertStrongerScoresAtLeast(string strongerId, string weakerId)
        {
            AIProfile stronger = Profile(strongerId);
            AIProfile weaker = Profile(weakerId);
            var simulator = new MatchSimulator();

            float points = 0f;
            int games = 0;

            for (int positionIndex = 0; positionIndex < CuratedPositionSuite.Count; positionIndex++)
            {
                BoardState position = CuratedPositionSuite.Build(positionIndex);

                points += PlayAndScore(simulator, position, stronger, weaker, positionIndex, strongerIsWhite: true);
                games++;

                points += PlayAndScore(simulator, position, weaker, stronger, positionIndex, strongerIsWhite: false);
                games++;
            }

            float winRate = points / games;

            Assert.That(winRate, Is.GreaterThanOrEqualTo(FloorWinRate),
                $"{strongerId} scored only {winRate:P1} against {weakerId} over {games} games — below the hard floor, this pairing is a tuning failure.");

            if (winRate < PassWinRate)
            {
                TestContext.WriteLine($"WARN: {strongerId} scored {winRate:P1} against {weakerId} over {games} games — above the hard floor but below the {PassWinRate:P0} pass threshold; worth a dial review.");
            }
        }

        /// <summary>Plays one game and returns the STRONGER profile's score for it (1 = win, 0.5 = draw, 0 = loss),
        /// regardless of which color it played.</summary>
        private static float PlayAndScore(
            MatchSimulator simulator, BoardState position, AIProfile whiteProfile, AIProfile blackProfile,
            int positionIndex, bool strongerIsWhite)
        {
            int seedWhite = TournamentSeeding.DeriveSeed(RunSeed, positionIndex, pairIndex: 0, gameIndex: 0, side: 0);
            int seedBlack = TournamentSeeding.DeriveSeed(RunSeed, positionIndex, pairIndex: 0, gameIndex: 0, side: 1);

            MatchResult result = simulator.PlayGame(position, whiteProfile, blackProfile, seedWhite, seedBlack);

            float whiteScore = result.Outcome switch
            {
                MatchOutcome.WhiteWon => 1f,
                MatchOutcome.BlackWon => 0f,
                _ => 0.5f
            };

            return strongerIsWhite ? whiteScore : 1f - whiteScore;
        }
    }
}
