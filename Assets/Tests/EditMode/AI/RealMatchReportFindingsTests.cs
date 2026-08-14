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
    /// Reproductions for three behaviours a real human-vs-AI match surfaced that no existing suite
    /// covers. Each one pins what the code does TODAY, so that whichever of them we decide to
    /// change has a test that flips when it changes rather than a claim in a discussion.
    ///
    /// These are deliberately written against the production entry points a real match uses —
    /// IChessEngine.Advance for the rules, AlphaBetaSearch.FindBestMove with the aggressive tier's
    /// own budget and margins for the search — because all three only appear when those pieces run
    /// together with the numbers a player actually gets.
    /// </summary>
    [TestFixture]
    public class RealMatchReportFindingsTests
    {
        private static readonly Vector2Int F3 = new Vector2Int(5, 2);
        private static readonly Vector2Int D2 = new Vector2Int(3, 1);
        private static readonly Vector2Int F6 = new Vector2Int(5, 5);
        private static readonly Vector2Int D7 = new Vector2Int(3, 6);

        private static AIProfile Aggressive() => new AIProfileTableProvider().Resolve("aggressive");

        /// <summary>Plays the one legal move that runs between these squares, through the same
        /// Advance the match driver uses. Throws rather than skipping if the move isn't legal, so a
        /// board edit that quietly breaks the shuffle fails loudly instead of passing vacuously.</summary>
        private static void PlayMoveBetween(IChessEngine engine, BoardState board, Vector2Int from, Vector2Int to)
        {
            var legal = new List<MoveCommand>();
            engine.GetLegalMoves(board, from, legal);

            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].EndPosition == to)
                {
                    engine.Advance(board, legal[i]);
                    return;
                }
            }

            Assert.Fail($"No legal move from {from} to {to} — the shuffle position has changed.");
        }

        /// <summary>
        /// Both sides walk a knight out and back twice, returning to the identical starting
        /// position three times over. Chess would call that a draw; this game plays on, because
        /// nothing in the live rules counts repeated positions — the only threefold detection in the
        /// codebase belongs to the tournament harness.
        ///
        /// The hash assertion is the load-bearing half. It shows the position genuinely recurs and
        /// that ZobristHash already distinguishes everything a repetition claim would have to care
        /// about, so what's missing is the rule, not the means to detect it.
        /// </summary>
        [Test]
        public void APositionRepeatedThreeTimes_IsStillANormalGame()
        {
            IChessEngine engine = new ChessEngineAdapter();
            BoardState board = DepthWallPositions.QuietMidgame();

            ulong startingPosition = board.ZobristHash;
            var occurrences = new Dictionary<ulong, int> { [startingPosition] = 1 };

            for (int cycle = 0; cycle < 2; cycle++)
            {
                PlayMoveBetween(engine, board, F3, D2);
                PlayMoveBetween(engine, board, F6, D7);
                PlayMoveBetween(engine, board, D2, F3);
                PlayMoveBetween(engine, board, D7, F6);

                occurrences.TryGetValue(board.ZobristHash, out int seen);
                occurrences[board.ZobristHash] = seen + 1;
            }

            Assert.That(board.ZobristHash, Is.EqualTo(startingPosition),
                "The shuffle must land back on the same position, or this proves nothing.");
            Assert.That(occurrences[startingPosition], Is.EqualTo(3),
                "The starting position should have occurred three times.");

            GameState state = engine.EvaluateGameState(board, board.CurrentTurn, null);

            Assert.That(state, Is.EqualTo(GameState.Normal),
                "Documents today's behaviour: three occurrences of one position is not a draw, and "
                + "nothing stops the two sides repeating it forever.");
        }

        /// <summary>
        /// A turn played the way the live agent plays one, on a position heavy enough that the pass
        /// cannot finish inside the tier's soft budget. What must come back is a partial settlement:
        /// more than the best move alone, fewer than all of them, and an honest report that the
        /// whole pass did not run.
        ///
        /// All three matter together. Reporting the whole pass as complete would let a selection
        /// read alpha-beta bounds as though they were scores; reporting nothing settled would cost
        /// the tier its dials on every heavy position, which is what a real match caught it doing.
        ///
        /// Measured on "hard" rather than "aggressive", even though a real match surfaced this on
        /// aggressive. Aggressive sat within measurement noise of its own budget on this position,
        /// so a test keyed to it reported whichever way the machine happened to fall.
        /// </summary>
        [Test]
        public void ATurnTooHeavyToFinishItsPass_SettlesSomeCandidatesButNotAll()
        {
            IChessEngine engine = new ChessEngineAdapter();
            AIProfile profile = new AIProfileTableProvider().Resolve("hard");
            AISearchSettings settings = AISearchSettings.FromProfile(BetrayalUsage.Full, profile);
            int rescoreMargin = Math.Max(profile.BlunderMarginCp, profile.TieBreakWindowCp);

            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(profile)));
            BoardState board = DepthWallPositions.QuietMidgame();

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(settings.TimeBudget.HardMs);

            var stopwatch = Stopwatch.StartNew();
            search.FindBestMove(board, settings, cts.Token, rescoreMargin, enableInstabilityTimeManagement: true);
            stopwatch.Stop();

            TestContext.WriteLine($"tier={profile.Id} soft={settings.TimeBudget.SoftMs}ms hard={settings.TimeBudget.HardMs}ms "
                + $"margin={rescoreMargin}cp -> elapsed={stopwatch.ElapsedMilliseconds}ms "
                + $"depth={search.LastCompletedDepth} stop={search.StopReason} "
                + $"settled={search.RootScoresExactCount}/{search.RootMoveCount}");

            Assume.That(search.RootScoresExactCount, Is.LessThan(search.RootMoveCount),
                "This position no longer outlasts the tier's pass, so it cannot demonstrate what a "
                + "partial settlement looks like. Pick a heavier position.");

            Assert.That(search.RootScoresExactForSelection, Is.False,
                "A pass that did not reach every root move must not report itself complete.");

            Assert.That(search.RootScoresExactCount, Is.GreaterThan(1),
                "Stopping the pass early must not cost the tier its dials outright: the candidates it "
                + "did settle are still honestly comparable, and choosing among those is the "
                + "difference between a tier playing with a personality and playing without one.");
        }

        /// <summary>
        /// The other half of the test above: with no clock running at all, the pass has nothing to
        /// cut it short and settles every root move. Without this, a partial reading there could
        /// just as well mean the pass never completes under any conditions, and the pair together is
        /// what pins time as the thing that decides how much of it runs.
        ///
        /// This is also what holds the depth-bound guarantee in place. A caller that asks for no
        /// time management has no deadline applied to the pass either, so the benchmark suites stay
        /// a measurement of work done rather than of who ran out of time first — if that ever stops
        /// being true, this test is where it shows up.
        /// </summary>
        [Test]
        public void ASearchWithNoClockRunning_SettlesEveryCandidate()
        {
            IChessEngine engine = new ChessEngineAdapter();
            AIProfile profile = new AIProfileTableProvider().Resolve("hard");
            AISearchSettings settings = AISearchSettings.FromProfile(BetrayalUsage.Full, profile);
            int rescoreMargin = Math.Max(profile.BlunderMarginCp, profile.TieBreakWindowCp);

            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(profile)));
            BoardState board = DepthWallPositions.QuietMidgame();

            var stopwatch = Stopwatch.StartNew();
            search.FindBestMove(board, settings, CancellationToken.None, rescoreMargin);
            stopwatch.Stop();

            TestContext.WriteLine($"no clock: elapsed={stopwatch.ElapsedMilliseconds}ms depth={search.LastCompletedDepth} "
                + $"stop={search.StopReason} settled={search.RootScoresExactCount}/{search.RootMoveCount} "
                + $"wholePassRan={search.RootScoresExactForSelection}");

            Assert.That(search.RootScoresExactForSelection, Is.True,
                "With no clock running there is no deadline and no cancellation, so the pass has "
                + "nothing to cut it short and every root move should come back settled.");
        }

        /// <summary>
        /// What the rescore pass costs, measured against the same position and depth without it.
        /// Both runs are depth-bound rather than clock-bound so the comparison is of work done, not
        /// of two searches that each stopped when the same timer fired.
        ///
        /// This is the mechanism behind a report full of moves sitting on the time budget: the depth
        /// loop can finish early and the pass that follows it still spends whatever is left.
        /// </summary>
        [Test]
        public void TheCandidateRescorePass_CostsRealTimeOnTopOfTheDepthLoop()
        {
            IChessEngine engine = new ChessEngineAdapter();
            AIProfile profile = Aggressive();
            AISearchSettings settings = AISearchSettings.FromProfile(BetrayalUsage.Full, profile);
            int rescoreMargin = Math.Max(profile.BlunderMarginCp, profile.TieBreakWindowCp);

            long withoutRescoreMs = TimeDepthBoundSearch(engine, profile, settings, candidateRescoreMarginCp: 0);
            long withRescoreMs = TimeDepthBoundSearch(engine, profile, settings, rescoreMargin);

            TestContext.WriteLine($"depth {settings.MaxDepth}, no cancellation: "
                + $"margin=0 -> {withoutRescoreMs}ms, margin={rescoreMargin}cp -> {withRescoreMs}ms "
                + $"(rescore adds {withRescoreMs - withoutRescoreMs}ms)");

            Assert.That(withRescoreMs, Is.GreaterThan(withoutRescoreMs),
                "The rescore pass re-searches every near-best root move with a full window, so it "
                + "cannot be free — if this ever stops being true the pass has stopped running.");
        }

        /// <summary>
        /// Every tier, under its own configured budget, with and without its own rescore margin.
        /// The question this answers is which tiers can actually afford the pass their personality
        /// dials depend on — a tier whose budget runs out first pays for the pass and then throws
        /// the result away, and plays its plain best move as though it had no personality at all.
        ///
        /// Capped at each tier's real budget rather than run to completion, both because that is
        /// what a player gets and because an uncapped depth-9 search on this position has no bound
        /// worth putting in a routine test run.
        /// </summary>
        [Test]
        public void EveryTier_ReportsWhetherItsOwnBudgetCoversItsRescorePass()
        {
            IChessEngine engine = new ChessEngineAdapter();
            int tiersMeasured = 0;

            foreach (AIProfile profile in AIProfileTable.BuiltIn)
            {
                AISearchSettings settings = AISearchSettings.FromProfile(BetrayalUsage.Full, profile);
                int margin = Math.Max(profile.BlunderMarginCp, profile.TieBreakWindowCp);

                long plainMs = TimeBudgetedSearch(engine, profile, settings, 0, out _, out _, out int plainDepth);
                long rescoredMs = TimeBudgetedSearch(engine, profile, settings, margin,
                    out bool wholePassRan, out int settled, out int rescoredDepth);

                // A tier with no dials at all (margin 0) is reported as not selecting because it
                // never asks to — that is the configured answer, not a pass it failed to afford.
                string personality = margin == 0 ? "n/a (no dials)" : (settled > 1).ToString();

                TestContext.WriteLine(
                    $"{profile.Id,-11} maxDepth={settings.MaxDepth} budget={settings.TimeBudget.HardMs,4}ms margin={margin,3}cp | "
                    + $"no rescore {plainMs,4}ms d{plainDepth} | with rescore {rescoredMs,4}ms d{rescoredDepth} | "
                    + $"settled {settled,3} (whole pass: {wholePassRan}) | selects: {personality}");

                // Deliberately no timing comparison between the two columns. Both runs stop at the
                // same budget, so once a tier is heavy enough to reach it they both report roughly
                // that budget and the difference is measurement noise — an earlier version of this
                // asserted the rescore could only add time and failed on 3001ms against 3014ms.
                // Where the pass actually costs something the "no rescore" column shows it directly.
                Assert.That(rescoredDepth, Is.GreaterThan(0),
                    $"{profile.Id}: a tier that completes no depth at all measures nothing here.");
                tiersMeasured++;
            }

            Assert.That(tiersMeasured, Is.EqualTo(AIProfileTable.BuiltIn.Count),
                "Every shipped tier has to appear in the table, including any added later.");
        }

        private static long TimeBudgetedSearch(IChessEngine engine, AIProfile profile,
            AISearchSettings settings, int candidateRescoreMarginCp, out bool wholePassRan,
            out int settledCount, out int completedDepth)
        {
            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(profile)));
            BoardState board = DepthWallPositions.QuietMidgame();

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(settings.TimeBudget.HardMs);

            var stopwatch = Stopwatch.StartNew();
            search.FindBestMove(board, settings, cts.Token, candidateRescoreMarginCp,
                enableInstabilityTimeManagement: true);
            stopwatch.Stop();

            wholePassRan = search.RootScoresExactForSelection;
            settledCount = search.RootScoresExactCount;
            completedDepth = search.LastCompletedDepth;
            return stopwatch.ElapsedMilliseconds;
        }

        private static long TimeDepthBoundSearch(IChessEngine engine, AIProfile profile,
            AISearchSettings settings, int candidateRescoreMarginCp)
        {
            // A fresh search (and so a fresh transposition table) each time, or the second run reads
            // the first one's conclusions and measures nothing.
            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(profile)));
            BoardState board = DepthWallPositions.QuietMidgame();

            var stopwatch = Stopwatch.StartNew();
            search.FindBestMove(board, settings, CancellationToken.None, candidateRescoreMarginCp);
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }
    }
}
