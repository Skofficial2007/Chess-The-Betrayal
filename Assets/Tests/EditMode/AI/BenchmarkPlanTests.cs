using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.DeviceBenchmark;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The tester plan's duration is quoted to whoever is about to run the build, so it has to be a
    /// real bound rather than a hopeful one. These pin both the arithmetic behind it and the ceiling
    /// it must stay under to be worth handing to someone.
    /// </summary>
    [TestFixture]
    public class BenchmarkPlanTests
    {
        /// <summary>What a tester will actually tolerate. A run past this gets abandoned, and an
        /// abandoned run measures nothing.</summary>
        private const double TesterCeilingSeconds = 300;

        [Test]
        public void TesterPlan_WorstCaseStaysUnderTheCeilingATesterWillSitThrough()
        {
            var plan = BenchmarkPlan.Tester();

            Assert.That(plan.EstimatedWorstCase.TotalSeconds, Is.LessThan(TesterCeilingSeconds),
                $"The tester plan would take up to {plan.EstimatedWorstCase}, past what anyone will " +
                "wait through on a phone they are holding.");
        }

        [Test]
        public void TesterPlan_WorstCaseIsTheSumOfEveryCellsOwnBudget()
        {
            var plan = BenchmarkPlan.Tester();

            long budgetSumMs = 0;
            foreach (AIProfile profile in AIProfileTable.BuiltIn)
                budgetSumMs += profile.TimeBudget.HardMs;

            // Cold search only, so one search per cell: every position/repeat sweeps all six tiers
            // once, and the main-thread control adds its own sweeps on top.
            long expectedMs =
                ((long)plan.PositionIndices.Count * plan.RepeatCount * budgetSumMs)
                + ((long)plan.MainThreadControlPositions * plan.MainThreadControlRepeats * budgetSumMs);

            Assert.That(plan.EstimatedWorstCase.TotalMilliseconds, Is.EqualTo(expectedMs));
        }

        [Test]
        public void TesterPlan_SkipsPlayForward_BecauseAColdSearchIsTheSlowerCaseTheBudgetPromiseIsAbout()
        {
            Assert.That(BenchmarkPlan.Tester().IncludePlayForward, Is.False);
        }

        [Test]
        public void TesterPlan_CoversBothHandPlacedProbesAndSomeCuratedOpenings()
        {
            var plan = BenchmarkPlan.Tester();

            Assert.That(plan.PositionIndices, Contains.Item(CuratedOpeningLines.Count),
                "The quiet-midgame probe must be covered.");
            Assert.That(plan.PositionIndices, Contains.Item(CuratedOpeningLines.Count + 1),
                "The semi-open-midgame probe must be covered.");
            Assert.That(plan.PositionIndices, Has.Some.LessThan(CuratedOpeningLines.Count),
                "At least one replayed opening must be covered — those are where the budget was " +
                "measured actually running short.");
        }

        [Test]
        public void TesterPlan_EveryPositionIndexIsInRange()
        {
            foreach (int index in BenchmarkPlan.Tester().PositionIndices)
            {
                Assert.That(index, Is.InRange(0, MobileSearchBenchmarkRunner.PositionCount - 1),
                    "An out-of-range index would throw partway through a tester's run.");
            }
        }

        [Test]
        public void TesterPlan_TotalCellsCountsBothTheWorkerPassAndTheMainThreadControl()
        {
            var plan = BenchmarkPlan.Tester();

            int expected =
                (plan.PositionIndices.Count * AIProfileTable.BuiltIn.Count * plan.RepeatCount)
                + (plan.MainThreadControlPositions * AIProfileTable.BuiltIn.Count * plan.MainThreadControlRepeats);

            Assert.That(plan.TotalCells, Is.EqualTo(expected),
                "The progress denominator must count every cell that will actually run, or progress " +
                "stalls short of its own total.");
        }

        [Test]
        public void ExhaustivePlan_IsFarLongerThanTheTesterPlan_AndIsNotSomethingToHandToATester()
        {
            var tester = BenchmarkPlan.Tester();
            var exhaustive = BenchmarkPlan.Exhaustive();

            Assert.That(exhaustive.EstimatedWorstCase, Is.GreaterThan(tester.EstimatedWorstCase));
            Assert.That(exhaustive.EstimatedWorstCase.TotalSeconds, Is.GreaterThan(TesterCeilingSeconds),
                "If the exhaustive plan ever fits inside the tester ceiling, the two plans have " +
                "stopped being different things and the split should be revisited.");
        }

        [Test]
        public void ExhaustivePlan_CoversEveryPosition()
        {
            Assert.That(BenchmarkPlan.Exhaustive().PositionIndices.Count,
                Is.EqualTo(MobileSearchBenchmarkRunner.PositionCount));
        }
    }
}
