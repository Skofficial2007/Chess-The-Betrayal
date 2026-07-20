using UnityEngine;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Reports tournament progress via Debug.Log so a slow run's Editor.log shows real, timestamped
    /// movement instead of going silent until the whole run finishes — the exact gap that made a
    /// stalled run indistinguishable from a working one. The batch/CI entry points
    /// (BenchmarkMenu.RunQuickBatch/RunFullBatch) use this.
    ///
    /// Throttling only kicks in past a couple hundred games: below that, every game logs — a Quick
    /// run's whole point is being small enough to watch move game by game, and throttling it away
    /// would undercut the "prove this is alive" purpose progress logging exists for. Above that, a
    /// per-game line really would flood the log without adding information a human can use, so it
    /// falls back to reporting every reportEveryNGames games (plus always the final one).
    /// </summary>
    public sealed class DebugLogProgressSink : ITournamentProgress
    {
        private const int ThrottleAboveGameCount = 200;

        private readonly string _label;
        private readonly int _reportEveryNGames;

        public DebugLogProgressSink(string label, int reportEveryNGames = 5)
        {
            _label = label;
            _reportEveryNGames = System.Math.Max(1, reportEveryNGames);
        }

        public void ReportGameCompleted(int current, int total)
        {
            bool shouldReport = total <= ThrottleAboveGameCount
                || current == total
                || current % _reportEveryNGames == 0;

            if (shouldReport)
                Debug.Log($"[{_label}] {current}/{total} games complete");
        }
    }
}
