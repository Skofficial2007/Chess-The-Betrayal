using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
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
    /// the first place. One test below (the mate one) pins a genuine finding rather than the
    /// originally-intended behavior: see its own comment for why "mate found" does not actually stop
    /// the search early today.
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
            _search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));
        }

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

        [Test]
        public void FindBestMove_ForcedMateAlreadyFound_StillReportsCeilingNotMateFound()
        {
            // Black King boxed in on g8 by its own pawns; White Rook on a1 delivers Ra1-a8# — a real
            // mate the search finds from depth 2 on. This PINS a genuine finding from building this
            // stop-reason telemetry, not the originally-intended behavior: the root's own
            // "bestScore >= MateScore" early exit is unreachable as written. A mate scores
            // MateScore MINUS at least one ply's worth of remaining depth at the moment it's detected
            // (see the mate-scoring branch inside Search), so bestScore can get arbitrarily close to
            // MateScore but a genuinely deeper MaxDepth always keeps it strictly below the threshold —
            // confirmed directly (temporary diagnostic, removed): depths 2/3/4/5 on this exact position
            // scored 899999/899998/899997/899996 against a MateScore of 900000, never >=. The search
            // therefore keeps re-searching every configured depth even after it has already found a
            // forced mate, which this test pins as the CURRENT behavior rather than papering over it.
            // Fixing the early exit itself is out of scope here — a search behavior change needs its
            // own investigation and safety pass, not a side effect of adding stop-reason telemetry.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();
            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(_search.Stats.StopReason, Is.EqualTo(SearchStopReason.Ceiling));
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
        public void FindBestMove_FixedPosition_StillAllocatesNoManagedMemory()
        {
            // The stop-reason field is a plain enum write behind the same guard as every other
            // counter — recording it must not introduce any boxing/GC on the search hot path.
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
