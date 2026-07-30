using System;
using NUnit.Framework;
using ChessTheBetrayal.AI.DeviceBenchmark;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// BenchmarkReport touches no Unity API and holds no threading primitive of its own, so every
    /// one of these runs with no scene or Play mode involved — the whole point of pulling report
    /// assembly out of the MonoBehaviour.
    /// </summary>
    [TestFixture]
    public class BenchmarkReportTests
    {
        [Test]
        public void Render_IncludesTheRunId()
        {
            var report = new BenchmarkReport("20260730-120000", totalCells: 10);

            string text = report.Render(TimeSpan.Zero);

            Assert.That(text, Does.Contain("20260730-120000"));
        }

        [Test]
        public void Render_WhileNotComplete_ReportsRunningWithProgressAndElapsed_AndCaveatsThatItMightBePartial()
        {
            var report = new BenchmarkReport("run", totalCells: 10);
            report.RecordCellCompleted();
            report.RecordCellCompleted();
            report.RecordCellCompleted();

            string text = report.Render(TimeSpan.FromSeconds(125));

            Assert.That(text, Does.Contain("RUNNING"));
            Assert.That(text, Does.Contain("3/10"));
            Assert.That(text, Does.Contain("02:05"), "125 seconds should format as 02:05.");
            Assert.That(text, Does.Contain("if this is the last line you see, the run did not finish"));
            Assert.That(text, Does.Not.Contain("COMPLETE"));
        }

        [Test]
        public void Render_OnceMarkedComplete_ReportsAnUnmistakableCompleteState_WithNoPartialCaveat()
        {
            var report = new BenchmarkReport("run", totalCells: 2);
            report.RecordCellCompleted();
            report.RecordCellCompleted();
            report.MarkComplete();

            string text = report.Render(TimeSpan.FromSeconds(5));

            Assert.That(text, Does.Contain("COMPLETE"));
            Assert.That(text, Does.Contain("2/2"));
            Assert.That(text, Does.Not.Contain("if this is the last line you see"));
        }

        [Test]
        public void Render_HeaderLinesComeBeforeTheStatusLine_WhichComesBeforeDetail()
        {
            var report = new BenchmarkReport("run", totalCells: 1);
            report.AppendHeaderLine("Device model: TestPhone");
            report.AppendDetailLine("[easy] some result");

            string text = report.Render(TimeSpan.Zero);

            int headerIndex = text.IndexOf("Device model: TestPhone", StringComparison.Ordinal);
            int statusIndex = text.IndexOf("STATUS:", StringComparison.Ordinal);
            int detailSectionIndex = text.IndexOf("--- Detail ---", StringComparison.Ordinal);
            int detailLineIndex = text.IndexOf("[easy] some result", StringComparison.Ordinal);

            Assert.That(headerIndex, Is.LessThan(statusIndex),
                "Device/build/run-id facts must appear before the first result, not after it.");
            Assert.That(statusIndex, Is.LessThan(detailSectionIndex));
            Assert.That(detailSectionIndex, Is.LessThan(detailLineIndex));
        }

        [Test]
        public void Render_SummaryComesBeforeDetail_SoItNeverScrollsBehindHundredsOfDetailLines()
        {
            var report = new BenchmarkReport("run", totalCells: 1);
            report.SetSummaryLines(new[] { "[easy main-thread] 3 samples: ..." });
            report.AppendDetailLine("[easy] some result");

            string text = report.Render(TimeSpan.Zero);

            int summaryIndex = text.IndexOf("[easy main-thread] 3 samples", StringComparison.Ordinal);
            int detailIndex = text.IndexOf("[easy] some result", StringComparison.Ordinal);

            Assert.That(summaryIndex, Is.LessThan(detailIndex));
        }

        [Test]
        public void Render_BeforeAnySummaryIsSet_SaysSoRatherThanShowingAnEmptySection()
        {
            var report = new BenchmarkReport("run", totalCells: 1);

            string text = report.Render(TimeSpan.Zero);

            Assert.That(text, Does.Contain("not yet available"));
        }

        [Test]
        public void Render_DetailLinesAppearInTheOrderTheyWereAppended()
        {
            var report = new BenchmarkReport("run", totalCells: 1);
            report.AppendDetailLine("first");
            report.AppendDetailLine("second");
            report.AppendDetailLine("third");

            string text = report.Render(TimeSpan.Zero);

            int first = text.IndexOf("first", StringComparison.Ordinal);
            int second = text.IndexOf("second", StringComparison.Ordinal);
            int third = text.IndexOf("third", StringComparison.Ordinal);

            Assert.That(first, Is.LessThan(second));
            Assert.That(second, Is.LessThan(third));
        }
    }
}
