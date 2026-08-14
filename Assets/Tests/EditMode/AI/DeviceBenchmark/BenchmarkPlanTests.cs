using NUnit.Framework;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Positions;
using ChessTheBetrayal.AI.DeviceBenchmark;

namespace ChessTheBetrayal.Tests.EditMode.AI.DeviceBenchmark
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

        /// <summary>
        /// The cells that actually run and the denominator the progress line counts against come
        /// from two different pieces of code, and a run whose progress stops short of its own total
        /// (or reaches it early) reads as a broken run. Checked on both plans, since only one of
        /// them asks for a main-thread control on every position.
        /// </summary>
        [Test]
        public void Cells_YieldExactlyAsManyCellsAsTheProgressDenominatorPromises(
            [Values("tester", "exhaustive", "thermal")] string planName)
        {
            BenchmarkPlan plan = planName switch
            {
                "tester" => BenchmarkPlan.Tester(),
                "exhaustive" => BenchmarkPlan.Exhaustive(),
                _ => BenchmarkPlan.Thermal(),
            };

            int yielded = 0;
            foreach (BenchmarkCell _ in plan.Cells()) yielded++;

            Assert.That(yielded, Is.EqualTo(plan.TotalCells));
        }

        [Test]
        public void Cells_FinishTheWholeWorkerPassBeforeStartingTheMainThreadControl()
        {
            var plan = BenchmarkPlan.Tester();

            bool seenControlCell = false;
            foreach (BenchmarkCell cell in plan.Cells())
            {
                if (!cell.OnWorkerThread) seenControlCell = true;
                else
                    Assert.That(seenControlCell, Is.False,
                        "A worker cell after the control has started means an interrupted run gets " +
                        "an arbitrary mix of the two rather than a complete worker pass.");
            }

            Assert.That(seenControlCell, "The control pass must actually run — it is the only thing " +
                "that shows whether a device schedules a worker differently from the frame thread.");
        }

        [Test]
        public void Cells_SweepEveryTierOnEveryPositionTheyCover()
        {
            var plan = BenchmarkPlan.Tester();

            foreach (int positionIndex in plan.PositionIndices)
            {
                foreach (AIProfile profile in AIProfileTable.BuiltIn)
                {
                    int matches = 0;
                    foreach (BenchmarkCell cell in plan.Cells())
                    {
                        if (cell.OnWorkerThread && cell.PositionIndex == positionIndex && cell.ProfileId == profile.Id)
                            matches++;
                    }

                    Assert.That(matches, Is.EqualTo(plan.RepeatCount),
                        $"Position {positionIndex} should be searched by '{profile.Id}' once per repeat.");
                }
            }
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

        [Test]
        public void ThermalPlan_OnlySweepsTheImpossibleTier()
        {
            var plan = BenchmarkPlan.Thermal();

            Assert.That(plan.Profiles.Count, Is.EqualTo(1),
                "Sweeping any other tier would dilute the sustained-load signal this run exists to isolate.");
            Assert.That(plan.Profiles[0].Id, Is.EqualTo("impossible"));
        }

        [Test]
        public void ThermalPlan_RunsTwoHundredSearches_WithATenMinuteWorstCase()
        {
            var plan = BenchmarkPlan.Thermal();

            Assert.That(plan.TotalCells, Is.EqualTo(BenchmarkPlan.ThermalSearchCount));
            Assert.That(plan.EstimatedWorstCase.TotalMilliseconds,
                Is.EqualTo(BenchmarkPlan.ThermalSearchCount * (double)plan.Profiles[0].TimeBudget.HardMs));
        }

        [Test]
        public void EveryPlan_HasItsOwnName_SoTwoReportsCanNeverBeConfusedForEachOther()
        {
            var names = new[]
            {
                BenchmarkPlan.Tester().Name,
                BenchmarkPlan.Thermal().Name,
                BenchmarkPlan.Exhaustive().Name,
            };

            foreach (string name in names)
                Assert.That(name, Is.Not.Null.And.Not.Empty);

            Assert.That(names, Is.Unique,
                "Two plans sharing a name would put the same words on reports answering different " +
                "questions, which is the confusion naming them exists to end.");
        }

        [Test]
        public void ThermalPlan_DescribesItselfAsRepetitionRatherThanBreadth()
        {
            string described = BenchmarkPlan.Thermal().Describe();

            // The cell count cannot tell these apart: 200 cells is 200 cells whether they are 200
            // positions or one position 200 times, and only one of those is a sustained-load probe.
            Assert.That(described, Does.Contain("1 position"));
            Assert.That(described, Does.Contain("the impossible tier alone"));
            Assert.That(described, Does.Contain($"{BenchmarkPlan.ThermalSearchCount} times each"));
            Assert.That(described, Does.Contain("worker thread only"));
        }

        [Test]
        public void TesterPlan_DescribesItsBreadthAndItsControl()
        {
            string described = BenchmarkPlan.Tester().Describe();

            Assert.That(described, Does.Contain("4 positions"));
            Assert.That(described, Does.Contain("6 tiers"));
            Assert.That(described, Does.Contain("main-thread control"));
        }

        [Test]
        public void CurveTracksSustainedLoad_OnlyForTheRunWhereEverySampleDidTheSameWork()
        {
            Assert.That(BenchmarkPlan.Thermal().CurveTracksSustainedLoad, Is.True,
                "One position at one tier, repeated — a change across minutes is a change in the device.");
            Assert.That(BenchmarkPlan.Tester().CurveTracksSustainedLoad, Is.False,
                "Four positions and six tiers: a minute holds whatever cells fell in it, so its depth " +
                "tracks the running order and not the hardware.");
            Assert.That(BenchmarkPlan.Exhaustive().CurveTracksSustainedLoad, Is.False);
        }

        [Test]
        public void Describe_ProducesNothingOutsidePlainAscii()
        {
            ReportTextAssertions.AssertPlainAscii(BenchmarkPlan.Tester().Describe());
            ReportTextAssertions.AssertPlainAscii(BenchmarkPlan.Thermal().Describe());
            ReportTextAssertions.AssertPlainAscii(BenchmarkPlan.Exhaustive().Describe());
        }

        [Test]
        public void HasMainThreadControl_IsTrueOnlyForAPlanThatActuallyRunsOne()
        {
            Assert.That(BenchmarkPlan.Tester().HasMainThreadControl, Is.True);
            Assert.That(BenchmarkPlan.Exhaustive().HasMainThreadControl, Is.True);
            Assert.That(BenchmarkPlan.Thermal().HasMainThreadControl, Is.False,
                "Reporting a main-thread absence against a plan that never asked for one says " +
                "nothing, and reads as a run that fell short.");
        }

        [Test]
        public void ThermalPlan_SkipsPlayForwardAndTheMainThreadControl()
        {
            var plan = BenchmarkPlan.Thermal();

            Assert.That(plan.IncludePlayForward, Is.False,
                "A play-forward cell would run five searches instead of one, breaking the 200-search arithmetic.");

            foreach (BenchmarkCell cell in plan.Cells())
            {
                Assert.That(cell.OnWorkerThread, Is.True,
                    "Production always dispatches a search onto a worker, so a main-thread control cell " +
                    "would measure a context sustained load never actually runs in.");
            }
        }
    }
}
