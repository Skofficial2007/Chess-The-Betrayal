using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Positions;
using ChessTheBetrayal.AI.Agent;
using ChessTheBetrayal.AI.DeviceBenchmark;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// This runner had no test coverage before now, even though it's genuinely Unity-free and never
    /// needed a scene or Play mode to exercise. Covers the transposition-table sizing, each cell's
    /// evaluator/settings wiring, the position-index mapping across the boundary between the
    /// replayed curated openings and the two hand-placed positions, and the worker-thread/
    /// main-thread labeling that keeps the two thread contexts from being blended together.
    /// </summary>
    [TestFixture]
    public class MobileSearchBenchmarkRunnerTests
    {
        [Test]
        public void ProductionTranspositionTableSize_MatchesAsyncAIAgentsSizing()
        {
            Assert.That(MobileSearchBenchmarkRunner.ProductionTranspositionTableLog2Size, Is.EqualTo(20),
                "AsyncAIAgent sizes its production table with log2Size 20 (~16 MB); a smaller table " +
                "here would measure worse move ordering than a real match ever has.");
        }

        [Test]
        public void SingleMoveSettings_UseBetrayalUsageFull_AndCarryTheProfilesDepthAndBudget()
        {
            var profile = new AIProfile("probe", maxDepth: 5, timeBudget: new AITimeBudget(1200, 1500),
                blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f,
                tieBreakWindowCp: 0, useOpeningBook: false);

            AISearchSettings settings = MobileSearchBenchmarkRunner.SingleMoveSettingsFor(profile);

            Assert.That(settings.BetrayalUsage, Is.EqualTo(BetrayalUsage.Full),
                "The single cold search never gets applied to a board, so it can safely measure the " +
                "setting a player actually gets by default.");
            Assert.That(settings.MaxDepth, Is.EqualTo(5));
            Assert.That(settings.TimeBudget.HardMs, Is.EqualTo(1500));
        }

        [Test]
        public void MultiMoveSettings_UseBetrayalUsageDefendOnly_AndCarryTheProfilesDepthAndBudget()
        {
            var profile = new AIProfile("probe", maxDepth: 6, timeBudget: new AITimeBudget(900, 1100),
                blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f,
                tieBreakWindowCp: 0, useOpeningBook: false);

            AISearchSettings settings = MobileSearchBenchmarkRunner.MultiMoveSettingsFor(profile);

            Assert.That(settings.BetrayalUsage, Is.EqualTo(BetrayalUsage.DefendOnly),
                "The play-forward loop has no Retribution model, so a staged Act at the root would " +
                "corrupt the rest of the run — this stays DefendOnly regardless of what the single " +
                "search measures.");
            Assert.That(settings.MaxDepth, Is.EqualTo(6));
            Assert.That(settings.TimeBudget.HardMs, Is.EqualTo(1100));
        }

        [Test]
        public void PositionCount_IsEveryCuratedOpeningPlusTheTwoWallShapes()
        {
            Assert.That(MobileSearchBenchmarkRunner.PositionCount, Is.EqualTo(CuratedOpeningLines.Count + 2));
        }

        [Test]
        public void PositionName_TheLastCuratedIndexAndTheFirstWallIndexDoNotOffByOne()
        {
            int lastCuratedIndex = CuratedOpeningLines.Count - 1;
            int firstWallIndex = CuratedOpeningLines.Count;
            int secondWallIndex = CuratedOpeningLines.Count + 1;

            Assert.That(MobileSearchBenchmarkRunner.PositionName(lastCuratedIndex),
                Is.EqualTo(CuratedOpeningLines.Line(lastCuratedIndex)));
            Assert.That(MobileSearchBenchmarkRunner.PositionName(firstWallIndex), Is.EqualTo("quiet-midgame"));
            Assert.That(MobileSearchBenchmarkRunner.PositionName(secondWallIndex), Is.EqualTo("semi-open-midgame"));
        }

        [Test]
        public void BuildPosition_TheWallIndicesReturnTheirGoldenPinnedBoards()
        {
            int firstWallIndex = CuratedOpeningLines.Count;
            int secondWallIndex = CuratedOpeningLines.Count + 1;

            // Same literals pinned in DepthWallPositionsTests — kept as literals here too so this
            // test still catches an index-mapping regression even if that file's constants change.
            Assert.That(MobileSearchBenchmarkRunner.BuildPosition(firstWallIndex).ZobristHash,
                Is.EqualTo(9567791423313919642UL));
            Assert.That(MobileSearchBenchmarkRunner.BuildPosition(secondWallIndex).ZobristHash,
                Is.EqualTo(18257401590649323034UL));
        }

        [Test]
        public void BuildSearch_ConstructsAWorkingSearchForEveryBuiltInProfile()
        {
            var engine = new ChessEngineAdapter();

            foreach (AIProfile profile in AIProfileTable.BuiltIn)
            {
                AlphaBetaSearch search = MobileSearchBenchmarkRunner.BuildSearch(engine, profile);
                Assert.That(search, Is.Not.Null, $"profile '{profile.Id}' failed to build a search.");
            }
        }

        [Test]
        public void RunCell_LabelsItsLinesWithThePositionNameAndTheGivenRepeatIndex()
        {
            var runner = new MobileSearchBenchmarkRunner();
            var lines = new List<string>();
            runner.OnLine += lines.Add;

            runner.RunCell(positionIndex: 0, AIProfile.None, repeatIndex: 2);

            string expectedPositionName = MobileSearchBenchmarkRunner.PositionName(0);
            Assert.That(lines.Any(l => l.Contains(expectedPositionName) && l.Contains("single-move rep3")), Is.True,
                "Expected a single-move line naming this position at rep3, got:\n" + string.Join("\n", lines));
            Assert.That(lines.Any(l => l.Contains(expectedPositionName) && l.Contains("multi-move rep3")), Is.True,
                "Expected at least one multi-move line naming this position at rep3, got:\n" + string.Join("\n", lines));
            Assert.That(lines.All(l => l.Contains(MobileSearchBenchmarkRunner.MainThreadLabel)), Is.True,
                "RunCell runs on the calling thread and must always label its lines main-thread.");
        }

        [Test]
        public void RunCell_OnAWallShapePosition_LabelsItsLinesWithTheWallShapesName()
        {
            var runner = new MobileSearchBenchmarkRunner();
            var lines = new List<string>();
            runner.OnLine += lines.Add;

            int firstWallIndex = MobileSearchBenchmarkRunner.PositionCount - 2;
            runner.RunCell(firstWallIndex, AIProfile.None, repeatIndex: 0);

            Assert.That(lines.Any(l => l.Contains("quiet-midgame")), Is.True,
                "Expected at least one line naming quiet-midgame, got:\n" + string.Join("\n", lines));
        }

        [Test]
        public void RunCellOnWorkerThread_CompletesSuccessfully_AndLabelsItsLinesWorkerThread()
        {
            var runner = new MobileSearchBenchmarkRunner();
            var lines = new List<string>();
            runner.OnLine += lines.Add;

            Task cellTask = runner.RunCellOnWorkerThread(positionIndex: 0, AIProfile.None, repeatIndex: 0);
            cellTask.Wait();

            Assert.That(cellTask.IsFaulted, Is.False,
                cellTask.Exception != null ? cellTask.Exception.ToString() : "no exception recorded");
            Assert.That(lines.Any(l => l.Contains(MobileSearchBenchmarkRunner.WorkerThreadLabel) && l.Contains("single-move rep1")), Is.True,
                "Expected a worker-thread single-move line, got:\n" + string.Join("\n", lines));
            Assert.That(lines.Any(l => l.Contains(MobileSearchBenchmarkRunner.MainThreadLabel)), Is.False,
                "A worker-thread cell must never label a line main-thread.");
        }

        [Test]
        public void BudgetNote_WithinItsOwnBudget_ReportsNoOvershoot()
        {
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(seconds: 2.5, budgetCapped: false, depthReached: 7, hardMs: 3000);

            string note = MobileSearchBenchmarkRunner.BudgetNote(timing);

            Assert.That(note, Does.Contain("within budget"));
            Assert.That(note, Does.Not.Contain("past budget"));
        }

        [Test]
        public void BudgetNote_PastItsOwnBudget_ReportsTheOvershootInMilliseconds()
        {
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(seconds: 3.2, budgetCapped: true, depthReached: 4, hardMs: 3000);

            string note = MobileSearchBenchmarkRunner.BudgetNote(timing);

            Assert.That(note, Does.Contain("+200ms past budget"));
        }

        [Test]
        public void BudgetNote_JudgesAgainstTheTiersOwnBudget_NotAFixedSixSeconds()
        {
            // easy's real budget is 1300ms. Two seconds is a genuine, serious overshoot for this
            // tier — the fixed 6-second threshold this replaced would have called it a pass.
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(seconds: 2.0, budgetCapped: true, depthReached: 3, hardMs: 1300);

            string note = MobileSearchBenchmarkRunner.BudgetNote(timing);

            Assert.That(note, Does.Contain("past budget"));
        }

        [Test]
        public void BudgetNote_AnOvershootTooSmallToPrint_ReadsAsWithinBudget()
        {
            // 0.3ms past a 3000ms budget. Exact enough to be a positive number, far too small to
            // survive being rendered at whole-millisecond precision — a run of 200 searches on a
            // real device produced 194 lines exactly like this one, each announcing an overshoot
            // and then naming it as zero.
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(seconds: 3.0003, budgetCapped: true, depthReached: 7, hardMs: 3000);

            string note = MobileSearchBenchmarkRunner.BudgetNote(timing);

            Assert.That(note, Does.Contain("within budget"),
                "An overshoot that rounds away to nothing must not be announced as one.");
            Assert.That(note, Does.Not.Contain("0ms past budget"));
        }

        [Test]
        public void EmitTierSummaries_AnOvershootTooSmallToPrint_ReportsNoneRatherThanPlusZero()
        {
            var runner = new MobileSearchBenchmarkRunner();
            string mainThread = MobileSearchBenchmarkRunner.MainThreadLabel;
            runner.RecordTiming("easy", mainThread,
                new MobileSearchBenchmarkRunner.SearchTiming(seconds: 1.3004, budgetCapped: true, depthReached: 3, hardMs: 1300));

            var lines = new List<string>();
            runner.OnLine += lines.Add;
            runner.EmitTierSummaries(AIProfileTable.BuiltIn);

            string line = lines.Single(l => l.StartsWith($"[easy {mainThread}]"));
            Assert.That(line, Does.Contain("worst overshoot none"),
                "The summary decides and prints from the same rounded value the detail lines do.");
            Assert.That(line, Does.Not.Contain("+0ms"));
        }

        [Test]
        public void EmitTierSummaries_ReportsWorstMeanMinElapsedAndWorstOvershoot()
        {
            var runner = new MobileSearchBenchmarkRunner();
            string mainThread = MobileSearchBenchmarkRunner.MainThreadLabel;
            runner.RecordTiming("easy", mainThread, new MobileSearchBenchmarkRunner.SearchTiming(seconds: 1.0, budgetCapped: false, depthReached: 5, hardMs: 1300));
            runner.RecordTiming("easy", mainThread, new MobileSearchBenchmarkRunner.SearchTiming(seconds: 1.5, budgetCapped: true, depthReached: 4, hardMs: 1300));
            runner.RecordTiming("easy", mainThread, new MobileSearchBenchmarkRunner.SearchTiming(seconds: 1.2, budgetCapped: false, depthReached: 6, hardMs: 1300));

            var lines = new List<string>();
            runner.OnLine += lines.Add;
            runner.EmitTierSummaries(AIProfileTable.BuiltIn);

            string line = lines.Single(l => l.StartsWith($"[easy {mainThread}]"));
            Assert.That(line, Does.Contain("3 samples"));
            Assert.That(line, Does.Contain("worst 1.50s"));
            Assert.That(line, Does.Contain("min 1.00s"));
            Assert.That(line, Does.Contain("mean 1.23s"));
            Assert.That(line, Does.Contain("+200ms"),
                "The worst sample (1500ms) is 200ms past easy's 1300ms budget.");
            Assert.That(line, Does.Contain("depth worst 4"));
            Assert.That(line, Does.Contain("mean 5.0"));
        }

        [Test]
        public void EmitTierSummaries_ReportsMainThreadAndWorkerThreadAsSeparateSamplesForTheSameTier()
        {
            var runner = new MobileSearchBenchmarkRunner();
            string mainThread = MobileSearchBenchmarkRunner.MainThreadLabel;
            string workerThread = MobileSearchBenchmarkRunner.WorkerThreadLabel;
            runner.RecordTiming("easy", mainThread, new MobileSearchBenchmarkRunner.SearchTiming(seconds: 1.0, budgetCapped: false, depthReached: 5, hardMs: 1300));
            runner.RecordTiming("easy", workerThread, new MobileSearchBenchmarkRunner.SearchTiming(seconds: 2.0, budgetCapped: true, depthReached: 2, hardMs: 1300));

            var lines = new List<string>();
            runner.OnLine += lines.Add;
            runner.EmitTierSummaries(AIProfileTable.BuiltIn);

            string mainLine = lines.Single(l => l.StartsWith($"[easy {mainThread}]"));
            string workerLine = lines.Single(l => l.StartsWith($"[easy {workerThread}]"));

            Assert.That(mainLine, Does.Contain("worst 1.00s"));
            Assert.That(workerLine, Does.Contain("worst 2.00s"),
                "The two thread contexts must stay separate samples, not blended into one aggregate.");
        }

        [Test]
        public void EmitTierSummaries_ATierWithNoSamples_StillGetsALineSayingSoForBothThreadContexts()
        {
            var runner = new MobileSearchBenchmarkRunner();
            runner.RecordTiming("easy", MobileSearchBenchmarkRunner.MainThreadLabel,
                new MobileSearchBenchmarkRunner.SearchTiming(seconds: 1.0, budgetCapped: false, depthReached: 5, hardMs: 1300));

            var lines = new List<string>();
            runner.OnLine += lines.Add;
            runner.EmitTierSummaries(AIProfileTable.BuiltIn);

            // "impossible" never got a RecordTiming call on either thread. An absent line would
            // read as "nothing to report" when it could just mean the run never reached this tier
            // -- exactly the gap this method exists to close.
            Assert.That(lines.Any(l => l.StartsWith($"[impossible {MobileSearchBenchmarkRunner.MainThreadLabel}]") && l.Contains("no samples")), Is.True,
                "Expected an explicit no-samples main-thread line, got:\n" + string.Join("\n", lines));
            Assert.That(lines.Any(l => l.StartsWith($"[impossible {MobileSearchBenchmarkRunner.WorkerThreadLabel}]") && l.Contains("no samples")), Is.True,
                "Expected an explicit no-samples worker-thread line, got:\n" + string.Join("\n", lines));
        }

        [Test]
        public void EmitTierSummaries_ForAPlanWithNoMainThreadControl_SaysNothingAboutTheMainThread()
        {
            var runner = new MobileSearchBenchmarkRunner();
            runner.RecordTiming("impossible", MobileSearchBenchmarkRunner.WorkerThreadLabel,
                new MobileSearchBenchmarkRunner.SearchTiming(seconds: 3.0, budgetCapped: true, depthReached: 7, hardMs: 3000));

            var lines = new List<string>();
            runner.OnLine += lines.Add;
            runner.EmitTierSummaries(AIProfileTable.BuiltIn, includeMainThreadControl: false);

            // A "no samples recorded" line is only worth printing where samples were expected. The
            // sustained-load run deliberately never touches the main thread, so saying it found
            // nothing there reads as a run that fell short of something it was asked to do.
            Assert.That(lines.Any(l => l.Contains(MobileSearchBenchmarkRunner.MainThreadLabel)), Is.False,
                "Got:\n" + string.Join("\n", lines));
            Assert.That(lines.Any(l => l.StartsWith($"[impossible {MobileSearchBenchmarkRunner.WorkerThreadLabel}]")), Is.True);
        }

        [Test]
        public void EmitTierSummaries_AllSixBuiltInTiersAlwaysGetALineForEachThreadContext()
        {
            var runner = new MobileSearchBenchmarkRunner();
            var lines = new List<string>();
            runner.OnLine += lines.Add;

            runner.EmitTierSummaries(AIProfileTable.BuiltIn);

            foreach (AIProfile profile in AIProfileTable.BuiltIn)
            {
                Assert.That(lines.Any(l => l.StartsWith($"[{profile.Id} {MobileSearchBenchmarkRunner.MainThreadLabel}]")), Is.True,
                    $"Expected a main-thread summary line for '{profile.Id}' even with zero samples recorded.");
                Assert.That(lines.Any(l => l.StartsWith($"[{profile.Id} {MobileSearchBenchmarkRunner.WorkerThreadLabel}]")), Is.True,
                    $"Expected a worker-thread summary line for '{profile.Id}' even with zero samples recorded.");
            }
        }

        /// <summary>
        /// The thermal plan sweeps one tier, so a summary listing all six put eleven "no samples
        /// recorded" lines above the single real one on the first device report anyone read. Absence
        /// is only worth reporting against the tiers a run was actually meant to cover.
        /// </summary>
        [Test]
        public void EmitTierSummaries_ForASingleTierPlan_SaysNothingAboutTheTiersThatPlanNeverSweeps()
        {
            var runner = new MobileSearchBenchmarkRunner();
            AIProfile impossible = AIProfileTable.BuiltIn.Single(p => p.Id == "impossible");
            runner.RecordTiming(impossible.Id, MobileSearchBenchmarkRunner.WorkerThreadLabel,
                new MobileSearchBenchmarkRunner.SearchTiming(seconds: 3.0, budgetCapped: true, depthReached: 7, hardMs: 3000));

            IReadOnlyList<string> lines = runner.EmitTierSummaries(BenchmarkPlan.Thermal().Profiles);

            Assert.That(lines, Has.Count.EqualTo(2),
                "One tier, two thread contexts — and nothing at all about the five tiers this plan " +
                "never asked for. Got:\n" + string.Join("\n", lines));
            Assert.That(lines.Any(l => l.StartsWith($"[{impossible.Id} {MobileSearchBenchmarkRunner.WorkerThreadLabel}]")
                && l.Contains("1 samples")), Is.True);
            Assert.That(lines.Any(l => l.Contains("[easy ") || l.Contains("[normal ") || l.Contains("[hard ")), Is.False,
                "A tier outside the plan must not appear at all, not even to say it has no samples.");
        }

        [Test]
        public void EmitTierSummaries_ReturnsExactlyTheLinesItEmitted()
        {
            var runner = new MobileSearchBenchmarkRunner();
            runner.RecordTiming("easy", MobileSearchBenchmarkRunner.MainThreadLabel,
                new MobileSearchBenchmarkRunner.SearchTiming(seconds: 1.0, budgetCapped: false, depthReached: 5, hardMs: 1300));

            var emittedLines = new List<string>();
            runner.OnLine += emittedLines.Add;

            IReadOnlyList<string> returnedLines = runner.EmitTierSummaries(AIProfileTable.BuiltIn);

            // A caller assembling a structured report (BenchmarkReport) reads the summary from the
            // return value rather than re-parsing the general OnLine stream for it -- the two must
            // never disagree about what the summary actually said.
            Assert.That(returnedLines, Is.EqualTo(emittedLines));
        }

        [Test]
        public void EmitThermalBuckets_GroupsSamplesByMinuteSinceRunStart()
        {
            var runner = new MobileSearchBenchmarkRunner();
            string workerThread = MobileSearchBenchmarkRunner.WorkerThreadLabel;
            runner.RecordTiming("impossible", workerThread,
                new MobileSearchBenchmarkRunner.SearchTiming(seconds: 2.8, budgetCapped: true, depthReached: 7, hardMs: 3000, elapsedSinceRunStartMs: 5_000));
            runner.RecordTiming("impossible", workerThread,
                new MobileSearchBenchmarkRunner.SearchTiming(seconds: 2.9, budgetCapped: true, depthReached: 6, hardMs: 3000, elapsedSinceRunStartMs: 65_000));
            runner.RecordTiming("impossible", workerThread,
                new MobileSearchBenchmarkRunner.SearchTiming(seconds: 2.9, budgetCapped: true, depthReached: 5, hardMs: 3000, elapsedSinceRunStartMs: 90_000));

            var lines = new List<string>();
            runner.OnLine += lines.Add;
            runner.EmitThermalBuckets();

            string minuteZero = lines.Single(l => l.StartsWith($"[impossible {workerThread}] minute 0:"));
            string minuteOne = lines.Single(l => l.StartsWith($"[impossible {workerThread}] minute 1:"));

            Assert.That(minuteZero, Does.Contain("1 samples"));
            Assert.That(minuteZero, Does.Contain("depth worst 7"));
            Assert.That(minuteOne, Does.Contain("2 samples"),
                "65s and 90s both fall in minute 1 (60s-119s), so they must be grouped together.");
            Assert.That(minuteOne, Does.Contain("depth worst 5"));
            Assert.That(minuteOne, Does.Contain("mean 5.5"));
        }

        [Test]
        public void EmitThermalBuckets_SkipsCombinationsWithNoSamplesRecorded_UnlikeEmitTierSummaries()
        {
            var runner = new MobileSearchBenchmarkRunner();
            runner.RecordTiming("impossible", MobileSearchBenchmarkRunner.WorkerThreadLabel,
                new MobileSearchBenchmarkRunner.SearchTiming(seconds: 2.9, budgetCapped: true, depthReached: 7, hardMs: 3000));

            var lines = new List<string>();
            runner.OnLine += lines.Add;
            runner.EmitThermalBuckets();

            // The other five built-in tiers never had RecordTiming called at all. Unlike
            // EmitTierSummaries (which reports a fixed universe of six tiers), there is no fixed set
            // of minute buckets to enumerate against, so an untouched tier gets no line rather than
            // a placeholder one.
            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines.Any(l => l.Contains("no samples")), Is.False);
        }

        [Test]
        public void EmitThermalBuckets_ReturnsExactlyTheLinesItEmitted()
        {
            var runner = new MobileSearchBenchmarkRunner();
            runner.RecordTiming("impossible", MobileSearchBenchmarkRunner.WorkerThreadLabel,
                new MobileSearchBenchmarkRunner.SearchTiming(seconds: 2.9, budgetCapped: true, depthReached: 7, hardMs: 3000));

            var emittedLines = new List<string>();
            runner.OnLine += emittedLines.Add;

            IReadOnlyList<string> returnedLines = runner.EmitThermalBuckets();

            Assert.That(returnedLines, Is.EqualTo(emittedLines));
        }

    }
}
