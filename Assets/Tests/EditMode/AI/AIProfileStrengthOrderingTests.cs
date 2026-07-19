using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.EditorTools.Benchmark;
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
    /// curated suite. Routine/per-commit runs must not pay this cost; run explicitly (or from a
    /// nightly/on-demand job) whenever a dial or search change might have shifted the ordering.
    ///
    /// Games within one pairing run across worker threads (each with its own MatchSimulator, same
    /// pattern ParallelTournamentExecutor uses) and report through TestContext.Progress as they
    /// finish — a real, previously-missing fix: a run that goes quiet for 30+ minutes was
    /// indistinguishable from a genuinely stuck one, since TestContext.WriteLine buffers and only
    /// surfaces once a test method returns. TestContext.Progress writes through immediately.
    /// </summary>
    [TestFixture]
    [Explicit("Plays real AI-vs-AI tournaments at full search depth — not a per-commit check.")]
    public class AIProfileStrengthOrderingTests
    {
        // Binomial standard error at N games is ~0.5/sqrt(N); N=40 (20 positions x 2 colors) keeps
        // a full run resolving a real strength gap without an excessive game count. The decision
        // rule itself (60% pass / 55% hard floor) is unchanged by N — a smaller N just widens the
        // confidence interval around it, which is why weaker/noisier pairs may want a larger N in
        // a dedicated on-demand run rather than here.
        private const float PassWinRate = 0.60f;
        private const float FloorWinRate = 0.55f;
        private const int RunSeed = 20260713;

        private static AIProfile Profile(string id) => AIProfileTable.BuiltIn.Single(p => p.Id == id);

        [Test]
        public void Normal_ScoresAtLeastSixtyPercent_AgainstEasy() => AssertStrongerScoresAtLeast("normal", "easy", pairIndex: 0);

        [Test]
        public void Hard_ScoresAtLeastSixtyPercent_AgainstNormal() => AssertStrongerScoresAtLeast("hard", "normal", pairIndex: 1);

        [Test]
        public void Extreme_ScoresAtLeastSixtyPercent_AgainstHard() => AssertStrongerScoresAtLeast("extreme", "hard", pairIndex: 2);

        [Test]
        public void Impossible_ScoresAtLeastSixtyPercent_AgainstExtreme() => AssertStrongerScoresAtLeast("impossible", "extreme", pairIndex: 3);

        [Test]
        public void Aggressive_ScoresAtLeastSixtyPercent_AgainstNormal() => AssertStrongerScoresAtLeast("aggressive", "normal", pairIndex: 4);

        [Test]
        public void Aggressive_ScoresAtLeastSixtyPercent_AgainstEasy() => AssertStrongerScoresAtLeast("aggressive", "easy", pairIndex: 5);

        // pairIndex distinguishes each of the six adjacency checks above so their seeds never
        // collide — without it, every pairing played at the same positionIndex derived the exact
        // same White/Black RNG streams as every other pairing, which is not a real independent
        // sample even though it looked like one (see TournamentSeeding.DeriveSeed's pairIndex
        // parameter, the same field TournamentSession keys its own pairings by).
        private static void AssertStrongerScoresAtLeast(string strongerId, string weakerId, int pairIndex)
        {
            AIProfile stronger = Profile(strongerId);
            AIProfile weaker = Profile(weakerId);

            // One PendingGame per curated position x color — mirrors TournamentSession's own
            // color-swap layout (each position played once with the stronger profile as White,
            // once as Black) so first-move advantage cancels the same way it does everywhere else
            // in this harness.
            int gameCount = CuratedPositionSuite.Count * 2;
            var games = new PendingGame[gameCount];
            for (int positionIndex = 0; positionIndex < CuratedPositionSuite.Count; positionIndex++)
            {
                games[positionIndex * 2] = new PendingGame(positionIndex, strongerIsWhite: true);
                games[positionIndex * 2 + 1] = new PendingGame(positionIndex, strongerIsWhite: false);
            }

            var scores = new float[gameCount];
            var progressSink = new TestContextProgressSink($"{strongerId} vs {weakerId}");
            int completed = 0;

            using (var threadLocalSimulator = new ThreadLocal<MatchSimulator>(() => new MatchSimulator()))
            {
                Parallel.For(0, gameCount, i =>
                {
                    PendingGame game = games[i];
                    scores[i] = PlayAndScore(threadLocalSimulator.Value, stronger, weaker, game.PositionIndex, pairIndex, game.StrongerIsWhite);

                    int nowCompleted = Interlocked.Increment(ref completed);
                    progressSink.ReportGameCompleted(nowCompleted, gameCount);
                });
            }

            float points = scores.Sum();
            float winRate = points / gameCount;

            Assert.That(winRate, Is.GreaterThanOrEqualTo(FloorWinRate),
                $"{strongerId} scored only {winRate:P1} against {weakerId} over {gameCount} games — below the hard floor, this pairing is a tuning failure.");

            if (winRate < PassWinRate)
            {
                TestContext.WriteLine($"WARN: {strongerId} scored {winRate:P1} against {weakerId} over {gameCount} games — above the hard floor but below the {PassWinRate:P0} pass threshold; worth a dial review.");
            }
        }

        private readonly struct PendingGame
        {
            public readonly int PositionIndex;
            public readonly bool StrongerIsWhite;

            public PendingGame(int positionIndex, bool strongerIsWhite)
            {
                PositionIndex = positionIndex;
                StrongerIsWhite = strongerIsWhite;
            }
        }

        /// <summary>Plays one game and returns the STRONGER profile's score for it (1 = win, 0.5 = draw, 0 = loss),
        /// regardless of which color it played. Builds its own starting position rather than sharing one across
        /// threads — CuratedPositionSuite.Build allocates a fresh BoardState per call, so this is naturally
        /// thread-safe with no extra synchronization.</summary>
        private static float PlayAndScore(
            MatchSimulator simulator, AIProfile stronger, AIProfile weaker,
            int positionIndex, int pairIndex, bool strongerIsWhite)
        {
            BoardState position = CuratedPositionSuite.Build(positionIndex);
            AIProfile whiteProfile = strongerIsWhite ? stronger : weaker;
            AIProfile blackProfile = strongerIsWhite ? weaker : stronger;

            int seedWhite = TournamentSeeding.DeriveSeed(RunSeed, positionIndex, pairIndex, gameIndex: 0, side: 0);
            int seedBlack = TournamentSeeding.DeriveSeed(RunSeed, positionIndex, pairIndex, gameIndex: 0, side: 1);

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
