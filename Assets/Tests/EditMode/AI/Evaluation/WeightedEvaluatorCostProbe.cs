using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.AI.Positions;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.EditMode.Support;

namespace ChessTheBetrayal.Tests.EditMode.AI.Evaluation
{
    /// <summary>
    /// Why the extreme tier reaches one ply less than impossible on the same position, the same
    /// budget and the same configured depth.
    ///
    /// The two differ in exactly one thing: extreme scales the evaluator's non-material terms
    /// (attack 1.2, defence 0.8, Betrayal option 1.15) where impossible leaves them alone. Under
    /// their own budgets impossible reached depth 9 in under a second while extreme spent its whole
    /// three seconds and stopped at 8, which costs that tier a ply outright and, because the depth
    /// loop then leaves nothing behind, its tie-break window as well.
    ///
    /// Scaling happens after every term is already computed - eight float multiplies on numbers the
    /// evaluator worked out either way - so paying three times the time for it would be surprising.
    /// The alternative is that different scores order and prune the tree differently, which costs
    /// nodes rather than time per node. Those two have opposite signatures and this separates them:
    ///
    ///   similar nodes, slower per node  -> the scaling itself is the cost
    ///   many more nodes, same per node  -> the weights are moving the search, not slowing it
    ///
    /// Every search here is fixed-depth with no clock, so both tiers are asked for identical work
    /// and the comparison is of what that work cost rather than of who ran out of time first.
    ///
    /// Explicit: this searches to a fixed depth with no budget and takes minutes.
    /// </summary>
    [TestFixture]
    [Explicit("Measurement only - fixed-depth searches with no time budget.")]
    [Category(TestCategories.OnDemand)]
    public class WeightedEvaluatorCostProbe
    {
        private const int TimeoutMs = 900_000;

        /// <summary>Deep enough for ordering and pruning differences to compound, shallow enough that
        /// six unbounded searches finish in a sitting. Extreme's own ceiling is 9; the question is
        /// what a ply costs it, not whether it can reach one.</summary>
        private const int FixedDepth = 8;

        private static IEnumerable<(string Name, Func<BoardState> Make)> Positions()
        {
            yield return ("quiet-midgame", DepthWallPositions.QuietMidgame);
            yield return ("semi-open-midgame", DepthWallPositions.SemiOpenMidgame);
            yield return ("opening", () => StandardChessPosition.Create(betrayalRightAvailable: true));
        }

        [Test]
        [Timeout(TimeoutMs)]
        public void WhatTheWeightedEvaluatorCostsAtAFixedDepth()
        {
            IChessEngine engine = new ChessEngineAdapter();
            var provider = new AIProfileTableProvider();

            AIProfile extreme = provider.Resolve("extreme");
            AIProfile impossible = provider.Resolve("impossible");

            TestContext.WriteLine($"Fixed depth {FixedDepth}, no clock, no cancellation.");
            TestContext.WriteLine(
                "position          tier        |    ms |      nodes |    qnodes | ns/node | 1st-move cutoff | TT hit");
            TestContext.WriteLine(
                "------------------------------|-------|------------|-----------|---------|-----------------|-------");

            foreach ((string name, Func<BoardState> make) in Positions())
            {
                Sample(engine, "extreme", extreme, make(), name);
                Sample(engine, "impossible", impossible, make(), name);
            }

            TestContext.WriteLine("");
            TestContext.WriteLine(
                "Both tiers carry the same max depth and budget in the profile table; the only "
                + "difference between these rows is how the evaluator's non-material terms are scaled.");
        }

        private static void Sample(IChessEngine engine, string label, AIProfile profile,
            BoardState board, string positionName)
        {
            // The weights are the whole subject, so they come from the profile exactly as the live
            // agent builds them rather than being written out here where they could drift.
            var evaluator = new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(profile));
            var search = new AlphaBetaSearch(engine, evaluator);
            var settings = new AISearchSettings(FixedDepth, TestTimeBudgetsForProbe, BetrayalUsage.Full);

            var stopwatch = Stopwatch.StartNew();
            search.FindBestMove(board, settings, CancellationToken.None);
            stopwatch.Stop();

            long nodes = search.Stats.NodesVisited;
            long qnodes = search.Stats.QNodesVisited;
            long total = nodes + qnodes;
            double nsPerNode = total > 0 ? stopwatch.Elapsed.TotalMilliseconds * 1_000_000.0 / total : 0;
            double firstMoveCutoff = search.Stats.BetaCutoffs > 0
                ? search.Stats.FirstMoveBetaCutoffs / (double)search.Stats.BetaCutoffs
                : 0;
            double ttHit = search.Stats.TTProbes > 0
                ? search.Stats.TTHits / (double)search.Stats.TTProbes
                : 0;

            TestContext.WriteLine(
                $"{positionName,-17} {label,-11} | {stopwatch.ElapsedMilliseconds,5} | {nodes,10:N0} | {qnodes,9:N0} "
                + $"| {nsPerNode,7:F0} | {firstMoveCutoff,15:P1} | {ttHit,6:P1}");
        }

        /// <summary>Both halves high enough that nothing here is ever clock-bound - the searches are
        /// depth-bound by construction and the budget must never become the thing being measured.</summary>
        private static readonly AITimeBudget TestTimeBudgetsForProbe = new AITimeBudget(600_000, 600_000);
    }
}
