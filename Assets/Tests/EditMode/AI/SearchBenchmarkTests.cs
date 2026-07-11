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
    /// The ADR_AI16 gate check: after TT + move-ordering tiers + NMP + LMR + PVS (AI-16..20) and
    /// telemetry (AI-21) are all in, hand-time a depth-7 search on a real midgame position and
    /// report it alongside the new SearchStats counters. This is the "run the timing before
    /// deciding what's next" step the ADR calls for — NOT a re-scope of AI-22 (the opening book is
    /// correctly deferred to ADR23's AI-27/AI-28; see ai-adr16-search-performance memory).
    ///
    /// Definition of Done: depth 7 under 2-3 seconds on a midgame position (previously observed at
    /// 22s/157s/340s across successive turns, pre-AI-16). This is written as a fast smoke assertion
    /// (a generous upper bound, not a tight benchmark) so it stays a reliable CI gate rather than a
    /// flaky timing test — treat the Console.WriteLine output as the actual number to eyeball.
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
        /// open, roughly balanced material with realistic piece density — the shape the ADR's
        /// escalation was originally observed on, not the day-1 standard setup (too few developed
        /// pieces) or an endgame (too few nodes to matter).
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
        public void FindBestMove_Depth7Midgame_CompletesWellUnderTenSeconds()
        {
            // Ten seconds is a deliberately generous CI backstop (the ADR's actual target is 2-3s) —
            // this test exists to catch a gross regression (an accidentally-disabled pruning
            // mechanism sending the search back toward the pre-AI-16 30-320s range), not to enforce
            // the tight DoD number itself, which should be read off the printed output below by a
            // human deciding whether the ADR's gate is met.
            BoardState board = MidgamePosition();
            var settings = new AISearchSettings(maxDepth: 7, softTimeBudgetMs: 60_000, BetrayalUsage.Full);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

            var stopwatch = Stopwatch.StartNew();
            MoveCommand best = search.FindBestMove(board, settings, CancellationToken.None);
            stopwatch.Stop();

            System.Console.WriteLine(
                $"Depth-7 midgame search: {stopwatch.Elapsed.TotalSeconds:F2}s, best={best}, stats={search.Stats}");

            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(10.0),
                $"Depth-7 midgame search took {stopwatch.Elapsed.TotalSeconds:F2}s — the ADR's DoD is 2-3s; " +
                "10s is a coarse regression backstop, so exceeding it means a pruning mechanism likely broke, " +
                "not just that the target needs re-tuning.");

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
