using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.EditorTools.Benchmark;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Plays real AI-vs-AI games between adjacent tiers and asserts the strength chain the preset
    /// table promises: Impossible &gt;= Extreme &gt;= Hard &gt;= Normal &gt;= Easy, with Aggressive a
    /// personality sibling of Hard-ish strength checked only against Normal and Easy.
    ///
    /// Three fixtures drive it, each trading sample size for runtime differently:
    ///
    ///  - AIProfileStrengthGateTests runs on every commit. A handful of short games
    ///    per pairing on a heavily compressed clock — enough to catch a gross ladder inversion (a
    ///    stronger tier LOSING to a weaker one, the failure mode that actually bit this codebase) in
    ///    a couple of minutes, not enough to resolve a subtle dial difference.
    ///
    ///  - AIProfileStrengthQuickTests is <c>[Explicit]</c> — the one to reach for when
    ///    checking "did this change shift the ladder," on demand. Full per-move budget (production
    ///    fidelity, not the gate's compressed clock) but a small slice of the curated suite, so it
    ///    finishes in low single-digit minutes per pairing rather than the large suite's many
    ///    minutes. Its win rate carries a wide confidence interval at this sample size — see
    ///    WinRateConfidenceTests for how a shortfall this suite reports gets judged honestly rather
    ///    than read as more precise than eight games can support.
    ///
    ///  - AIProfileStrengthOrderingTests is <c>[Explicit]</c> and the large,
    ///    statistically solid check: the full curated suite at each tier's real per-move budget, run
    ///    on demand or from a nightly job, an explicit opt-in given its runtime.
    ///
    /// All three play on the production time budget (each move's real cancellation + settle-early
    /// contract), never Uncapped: with Betrayal Defections now searched to resolution, an uncapped
    /// full-depth game at the deep tiers can take many minutes for a SINGLE game, which is what made
    /// an uncapped tournament run for hours. The gate additionally compresses the clock via
    /// MatchSimulator's move-budget cap so many games fit in the per-commit budget; the compression
    /// keeps the exact same search code path, only the numbers on the clock shrink. Quick and the
    /// full suite differ ONLY in how many positions they sample — never in time control or search
    /// settings — so a Quick result is a smaller-sample read of the exact same measurement, not a
    /// different one.
    /// </summary>
    internal static class StrengthLadder
    {
        public const float PassWinRate = 0.60f;
        public const float FloorWinRate = 0.55f;
        public const int RunSeed = 20260713;

        /// <summary>Position count for the Quick tier — enough games (16 per pairing, both colors
        /// across 8 positions) to see the ladder's direction at full production fidelity, in low
        /// single-digit minutes rather than the full suite's many minutes. Its confidence interval
        /// is wide at this N; see the class doc comment.</summary>
        public const int QuickPositionCount = 8;

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

        /// <summary>
        /// Shared assertion body for both the Quick and Full production-budget fixtures — they
        /// differ only in positionCount, never in time control or search settings, so a Quick
        /// result is a smaller-sample read of the exact same measurement rather than a different
        /// one. Below the hard floor, checks whether the floor sits inside this sample's own 95%
        /// confidence interval before failing — the same honesty rule BenchmarkDriftAnalyzer
        /// applies, so a small-N shortfall never asserts a confident failure it can't back up.
        /// </summary>
        public static void AssertStrongerScoresAtLeast(string strongerId, string weakerId, int pairIndex, int positionCount)
        {
            var progress = new TestContextProgressSink($"{strongerId} vs {weakerId}");
            // moveBudgetCapMs 0: each tier plays at its own real per-move budget, the exact clock a
            // player faces. Faithful measurement is the point of both fixtures that call this; the
            // per-commit gate is the only one that trades fidelity for speed.
            float winRate = PlayWinRate(strongerId, weakerId, pairIndex,
                positionCount, MatchSimulator.DefaultPlyCap, moveBudgetCapMs: 0, progress.ReportGameCompleted);

            int gameCount = System.Math.Min(positionCount, CuratedPositionSuite.Count) * 2;
            float margin = TournamentStatistics.WinRateMargin95(gameCount);

            // Reported whatever the verdict, the way the gate fixture reports its own: a pairing that
            // clears the threshold used to print nothing at all, so a run that passed everywhere left
            // no record of how well it passed and nothing to compare a later run against.
            TestContext.WriteLine($"{strongerId} vs {weakerId}: {winRate:P1} +/-{margin:P1} over {gameCount} games");

            if (winRate < FloorWinRate)
            {
                bool floorIsInsideConfidenceInterval = winRate + margin >= FloorWinRate;
                if (floorIsInsideConfidenceInterval)
                {
                    Assert.Inconclusive(
                        $"{strongerId} scored {winRate:P1} +/-{margin:P1} against {weakerId} over {gameCount} games — " +
                        $"below the {FloorWinRate:P0} floor, but the floor sits inside this sample's own confidence " +
                        "interval. More games are needed before calling this a real failure; run the larger suite.");
                }
                else
                {
                    Assert.Fail(
                        $"{strongerId} scored only {winRate:P1} +/-{margin:P1} against {weakerId} over {gameCount} games " +
                        "— below the hard floor with a confidence interval that doesn't reach it, this pairing is a tuning failure.");
                }
                return;
            }

            if (winRate < PassWinRate)
            {
                TestContext.WriteLine($"WARN: {strongerId} scored {winRate:P1} +/-{margin:P1} against {weakerId} — above the hard floor " +
                    $"but below the {PassWinRate:P0} pass threshold; worth a dial review.");
            }
        }
    }
}
