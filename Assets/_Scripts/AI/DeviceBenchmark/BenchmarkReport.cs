using System;
using System.Collections.Generic;
using System.Text;

namespace ChessTheBetrayal.AI.DeviceBenchmark
{
    /// <summary>
    /// How a rendered report decorates itself. The content, its ordering and its wording are the
    /// same either way — only whether section titles carry markup differs, so a screen that can
    /// draw bold text and a plain text file stay two views of one report rather than two reports.
    /// </summary>
    public enum ReportStyle
    {
        /// <summary>No markup at all. What a saved file, a clipboard paste or a console log wants.</summary>
        Plain,

        /// <summary>Section titles in bold, using the tags TextMeshPro understands.</summary>
        RichText,
    }

    /// <summary>
    /// Assembles one coherent report from a benchmark run: a header (run id, device facts, build
    /// config), a status line (progress and an elapsed clock, or an unmistakable complete state),
    /// the per-tier summary table, and the full scrolling detail log underneath it — in that
    /// order, so the summary (the part worth screenshotting) never scrolls out of reach behind
    /// hundreds of detail lines. The screen, the clipboard and an exported file all render this
    /// same string, so they can never disagree with each other.
    ///
    /// Holds no Unity API and no threading primitive of its own — a caller touching this from more
    /// than one thread is responsible for its own locking — so it can be built and asserted on
    /// directly in an EditMode test with no scene or Play mode involved.
    /// </summary>
    public sealed class BenchmarkReport
    {
        private readonly List<string> _headerLines = new List<string>();
        private readonly List<string> _detailLines = new List<string>();
        private readonly int _totalCells;
        private IReadOnlyList<string> _summaryLines = Array.Empty<string>();
        private IReadOnlyList<string> _thermalLines = Array.Empty<string>();
        private int _completedCells;
        private bool _isComplete;
        private TimeSpan? _estimatedWorstCase;

        public BenchmarkReport(string runId, int totalCells)
        {
            RunId = runId;
            _totalCells = totalCells;
        }

        public string RunId { get; }

        /// <summary>
        /// Bumped by every change to the report. A display can compare this against the last value
        /// it drew and skip re-rendering when nothing has happened — worth doing, because building
        /// and laying out several thousand characters of text every frame is real work on a phone,
        /// competing for the same cores the run is trying to measure.
        /// </summary>
        public int Revision { get; private set; }

        /// <summary>A fact that holds for the whole run and never changes — device model, CPU,
        /// build config, battery at start, and so on. Rendered once, before the first result.</summary>
        public void AppendHeaderLine(string line)
        {
            _headerLines.Add(line);
            Revision++;
        }

        /// <summary>One line from the scrolling per-cell log, in the order it happened.</summary>
        public void AppendDetailLine(string line)
        {
            _detailLines.Add(line);
            Revision++;
        }

        /// <summary>The per-tier summary table, replacing whatever was set before. Call once the
        /// run has produced it — an empty report before that point is expected, not an error.</summary>
        public void SetSummaryLines(IReadOnlyList<string> lines)
        {
            _summaryLines = lines;
            Revision++;
        }

        /// <summary>The per-minute depth trend (see MobileSearchBenchmarkRunner.EmitThermalBuckets).
        /// Left empty for a run too short to say anything about a trend — the section is omitted
        /// from Render entirely rather than printed empty, so a short run's report reads exactly as
        /// it did before this existed.</summary>
        public void SetThermalLines(IReadOnlyList<string> lines)
        {
            _thermalLines = lines;
            Revision++;
        }

        /// <summary>
        /// The longest this run can take, known before it starts because every search is stopped by
        /// a wall-clock timer. Shown up front so whoever is holding the phone knows what they have
        /// agreed to sit through, rather than watching an open-ended progress count.
        /// </summary>
        public void SetEstimatedWorstCase(TimeSpan estimate)
        {
            _estimatedWorstCase = estimate;
            Revision++;
        }

        /// <summary>Advances the n in "n/N cells done" by one.</summary>
        public void RecordCellCompleted()
        {
            _completedCells++;
            Revision++;
        }

        /// <summary>Flips the status line from RUNNING to an unmistakable COMPLETE. Never call this
        /// unless every planned cell has actually finished.</summary>
        public void MarkComplete()
        {
            _isComplete = true;
            Revision++;
        }

        /// <summary>
        /// Renders the whole report. elapsed is supplied by the caller rather than measured here so
        /// the clock reads correctly whatever the caller times itself with — a Stopwatch on a
        /// device, a fixed value in a test.
        ///
        /// maxDetailLines trims the oldest detail lines and says on the page how many it dropped.
        /// That exists for one specific reason: TextMeshPro stops drawing a text object past
        /// 16,383 characters and gives no warning when it does, so an unbounded log would quietly
        /// start losing its own tail on screen. Only the scrolling detail is ever trimmed — the
        /// header, the status line and the per-tier summary are always complete — and a file or a
        /// clipboard copy, which have no such limit, should leave this at its default and take
        /// everything.
        /// </summary>
        public string Render(TimeSpan elapsed, ReportStyle style = ReportStyle.Plain,
            int maxDetailLines = int.MaxValue)
        {
            var text = new StringBuilder();

            text.AppendLine($"Run ID: {RunId}");
            if (_headerLines.Count > 0)
            {
                text.AppendLine(SectionTitle("--- Device info ---", style));
                foreach (string line in _headerLines) text.AppendLine(line);
            }
            text.AppendLine();

            string estimateNote = _estimatedWorstCase.HasValue
                ? $" of at most {FormatElapsed(_estimatedWorstCase.Value)}"
                : "";

            text.AppendLine(SectionTitle(_isComplete
                ? $"STATUS: COMPLETE — {_completedCells}/{_totalCells} cells, elapsed {FormatElapsed(elapsed)}"
                : $"STATUS: RUNNING — {_completedCells}/{_totalCells} cells, elapsed {FormatElapsed(elapsed)}{estimateNote} "
                    + "(if this is the last line you see, the run did not finish)", style));
            text.AppendLine();

            text.AppendLine(SectionTitle("--- Summary ---", style));
            if (_summaryLines.Count == 0)
                text.AppendLine("(not yet available — printed once the run completes)");
            else
                foreach (string line in _summaryLines) text.AppendLine(line);
            text.AppendLine();

            if (_thermalLines.Count > 0)
            {
                text.AppendLine(SectionTitle("--- Thermal curve ---", style));
                foreach (string line in _thermalLines) text.AppendLine(line);
                text.AppendLine();
            }

            text.AppendLine(SectionTitle("--- Detail ---", style));
            int shown = Math.Max(0, Math.Min(maxDetailLines, _detailLines.Count));
            int firstShown = _detailLines.Count - shown;
            if (firstShown > 0)
                text.AppendLine($"(showing the last {shown} of {_detailLines.Count} lines — a saved copy has all of them)");
            for (int i = firstShown; i < _detailLines.Count; i++) text.AppendLine(_detailLines[i]);

            return text.ToString();
        }

        private static string SectionTitle(string title, ReportStyle style) =>
            style == ReportStyle.RichText ? $"<b>{title}</b>" : title;

        private static string FormatElapsed(TimeSpan elapsed) =>
            elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
    }
}
