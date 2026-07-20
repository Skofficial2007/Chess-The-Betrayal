using NUnit.Framework;
using ChessTheBetrayal.EditorTools.Benchmark;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the honesty rule this ticket added to the drift analyzer: a win rate below the hard
    /// floor is only reported as a real Fail when the sample is large enough that the floor sits
    /// outside its own 95% confidence interval. Below that sample size, the exact same shortfall
    /// reports Inconclusive instead — the instrument admitting it cannot yet tell a real regression
    /// from ordinary sampling noise, rather than asserting a confident answer it has no power for.
    /// </summary>
    [TestFixture]
    public class WinRateConfidenceTests
    {
        [Test]
        public void WinRateMargin95_ShrinksAsGamesIncrease()
        {
            float marginAt10 = TournamentStatistics.WinRateMargin95(10);
            float marginAt100 = TournamentStatistics.WinRateMargin95(100);
            float marginAt1000 = TournamentStatistics.WinRateMargin95(1000);

            Assert.That(marginAt100, Is.LessThan(marginAt10));
            Assert.That(marginAt1000, Is.LessThan(marginAt100));
        }

        [Test]
        public void WinRateMargin95_AtZeroGames_SaturatesAtOne()
        {
            Assert.That(TournamentStatistics.WinRateMargin95(0), Is.EqualTo(1f));
        }

        private static BenchmarkReport ReportWithPair(string subject, string opponent, float winRate, int games)
        {
            var report = new BenchmarkReport { RunSeed = 1, Mode = "Quick" };
            int subjectWins = (int)(winRate * games);
            report.PairResults.Add(new PairResult(subject, opponent, games, subjectWins, games - subjectWins, 0));
            return report;
        }

        [Test]
        public void Analyze_BelowFloor_SmallSample_FloorInsideConfidenceInterval_ReportsInconclusive()
        {
            // 8 games at 50% has a huge margin (WinRateMargin95(8) ~= 34.6 points), so 50% +/- 34.6
            // comfortably reaches past the 55% floor — the floor sits inside this sample's own
            // confidence interval, so this shortfall cannot be distinguished from noise yet.
            BenchmarkReport current = ReportWithPair("hard", "normal", winRate: 0.50f, games: 8);

            var findings = BenchmarkDriftAnalyzer.Analyze(current, baseline: null);

            Assert.That(findings, Has.Some.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Inconclusive));
            Assert.That(findings, Has.None.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Fail));
        }

        [Test]
        public void Analyze_BelowFloor_LargeSample_FloorOutsideConfidenceInterval_ReportsFail()
        {
            // 400 games at 40% has a tight margin (WinRateMargin95(400) ~= 4.9 points); 40% + 4.9%
            // is still well under the 55% floor, so this really is a confident failure, not noise.
            BenchmarkReport current = ReportWithPair("hard", "normal", winRate: 0.40f, games: 400);

            var findings = BenchmarkDriftAnalyzer.Analyze(current, baseline: null);

            Assert.That(findings, Has.Some.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Fail));
            Assert.That(findings, Has.None.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Inconclusive));
        }

        [Test]
        public void Analyze_AboveFloor_NeverReportsInconclusiveOrFail_RegardlessOfSampleSize()
        {
            BenchmarkReport current = ReportWithPair("hard", "normal", winRate: 0.65f, games: 8);

            var findings = BenchmarkDriftAnalyzer.Analyze(current, baseline: null);

            Assert.That(findings, Has.None.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Fail));
            Assert.That(findings, Has.None.Matches<DriftFinding>(f => f.Severity == DriftSeverity.Inconclusive));
        }

        [Test]
        public void ToPlainText_PairLine_IncludesConfidenceMargin()
        {
            BenchmarkReport report = ReportWithPair("hard", "normal", winRate: 0.65f, games: 40);

            string text = BenchmarkReportFormatter.ToPlainText(report, baseline: null);

            Assert.That(text, Does.Contain("+/-"));
        }

        [Test]
        public void ToMarkdown_PairTable_IncludesConfidenceColumn()
        {
            BenchmarkReport report = ReportWithPair("hard", "normal", winRate: 0.65f, games: 40);

            string markdown = BenchmarkReportFormatter.ToMarkdown(report);

            Assert.That(markdown, Does.Contain("95% CI"));
            Assert.That(markdown, Does.Contain("+/-"));
        }
    }
}
