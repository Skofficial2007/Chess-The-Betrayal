using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.DeviceBenchmark;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// This runner had no test coverage before now, even though it's genuinely Unity-free (bar the
    /// single device-info method) and never needed a scene or Play mode to exercise. Covers the
    /// transposition-table sizing, each cell's evaluator/settings wiring, and the position-index
    /// mapping across the boundary between the replayed curated openings and the two hand-placed
    /// positions.
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
    }
}
