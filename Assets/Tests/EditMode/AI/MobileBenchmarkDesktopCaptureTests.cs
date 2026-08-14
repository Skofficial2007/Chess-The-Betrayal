using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.DeviceBenchmark;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// A recording harness, not a pass/fail gate. Runs the same plan a device runs, on this machine,
    /// so the desktop reference row can be captured without a device build. Marked so it never runs
    /// in a normal pass; run it on purpose and read the numbers from the log.
    ///
    /// Every line goes to Debug.Log as it happens rather than being collected and printed at the
    /// end. That matters more than it looks: an earlier version buffered its output, ran for over an
    /// hour, and produced nothing at all when it was stopped — a long run that only reports at the
    /// end is indistinguishable from a hung one while it runs, and throws away everything it learned
    /// if anyone gives up on it.
    /// </summary>
    [TestFixture]
    [Explicit("Recording harness — run manually and read the reference numbers from the log.")]
    public class MobileBenchmarkDesktopCaptureTests
    {
        [Test]
        public void CaptureTesterPlanReference() => Capture(BenchmarkPlan.Tester());

        /// <summary>
        /// Impossible tier only, 200 cold searches in a row — the sustained-load probe, not a
        /// breadth probe. Ten minutes worst case, notably longer than the tester plan's, so it gets
        /// its own explicit note the same way the exhaustive run does. Needs its own Timeout: NUnit's
        /// default per-test watchdog is 180,000 ms, well under this plan's own ceiling, and it fires
        /// even though the run itself completes and logs everything correctly — a real desktop
        /// capture tripped exactly this the first time this test existed.
        /// </summary>
        [Test]
        [Explicit("Ten-minute sustained-load run — start it deliberately.")]
        [Timeout(700_000)]
        public void CaptureThermalPlanReference() => Capture(BenchmarkPlan.Thermal());

        /// <summary>
        /// Every position, every repeat, play-forward included, both thread contexts. Hours long —
        /// only worth starting on a machine you can leave alone.
        /// </summary>
        [Test]
        [Explicit("Multi-hour exhaustive run — start it deliberately.")]
        public void CaptureExhaustiveReference() => Capture(BenchmarkPlan.Exhaustive());

        private static void Capture(BenchmarkPlan plan)
        {
            var runner = new MobileSearchBenchmarkRunner();
            var profileProvider = new AIProfileTableProvider();
            runner.OnLine += line => Debug.Log($"[capture] {line}");

            // The plan names and describes itself, so a desktop reference and the device report it
            // is the reference for cannot end up labelled differently.
            Debug.Log($"[capture] === {plan.Name}: {plan.TotalCells} cells, at most {plan.EstimatedWorstCase:mm\\:ss} ===");
            Debug.Log($"[capture] {plan.Describe()}");

            // The same cells, in the same order, that a device runs — walked from the plan itself
            // rather than re-derived here, so a desktop reference row can't quietly describe a
            // different run from the one it is supposed to be the reference for.
            foreach (BenchmarkCell cell in plan.Cells())
            {
                AIProfile profile = profileProvider.Resolve(cell.ProfileId);

                if (cell.OnWorkerThread)
                {
                    Task workerCell = runner.RunCellOnWorkerThread(
                        cell.PositionIndex, profile, cell.RepeatIndex, plan.IncludePlayForward);
                    workerCell.Wait();
                }
                else
                {
                    runner.RunCell(cell.PositionIndex, profile, cell.RepeatIndex, plan.IncludePlayForward);
                }
            }

            Debug.Log($"[capture] === {plan.Name} SUMMARY ===");
            foreach (string line in runner.EmitTierSummaries(plan.Profiles, plan.HasMainThreadControl))
                Debug.Log($"[capture-summary] {line}");

            Debug.Log($"[capture] === {plan.Name} THERMAL CURVE ===");
            foreach (string line in runner.EmitThermalBuckets())
                Debug.Log($"[capture-thermal] {line}");
        }
    }
}
