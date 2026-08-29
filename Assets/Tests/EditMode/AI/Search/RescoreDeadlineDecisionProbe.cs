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

namespace ChessTheBetrayal.Tests.EditMode.AI.Search
{
    /// <summary>
    /// Decides whether the candidate rescore pass should be stopped at the soft budget, by measuring
    /// what that would cost across several positions rather than one.
    ///
    /// The question is a trade. Letting the pass run costs a player about two and a half seconds on
    /// every heavy turn at the deeper tiers, which is most of what they wait for. Stopping it saves
    /// that but settles fewer root moves, and the ones a tie-break chooses between are spread along
    /// the root list rather than gathered at its front — so the cost depends entirely on how many
    /// moves are genuinely in contention, which is a property of the position and not of the tier.
    ///
    /// On one quiet midgame the answer looked lopsided in both directions depending on how it was
    /// counted, which is exactly why this exists. Counting contenders from a bounded turn's own
    /// leftover scores overstates them badly: an unsettled entry carries an alpha-beta upper bound,
    /// and a bound never looks worse than the truth. Every window here is therefore measured against
    /// a pass allowed to finish, where every score is real.
    ///
    /// Scoped to the two tiers the deadline can actually change. Easy and normal finish their pass
    /// inside their budget already, and extreme spends its whole budget on the depth loop so the
    /// pass never starts — none of the three can move either way.
    ///
    /// Explicit and generously timed: this runs unbounded searches on purpose and is decision
    /// support, not a gate. Nothing here asserts a verdict; read the table.
    /// </summary>
    [TestFixture]
    [Explicit("Measurement only - runs unbounded searches to establish the true tie-break window.")]
    [Category(TestCategories.OnDemand)]
    public class RescoreDeadlineDecisionProbe
    {
        private const int TimeoutMs = 600_000;

        private static readonly string[] TierIds = { "hard", "aggressive" };

        private static IEnumerable<(string Name, Func<BoardState> Make)> Positions()
        {
            yield return ("quiet-midgame", DepthWallPositions.QuietMidgame);
            yield return ("semi-open-midgame", DepthWallPositions.SemiOpenMidgame);
            yield return ("opening", () => StandardChessPosition.Create(betrayalRightAvailable: true));
        }

        [Test]
        [Timeout(TimeoutMs)]
        public void WhatStoppingTheRescoreAtTheSoftBudgetWouldCost()
        {
            IChessEngine engine = new ChessEngineAdapter();
            var provider = new AIProfileTableProvider();

            TestContext.WriteLine(
                "position          tier        | full pass      | budgeted        | with deadline   | window kept");
            TestContext.WriteLine(
                "------------------------------|----------------|-----------------|-----------------|------------");

            foreach ((string positionName, Func<BoardState> make) in Positions())
            {
                foreach (string tierId in TierIds)
                {
                    AIProfile profile = provider.Resolve(tierId);
                    AISearchSettings settings = AISearchSettings.FromProfile(BetrayalUsage.Full, profile);
                    int margin = profile.RescoreMarginCp;

                    // The reference: no clock, no cancellation, so every candidate is settled and the
                    // window it reports is the real one.
                    Arm reference = Run(engine, profile, settings, make(), margin,
                        underBudget: false, deadlineMs: AlphaBetaSearch.NoRescoreDeadline);

                    Arm budgeted = Run(engine, profile, settings, make(), margin,
                        underBudget: true, deadlineMs: AlphaBetaSearch.NoRescoreDeadline);

                    Arm deadlined = Run(engine, profile, settings, make(), margin,
                        underBudget: true, deadlineMs: settings.TimeBudget.SoftMs);

                    int trueWindow = reference.InWindowTotal;
                    (int keptBudgeted, string ranksBudgeted) = CompareAgainst(reference, budgeted);
                    (int keptDeadlined, string ranksDeadlined) = CompareAgainst(reference, deadlined);

                    TestContext.WriteLine(
                        $"{positionName,-17} {tierId,-11} | {reference.ElapsedMs,6}ms w={trueWindow,-3} "
                        + $"| {budgeted.ElapsedMs,6}ms kept {keptBudgeted,-2} ranks [{ranksBudgeted}] "
                        + $"| {deadlined.ElapsedMs,6}ms kept {keptDeadlined,-2} ranks [{ranksDeadlined}]");
                }
            }

            TestContext.WriteLine("");
            TestContext.WriteLine(
                "'kept' counts how many of the real window's moves a run settled, so the selection "
                + "could actually choose between them. A window of one was never a choice.");
        }

        /// <summary>
        /// How many of the reference run's window the arm settled, and how far down that window it
        /// had to reach to get them.
        ///
        /// Matched on the moves themselves rather than on positions in the list. The two runs sort
        /// their candidates by their own scores, and a re-search's score is not perfectly stable
        /// under a different visiting order once forward pruning is in play, so the same index does
        /// not reliably mean the same move in both — comparing by index was quietly comparing
        /// different moves.
        ///
        /// The rank figure is what says whether meeting candidates in score order helped. Keeping
        /// two moves out of nine says nothing on its own; keeping the window's top two rather than
        /// its fifth and eighth is the whole point, and only a rank can tell those apart.
        /// </summary>
        private static (int Kept, string Ranks) CompareAgainst(Arm reference, Arm arm)
        {
            int kept = 0;
            var ranks = new List<int>();

            for (int rank = 0; rank < reference.WindowByScore.Count; rank++)
            {
                if (arm.SettledMoves.Contains(reference.WindowByScore[rank]))
                {
                    kept++;
                    ranks.Add(rank);
                }
            }

            return (kept, ranks.Count == 0 ? "-" : string.Join(",", ranks));
        }

        private readonly struct Arm
        {
            public readonly long ElapsedMs;
            public readonly int Settled;
            public readonly int InWindowTotal;

            /// <summary>The window's moves, best true score first. Only meaningful on a reference run,
            /// where every score is real.</summary>
            public readonly List<uint> WindowByScore;

            /// <summary>Every move this run settled, by identity, so an arm can be asked whether it
            /// settled a particular move without relying on where that move sat in its list.</summary>
            public readonly HashSet<uint> SettledMoves;

            public Arm(long elapsedMs, int settled, List<uint> windowByScore, HashSet<uint> settledMoves)
            {
                ElapsedMs = elapsedMs;
                Settled = settled;
                WindowByScore = windowByScore;
                SettledMoves = settledMoves;
                InWindowTotal = windowByScore.Count;
            }
        }

        private static Arm Run(IChessEngine engine, AIProfile profile, AISearchSettings settings,
            BoardState board, int margin, bool underBudget, long deadlineMs)
        {
            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(profile)));

            using var cts = new CancellationTokenSource();
            if (underBudget) cts.CancelAfter(settings.TimeBudget.HardMs);

            var stopwatch = Stopwatch.StartNew();
            search.FindBestMove(board, settings,
                underBudget ? cts.Token : CancellationToken.None,
                margin,
                enableInstabilityTimeManagement: underBudget,
                enableAspirationWindows: false,
                rescoreDeadlineMs: deadlineMs);
            stopwatch.Stop();

            var settledMoves = new HashSet<uint>();
            for (int i = 0; i < search.RootScoresExactCount && i < search.RootMoveCount; i++)
            {
                settledMoves.Add(PackedMove.Pack(search.RootMoves[i]));
            }

            var window = new List<(uint Key, int Score)>();
            if (search.RootMoveCount > 0)
            {
                int threshold = search.RootScores[search.BestRootIndex] - profile.TieBreakWindowCp;
                for (int i = 0; i < search.RootMoveCount; i++)
                {
                    if (search.RootScores[i] >= threshold)
                        window.Add((PackedMove.Pack(search.RootMoves[i]), search.RootScores[i]));
                }
            }

            window.Sort((a, b) => b.Score.CompareTo(a.Score));
            var windowByScore = new List<uint>(window.Count);
            for (int i = 0; i < window.Count; i++) windowByScore.Add(window[i].Key);

            return new Arm(stopwatch.ElapsedMilliseconds, search.RootScoresExactCount, windowByScore, settledMoves);
        }
    }
}
