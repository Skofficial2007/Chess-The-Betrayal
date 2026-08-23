using System.Collections.Generic;
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

        /// <summary>How many times the high-confidence pass replays the whole position list per
        /// pairing. Five passes is 200 games per pairing, tightening the 95% interval from roughly
        /// +/-15% to +/-7% — enough to tell a real result from a coin flip near a decision
        /// boundary, which a single pass cannot do.</summary>
        private const int HighConfidenceRepeats = 5;

        [MenuItem("Chess: The Betrayal/AI/Run Strength Benchmark (Quick)")]
        private static void RunQuickFromMenu() => RunAndLog(BenchmarkMode.Quick);

        [MenuItem("Chess: The Betrayal/AI/Run Strength Benchmark (Full, slow)")]
        private static void RunFullFromMenu() => RunAndLog(BenchmarkMode.Full);

        [MenuItem("Chess: The Betrayal/AI/Run Strength Benchmark (High confidence, slowest)")]
        private static void RunHighConfidenceFromMenu() => RunAndLog(BenchmarkMode.Full, HighConfidenceRepeats);

        [MenuItem("Chess: The Betrayal/AI/Run Strength Benchmark (Top of ladder only)")]
        private static void RunTopOfLadderFromMenu() =>
            RunAndLog(BenchmarkMode.Full, TopOfLadderRepeats, TopOfLadderPairings);

        [MenuItem("Chess: The Betrayal/AI/Propose Benchmark Baseline Update...")]
        private static void ProposeBaselineFromMenu()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Propose a baseline update",
                "This runs a Full benchmark pass (around an hour) and writes the result to " +
                "Docs/Benchmarks/baseline.proposed.json for review. It does NOT touch the committed " +
                "baseline — see the note on BenchmarkBaselineIO for why that is a hand-merged file.",
                "Run", "Cancel");
            if (!confirmed) return;

            BenchmarkReport report = BenchmarkRunner.RunAll(DefaultRunSeed, BenchmarkMode.Full,
                AIProfileTable.BuiltIn, progress: new DebugLogProgressSink("Propose Baseline"),
                persistRunsUnderDirectory: RunsDirectory, logGamesToConsole: true);

            BenchmarkBaselineIO.Write(report, BenchmarkBaselineIO.ProposedPath);

            BenchmarkReport committed = BenchmarkBaselineIO.TryRead(BenchmarkBaselineIO.DefaultPath);
            Debug.Log(BenchmarkReportFormatter.ToPlainText(report, committed));
            Debug.Log($"Proposed baseline written to '{BenchmarkBaselineIO.ProposedPath}'. Review it against " +
                      "the committed baseline, then copy across the rows this run actually improves on.");
        }

        private static void RunAndLog(BenchmarkMode mode, int repeats = 1,
            IReadOnlyList<(string Subject, string Opponent)> pairings = null)
        {
            BenchmarkReport report = BenchmarkRunner.RunAll(DefaultRunSeed, mode,
                AIProfileTable.BuiltIn, progress: new DebugLogProgressSink(mode.ToString()),
                persistRunsUnderDirectory: RunsDirectory, logGamesToConsole: true, repeats: repeats,
                pairings: pairings);
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

        /// <summary>The run to reach for when a number has to be trusted rather than merely
        /// glanced at — a full matrix replayed HighConfidenceRepeats times, which is what it takes
        /// to separate a genuine result from noise near a decision boundary. Costs proportionally
        /// more wall clock than RunFullBatch, so it is a deliberate choice, not the default.</summary>
        public static void RunHighConfidenceBatch() => RunBatch(BenchmarkMode.Full, HighConfidenceRepeats);

        /// <summary>How many passes the top-of-ladder run plays. Eight passes is 320 games per
        /// pairing and a 95% interval near +/-5.5%, tight enough that a result close to the 55%
        /// strength floor lands decisively on one side of it instead of straddling it — which is
        /// the whole reason to spend a long run on these two pairings rather than a wider one.</summary>
        private const int TopOfLadderRepeats = 8;

        /// <summary>The two relationships the difficulty ladder actually stands or falls on. Every
        /// other pairing in the roster is settled by a wide margin, so more games there change no
        /// conclusion; these two sit close enough to the floor that only a large sample can
        /// separate a real ordering from a coin flip.</summary>
        private static readonly (string Subject, string Opponent)[] TopOfLadderPairings =
        {
            ("extreme", "hard"),
            ("impossible", "extreme"),
        };

        /// <summary>Spends a long run entirely on the top of the ladder — see TopOfLadderPairings
        /// for why that beats a wider matrix when the question is whether the deepest tiers are
        /// genuinely ordered.</summary>
        public static void RunTopOfLadderBatch() =>
            RunBatch(BenchmarkMode.Full, TopOfLadderRepeats, TopOfLadderPairings);

        private static void RunBatch(BenchmarkMode mode, int repeats = 1,
            IReadOnlyList<(string Subject, string Opponent)> pairings = null)
        {
            BenchmarkReport report;
            try
            {
                report = BenchmarkRunner.RunAll(DefaultRunSeed, mode,
                    AIProfileTable.BuiltIn, progress: new DebugLogProgressSink($"{mode} Batch"),
                    persistRunsUnderDirectory: RunsDirectory, useWatchdog: true, logGamesToConsole: true,
                    repeats: repeats, pairings: pairings);
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
