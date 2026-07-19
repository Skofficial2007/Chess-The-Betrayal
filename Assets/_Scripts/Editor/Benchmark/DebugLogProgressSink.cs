using UnityEngine;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Reports tournament progress via Debug.Log, throttled to every reportEveryNGames games (plus
    /// always the final one) so a fast run doesn't spam the console with a line per game. The
    /// batch/CI entry points (BenchmarkMenu.RunQuickBatch/RunFullBatch) use this so a slow run's
    /// Editor.log shows real, timestamped movement instead of going silent until the whole run
    /// finishes — the exact gap that made a stalled run indistinguishable from a working one.
    /// </summary>
    public sealed class DebugLogProgressSink : ITournamentProgress
    {
        private readonly string _label;
        private readonly int _reportEveryNGames;

        public DebugLogProgressSink(string label, int reportEveryNGames = 5)
        {
            _label = label;
            _reportEveryNGames = System.Math.Max(1, reportEveryNGames);
        }

        public void ReportGameCompleted(int current, int total)
        {
            if (current == total || current % _reportEveryNGames == 0)
                Debug.Log($"[{_label}] {current}/{total} games complete");
        }
    }
}
