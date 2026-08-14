using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins why iterative deepening stopped, not just where. A search that reaches depth 7 because
    /// it ran out of budget and one that reaches depth 7 because it decided the position was settled
    /// look identical in LastCompletedDepth alone — telling those apart is what a depth-ceiling
    /// decision actually needs, since raising MaxDepth only helps the search that was budget-bound in
    /// the first place.
    /// </summary>
    [TestFixture]
    public class SearchStopReasonTests
    {
        private ChessEngineAdapter _engine;
        private AlphaBetaSearch _search;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _search = NewSearch();
        }

        /// <summary>A search with its own empty table, for the cases that run several searches and
        /// must not let one warm the next into stopping for a different reason.</summary>
        private AlphaBetaSearch NewSearch() =>
            new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));

        /// <summary>A quiet, materially balanced, fully-developed position with no immediate
        /// tactics — the best move is obvious from a shallow depth on and stays obvious as the
        /// search goes deeper, so a generous soft/hard gap lets the settle-early logic fire well
        /// before either budget edge.</summary>
        private static BoardState QuietPosition() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("f1", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook)
                .WithPiece("c3", Team.White, ChessPieceType.Bishop)
                .WithPiece("f3", Team.White, ChessPieceType.Knight)
                .WithPiece("c6", Team.Black, ChessPieceType.Bishop)
                .WithPiece("f6", Team.Black, ChessPieceType.Knight)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g3", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("c7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g6", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        [Test]
        public void FindBestMove_GenerousBudgetShallowCeiling_ReportsCeiling()
        {
            // A budget with no real chance of being hit, at a shallow enough MaxDepth that the loop
            // exhausts every depth 1..MaxDepth on its own. Nothing else in the loop can have fired.
            BoardState board = QuietPosition();
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(_search.Stats.StopReason, Is.EqualTo(SearchStopReason.Ceiling));
        }

        [Test]
        public void FindBestMove_ExternalCancellationFiresMidSearch_ReportsBudget()
        {
            // A hard budget so tight the external token fires before the first depth even completes —
            // the plain CancellationToken.None path (no instability management) still must record
            // Budget, since a live match always races this same external CancelAfter timer.
            BoardState board = QuietPosition();
            var settings = new AISearchSettings(maxDepth: 9, new AITimeBudget(5, 5), BetrayalUsage.Full);

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(5);
                _search.FindBestMove(board, settings, cts.Token);
            }

            Assert.That(_search.Stats.StopReason, Is.EqualTo(SearchStopReason.Budget));
        }

        /// <summary>Black King boxed in on g8 by its own pawns; White Rook on a1 delivers Ra1-a8#
        /// as a genuine forced mate.</summary>
        private static BoardState BackRankMateInOne() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

        [Test]
        public void FindBestMove_ForcedMateFound_ReportsMateFoundAndStopsBeforeMaxDepth()
        {
            // Ra1-a8# is a real mate the search finds from depth 2 on. Once found, no deeper search
            // can change the decision, so the deepening loop should stop right there instead of
            // burning through every remaining configured depth for nothing — a genuine fix, not the
            // pre-fix behavior this same fixture used to pin (see the mate-early-exit finding this
            // ticket recorded: the root check compared against the exact MateScore constant, but a
            // mate's score only ever gets close to it, never equal, at any real search depth).
            BoardState board = BackRankMateInOne();
            var settings = new AISearchSettings(maxDepth: 9, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(_search.Stats.StopReason, Is.EqualTo(SearchStopReason.MateFound));
            Assert.That(_search.Stats.LastCompletedDepth, Is.LessThan(9),
                "A mate found well short of MaxDepth 9 should stop the search there, not run every depth.");
        }

        [Test]
        public void FindBestMove_ForcedMateFound_StopsAtTheSameDepthRegardlessOfHowMuchDeeperMaxDepthIs()
        {
            // The search should stop at whatever depth it FIRST finds the mate, independent of how
            // much further MaxDepth would have allowed it to go — proof the exit is actually firing
            // on discovery, not coincidentally landing on the same depth for an unrelated reason.
            BoardState shallowCeiling = BackRankMateInOne();
            var shallowSettings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            _search.FindBestMove(shallowCeiling, shallowSettings, CancellationToken.None);
            int depthWithShallowCeiling = _search.Stats.LastCompletedDepth;

            var deepSearch = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));
            BoardState deepCeiling = BackRankMateInOne();
            var deepSettings = new AISearchSettings(maxDepth: 9, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            deepSearch.FindBestMove(deepCeiling, deepSettings, CancellationToken.None);
            int depthWithDeepCeiling = deepSearch.Stats.LastCompletedDepth;

            Assert.That(depthWithDeepCeiling, Is.EqualTo(depthWithShallowCeiling),
                "The mate should be found and stopped on at the same depth regardless of how much " +
                "further MaxDepth would allow — a MaxDepth-independent result is what proves this is " +
                "a genuine mate-found exit, not just two searches happening to agree.");
        }

        [Test]
        public void FindBestMove_SideAboutToBeMated_NeverEarlyExitsOnItsOwnLoss()
        {
            // The mate-found exit is one-sided: it must only fire when THIS side (the search's own
            // root perspective) has found a WINNING mate, never when this side is the one about to
            // be mated. The exact BackRankMateInOne position, but with BLACK to move instead of
            // White — Black has no way to stop Ra1-a8# next move, so Black's own search (root
            // perspective = Black, the losing side) must score this near -MateScore for itself and
            // must never report MateFound for what is actually its own forthcoming loss.
            BoardState board = BackRankMateInOne().WithTurn(Team.Black).WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(_search.Stats.StopReason, Is.Not.EqualTo(SearchStopReason.MateFound),
                "A position where this side is about to be mated must never report MateFound — that " +
                "would mean the one-sided check fired on the losing side's score instead of the " +
                "winning one's.");
        }

        [Test]
        public void FindBestMove_BetrayalLiveForcedMate_StillReportsMateFound()
        {
            // Same mate as above, but with the Betrayal right live so PlayForcedSaveMoves' own,
            // DIFFERENT mate-scoring convention (a raw +/-MateScore rather than the depth-adjusted
            // one Search itself uses) is reachable too — the fix must cover both conventions, not
            // just the common one.
            BoardState board = BackRankMateInOne().WithBetrayalRight(true).WithComputedHash();
            var settings = new AISearchSettings(maxDepth: 9, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(_search.Stats.StopReason, Is.EqualTo(SearchStopReason.MateFound));
        }

        [Test]
        public void FindBestMove_SettledPositionWithInstabilityManagement_ReportsSettledEarly()
        {
            // A quiet position whose best move stabilizes quickly, searched with instability
            // management on and a soft budget the search will comfortably clear before its
            // generous hard ceiling ever comes into play — the settle-early path, not the clock,
            // must be what stops this search.
            BoardState board = QuietPosition();
            var settings = new AISearchSettings(maxDepth: 9, new AITimeBudget(50, 10_000), BetrayalUsage.Full);

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(10_000);
                _search.FindBestMove(board, settings, cts.Token, enableInstabilityTimeManagement: true);
            }

            Assert.That(_search.Stats.StopReason, Is.EqualTo(SearchStopReason.SettledEarly));
        }

        [Test]
        public void FindBestMove_UnsettledPositionUnderInstabilityManagement_HitsHardBudgetAndReportsBudget()
        {
            // The same quiet position, but with the hard budget set so tight relative to the soft
            // budget that the search cannot possibly have settled by the time it runs out — proves
            // the internal instability-management hard-budget exit is also labeled Budget, the same
            // as the external cancellation case, since both mean "time ran out."
            BoardState board = QuietPosition();
            var settings = new AISearchSettings(maxDepth: 9, new AITimeBudget(1, 3), BetrayalUsage.Full);

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(3);
                _search.FindBestMove(board, settings, cts.Token, enableInstabilityTimeManagement: true);
            }

            Assert.That(_search.Stats.StopReason, Is.EqualTo(SearchStopReason.Budget));
        }

        [Test]
        public void ASearchStoppedShortOfItsCeiling_ReportsBudgetNotCeiling()
        {
            // Reporting a ceiling stop for a search the clock cut short would claim it finished what
            // it was asked to do, which points a depth-ceiling decision in exactly the wrong
            // direction: only a search that ran out of time stands to gain from a deeper ceiling.
            // The hardest version of that to get right is cancellation landing while the LAST
            // configured depth is still in flight, because that depth is abandoned without
            // committing and the loop then ends on its own counter rather than through any of its
            // exits — leaving the reason to be worked out afterwards from how far it actually got.
            //
            // The budget is MEASURED here rather than written down. An earlier version of this test
            // hard-coded one and guarded the case with an assumption, and the search has since become
            // fast enough to finish every configured depth inside that budget — so the case stopped
            // occurring, the assumption stopped holding, and the test went from proving something to
            // reporting inconclusive, which no failure count notices. Timing the position first puts
            // the interruption in the right place however fast the machine or the search becomes.
            //
            // The interruption has to land inside the FINAL depth specifically. Cancelled any earlier
            // and the loop comes back around to its own cancellation check, which labels the stop
            // itself and never consults the after-the-loop reasoning this test is for — so a budget
            // picked as a rough fraction of the whole search proves nothing here, however short it is.
            // The window to aim at is therefore between the time the second-to-last depth finishes and
            // the time the last one would have, both of which the search records as it goes.
            //
            // Time management is on, as a real match has it, but the generous soft/hard pair keeps its
            // two exits out of reach — both need sixty seconds on the clock. So the only things that
            // can stop this search are the token and the ceiling.
            const int maxDepth = 9;
            var settings = new AISearchSettings(maxDepth, TestTimeBudgets.Generous, BetrayalUsage.Full);

            // Warmed before anything is timed, so the measurement reflects the search running at
            // speed rather than paying first-call costs it will not pay again.
            NewSearch().FindBestMove(QuietPosition(),
                new AISearchSettings(maxDepth: 4, TestTimeBudgets.Generous, BetrayalUsage.Full),
                CancellationToken.None, enableInstabilityTimeManagement: true);

            AlphaBetaSearch control = NewSearch();
            control.FindBestMove(QuietPosition(), settings, CancellationToken.None,
                enableInstabilityTimeManagement: true);

            // Half the pin, and the half that gives the other half its meaning: given all the time it
            // wants, this search does reach its ceiling. Without it, an interrupted run that stopped
            // short for some entirely unrelated reason would read as a pass.
            Assert.That(control.LastCompletedDepth, Is.EqualTo(maxDepth),
                "The control run has to reach the ceiling for the interrupted run below to be " +
                "measuring an interruption rather than a position that never gets there anyway.");
            Assert.That(control.StopReason, Is.EqualTo(SearchStopReason.Ceiling));

            long throughPenultimate = control.Stats.ElapsedMsAfterDepth(maxDepth - 1);
            long throughFinal = control.Stats.ElapsedMsAfterDepth(maxDepth);
            TestContext.Out.WriteLine(
                $"depth {maxDepth - 1} completed at {throughPenultimate}ms, depth {maxDepth} at {throughFinal}ms");

            // The last depth on a quiet position is where the tree stops being cheap, so this gap is
            // normally the widest one in the whole curve — which is what leaves room to aim into it.
            // If it ever closes, the aim below becomes a coin flip and the test says so here rather
            // than failing intermittently later.
            Assert.That(throughFinal - throughPenultimate, Is.GreaterThan(8L),
                $"The final depth only costs {throughFinal - throughPenultimate}ms on this position, " +
                "which is too narrow a window to reliably interrupt. This test needs a position where " +
                "the last depth is expensive.");

            int budgetMs = (int)((throughPenultimate + throughFinal) / 2);

            AlphaBetaSearch interrupted = NewSearch();
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(budgetMs);
                interrupted.FindBestMove(QuietPosition(), settings, cts.Token,
                    enableInstabilityTimeManagement: true);
            }

            // Exactly one short of the ceiling is what proves the interruption landed in the final
            // depth: the depth before it committed, the final one did not, and the loop then ran out
            // of depths rather than noticing the cancellation itself. Anything shallower means the
            // budget landed too early and the loop's own check did the labelling, which some other
            // test in this fixture already covers.
            Assert.That(interrupted.LastCompletedDepth, Is.EqualTo(maxDepth - 1),
                $"A {budgetMs}ms budget was aimed between {throughPenultimate}ms and {throughFinal}ms " +
                "so the search would be cut off during its last depth. Landing anywhere else means " +
                "this run is not exercising the case, whatever stop reason it reports.");
            Assert.That(interrupted.StopReason, Is.EqualTo(SearchStopReason.Budget),
                "A search that stopped short of its configured depth was stopped by the clock, not " +
                "by reaching its ceiling.");
        }

        /// <summary>
        /// The search reports the depth it reached and why it stopped on every build it can be
        /// compiled into; the telemetry struct keeps a copy of both, but only for the editor and
        /// development builds the measurement suites run against. This walks every exit the
        /// deepening loop has and pins that the two never disagree — a shipped build and a measured
        /// build have to be describing the same search, or a timing taken on a device says nothing
        /// about what a player actually runs.
        ///
        /// Each case asserts the stop reason it was built to produce before comparing the copies,
        /// because two values that are both still unset would agree with each other perfectly while
        /// proving nothing at all.
        /// </summary>
        [Test]
        public void EveryStopReason_IsReportedIdenticallyByTheSearchAndItsTelemetryCopy()
        {
            void AssertBothCopiesAgree(string exit, SearchStopReason expected, AlphaBetaSearch search)
            {
                Assert.That(search.StopReason, Is.EqualTo(expected),
                    $"The {exit} case no longer produces the stop reason it was written to cover, so " +
                    "it is not exercising the exit it claims to.");
                Assert.That(search.Stats.StopReason, Is.EqualTo(search.StopReason),
                    $"The telemetry copy of the stop reason disagrees with the search's own after the {exit} exit.");
                Assert.That(search.Stats.LastCompletedDepth, Is.EqualTo(search.LastCompletedDepth),
                    $"The telemetry copy of the completed depth disagrees with the search's own after the {exit} exit.");
            }

            AlphaBetaSearch ceiling = NewSearch();
            ceiling.FindBestMove(QuietPosition(),
                new AISearchSettings(maxDepth: 4, TestTimeBudgets.Generous, BetrayalUsage.Full),
                CancellationToken.None);
            AssertBothCopiesAgree("ceiling", SearchStopReason.Ceiling, ceiling);

            AlphaBetaSearch mate = NewSearch();
            mate.FindBestMove(BackRankMateInOne(),
                new AISearchSettings(maxDepth: 9, TestTimeBudgets.Generous, BetrayalUsage.Full),
                CancellationToken.None);
            AssertBothCopiesAgree("mate-found", SearchStopReason.MateFound, mate);

            AlphaBetaSearch outOfTime = NewSearch();
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(5);
                outOfTime.FindBestMove(QuietPosition(),
                    new AISearchSettings(maxDepth: 9, new AITimeBudget(5, 5), BetrayalUsage.Full),
                    cts.Token);
            }
            AssertBothCopiesAgree("out-of-time", SearchStopReason.Budget, outOfTime);

            AlphaBetaSearch settled = NewSearch();
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(10_000);
                settled.FindBestMove(QuietPosition(),
                    new AISearchSettings(maxDepth: 9, new AITimeBudget(50, 10_000), BetrayalUsage.Full),
                    cts.Token, enableInstabilityTimeManagement: true);
            }
            AssertBothCopiesAgree("settled-early", SearchStopReason.SettledEarly, settled);
        }

        [Test]
        public void ASearchThatCompletesNoDepth_ReportsNothingCarriedOverFromTheSearchBefore()
        {
            // Both values are per-search state on a search object that outlives any one call, so a
            // call that gets no work done has to say so rather than leave the previous answer
            // standing. A pre-cancelled token is the one way to guarantee that case: the deepening
            // loop checks for cancellation before its very first depth, so nothing can complete.
            _search.FindBestMove(BackRankMateInOne(),
                new AISearchSettings(maxDepth: 9, TestTimeBudgets.Generous, BetrayalUsage.Full),
                CancellationToken.None);

            Assume.That(_search.LastCompletedDepth, Is.GreaterThan(0),
                "The first search has to have gotten somewhere for the second one to be able to " +
                "inherit anything from it.");
            Assume.That(_search.StopReason, Is.EqualTo(SearchStopReason.MateFound));

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                _search.FindBestMove(QuietPosition(),
                    new AISearchSettings(maxDepth: 9, TestTimeBudgets.Generous, BetrayalUsage.Full),
                    cts.Token);
            }

            Assert.That(_search.LastCompletedDepth, Is.EqualTo(0),
                "A search that completed no depth at all must report 0, not the depth the previous " +
                "search on this instance reached.");
            Assert.That(_search.StopReason, Is.EqualTo(SearchStopReason.Budget),
                "A search cancelled before it started ran out of time; reporting the previous " +
                "search's mate would credit it with a result it never produced.");
        }

        [Test]
        public void FindBestMove_FixedPosition_StillAllocatesNoManagedMemory()
        {
            // The stop reason and the depth reached are plain field writes, a couple per search —
            // recording them must not introduce any boxing/GC on the search hot path. This covers
            // them on every build, since unlike the counters beside them they are not compiled out
            // of one.
            BoardState warmup = QuietPosition();
            _search.FindBestMove(warmup, new AISearchSettings(2, TestTimeBudgets.Generous, BetrayalUsage.Full), CancellationToken.None);

            BoardState board = QuietPosition();
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            _search.FindBestMove(board, settings, CancellationToken.None);
            long after = System.GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L),
                "Recording the stop reason must be a plain enum field write — any allocation delta " +
                "means something snuck onto the guarded search path.");
        }
    }
}
