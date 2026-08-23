using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// A recording harness, not a pass/fail gate — mirrors SearchDepthProfileCaptureTests' shape but
    /// measures the opposite thing on purpose. That harness searches UNCAPPED to a fixed depth to see
    /// what a depth costs; this one searches CAPPED, through the exact contract a real match gives
    /// the search (CancelAfter(HardMs) plus the settle-early/panic-extend logic), to see what depth a
    /// real move actually reaches. The two questions are not interchangeable: a cheaper tree (fewer
    /// nodes to reach a depth) does not automatically mean a deeper capped search, because the deep
    /// tiers can also stop EARLY on a settled position long before their time or their MaxDepth ceiling
    /// is reached. Recording the stop reason alongside the depth is what tells those apart.
    ///
    /// Position mix is deliberately the union of two existing sets, not one: the five hand-authored
    /// shapes from SearchDepthProfileCaptureTests (quiet-midgame, semi-open-midgame, tactical-midgame,
    /// betrayal-live-midgame, quiet-endgame) are the only positions with an existing uncapped
    /// node-count table to compare a capped depth result against, and a sample of CuratedPositionSuite's
    /// real opening lines checks whether the hand-authored shapes generalize to positions nobody
    /// hand-picked for this specific question.
    ///
    /// Only the tiers with a genuine soft/hard gap are worth measuring here — easy and normal are
    /// shallow by design (their MaxDepth is part of the difficulty identity, not a performance
    /// ceiling) and complete their full configured depth in a fraction of their budget regardless.
    /// </summary>
    [TestFixture]
    [Explicit("Recording harness — run manually and read the per-position/per-tier depth-ceiling profile from the log.")]
    public class CappedDepthCeilingReverifyTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        private static readonly (string name, System.Func<BoardState> build)[] Positions =
        {
            ("quiet-midgame", SearchDepthProfileCaptureTests.QuietMidgame),
            ("semi-open-midgame", SearchDepthProfileCaptureTests.SemiOpenMidgame),
            ("tactical-midgame", SearchDepthProfileCaptureTests.TacticalMidgame),
            ("betrayal-live-midgame", SearchDepthProfileCaptureTests.BetrayalLiveMidgame),
            ("quiet-endgame", SearchDepthProfileCaptureTests.QuietEndgame),
            ("curated-0-italian", () => CuratedPositionSuite.Build(0)),
            ("curated-8-qgd", () => CuratedPositionSuite.Build(8)),
            ("curated-12-dutch", () => CuratedPositionSuite.Build(12)),
        };

        private static readonly string[] DeepTierIds = { "hard", "aggressive", "extreme", "impossible" };

        private static AIProfile FindProfile(string id)
        {
            foreach (AIProfile profile in AIProfileTable.BuiltIn)
                if (profile.Id == id) return profile;
            Assert.Fail($"No built-in profile named '{id}'.");
            return default;
        }

        /// <summary>Runs one search exactly the way AsyncAIAgent does in a real match — a hard-budget
        /// cancellation timer plus the settle-early logic — and prints the position/tier/depth/
        /// stop-reason/node-count line this whole harness exists to produce.</summary>
        private void CaptureOne(string positionName, BoardState board, string tierId)
        {
            AIProfile profile = FindProfile(tierId);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));
            var settings = new AISearchSettings(profile.MaxDepth, profile.TimeBudget, BetrayalUsage.Full);

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(settings.TimeBudget.HardMs);
                search.FindBestMove(board, settings, cts.Token, enableInstabilityTimeManagement: true);
            }

            SearchStats stats = search.Stats;
            System.Console.WriteLine(
                $"[capped-reverify] position={positionName} tier={tierId} maxDepth={profile.MaxDepth} " +
                $"softMs={settings.TimeBudget.SoftMs} hardMs={settings.TimeBudget.HardMs} " +
                $"stopReason={stats.StopReason} lastDepth={stats.LastCompletedDepth} " +
                $"nodes={stats.NodesVisited} qnodes={stats.QNodesVisited}");
        }

        [Test]
        public void CapturePositionSweep_AllDeepTiers()
        {
            foreach ((string name, System.Func<BoardState> build) in Positions)
                foreach (string tierId in DeepTierIds)
                    CaptureOne(name, build(), tierId);
        }
    }
}
