using System;
using System.Text.RegularExpressions;
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
        private const string TestPlanName = "Test Run";

        [Test]
        public void Render_NamesThePlanThatRan_NotJustItsCellCount()
        {
            var report = new BenchmarkReport("run", "Long Run (sustained load)", totalCells: 200);
            for (int i = 0; i < 200; i++) report.RecordCellCompleted();
            report.MarkComplete();

            string text = report.Render(TimeSpan.FromMinutes(10));

            // "200/200 cells" on its own reads like a matrix of 200 positions. It was one position
            // searched 200 times, which answers an entirely different question.
            Assert.That(text, Does.Contain("Long Run (sustained load)"));
        }

        [Test]
        public void Render_IncludesTheRunId()
        {
            var report = new BenchmarkReport("20260730-120000", TestPlanName, totalCells: 10);

            string text = report.Render(TimeSpan.Zero);

            Assert.That(text, Does.Contain("20260730-120000"));
        }

        [Test]
        public void Render_WhileNotComplete_ReportsRunningWithProgressAndElapsed_AndCaveatsThatItMightBePartial()
        {
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 10);
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
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 2);
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
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 1);
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
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 1);
            report.SetSummaryLines(new[] { "[easy main-thread] 3 samples: ..." });
            report.AppendDetailLine("[easy] some result");

            string text = report.Render(TimeSpan.Zero);

            int summaryIndex = text.IndexOf("[easy main-thread] 3 samples", StringComparison.Ordinal);
            int detailIndex = text.IndexOf("[easy] some result", StringComparison.Ordinal);

            Assert.That(summaryIndex, Is.LessThan(detailIndex));
        }

        [Test]
        public void Render_WithNoThermalLinesSet_OmitsTheThermalSectionEntirely()
        {
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 1);
            report.SetSummaryLines(new[] { "[easy main-thread] 3 samples: ..." });

            string text = report.Render(TimeSpan.Zero);

            Assert.That(text, Does.Not.Contain("Thermal curve"),
                "A Tester run never calls SetThermalLines with anything worth showing, and the " +
                "report's shape must stay exactly as it read before the thermal section existed.");
        }

        [Test]
        public void Render_WithThermalLinesSet_ShowsThemBetweenSummaryAndDetail()
        {
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 1);
            report.SetSummaryLines(new[] { "[impossible worker-thread] 200 samples: ..." });
            report.SetThermalLines(new[] { "[impossible worker-thread] minute 0: 20 samples, depth worst 7 mean 7.0" }, tracksSustainedLoad: true);
            report.AppendDetailLine("[impossible] some result");

            string text = report.Render(TimeSpan.Zero);

            int summaryIndex = text.IndexOf("--- Summary ---", StringComparison.Ordinal);
            int thermalIndex = text.IndexOf("minute 0: 20 samples", StringComparison.Ordinal);
            int detailIndex = text.IndexOf("--- Detail ---", StringComparison.Ordinal);

            Assert.That(text, Does.Contain("Thermal curve"));
            Assert.That(summaryIndex, Is.LessThan(thermalIndex));
            Assert.That(thermalIndex, Is.LessThan(detailIndex));
        }

        [Test]
        public void Render_WhenTheRunVariedItsWork_DoesNotCallThePerMinuteBreakdownAThermalCurve()
        {
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 54);
            report.SetThermalLines(new[]
            {
                "[hard worker-thread] minute 0: 4 samples, elapsed mean 3.00s; depth worst 5 mean 5.5",
                "[hard worker-thread] minute 1: 4 samples, elapsed mean 3.00s; depth worst 7 mean 7.0",
            }, tracksSustainedLoad: false);

            string text = report.Render(TimeSpan.Zero);

            // Those two lines are real and worth printing, but the rise between them is the running
            // order: the deeper-searching positions come second. Headed "Thermal curve" it reads as
            // a device getting a ply and a half faster as it warms up, which is nonsense.
            Assert.That(text, Does.Not.Contain("Thermal curve"));
            Assert.That(text, Does.Contain("Per-minute breakdown"));
            Assert.That(text, Does.Contain("not a thermal reading"));
            Assert.That(text, Does.Contain("minute 1: 4 samples"),
                "The numbers still belong on the page — only the claim made about them changes.");
        }

        [Test]
        public void Render_ProducesNothingOutsidePlainAscii()
        {
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 3);
            report.AppendHeaderLine("Device model: TestPhone");
            report.SetEstimatedWorstCase(TimeSpan.FromMinutes(2));
            report.SetSummaryLines(new[] { "[easy worker-thread] 3 samples: ..." });
            report.SetThermalLines(new[] { "[easy worker-thread] minute 0: 3 samples" }, tracksSustainedLoad: false);
            report.AppendDetailLine("a result");
            report.AppendDetailLine("another result");
            report.AppendDetailLine("a third result");

            // A report is written on a phone and opened by whoever it is forwarded to. Anything
            // outside ASCII depends on that reader guessing the encoding correctly, and the first
            // real device report came back with every em-dash rendered as mojibake. A byte-order
            // mark helps a reader that looks for one; staying inside ASCII needs no reader to do
            // anything at all.
            ReportTextAssertions.AssertPlainAscii(report.Render(TimeSpan.FromSeconds(30), ReportStyle.Plain, maxDetailLines: 2));
        }

        [Test]
        public void Render_BeforeAnySummaryIsSet_SaysSoRatherThanShowingAnEmptySection()
        {
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 1);

            string text = report.Render(TimeSpan.Zero);

            Assert.That(text, Does.Contain("not yet available"));
        }

        [Test]
        public void Render_DetailLinesAppearInTheOrderTheyWereAppended()
        {
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 1);
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

        /// <summary>
        /// The whole reason styling is a parameter rather than a second render method: a report
        /// drawn on screen and the same report saved to a file must be the same report. Strip the
        /// markup back off the styled one and it has to match the plain one character for
        /// character — if it doesn't, the two sinks have started disagreeing about content.
        /// </summary>
        [Test]
        public void Render_InRichText_IsTheSameReportAsPlain_OnceTheMarkupIsStripped()
        {
            BenchmarkReport report = BuildPopulatedReport();

            string plain = report.Render(TimeSpan.FromSeconds(90));
            string rich = report.Render(TimeSpan.FromSeconds(90), ReportStyle.RichText);

            Assert.That(Regex.Replace(rich, "<[^>]+>", ""), Is.EqualTo(plain));
        }

        [Test]
        public void Render_InRichText_BoldsTheSectionTitles_AndLeavesResultRowsAlone()
        {
            BenchmarkReport report = BuildPopulatedReport();

            string rich = report.Render(TimeSpan.Zero, ReportStyle.RichText);

            Assert.That(rich, Does.Contain("<b>--- Summary ---</b>"));
            Assert.That(rich, Does.Contain("<b>--- Detail ---</b>"));
            Assert.That(rich, Does.Contain("[easy worker-thread] cell one"),
                "A result row carries no markup of its own.");
        }

        [Test]
        public void Render_ByDefault_ShowsEveryDetailLineWithNoTrimNote()
        {
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 1);
            report.AppendDetailLine("oldest");
            report.AppendDetailLine("newest");

            string text = report.Render(TimeSpan.Zero);

            Assert.That(text, Does.Contain("oldest"));
            Assert.That(text, Does.Contain("newest"));
            Assert.That(text, Does.Not.Contain("showing the last"));
        }

        /// <summary>
        /// A screen can only draw so much text before it silently stops, so the on-screen copy is
        /// capped. It must drop the OLDEST lines (the newest are what someone watching a run cares
        /// about) and it must say on the page that it did, otherwise a trimmed log is
        /// indistinguishable from a run that never produced those lines.
        /// </summary>
        [Test]
        public void Render_WithADetailCap_KeepsTheNewestLines_AndSaysHowManyItHid()
        {
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 1);
            report.AppendDetailLine("oldest");
            report.AppendDetailLine("middle");
            report.AppendDetailLine("newest");

            string text = report.Render(TimeSpan.Zero, ReportStyle.Plain, maxDetailLines: 2);

            Assert.That(text, Does.Not.Contain("oldest"));
            Assert.That(text, Does.Contain("middle"));
            Assert.That(text, Does.Contain("newest"));
            Assert.That(text, Does.Contain("showing the last 2 of 3 lines"));
        }

        [Test]
        public void Render_WithADetailCap_StillShowsTheHeaderStatusAndSummaryInFull()
        {
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 3);
            report.AppendHeaderLine("Device model: TestPhone");
            report.SetSummaryLines(new[] { "[easy main-thread] 3 samples: ...", "[easy worker-thread] 3 samples: ..." });
            report.AppendDetailLine("dropped");
            report.AppendDetailLine("kept");

            string text = report.Render(TimeSpan.Zero, ReportStyle.Plain, maxDetailLines: 1);

            Assert.That(text, Does.Contain("Device model: TestPhone"));
            Assert.That(text, Does.Contain("STATUS:"));
            Assert.That(text, Does.Contain("[easy main-thread] 3 samples"));
            Assert.That(text, Does.Contain("[easy worker-thread] 3 samples"),
                "Trimming must only ever touch the scrolling detail, never the summary someone is " +
                "meant to read off the screen.");
        }

        [Test]
        public void Revision_ChangesOnEveryKindOfUpdate_SoADisplayCanTellWhenThereIsSomethingNewToDraw()
        {
            var report = new BenchmarkReport("run", TestPlanName, totalCells: 1);
            int start = report.Revision;

            report.AppendHeaderLine("Device model: TestPhone");
            Assert.That(report.Revision, Is.GreaterThan(start), "A new header line is a change.");

            int afterHeader = report.Revision;
            report.AppendDetailLine("a result");
            Assert.That(report.Revision, Is.GreaterThan(afterHeader), "A new detail line is a change.");

            int afterDetail = report.Revision;
            report.RecordCellCompleted();
            Assert.That(report.Revision, Is.GreaterThan(afterDetail), "Progress moving is a change.");

            int afterProgress = report.Revision;
            report.SetSummaryLines(new[] { "[easy main-thread] 1 samples: ..." });
            Assert.That(report.Revision, Is.GreaterThan(afterProgress), "A summary arriving is a change.");

            int afterSummary = report.Revision;
            report.SetThermalLines(new[] { "[easy main-thread] minute 0: 1 samples, depth worst 5 mean 5.0" }, tracksSustainedLoad: true);
            Assert.That(report.Revision, Is.GreaterThan(afterSummary), "A thermal curve arriving is a change.");

            int afterThermal = report.Revision;
            report.MarkComplete();
            Assert.That(report.Revision, Is.GreaterThan(afterThermal), "Finishing is a change.");
        }

        private static BenchmarkReport BuildPopulatedReport()
        {
            var report = new BenchmarkReport("20260730-120000", TestPlanName, totalCells: 2);
            report.AppendHeaderLine("Device model: TestPhone");
            report.SetEstimatedWorstCase(TimeSpan.FromSeconds(140));
            report.SetSummaryLines(new[] { "[easy main-thread] 2 samples: elapsed worst 0.20s" });
            report.AppendDetailLine("[easy worker-thread] cell one");
            report.AppendDetailLine("[easy main-thread] cell two");
            report.RecordCellCompleted();
            return report;
        }
    }
}
