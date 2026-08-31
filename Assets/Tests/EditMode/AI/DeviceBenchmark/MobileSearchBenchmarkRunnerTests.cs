using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Positions;
using ChessTheBetrayal.AI.DeviceBenchmark;
using ChessTheBetrayal.AI.Agent;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.AI.DeviceBenchmark
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
        public void TheProductionTableSizeIsTheOneARealMatchAllocates()
        {
            // This used to read its own literal and compare it to another literal, under a name
            // claiming it checked the agent. It checked nothing: either number could move alone.
            // The runner now reads the agent's constant, so the compiler holds them together and
            // the only thing left worth asserting is the value itself.
            Assert.That(MobileSearchBenchmarkRunner.ProductionTranspositionTableLog2Size,
                Is.EqualTo(AsyncAIAgent.ProductionTranspositionTableLog2Size));
            Assert.That(AsyncAIAgent.ProductionTranspositionTableLog2Size, Is.EqualTo(20),
                "A real match runs on 2^20 entries, about 16 MB. Measuring a device against anything " +
                "smaller reports worse move ordering than a player ever meets.");
        }

        [Test]
        public void TheRescoreMarginIsTheOneTheAgentWouldHaveUsed()
        {
            // Whichever dial reaches further sets the margin. A tier with neither asks for no
            // rescore pass at all, which is the case that has to stay at zero: a nonzero margin
            // there buys a second pass over the root for a profile that cannot use the result.
            var blunderer = ProfileWith(blunderMarginCp: 90, tieBreakWindowCp: 20);
            var tieBreaker = ProfileWith(blunderMarginCp: 15, tieBreakWindowCp: 75);
            var neither = ProfileWith(blunderMarginCp: 0, tieBreakWindowCp: 0);

            Assert.That(blunderer.RescoreMarginCp, Is.EqualTo(90));
            Assert.That(tieBreaker.RescoreMarginCp, Is.EqualTo(75));
            Assert.That(neither.RescoreMarginCp, Is.Zero);
        }

        [Test]
        public void EachProfileIsMeasuredAgainstItsOwnWeightedEvaluator()
        {
            // The defect this replaced: every tier was benchmarked with the identity evaluator, so
            // an aggressive tier's numbers described a player nobody faces. A profile whose attack
            // dial is turned up must score a position differently from one left at identity, or the
            // weighting is not reaching the evaluator the runner hands to the search.
            //
            // An ordinary midgame will not do, and that is worth saying: the attack-weighted terms
            // there run close enough to level between the two sides that scaling both cancels, and
            // the two evaluators agree to the point. This board is one-sided on purpose — White
            // holds mating material against a bare king, which is the one thing only the attacking
            // side ever scores.
            BoardState board = BoardSetup.CreateEmpty()
                .WithPiece("e5", Team.White, ChessPieceType.King)
                .WithPiece("a4", Team.White, ChessPieceType.Queen)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithBetrayalRight(false)
                .WithComputedHash();

            int aggressive = MobileSearchBenchmarkRunner
                .EvaluatorFor(ProfileWith(attackDefenseBias: 1.5f)).Evaluate(board, Team.White);
            int identity = MobileSearchBenchmarkRunner
                .EvaluatorFor(ProfileWith(attackDefenseBias: 1.0f)).Evaluate(board, Team.White);

            Assert.That(aggressive, Is.Not.EqualTo(identity),
                "An attack-weighted profile scored this position exactly as the identity evaluator " +
                "did, so the profile's dials are not reaching the evaluator being measured.");
        }

        private static AIProfile ProfileWith(int blunderMarginCp = 0, int tieBreakWindowCp = 0,
            float attackDefenseBias = 1f) =>
            new AIProfile("probe", maxDepth: 4, timeBudget: new AITimeBudget(500, 700),
                blunderRate: 0f, blunderMarginCp: blunderMarginCp, betrayalAggression: 0f,
                attackDefenseBias: attackDefenseBias, tieBreakWindowCp: tieBreakWindowCp,
                useOpeningBook: false);

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

        /// <summary>
        /// A cell that did exactly what it was asked used to announce itself as "[budget-capped]".
        /// The normal tier is the plain case: it completes depth 5, which is its configured ceiling,
        /// and then spends what is left of its budget in the tie-break pass, which runs until the
        /// hard timer stops it. The timer firing was the whole test, so the line read as a failure
        /// and testers reported it as one.
        /// </summary>
        [Test]
        public void OutcomeNote_ATierThatReachedItsCeilingAndSpentTheRest_DoesNotReadAsAFailure()
        {
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(
                seconds: 2.25, budgetCapped: true, depthReached: 5, hardMs: 2250,
                stopReason: SearchStopReason.Ceiling);

            string note = MobileSearchBenchmarkRunner.OutcomeNote(timing);

            Assert.That(note, Does.Contain("ceiling"));
            Assert.That(note, Does.Contain("tie-break"));
            Assert.That(note, Does.Not.Contain("clock stopped"),
                "Nothing stopped this search short - it finished every depth it was configured for.");
        }

        [Test]
        public void OutcomeNote_ASearchTheClockCutShort_SaysSoPlainly()
        {
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(
                seconds: 3.0, budgetCapped: true, depthReached: 7, hardMs: 3000,
                stopReason: SearchStopReason.Budget);

            Assert.That(MobileSearchBenchmarkRunner.OutcomeNote(timing),
                Does.Contain("the clock stopped it at depth 7"));
        }

        /// <summary>
        /// The load-bearing half of the pair above. Both searches ran their timer out, so a check
        /// that only looks for a word in one of them passes just as happily when the two render
        /// identically - which is the state this replaced, where they did.
        ///
        /// Same elapsed time, same budget and above all the same depth, so the stop reason is the
        /// only thing left that can tell them apart. Written first with different depths, which made
        /// it pass on the depth alone and prove nothing about the branch it is here to guard.
        /// </summary>
        [Test]
        public void OutcomeNote_ReachingTheCeilingAndBeingCutShortDoNotReadTheSame()
        {
            var reachedCeiling = new MobileSearchBenchmarkRunner.SearchTiming(
                seconds: 3.0, budgetCapped: true, depthReached: 7, hardMs: 3000,
                stopReason: SearchStopReason.Ceiling);
            var cutShort = new MobileSearchBenchmarkRunner.SearchTiming(
                seconds: 3.0, budgetCapped: true, depthReached: 7, hardMs: 3000,
                stopReason: SearchStopReason.Budget);

            Assert.That(MobileSearchBenchmarkRunner.OutcomeNote(reachedCeiling),
                Is.Not.EqualTo(MobileSearchBenchmarkRunner.OutcomeNote(cutShort)));
        }

        [Test]
        public void OutcomeNote_ASearchThatSettledEarly_SaysItStoppedOnItsOwn()
        {
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(
                seconds: 0.9, budgetCapped: false, depthReached: 6, hardMs: 3000,
                stopReason: SearchStopReason.SettledEarly);

            Assert.That(MobileSearchBenchmarkRunner.OutcomeNote(timing), Does.Contain("stopped early"));
        }

        [Test]
        public void OutcomeNote_ASearchThatFoundAMate_SaysWhyItStoppedShallow()
        {
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(
                seconds: 0.04, budgetCapped: false, depthReached: 2, hardMs: 3000,
                stopReason: SearchStopReason.MateFound);

            Assert.That(MobileSearchBenchmarkRunner.OutcomeNote(timing), Does.Contain("forced mate"));
        }

        /// <summary>
        /// A search cancelled before it finished a single depth has no depth to report, and the line
        /// has to say that rather than leave a reader working it out from a missing clause.
        /// </summary>
        [Test]
        public void OutcomeNote_ASearchThatCompletedNoDepth_SaysSo()
        {
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(
                seconds: 3.0, budgetCapped: true, depthReached: 0, hardMs: 3000,
                stopReason: SearchStopReason.Budget);

            Assert.That(MobileSearchBenchmarkRunner.OutcomeNote(timing), Is.EqualTo("no depth completed"));
        }

        [Test]
        public void BudgetNote_WithinItsOwnBudget_ReportsNoOvershoot()
        {
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(seconds: 2.5, budgetCapped: false, depthReached: 7, hardMs: 3000);

            string note = MobileSearchBenchmarkRunner.BudgetNote(timing);

            // Says how much room was left, not merely that there was some. "Within budget" read the
            // same on a tier finishing in a fifth of its budget as on one a millisecond inside it,
            // and 129 cells of a real 200-cell run said exactly that.
            Assert.That(note, Does.Contain("500ms inside budget"));
            Assert.That(note, Does.Not.Contain("past budget"));
        }

        /// <summary>
        /// Rounded to the millisecond, a search a fraction inside its budget has no room left to
        /// report. Printing "0ms inside budget" is the shape that already taught a reader to stop
        /// believing these lines, on a run where 194 rows of 200 announced an overshoot of zero.
        /// </summary>
        [Test]
        public void BudgetNote_AFractionInsideItsBudget_SaysSoInWordsRatherThanPrintingZero()
        {
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(
                seconds: 2.9997, budgetCapped: true, depthReached: 7, hardMs: 3000);

            string note = MobileSearchBenchmarkRunner.BudgetNote(timing);

            Assert.That(note, Does.Contain("on budget to the millisecond"));
            Assert.That(note, Does.Not.Contain("0ms inside"),
                "A rounded-away margin must not be printed as a magnitude of zero.");
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
        public void BudgetNote_AnOvershootTooSmallToPrint_IsNotAnnouncedAsOne()
        {
            // 0.3ms past a 3000ms budget. Exact enough to be a positive number, far too small to
            // survive being rendered at whole-millisecond precision — a run of 200 searches on a
            // real device produced 194 lines exactly like this one, each announcing an overshoot
            // and then naming it as zero.
            var timing = new MobileSearchBenchmarkRunner.SearchTiming(seconds: 3.0003, budgetCapped: true, depthReached: 7, hardMs: 3000);

            string note = MobileSearchBenchmarkRunner.BudgetNote(timing);

            Assert.That(note, Does.Contain("on budget to the millisecond"),
                "An overshoot that rounds away to nothing must not be announced as one.");
            Assert.That(note, Does.Not.Contain("past budget"));
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
