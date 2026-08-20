using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tooling.Tournament;
using ChessTheBetrayal.Tooling;
using ChessTheBetrayal.Tooling.Strength;

namespace ChessTheBetrayal.Tests.EditMode.AI.Profiles
{
    /// <summary>
    /// Per-commit ladder gate: short games per pairing, asserting only that no stronger tier is
    /// actually LOSING to a weaker one. This is the regression net for the class of bug that inverts
    /// the difficulty ladder — deliberately NOT a precise win-rate check, which is what the
    /// [Explicit] fixtures are for. Kept fast enough to run every commit.
    ///
    /// Only pairings whose tiers genuinely differ in strength are gated here. `extreme` against
    /// `hard`, and `impossible` against `extreme`, score near an even split however long they are
    /// given — measured flat from a heavily compressed clock all the way up to the real one — so a
    /// gate asserting either of them would be settling a coin toss on every commit rather than
    /// testing anything. Both remain measured by the [Explicit] suites, where a wide interval is
    /// reported honestly instead of being turned into a pass or a fail.
    /// </summary>
    [TestFixture]
    public class AIProfileStrengthGateTests
    {
        // A genuinely inverted tier shows up far below half — the bug this exists to catch had the
        // stronger tier scoring five percent. A tier merely playing to a rough draw sits near half.
        private const float NotLosingFloor = 0.40f;

        // Eight positions (sixteen games) rather than a handful: on any machine with a dozen or more
        // cores they still run as a single parallel batch, so the extra games cost almost nothing in
        // wall time and halve the width of the interval below.
        private const int PositionCount = 8;
        private const int PlyCap = 60;

        // What separates a deeper tier from a shallower one IS the depth its clock buys, so squeezing
        // the per-move budget too hard removes the very difference this gate looks for. Measured on
        // hard-versus-normal: about even at 150ms, around 70% at 800ms, around 77% at 1500ms, and
        // around 93% at the real budget. Below roughly a second, both tiers are truncated to the same
        // shallow search and the gate is reading a coin toss — which is what it did for a long time.
        private const int MoveBudgetCapMs = 1500;

        private static void AssertNotLosing(string strongerId, string weakerId, int pairIndex)
        {
            var progress = new TestContextProgressSink($"gate {strongerId} vs {weakerId}");
            float winRate = StrengthLadder.PlayWinRate(strongerId, weakerId, pairIndex,
                PositionCount, PlyCap, MoveBudgetCapMs, progress.ReportGameCompleted);

            int gameCount = PositionCount * 2;
            float margin = TournamentStatistics.WinRateMargin95(gameCount);

            TestContext.WriteLine($"{strongerId} vs {weakerId}: {winRate:P1} +/-{margin:P1} over {gameCount} short games");

            // A plain floor, deliberately: widening it into a confidence-interval test was tried and
            // reverted, because at any sample size this gate can afford the interval is wide enough to
            // swallow a real inversion. A pairing measured deliberately backwards scored 28% and still
            // passed such a test. The floor only works because the budget above leaves these pairings
            // scoring around 70-77%, several standard deviations clear of it.
            Assert.That(winRate, Is.GreaterThanOrEqualTo(NotLosingFloor),
                $"{strongerId} scored only {winRate:P1} +/-{margin:P1} against {weakerId} — a stronger tier losing " +
                "to a weaker one is a ladder inversion, not tuning noise. Run the [Explicit] full suite to confirm " +
                "and diagnose.");
        }

        [Test] public void Normal_DoesNotLose_ToEasy() => AssertNotLosing("normal", "easy", pairIndex: 0);
        [Test] public void Hard_DoesNotLose_ToNormal() => AssertNotLosing("hard", "normal", pairIndex: 1);
        [Test] public void Aggressive_DoesNotLose_ToNormal() => AssertNotLosing("aggressive", "normal", pairIndex: 4);
    }

    /// <summary>
    /// The tier to reach for by default when checking "did this change shift the ladder": full
    /// production-budget fidelity (the real clock a player faces), but only StrengthLadder.
    /// QuickPositionCount positions, so a pairing finishes in low single-digit minutes rather than
    /// the large suite's many. [Explicit] since it still plays real games at real depth — too slow
    /// for every commit, which is what AIProfileStrengthGateTests is for — but its whole point is
    /// being the fast thing to run on demand, with the large suite as the deliberate slow opt-in
    /// below. Its win rate carries a wide confidence interval at this sample size; a shortfall below
    /// the floor is only reported as a genuine failure when the floor sits outside that interval —
    /// see StrengthLadder.AssertStrongerScoresAtLeast.
    /// </summary>
    [TestFixture]
    [Explicit("Quick AI-vs-AI ladder check at each tier's real per-move budget, small sample — run on demand.")]
    [Timeout(10 * 60 * 1000)]
    public class AIProfileStrengthQuickTests
    {
        [Test] public void Normal_ScoresAtLeastSixtyPercent_AgainstEasy() =>
            StrengthLadder.AssertStrongerScoresAtLeast("normal", "easy", pairIndex: 0, StrengthLadder.QuickPositionCount);
        [Test] public void Hard_ScoresAtLeastSixtyPercent_AgainstNormal() =>
            StrengthLadder.AssertStrongerScoresAtLeast("hard", "normal", pairIndex: 1, StrengthLadder.QuickPositionCount);
        [Test] public void Extreme_ScoresAtLeastSixtyPercent_AgainstHard() =>
            StrengthLadder.AssertStrongerScoresAtLeast("extreme", "hard", pairIndex: 2, StrengthLadder.QuickPositionCount);
        [Test] public void Impossible_ScoresAtLeastSixtyPercent_AgainstExtreme() =>
            StrengthLadder.AssertStrongerScoresAtLeast("impossible", "extreme", pairIndex: 3, StrengthLadder.QuickPositionCount);
        [Test] public void Aggressive_ScoresAtLeastSixtyPercent_AgainstNormal() =>
            StrengthLadder.AssertStrongerScoresAtLeast("aggressive", "normal", pairIndex: 4, StrengthLadder.QuickPositionCount);
        [Test] public void Aggressive_ScoresAtLeastSixtyPercent_AgainstEasy() =>
            StrengthLadder.AssertStrongerScoresAtLeast("aggressive", "easy", pairIndex: 5, StrengthLadder.QuickPositionCount);
    }

    /// <summary>
    /// The large statistical ladder check — an explicit opt-in given its runtime, never the default
    /// reach-for-it tier (see AIProfileStrengthQuickTests above). Plays the whole curated suite at
    /// each tier's real per-move budget for the tightest confidence interval this harness can offer.
    /// Run on demand or from a nightly job when a dial or search change might have shifted the
    /// ordering and the Quick tier's smaller sample wasn't decisive enough to be sure.
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
        [Test] public void Normal_ScoresAtLeastSixtyPercent_AgainstEasy() =>
            StrengthLadder.AssertStrongerScoresAtLeast("normal", "easy", pairIndex: 0, CuratedPositionSuite.Count);
        [Test] public void Hard_ScoresAtLeastSixtyPercent_AgainstNormal() =>
            StrengthLadder.AssertStrongerScoresAtLeast("hard", "normal", pairIndex: 1, CuratedPositionSuite.Count);
        [Test] public void Extreme_ScoresAtLeastSixtyPercent_AgainstHard() =>
            StrengthLadder.AssertStrongerScoresAtLeast("extreme", "hard", pairIndex: 2, CuratedPositionSuite.Count);
        [Test] public void Impossible_ScoresAtLeastSixtyPercent_AgainstExtreme() =>
            StrengthLadder.AssertStrongerScoresAtLeast("impossible", "extreme", pairIndex: 3, CuratedPositionSuite.Count);
        [Test] public void Aggressive_ScoresAtLeastSixtyPercent_AgainstNormal() =>
            StrengthLadder.AssertStrongerScoresAtLeast("aggressive", "normal", pairIndex: 4, CuratedPositionSuite.Count);
        [Test] public void Aggressive_ScoresAtLeastSixtyPercent_AgainstEasy() =>
            StrengthLadder.AssertStrongerScoresAtLeast("aggressive", "easy", pairIndex: 5, CuratedPositionSuite.Count);
    }
}
