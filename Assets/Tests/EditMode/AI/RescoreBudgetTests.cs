using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.AI.Positions;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// What a turn costs a player once the candidate rescore pass is counted, and what the tier's
    /// personality gets for it.
    ///
    /// SearchTimeBudgetTests bounds the depth loop but cannot bound this: it sets the soft budget
    /// equal to the hard one, passes no rescore margin and leaves instability time management off,
    /// so the configuration it guards is not the one any tier ships with. A search can finish its
    /// depth loop in half a second, spend two and a half more on the pass that follows, and still
    /// satisfy it.
    ///
    /// Cutting that pass short on a wall clock was tried and dropped. Stopping it at the soft budget
    /// took a heavy turn from about three seconds to one, but the pass stops part-way down the root
    /// list and the moves a tie-break chooses between are spread along that list rather than
    /// gathered at the front — on a quiet midgame it took the aggressive tier from four settled
    /// moves inside its window to one, and a window reaching the selection with one move in it is a
    /// tier with no personality at all. The wall-clock figures here are therefore reported and not
    /// asserted on: the cost is real and worth watching, but no bound measured so far buys it back
    /// without costing more than it saves.
    /// </summary>
    [TestFixture]
    public class RescoreBudgetTests
    {
        /// <summary>One turn's worth of measurements, gathered together so the helper below keeps a
        /// single job instead of growing another out-parameter each time a question is added.</summary>
        private readonly struct TurnMeasurement
        {
            public readonly long ElapsedMs;
            public readonly int Settled;
            public readonly int RootMoves;
            public readonly int InWindowSettled;
            public readonly int InWindowTotal;

            public TurnMeasurement(long elapsedMs, int settled, int rootMoves, int inWindowSettled, int inWindowTotal)
            {
                ElapsedMs = elapsedMs;
                Settled = settled;
                RootMoves = rootMoves;
                InWindowSettled = inWindowSettled;
                InWindowTotal = inWindowTotal;
            }

            public bool PassWasCutShort => Settled < RootMoves;
        }

        private static int RescoreMarginFor(AIProfile profile) =>
            Math.Max(profile.BlunderMarginCp, profile.TieBreakWindowCp);

        [Test]
        public void EveryTierWithDials_ChoosesFromItsWholeTieBreakWindow()
        {
            IChessEngine engine = new ChessEngineAdapter();
            int tiersChecked = 0;

            // Every tier is measured before anything is asserted. Failing on the first bad one hides
            // the rest, and which tiers are affected is the whole point of measuring all of them.
            var problems = new List<string>();

            foreach (AIProfile profile in AIProfileTable.BuiltIn)
            {
                int margin = RescoreMarginFor(profile);

                // A tier with no dials never runs the pass, so it has nothing to be judged on here.
                if (margin <= 0) continue;

                AISearchSettings settings = AISearchSettings.FromProfile(BetrayalUsage.Full, profile);

                TurnMeasurement loopOnly = MeasureTurn(engine, profile, settings, 0, underItsBudget: true);
                TurnMeasurement turn = MeasureTurn(engine, profile, settings, margin, underItsBudget: true);

                TestContext.WriteLine(
                    $"{profile.Id,-11} soft={settings.TimeBudget.SoftMs,4}ms hard={settings.TimeBudget.HardMs,4}ms "
                    + $"margin={margin,3}cp | depth loop {loopOnly.ElapsedMs,4}ms | whole turn {turn.ElapsedMs,4}ms "
                    + $"| pass costs {turn.ElapsedMs - loopOnly.ElapsedMs,4}ms "
                    + $"| settled {turn.Settled,3}/{turn.RootMoves}");

                tiersChecked++;

                // A tier whose depth loop leaves nothing behind never starts the pass, and there is
                // then no window for this rule to speak to. Decided on whether the pass actually
                // settled anything rather than on the loop overrunning the soft budget: those agreed
                // only while a loop that overran soft went on to eat the whole hard budget too. Once
                // one tier's ceiling was lowered enough to finish inside its budget, its loop still
                // ran past soft while leaving well over a second for a pass that settled nine moves
                // - and the old wording announced that pass had never started.
                if (turn.Settled <= 1)
                {
                    TestContext.WriteLine(
                        $"    {profile.Id}: the pass settled nothing beyond the best move, so there is no "
                        + "window here to judge - its depth loop leaves the pass no room.");
                    continue;
                }

                // The window has to be measured against a pass that ran to the end. An unsettled
                // entry carries an alpha-beta upper bound, and a bound only ever overstates, so a
                // turn that was cut short reports a window larger than the one really there. Only
                // the run where every score is real can say how many moves the dials had to choose
                // between, and so whether the budgeted turn lost any of them.
                int trueWindow = turn.InWindowTotal;
                if (turn.PassWasCutShort)
                {
                    TurnMeasurement reference = MeasureTurn(engine, profile, settings, margin, underItsBudget: false);
                    trueWindow = reference.InWindowTotal;
                    TestContext.WriteLine(
                        $"    {profile.Id}: against a full pass ({reference.ElapsedMs}ms, settled "
                        + $"{reference.Settled}/{reference.RootMoves}) the window is {trueWindow} move(s); "
                        + $"the budgeted turn settled {turn.InWindowSettled} of them.");
                }

                // A window genuinely holding one move is a fact about the position, not a fault —
                // the tier correctly plays its best move and there was never a choice to make.
                if (trueWindow > 1 && turn.InWindowSettled < trueWindow)
                {
                    problems.Add(
                        $"{profile.Id}: {turn.InWindowSettled} of a real {trueWindow}-move tie-break window "
                        + "came back settled, so the tier chooses from part of its window rather than all "
                        + "of it. A window that reaches the selection holding one move leaves a tie-break "
                        + "with nothing to choose between and a blunder roll with nothing to land on.");
                }
            }

            Assert.That(tiersChecked, Is.GreaterThan(0),
                "No shipping tier carries a rescore margin, so this suite is guarding nothing.");

            Assert.That(problems, Is.Empty,
                "Tie-break window truncated on " + problems.Count + " tier(s):"
                + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// One turn. With <paramref name="underItsBudget"/> set it is played exactly as the live
        /// agent plays one — the tier's own budget on the token and instability time management on.
        /// Cleared, it runs with no clock and no cancellation at all, which is the only way to see
        /// every candidate settled and so the only way to learn the real size of the window.
        /// </summary>
        private static TurnMeasurement MeasureTurn(IChessEngine engine, AIProfile profile,
            AISearchSettings settings, int candidateRescoreMarginCp, bool underItsBudget)
        {
            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(profile)));
            BoardState board = DepthWallPositions.QuietMidgame();

            using var cts = new CancellationTokenSource();
            if (underItsBudget) cts.CancelAfter(settings.TimeBudget.HardMs);

            var stopwatch = Stopwatch.StartNew();
            search.FindBestMove(board, settings,
                underItsBudget ? cts.Token : CancellationToken.None,
                candidateRescoreMarginCp,
                enableInstabilityTimeManagement: underItsBudget);
            stopwatch.Stop();

            int settled = search.RootScoresExactCount;
            int inWindowSettled = 0;
            int inWindowTotal = 0;

            if (search.RootMoveCount > 0)
            {
                int threshold = search.RootScores[search.BestRootIndex] - profile.TieBreakWindowCp;
                for (int i = 0; i < search.RootMoveCount; i++)
                {
                    if (search.RootScores[i] < threshold) continue;
                    inWindowTotal++;
                    if (i < settled) inWindowSettled++;
                }
            }

            return new TurnMeasurement(stopwatch.ElapsedMilliseconds, settled, search.RootMoveCount,
                inWindowSettled, inWindowTotal);
        }
    }
}
