using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI;
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
        public void CaptureTesterPlanReference() => Capture(BenchmarkPlan.Tester(), "TESTER");

        /// <summary>
        /// Every position, every repeat, play-forward included, both thread contexts. Hours long —
        /// only worth starting on a machine you can leave alone.
        /// </summary>
        [Test]
        [Explicit("Multi-hour exhaustive run — start it deliberately.")]
        public void CaptureExhaustiveReference() => Capture(BenchmarkPlan.Exhaustive(), "EXHAUSTIVE");

        private static void Capture(BenchmarkPlan plan, string label)
        {
            var runner = new MobileSearchBenchmarkRunner();
            var profileProvider = new AIProfileTableProvider();
            runner.OnLine += line => Debug.Log($"[capture] {line}");

            Debug.Log($"[capture] === {label} PLAN: {plan.TotalCells} cells, at most {plan.EstimatedWorstCase:mm\\:ss} ===");

            foreach (int positionIndex in plan.PositionIndices)
            {
                foreach (AIProfile row in AIProfileTable.BuiltIn)
                {
                    AIProfile profile = profileProvider.Resolve(row.Id);

                    for (int repeatIndex = 0; repeatIndex < plan.RepeatCount; repeatIndex++)
                    {
                        Task workerCell = runner.RunCellOnWorkerThread(
                            positionIndex, profile, repeatIndex, plan.IncludePlayForward);
                        workerCell.Wait();
                    }
                }
            }

            for (int i = 0; i < plan.MainThreadControlPositions && i < plan.PositionIndices.Count; i++)
            {
                foreach (AIProfile row in AIProfileTable.BuiltIn)
                {
                    AIProfile profile = profileProvider.Resolve(row.Id);

                    for (int repeatIndex = 0; repeatIndex < plan.MainThreadControlRepeats; repeatIndex++)
                        runner.RunCell(plan.PositionIndices[i], profile, repeatIndex, plan.IncludePlayForward);
                }
            }

            Debug.Log($"[capture] === {label} SUMMARY ===");
            foreach (string line in runner.EmitTierSummaries())
                Debug.Log($"[capture-summary] {line}");
        }
    }
}
