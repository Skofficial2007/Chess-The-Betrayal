using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Hand-times a depth-7 search on a real midgame position and reports it alongside the
    /// SearchStats counters (TT hit rate, null-move/LMR/PVS activity, quiescence node split).
    /// Target: depth 7 under a few seconds on a midgame position (this search used to take
    /// 22s/157s/340s across successive turns before the pruning/quiescence work landed). Written
    /// as a fast smoke assertion (a generous upper bound, not a tight benchmark) so it stays a
    /// reliable CI gate rather than a flaky timing test — treat the Console.WriteLine output as
    /// the actual number to eyeball.
    /// </summary>
    [TestFixture]
    public class SearchBenchmarkTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        /// <summary>
        /// A representative post-opening midgame position: both sides developed, no Betrayal state
        /// open, roughly balanced material with realistic piece density — the position shape that
        /// originally exposed the slow-search problem, not the day-1 standard setup (too few
        /// developed pieces) or an endgame (too few nodes to matter).
        /// </summary>
        private static BoardState MidgamePosition() =>
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
                .WithPiece("g2", Team.White, ChessPieceType.Bishop)
                .WithPiece("d2", Team.White, ChessPieceType.Knight)
                .WithPiece("g7", Team.Black, ChessPieceType.Bishop)
                .WithPiece("b8", Team.Black, ChessPieceType.Knight)
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
        public void FindBestMove_Depth7Midgame_CompletesWellUnderFiveSeconds()
        {
            // Five seconds is a CI regression backstop with headroom over the ~3.8s this search
            // currently takes on the fixed midgame position — this test exists to catch a gross
            // regression (an accidentally-disabled pruning mechanism sending the search back toward
            // the many-second range it used to take), not to pin the exact number, which drifts as
            // the search improves and should be read off the printed output below.
            BoardState board = MidgamePosition();
            var settings = new AISearchSettings(maxDepth: 7, softTimeBudgetMs: 60_000, BetrayalUsage.Full);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

            var stopwatch = Stopwatch.StartNew();
            MoveCommand best = search.FindBestMove(board, settings, CancellationToken.None);
            stopwatch.Stop();

            System.Console.WriteLine(
                $"Depth-7 midgame search: {stopwatch.Elapsed.TotalSeconds:F2}s, best={best}, stats={search.Stats}");

            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(5.0),
                $"Depth-7 midgame search took {stopwatch.Elapsed.TotalSeconds:F2}s — expected well under 5s; " +
                "exceeding this means a pruning mechanism likely broke, not just that the target needs re-tuning.");

            Assert.That(search.Stats.NodesVisited, Is.GreaterThan(0));
        }

        [Test]
        public void FindBestMove_Depth7Midgame_ReportsPruningMultipliersAreActive()
        {
            // A depth-7 midgame search should show every AI-16..20 mechanism doing real work — this
            // is the gate check's other half: proving the DoD (if met) is met BECAUSE the pruning
            // stack is engaged, not because the position happened to be trivial.
            BoardState board = MidgamePosition();
            var settings = new AISearchSettings(maxDepth: 7, softTimeBudgetMs: 60_000, BetrayalUsage.Full);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

            search.FindBestMove(board, settings, CancellationToken.None);

            System.Console.WriteLine($"Depth-7 midgame telemetry: {search.Stats}");

            Assert.That(search.Stats.TTProbes, Is.GreaterThan(0), "TT should be probed at every recursive node.");
            Assert.That(search.Stats.TTHits, Is.GreaterThan(0), "A depth-7 search must re-visit transposed positions.");
            Assert.That(search.Stats.NullMoveAttempts, Is.GreaterThan(0), "NMP's depth>=4 guard should fire repeatedly at this depth.");
            Assert.That(search.Stats.PvsScouts, Is.GreaterThan(0), "Every non-PV sibling should be scouted with a null window.");
        }
    }
}
