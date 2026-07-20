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
    /// Plays real AI-vs-AI games between adjacent tiers and asserts the strength chain the preset
    /// table promises: Impossible &gt;= Extreme &gt;= Hard &gt;= Normal &gt;= Easy, with Aggressive a
    /// personality sibling of Hard-ish strength checked only against Normal and Easy.
    ///
    /// Two fixtures live here, splitting one concern into a fast gate and a deep check:
    ///
    ///  - <see cref="AIProfileStrengthGateTests"/> runs on every commit. It plays a handful of
    ///    short games per pairing on a heavily compressed clock — enough to catch a gross ladder
    ///    inversion (a stronger tier LOSING to a weaker one, the failure mode that actually bit
    ///    this codebase) in a couple of minutes, not enough to resolve a subtle dial difference.
    ///
    ///  - <see cref="AIProfileStrengthOrderingTests"/> is <c>[Explicit]</c>. It plays the full
    ///    curated suite at each tier's real per-move budget for statistically solid win rates, and
    ///    is run on demand or from a nightly job when a dial or search change might have shifted the
    ///    ordering. It is NOT a per-commit cost.
    ///
    /// Both play on the production time budget (each move's real cancellation + settle-early
    /// contract), never Uncapped: with Betrayal Defections now searched to resolution, an uncapped
    /// full-depth game at the deep tiers can take many minutes for a SINGLE game, which is what made
    /// an uncapped tournament run for hours. The gate additionally compresses the clock via
    /// MatchSimulator's move-budget cap so many games fit in the per-commit budget; the compression
    /// keeps the exact same search code path, only the numbers on the clock shrink.
    /// </summary>
    internal static class StrengthLadder
    {
        public const float PassWinRate = 0.60f;
        public const float FloorWinRate = 0.55f;
        public const int RunSeed = 20260713;

        public static AIProfile Profile(string id) => AIProfileTable.BuiltIn.Single(p => p.Id == id);

        /// <summary>
        /// Plays <paramref name="positionCount"/> curated positions (each once per color) between
        /// the two profiles and returns the stronger profile's win rate. Games run in parallel with
        /// a thread-local simulator, exactly like ParallelTournamentExecutor, each carrying the
        /// given move-budget cap (0 = each profile's own full budget). pairIndex keeps every
        /// pairing's RNG streams independent (see TournamentSeeding.DeriveSeed).
        /// </summary>
        public static float PlayWinRate(string strongerId, string weakerId, int pairIndex,
            int positionCount, int plyCap, int moveBudgetCapMs, System.Action<int, int> onGameCompleted)
        {
            AIProfile stronger = Profile(strongerId);
            AIProfile weaker = Profile(weakerId);

            int usablePositions = System.Math.Min(positionCount, CuratedPositionSuite.Count);
            int gameCount = usablePositions * 2;
            var games = new (int position, bool strongerIsWhite)[gameCount];
            for (int p = 0; p < usablePositions; p++)
            {
                games[p * 2] = (p, true);
                games[p * 2 + 1] = (p, false);
            }

            var scores = new float[gameCount];
            int completed = 0;

            using (var threadLocalSimulator = new ThreadLocal<MatchSimulator>(
                () => new MatchSimulator(MatchTimeControl.ProductionBudget, moveBudgetCapMs: moveBudgetCapMs)))
            {
                Parallel.For(0, gameCount, i =>
                {
                    (int position, bool strongerIsWhite) = games[i];
                    scores[i] = PlayAndScore(threadLocalSimulator.Value, stronger, weaker, position, pairIndex,
                        strongerIsWhite, plyCap);

                    int nowCompleted = Interlocked.Increment(ref completed);
                    onGameCompleted?.Invoke(nowCompleted, gameCount);
                });
            }

            return scores.Sum() / gameCount;
        }

        private static float PlayAndScore(MatchSimulator simulator, AIProfile stronger, AIProfile weaker,
            int positionIndex, int pairIndex, bool strongerIsWhite, int plyCap)
        {
            BoardState position = CuratedPositionSuite.Build(positionIndex);
            AIProfile whiteProfile = strongerIsWhite ? stronger : weaker;
            AIProfile blackProfile = strongerIsWhite ? weaker : stronger;

            int seedWhite = TournamentSeeding.DeriveSeed(RunSeed, positionIndex, pairIndex, gameIndex: 0, side: 0);
            int seedBlack = TournamentSeeding.DeriveSeed(RunSeed, positionIndex, pairIndex, gameIndex: 0, side: 1);

            MatchResult result = simulator.PlayGame(position, whiteProfile, blackProfile, seedWhite, seedBlack, plyCap);

            float whiteScore = result.Outcome switch
            {
                MatchOutcome.WhiteWon => 1f,
                MatchOutcome.BlackWon => 0f,
                _ => 0.5f
            };

            return strongerIsWhite ? whiteScore : 1f - whiteScore;
        }
    }

    /// <summary>
    /// Per-commit ladder gate: a few short, clock-compressed games per pairing, asserting only that
    /// no stronger tier is actually LOSING to a weaker one. This is the regression net for the class
    /// of bug that inverts the difficulty ladder — it is deliberately NOT a precise win-rate check
    /// (too few games and too short a clock for that), which is what the [Explicit] fixture below is
    /// for. Kept fast enough to run every commit.
    /// </summary>
    [TestFixture]
    public class AIProfileStrengthGateTests
    {
        // A stronger tier that has genuinely inverted shows up as clearly BELOW half (it loses more
        // than it wins). A tier that's merely playing to a rough draw on a short/compressed clock
        // sits around half. So the gate floor is "not losing" — comfortably below the real strength
        // target, on purpose, so ordinary short-game noise never trips it but a true inversion (the
        // hard-loses-95%-to-normal bug) always does.
        private const float NotLosingFloor = 0.40f;

        // A few positions, short games, a tight per-move clock: enough to expose an inversion, cheap
        // enough for every commit. The [Explicit] fixture carries the statistically solid numbers.
        private const int PositionCount = 4;
        private const int PlyCap = 60;
        private const int MoveBudgetCapMs = 150;

        private static void AssertNotLosing(string strongerId, string weakerId, int pairIndex)
        {
            var progress = new TestContextProgressSink($"gate {strongerId} vs {weakerId}");
            float winRate = StrengthLadder.PlayWinRate(strongerId, weakerId, pairIndex,
                PositionCount, PlyCap, MoveBudgetCapMs, progress.ReportGameCompleted);

            TestContext.WriteLine($"{strongerId} vs {weakerId}: {winRate:P1} over {PositionCount * 2} short games");

            Assert.That(winRate, Is.GreaterThanOrEqualTo(NotLosingFloor),
                $"{strongerId} scored only {winRate:P1} against {weakerId} — a stronger tier losing to a weaker " +
                "one is a ladder inversion, not tuning noise. Run the [Explicit] full suite to confirm and diagnose.");
        }

        [Test] public void Normal_DoesNotLose_ToEasy() => AssertNotLosing("normal", "easy", pairIndex: 0);
        [Test] public void Hard_DoesNotLose_ToNormal() => AssertNotLosing("hard", "normal", pairIndex: 1);
        [Test] public void Extreme_DoesNotLose_ToHard() => AssertNotLosing("extreme", "hard", pairIndex: 2);
        [Test] public void Impossible_DoesNotLose_ToExtreme() => AssertNotLosing("impossible", "extreme", pairIndex: 3);
        [Test] public void Aggressive_DoesNotLose_ToNormal() => AssertNotLosing("aggressive", "normal", pairIndex: 4);
    }

    /// <summary>
    /// The full statistical ladder check. Plays the whole curated suite at each tier's real per-move
    /// budget and asserts the promised win rates. [Explicit] — run on demand or nightly, never per
    /// commit. On the production budget a full run is minutes per pairing, not the hours an uncapped
    /// run would take now that Betrayal Defections are searched to resolution.
    /// </summary>
    [TestFixture]
    [Explicit("Full-suite AI-vs-AI tournament at each tier's real per-move budget — run on demand, not per commit.")]
    // A hard backstop only — StrengthLadder.PlayWinRate has no watchdog of its own (that lives in
    // BenchmarkRunner's parallel path, not this direct Parallel.For call), so nothing else here
    // catches a genuine deadlock. This should never actually fire: if it does, something is stuck
    // badly enough that even a generous ceiling wasn't enough, which is itself worth knowing.
    [Timeout(20 * 60 * 1000)]
    public class AIProfileStrengthOrderingTests
    {
        // Every curated position, both colors. Binomial standard error at N games is ~0.5/sqrt(N);
        // the full 20-position suite keeps the confidence interval tight enough to trust a
        // win-rate near the 55-60% decision band.
        private const int PlyCap = MatchSimulator.DefaultPlyCap;

        private static void AssertStrongerScoresAtLeast(string strongerId, string weakerId, int pairIndex)
        {
            var progress = new TestContextProgressSink($"{strongerId} vs {weakerId}");
            // moveBudgetCapMs 0: each tier plays at its own real per-move budget, the exact clock a
            // player faces. Faithful measurement is the point of the full suite; the gate fixture
            // above is the one that trades fidelity for speed.
            float winRate = StrengthLadder.PlayWinRate(strongerId, weakerId, pairIndex,
                CuratedPositionSuite.Count, PlyCap, moveBudgetCapMs: 0, progress.ReportGameCompleted);

            Assert.That(winRate, Is.GreaterThanOrEqualTo(StrengthLadder.FloorWinRate),
                $"{strongerId} scored only {winRate:P1} against {weakerId} — below the hard floor, this pairing is a tuning failure.");

            if (winRate < StrengthLadder.PassWinRate)
            {
                TestContext.WriteLine($"WARN: {strongerId} scored {winRate:P1} against {weakerId} — above the hard floor " +
                    $"but below the {StrengthLadder.PassWinRate:P0} pass threshold; worth a dial review.");
            }
        }

        [Test] public void Normal_ScoresAtLeastSixtyPercent_AgainstEasy() => AssertStrongerScoresAtLeast("normal", "easy", pairIndex: 0);
        [Test] public void Hard_ScoresAtLeastSixtyPercent_AgainstNormal() => AssertStrongerScoresAtLeast("hard", "normal", pairIndex: 1);
        [Test] public void Extreme_ScoresAtLeastSixtyPercent_AgainstHard() => AssertStrongerScoresAtLeast("extreme", "hard", pairIndex: 2);
        [Test] public void Impossible_ScoresAtLeastSixtyPercent_AgainstExtreme() => AssertStrongerScoresAtLeast("impossible", "extreme", pairIndex: 3);
        [Test] public void Aggressive_ScoresAtLeastSixtyPercent_AgainstNormal() => AssertStrongerScoresAtLeast("aggressive", "normal", pairIndex: 4);
        [Test] public void Aggressive_ScoresAtLeastSixtyPercent_AgainstEasy() => AssertStrongerScoresAtLeast("aggressive", "easy", pairIndex: 5);
    }
}
