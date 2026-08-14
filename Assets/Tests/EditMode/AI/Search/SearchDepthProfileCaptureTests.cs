using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.AI.Positions;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI.Search
{
    /// <summary>
    /// A recording harness, not a pass/fail gate. It searches the same fixed positions at depths 7,
    /// 8, and 9 with the full telemetry on and prints the per-depth node curve, per-depth time,
    /// branching factor, and the eval/movegen/table/quiescence split — the raw material for judging
    /// exactly what the deepest tiers pay to go one ply deeper. Marked so it never runs in a normal
    /// pass; run it on purpose and read the numbers from the log.
    ///
    /// The searches are uncapped on purpose: the point is to SEE the cost of reaching depth 9, so the
    /// budget must not cut the search off before it gets there. That is the opposite of what a real
    /// match does, and it is deliberate — this measures the tree, not the clock.
    ///
    /// Five positions on purpose, not one. A single fixed opening understates the real cost: a quiet
    /// middlegame, a tactical one with a live capture chain, a Betrayal-live one, a reduced-material
    /// endgame, and a second, structurally different quiet position all grow their trees differently
    /// past depth 7, and the deepest tiers meet all of these shapes in real games. The two quiet
    /// positions exist so "quiet costs 24 seconds at depth 9" is a claim about quiet play in general,
    /// not an artifact of one specific board.
    /// </summary>
    [TestFixture]
    [Explicit("Recording harness — run manually and read the per-depth profile from the log.")]
    public class SearchDepthProfileCaptureTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        private static readonly int[] CaptureDepths = { 7, 8, 9 };

        private void CaptureProfile(string positionName, BoardState board)
        {
            foreach (int depth in CaptureDepths)
            {
                // A fresh search per depth so each reading is a clean cold search to exactly that
                // depth — no iterative-deepening carryover from a deeper run muddying the per-depth
                // node/time numbers. Uncapped: the whole point is to reach the depth and see its cost.
                var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                    transpositionTable: new TranspositionTable(log2Size: 20));
                var settings = new AISearchSettings(maxDepth: depth, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

                search.FindBestMove(board, settings, CancellationToken.None);

                System.Console.WriteLine($"[profile] {positionName} @ maxDepth {depth}: {search.Stats}");
            }
        }

        [Test]
        public void CaptureDepthProfile_QuietMidgame() => CaptureProfile("quiet-midgame", SearchProfilePositions.QuietMidgame());

        [Test]
        public void CaptureDepthProfile_TacticalMidgame() => CaptureProfile("tactical-midgame", SearchProfilePositions.TacticalMidgame());

        [Test]
        public void CaptureDepthProfile_BetrayalLiveMidgame() => CaptureProfile("betrayal-live-midgame", SearchProfilePositions.BetrayalLiveMidgame());

        [Test]
        public void CaptureDepthProfile_QuietEndgame() => CaptureProfile("quiet-endgame", SearchProfilePositions.QuietEndgame());

        [Test]
        public void CaptureDepthProfile_SemiOpenMidgame() => CaptureProfile("semi-open-midgame", SearchProfilePositions.SemiOpenMidgame());
    }
}
