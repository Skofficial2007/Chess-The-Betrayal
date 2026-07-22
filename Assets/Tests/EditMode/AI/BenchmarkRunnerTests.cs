using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.EditorTools.Benchmark;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the benchmark artifact's shape and reproducibility, plus each drift-threshold rule in
    /// isolation against synthetic reports. RunAll's own contract is exercised against a fast
    /// fixture roster (same six ids as AIProfileTable.BuiltIn, but shallow depths) rather than the
    /// real built-in table — a real tournament at each tier's configured depth costs the same many
    /// minutes AIProfileStrengthOrderingTests does, which the menu/batchmode entry points pay, not
    /// this per-commit suite.
    /// </summary>
    [TestFixture]
    public class BenchmarkRunnerTests
    {
        /// <summary>Short enough that a depth 1-2 game against the curated positions finishes in a
        /// handful of plies rather than running out the search clock toward MatchSimulator's real
        /// 120-ply cap across every one of Full mode's 600 games.</summary>
        private const int TestPlyCap = 10;

        /// <summary>Same six ids the shipped table uses (so Quick mode's hardcoded adjacent pairs
        /// still resolve), but depth 1-2 everywhere — fast enough for a per-commit suite while
        /// still exercising the exact code paths RunAll uses against the real roster.</summary>
        private static readonly IReadOnlyList<AIProfile> FastFixtureRoster = new[]
        {
            new AIProfile("easy", maxDepth: 1, timeBudget: new AITimeBudget(500, 750), blunderRate: 0.3f, blunderMarginCp: 120, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 30, useOpeningBook: false),
            new AIProfile("normal", maxDepth: 1, timeBudget: new AITimeBudget(500, 750), blunderRate: 0.1f, blunderMarginCp: 80, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 20, useOpeningBook: false),
            new AIProfile("hard", maxDepth: 2, timeBudget: new AITimeBudget(500, 750), blunderRate: 0.02f, blunderMarginCp: 40, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 15, useOpeningBook: false),
            new AIProfile("aggressive", maxDepth: 1, timeBudget: new AITimeBudget(500, 750), blunderRate: 0.05f, blunderMarginCp: 60, betrayalAggression: 0.7f, attackDefenseBias: 1.5f, tieBreakWindowCp: 25, useOpeningBook: false),
            new AIProfile("extreme", maxDepth: 2, timeBudget: new AITimeBudget(500, 750), blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0.3f, attackDefenseBias: 1.2f, tieBreakWindowCp: 10, useOpeningBook: false),
            new AIProfile("impossible", maxDepth: 1, timeBudget: new AITimeBudget(500, 750), blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 0, useOpeningBook: false),
        };

        [Test]
        public void RunAll_QuickMode_SameSeed_ProducesReproducibleWinRates()
        {
            // Uncapped deliberately: under MatchTimeControl.ProductionBudget (the default for a
            // real run), a search that hits its clock can complete a different depth run to run
            // depending on CPU contention — genuinely reproducing bit-identical results needs the
            // depth-bound path instead of the time-bound one. See TournamentSession.CreateQuick's
            // own doc comment for this trade-off.
            BenchmarkReport report1 = BenchmarkRunner.RunAll(runSeed: 999, BenchmarkMode.Quick, FastFixtureRoster, TestPlyCap, timeControl: MatchTimeControl.Uncapped);
            BenchmarkReport report2 = BenchmarkRunner.RunAll(runSeed: 999, BenchmarkMode.Quick, FastFixtureRoster, TestPlyCap, timeControl: MatchTimeControl.Uncapped);

            Assert.That(report1.PairResults.Count, Is.EqualTo(report2.PairResults.Count));
            for (int i = 0; i < report1.PairResults.Count; i++)
            {
                Assert.That(report2.PairResults[i].SubjectWinRate, Is.EqualTo(report1.PairResults[i].SubjectWinRate),
                    $"Pair {report1.PairResults[i].Subject} vs {report1.PairResults[i].Opponent} must reproduce bit-identically under the same run seed.");
            }
        }

        [Test]
        public void RunAll_QuickMode_CoversExactlyTheAdjacentStrengthChainPairs()
        {
            BenchmarkReport report = BenchmarkRunner.RunAll(runSeed: 1, BenchmarkMode.Quick, FastFixtureRoster, TestPlyCap);

            Assert.That(report.PairResults.Count, Is.EqualTo(6),
                "Quick mode is defined as the six adjacent-chain pairs the preset table promises.");
        }

        [Test]
        public void RunAll_FullMode_CoversAllFifteenRoundRobinPairs()
        {
            BenchmarkReport report = BenchmarkRunner.RunAll(runSeed: 1, BenchmarkMode.Full, FastFixtureRoster, TestPlyCap);

            Assert.That(report.PairResults.Count, Is.EqualTo(15),
                "Six tiers means 6*5/2 = 15 unordered pairs in the full round-robin matrix.");
        }

        [Test]
        public void RunAll_EveryPairResult_GamesEqualsWinsPlusLossesPlusDraws()
        {
            BenchmarkReport report = BenchmarkRunner.RunAll(runSeed: 5, BenchmarkMode.Quick, FastFixtureRoster, TestPlyCap);

            foreach (var pair in report.PairResults)
            {
                Assert.That(pair.SubjectWins + pair.OpponentWins + pair.Draws, Is.EqualTo(pair.Games),
                    $"{pair.Subject} vs {pair.Opponent}: win/loss/draw counts must sum to the game count.");
            }
        }

        [Test]
        public void RunAll_WithWatchdog_NormalRun_CompletesWithoutTripping()
        {
            // The watchdog must not be a false-positive machine — a fast fixture run finishing
            // well inside its (generously derived) stall window must return a normal report, not
            // throw TournamentStalledException.
            BenchmarkReport report = BenchmarkRunner.RunAll(runSeed: 21, BenchmarkMode.Quick,
                FastFixtureRoster, TestPlyCap, useWatchdog: true);

            Assert.That(report.PairResults.Count, Is.EqualTo(6));
        }

        [Test]
        public void TournamentStalledException_Message_NamesGamesCompletedAndRunDirectory()
        {
            var ex = new TournamentStalledException("no game finished for 720s (stall window 720s).",
                "C:/some/run/dir", gamesCompleted: 12, totalGames: 48);

            Assert.That(ex.Message, Does.Contain("12/48"));
            Assert.That(ex.Message, Does.Contain("C:/some/run/dir"));
            Assert.That(ex.RunDirectory, Is.EqualTo("C:/some/run/dir"));
            Assert.That(ex.GamesCompleted, Is.EqualTo(12));
            Assert.That(ex.TotalGames, Is.EqualTo(48));
        }

        [Test]
        public void TournamentStalledException_NoRunDirectory_MessageSaysNothingWasPersisted()
        {
            var ex = new TournamentStalledException("stalled", runDirectory: null, gamesCompleted: 0, totalGames: 10);

            Assert.That(ex.Message, Does.Contain("nothing was persisted"));
        }

        [Test]
        public void RunAll_TierPerformances_CoverEveryTierThatAppearedInAPairing()
        {
            BenchmarkReport report = BenchmarkRunner.RunAll(runSeed: 5, BenchmarkMode.Quick, FastFixtureRoster, TestPlyCap);

            var expectedTiers = new HashSet<string>();
            foreach (var pair in report.PairResults)
            {
                expectedTiers.Add(pair.Subject);
                expectedTiers.Add(pair.Opponent);
            }

            foreach (string tierId in expectedTiers)
            {
                Assert.That(report.FindTier(tierId), Is.Not.Null, $"Tier '{tierId}' appeared in a pairing but has no recorded performance.");
                Assert.That(report.FindTier(tierId).MovesSampled, Is.GreaterThan(0));
            }
        }

        // --- Drift analyzer: each threshold in isolation, against synthetic reports ---

        private static BenchmarkReport ReportWithPair(string subject, string opponent, float winRate, int games = 40)
        {
            var report = new BenchmarkReport { RunSeed = 1, Mode = "Quick" };
            int subjectWins = (int)(winRate * games);
            report.PairResults.Add(new PairResult(subject, opponent, games, subjectWins, games - subjectWins, 0));
            return report;
        }

        [Test]
        public void Analyze_WinRateBelowHardFloor_LargeEnoughSampleToBeConfident_ProducesFailFinding()
        {
            // 400 games at 40% keeps the 95% confidence interval (~+/-4.9 points) well clear of the
            // 55% floor — a genuinely confident failure, not a small sample that merely looks bad.
            // See WinRateConfidenceTests for the dedicated coverage of the Inconclusive case this
            // same shortfall produces at a small N (e.g. the suite's default 40-game helper).
            BenchmarkReport current = ReportWithPair("hard", "normal", winRate: 0.40f, games: 400);

            var findings = BenchmarkDriftAnalyzer.Analyze(current, baseline: null);

            Assert.That(findings, Has.Some.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Fail));
        }

        [Test]
        public void Analyze_WinRateAboveHardFloor_NoOrderingFailFinding()
        {
            BenchmarkReport current = ReportWithPair("hard", "normal", winRate: 0.65f);

            var findings = BenchmarkDriftAnalyzer.Analyze(current, baseline: null);

            Assert.That(findings, Has.None.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Fail));
        }

        [Test]
        public void Analyze_WinRateDriftedFromBaseline_ProducesWarnFinding()
        {
            BenchmarkReport baseline = ReportWithPair("hard", "normal", winRate: 0.70f);
            BenchmarkReport current = ReportWithPair("hard", "normal", winRate: 0.50f); // 20-point drop, still above the 55% floor

            var findings = BenchmarkDriftAnalyzer.Analyze(current, baseline);

            Assert.That(findings, Has.Some.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Warn));
        }

        [Test]
        public void Analyze_WinRateWithinDriftTolerance_NoWarnFinding()
        {
            BenchmarkReport baseline = ReportWithPair("hard", "normal", winRate: 0.65f);
            BenchmarkReport current = ReportWithPair("hard", "normal", winRate: 0.68f); // 3-point drift, inside the 15-point tolerance

            var findings = BenchmarkDriftAnalyzer.Analyze(current, baseline);

            Assert.That(findings, Is.Empty);
        }

        [Test]
        public void Analyze_TimingOverDoDThreshold_ProducesFailFinding()
        {
            var current = new BenchmarkReport { RunSeed = 1, Mode = "Quick" };
            current.TierPerformances.Add(new TierPerformance("hard", movesSampled: 10,
                meanNodesPerMove: 100000, meanMsPerMove: 5000, deepestCompletedDepth: 7,
                meanCompletedDepth: 7, shallowestCompletedDepth: 7, depthHistogram: null, observedBlunderActuationRate: 0f));

            var findings = BenchmarkDriftAnalyzer.Analyze(current, baseline: null);

            Assert.That(findings, Has.Some.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Fail && f.Message.Contains("hard")));
        }

        [Test]
        public void Analyze_TimingUnderDoDThreshold_NoTimingFailFinding()
        {
            var current = new BenchmarkReport { RunSeed = 1, Mode = "Quick" };
            current.TierPerformances.Add(new TierPerformance("easy", movesSampled: 10,
                meanNodesPerMove: 3000, meanMsPerMove: 300, deepestCompletedDepth: 3,
                meanCompletedDepth: 3, shallowestCompletedDepth: 3, depthHistogram: null, observedBlunderActuationRate: 0.3f));

            var findings = BenchmarkDriftAnalyzer.Analyze(current, baseline: null);

            Assert.That(findings, Has.None.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Fail));
        }

        [Test]
        public void Analyze_NodesPerSecondRegression_ProducesWarnFinding()
        {
            var baseline = new BenchmarkReport { RunSeed = 1, Mode = "Quick" };
            baseline.TierPerformances.Add(new TierPerformance("hard", movesSampled: 10,
                meanNodesPerMove: 100000, meanMsPerMove: 1000, deepestCompletedDepth: 7,
                meanCompletedDepth: 7, shallowestCompletedDepth: 7, depthHistogram: null, observedBlunderActuationRate: 0f)); // 100k nodes/sec

            var current = new BenchmarkReport { RunSeed = 1, Mode = "Quick" };
            current.TierPerformances.Add(new TierPerformance("hard", movesSampled: 10,
                meanNodesPerMove: 60000, meanMsPerMove: 1000, deepestCompletedDepth: 7,
                meanCompletedDepth: 7, shallowestCompletedDepth: 7, depthHistogram: null, observedBlunderActuationRate: 0f)); // 60k nodes/sec, 40% drop

            var findings = BenchmarkDriftAnalyzer.Analyze(current, baseline);

            Assert.That(findings, Has.Some.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Warn && f.Message.Contains("nodes/sec")));
        }

        [Test]
        public void Analyze_BlunderActuationDriftedFromConfiguredRate_ProducesWarnFinding()
        {
            // "easy" is configured with BlunderRate 0.30 in AIProfileTable.BuiltIn.
            var current = new BenchmarkReport { RunSeed = 1, Mode = "Quick" };
            current.TierPerformances.Add(new TierPerformance("easy", movesSampled: 100,
                meanNodesPerMove: 3000, meanMsPerMove: 300, deepestCompletedDepth: 3,
                meanCompletedDepth: 3, shallowestCompletedDepth: 3, depthHistogram: null, observedBlunderActuationRate: 0.05f));

            var findings = BenchmarkDriftAnalyzer.Analyze(current, baseline: null);

            Assert.That(findings, Has.Some.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Warn && f.Message.Contains("blunder-actuation")));
        }

        // --- Baseline round-trip ---

        [Test]
        public void BaselineIO_WriteThenRead_RoundTripsPairAndTierData()
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                var written = new BenchmarkReport { RunSeed = 42, Mode = "Full" };
                written.PairResults.Add(new PairResult("hard", "normal", 40, 26, 10, 4));
                written.TierPerformances.Add(new TierPerformance("hard", 80, 95000, 2800, 7, 7, 7, null, 0.02f,
                    actsPlayed: 5, actsResolvedByRetribution: 3, actsResolvedByDefection: 2));

                BenchmarkBaselineIO.Write(written, path);
                BenchmarkReport read = BenchmarkBaselineIO.TryRead(path);

                Assert.That(read.RunSeed, Is.EqualTo(42));
                Assert.That(read.PairResults.Count, Is.EqualTo(1));
                Assert.That(read.PairResults[0].SubjectWinRate, Is.EqualTo(written.PairResults[0].SubjectWinRate));
                Assert.That(read.TierPerformances.Count, Is.EqualTo(1));
                Assert.That(read.TierPerformances[0].MeanMsPerMove, Is.EqualTo(2800));
                Assert.That(read.TierPerformances[0].ActsPlayed, Is.EqualTo(5));
                Assert.That(read.TierPerformances[0].ActsResolvedByRetribution, Is.EqualTo(3));
                Assert.That(read.TierPerformances[0].ActsResolvedByDefection, Is.EqualTo(2));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public void BaselineIO_TryRead_MissingFile_ReturnsNull()
        {
            string missingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString());

            BenchmarkReport result = BenchmarkBaselineIO.TryRead(missingPath);

            Assert.That(result, Is.Null);
        }
    }
}
