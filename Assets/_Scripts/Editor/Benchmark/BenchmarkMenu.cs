using ChessTheBetrayal.AI;
using UnityEditor;
using UnityEngine;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Interactive entry point for BenchmarkRunner. Runs a Quick-mode pass by default (the routine
    /// check) and logs a drift report against the committed baseline, but never overwrites it —
    /// updating the baseline is a deliberate act (see UpdateBaselineFromMenu), not a side effect of
    /// every run.
    /// </summary>
    public static class BenchmarkMenu
    {
        private const int DefaultRunSeed = 20260713;

        [MenuItem("Chess: The Betrayal/AI/Run Strength Benchmark (Quick)")]
        private static void RunQuickFromMenu() => RunAndLog(BenchmarkMode.Quick);

        [MenuItem("Chess: The Betrayal/AI/Run Strength Benchmark (Full, slow)")]
        private static void RunFullFromMenu() => RunAndLog(BenchmarkMode.Full);

        [MenuItem("Chess: The Betrayal/AI/Update Benchmark Baseline...")]
        private static void UpdateBaselineFromMenu()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Update benchmark baseline",
                "This runs a Full benchmark pass (several minutes) and OVERWRITES Docs/Benchmarks/baseline.json " +
                "with the result. Only do this after deliberately reviewing the new numbers — the baseline is " +
                "what every future run diffs against.",
                "Run and overwrite", "Cancel");
            if (!confirmed) return;

            BenchmarkReport report = BenchmarkRunner.RunAll(DefaultRunSeed, BenchmarkMode.Full,
                AIProfileTable.BuiltIn, progress: new DebugLogProgressSink("Update Baseline"));
            BenchmarkBaselineIO.Write(report, BenchmarkBaselineIO.DefaultPath);

            Debug.Log($"Benchmark baseline updated: {report.PairResults.Count} pairing(s), {report.TierPerformances.Count} tier(s) recorded.");
        }

        private static void RunAndLog(BenchmarkMode mode)
        {
            BenchmarkReport report = BenchmarkRunner.RunAll(DefaultRunSeed, mode,
                AIProfileTable.BuiltIn, progress: new DebugLogProgressSink(mode.ToString()),
                persistRunsUnderDirectory: RunsDirectory);
            BenchmarkReport baseline = BenchmarkBaselineIO.TryRead(BenchmarkBaselineIO.DefaultPath);

            Debug.Log(BenchmarkReportFormatter.ToPlainText(report, baseline));
        }

        /// <summary>Batchmode/CI entry point: <c>Unity -batchmode -executeMethod
        /// ChessTheBetrayal.EditorTools.Benchmark.BenchmarkMenu.RunQuickBatch</c> (or RunFullBatch).
        /// Logs the same report a menu run would and exits with a nonzero code if any Fail-severity
        /// finding survives, so a CI job can gate on it without parsing log text. Progress logs
        /// through Debug.Log — visible in a batchmode run's Editor.log even with no console
        /// attached, the exact signal that was missing when this tooling used to go silent for
        /// 30+ minutes with no way to tell a slow run from a stalled one.</summary>
        public static void RunQuickBatch() => RunBatch(BenchmarkMode.Quick);

        public static void RunFullBatch() => RunBatch(BenchmarkMode.Full);

        private static void RunBatch(BenchmarkMode mode)
        {
            BenchmarkReport report;
            try
            {
                report = BenchmarkRunner.RunAll(DefaultRunSeed, mode,
                    AIProfileTable.BuiltIn, progress: new DebugLogProgressSink($"{mode} Batch"),
                    persistRunsUnderDirectory: RunsDirectory, useWatchdog: true);
            }
            catch (TournamentStalledException stalled)
            {
                // A stall means the run is almost certainly deadlocked, not just slow — exit
                // nonzero so CI treats this as a failure rather than hanging until an external
                // timeout kills the whole job with no explanation. Every game that DID finish is
                // already durable on disk (stalled.RunDirectory), which is the entire point of
                // wiring persistence in before this watchdog.
                Debug.LogError(stalled.Message);
                EditorApplication.Exit(2);
                return;
            }

            BenchmarkReport baseline = BenchmarkBaselineIO.TryRead(BenchmarkBaselineIO.DefaultPath);

            Debug.Log(BenchmarkReportFormatter.ToPlainText(report, baseline));

            var findings = BenchmarkDriftAnalyzer.Analyze(report, baseline);
            bool anyFailure = false;
            foreach (var finding in findings)
                if (finding.Severity == DriftSeverity.Fail) anyFailure = true;

            EditorApplication.Exit(anyFailure ? 1 : 0);
        }

        private static string RunsDirectory =>
            System.IO.Path.Combine(Application.dataPath, "..", "Docs", "Benchmarks", "Runs");
    }
}
