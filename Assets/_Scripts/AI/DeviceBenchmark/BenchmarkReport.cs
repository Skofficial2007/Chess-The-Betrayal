using System;
using System.Collections.Generic;
using System.Text;

namespace ChessTheBetrayal.AI.DeviceBenchmark
{
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
        private int _completedCells;
        private bool _isComplete;

        public BenchmarkReport(string runId, int totalCells)
        {
            RunId = runId;
            _totalCells = totalCells;
        }

        public string RunId { get; }

        /// <summary>A fact that holds for the whole run and never changes — device model, CPU,
        /// build config, battery at start, and so on. Rendered once, before the first result.</summary>
        public void AppendHeaderLine(string line) => _headerLines.Add(line);

        /// <summary>One line from the scrolling per-cell log, in the order it happened.</summary>
        public void AppendDetailLine(string line) => _detailLines.Add(line);

        /// <summary>The per-tier summary table, replacing whatever was set before. Call once the
        /// run has produced it — an empty report before that point is expected, not an error.</summary>
        public void SetSummaryLines(IReadOnlyList<string> lines) => _summaryLines = lines;

        /// <summary>Advances the n in "n/N cells done" by one.</summary>
        public void RecordCellCompleted() => _completedCells++;

        /// <summary>Flips the status line from RUNNING to an unmistakable COMPLETE. Never call this
        /// unless every planned cell has actually finished.</summary>
        public void MarkComplete() => _isComplete = true;

        /// <summary>
        /// Renders the full report. elapsed is supplied by the caller rather than measured here so
        /// the clock reads correctly whatever the caller times itself with — a Stopwatch on a
        /// device, a fixed value in a test.
        /// </summary>
        public string Render(TimeSpan elapsed)
        {
            var text = new StringBuilder();

            text.AppendLine($"Run ID: {RunId}");
            foreach (string line in _headerLines) text.AppendLine(line);
            text.AppendLine();

            text.AppendLine(_isComplete
                ? $"STATUS: COMPLETE — {_completedCells}/{_totalCells} cells, elapsed {FormatElapsed(elapsed)}"
                : $"STATUS: RUNNING — {_completedCells}/{_totalCells} cells, elapsed {FormatElapsed(elapsed)} "
                    + "(if this is the last line you see, the run did not finish)");
            text.AppendLine();

            text.AppendLine("--- Summary ---");
            if (_summaryLines.Count == 0)
                text.AppendLine("(not yet available — printed once the run completes)");
            else
                foreach (string line in _summaryLines) text.AppendLine(line);
            text.AppendLine();

            text.AppendLine("--- Detail ---");
            foreach (string line in _detailLines) text.AppendLine(line);

            return text.ToString();
        }

        private static string FormatElapsed(TimeSpan elapsed) =>
            elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
    }
}
