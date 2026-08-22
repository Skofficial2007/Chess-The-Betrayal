using System.Text;
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

            BenchmarkReport report = BenchmarkRunner.RunAll(DefaultRunSeed, BenchmarkMode.Full);
            BenchmarkBaselineIO.Write(report, BenchmarkBaselineIO.DefaultPath);

            Debug.Log($"Benchmark baseline updated: {report.PairResults.Count} pairing(s), {report.TierPerformances.Count} tier(s) recorded.");
        }

        private static void RunAndLog(BenchmarkMode mode)
        {
            BenchmarkReport report = BenchmarkRunner.RunAll(DefaultRunSeed, mode);
            BenchmarkReport baseline = BenchmarkBaselineIO.TryRead(BenchmarkBaselineIO.DefaultPath);

            Debug.Log(FormatReport(report, baseline));
        }

        /// <summary>Batchmode/CI entry point: <c>Unity -batchmode -executeMethod
        /// ChessTheBetrayal.EditorTools.Benchmark.BenchmarkMenu.RunQuickBatch</c> (or RunFullBatch).
        /// Logs the same report a menu run would and exits with a nonzero code if any Fail-severity
        /// finding survives, so a CI job can gate on it without parsing log text.</summary>
        public static void RunQuickBatch() => RunBatch(BenchmarkMode.Quick);

        public static void RunFullBatch() => RunBatch(BenchmarkMode.Full);

        private static void RunBatch(BenchmarkMode mode)
        {
            BenchmarkReport report = BenchmarkRunner.RunAll(DefaultRunSeed, mode);
            BenchmarkReport baseline = BenchmarkBaselineIO.TryRead(BenchmarkBaselineIO.DefaultPath);

            Debug.Log(FormatReport(report, baseline));

            var findings = BenchmarkDriftAnalyzer.Analyze(report, baseline);
            bool anyFailure = false;
            foreach (var finding in findings)
                if (finding.Severity == DriftSeverity.Fail) anyFailure = true;

            EditorApplication.Exit(anyFailure ? 1 : 0);
        }

        private static string FormatReport(BenchmarkReport report, BenchmarkReport baseline)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Benchmark run — mode={report.Mode} seed={report.RunSeed}");

            foreach (PairResult pair in report.PairResults)
            {
                sb.AppendLine($"  {pair.Subject} vs {pair.Opponent}: {pair.SubjectWinRate:P1} " +
                    $"({pair.SubjectWins}W {pair.OpponentWins}L {pair.Draws}D over {pair.Games} games)");
            }

            foreach (TierPerformance tier in report.TierPerformances)
            {
                sb.AppendLine($"  [{tier.ProfileId}] {tier.MovesSampled} moves, " +
                    $"{tier.MeanNodesPerMove:F0} nodes/move, {tier.MeanMsPerMove:F0}ms/move, " +
                    $"depth reached {tier.DeepestCompletedDepth}, blunder-actuation {tier.ObservedBlunderActuationRate:P1}");
            }

            var findings = BenchmarkDriftAnalyzer.Analyze(report, baseline);
            if (findings.Count == 0)
            {
                sb.AppendLine(baseline == null
                    ? "  No baseline on disk yet — nothing to diff against."
                    : "  No drift findings.");
            }
            else
            {
                foreach (var finding in findings)
                    sb.AppendLine($"  [{finding.Severity}] {finding.Message}");
            }

            return sb.ToString();
        }
    }
}
