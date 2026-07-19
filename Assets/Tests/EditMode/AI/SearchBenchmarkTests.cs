using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;
using ChessTheBetrayal.EditorTools.OpeningBook;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Times a raw (profile-free) search on a real midgame position under a production-style
    /// 3-second budget and reports it alongside the SearchStats counters (TT hit rate,
    /// null-move/LMR/PVS activity, quiescence node split). The gate is the budget contract — the
    /// move arrives on time and the search completes a meaningful depth before the timer cuts it
    /// off — rather than wall clock at a fixed depth: an honestly-valued Betrayal-live position
    /// simply has a larger correct tree than a fixed-depth timing target can absorb across
    /// machines (this search used to take 22s/157s/340s across successive turns before the
    /// pruning/quiescence work landed; the budget cap is what protects the player either way).
    /// Treat the Console.WriteLine output as the number to eyeball for drift.
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
        public void FindBestMove_BudgetedMidgame_ArrivesOnTimeAndReachesDepthSix()
        {
            // 3 seconds is the per-move promise every difficulty tier makes to the player. The
            // depth floor of 6 is measured with a full ply of margin on the desktop baseline
            // (depth 7 completes cold within the budget) — dropping below it means the search is
            // spending its budget without getting deep, i.e. a pruning mechanism broke.
            BoardState board = MidgamePosition();
            var settings = new AISearchSettings(maxDepth: 9, new AITimeBudget(3_000, 3_000), BetrayalUsage.Full);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));

            var stopwatch = Stopwatch.StartNew();
            MoveCommand best;
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(settings.TimeBudget.HardMs);
                best = search.FindBestMove(board, settings, cts.Token, enableInstabilityTimeManagement: true);
            }
            stopwatch.Stop();

            System.Console.WriteLine(
                $"Budgeted midgame search: {stopwatch.Elapsed.TotalSeconds:F2}s, best={best}, stats={search.Stats}");

            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(3.75),
                $"Budgeted midgame search took {stopwatch.Elapsed.TotalSeconds:F2}s against a 3.0s hard budget — " +
                "the cancellation timer or the search's own budget checks are not stopping it on time.");

            Assert.That(search.Stats.LastCompletedDepth, Is.GreaterThanOrEqualTo(6),
                $"Only completed depth {search.Stats.LastCompletedDepth} within the budget — " +
                "the search is spending its time without getting deep.");

            Assert.That(search.Stats.NodesVisited, Is.GreaterThan(0));
        }

        [Test]
        public void FindBestMove_Depth6Midgame_ReportsPruningMultipliersAreActive()
        {
            // A deep midgame search should show every pruning mechanism doing real work — this is
            // the gate check's other half: proving the budget's depth floor is met BECAUSE the
            // pruning stack is engaged, not because the position happened to be trivial. Depth 6
            // is deep enough to engage every mechanism and completes fast uncapped, so this stays
            // a quick deterministic telemetry probe rather than a timing test.
            BoardState board = MidgamePosition();
            var settings = new AISearchSettings(maxDepth: 6, new AITimeBudget(60_000, 60_000), BetrayalUsage.Full);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

            search.FindBestMove(board, settings, CancellationToken.None);

            System.Console.WriteLine($"Depth-6 midgame telemetry: {search.Stats}");

            Assert.That(search.Stats.TTProbes, Is.GreaterThan(0), "TT should be probed at every recursive node.");
            Assert.That(search.Stats.TTHits, Is.GreaterThan(0), "A depth-7 search must re-visit transposed positions.");
            Assert.That(search.Stats.NullMoveAttempts, Is.GreaterThan(0), "NMP's depth>=4 guard should fire repeatedly at this depth.");
            Assert.That(search.Stats.PvsScouts, Is.GreaterThan(0), "Every non-PV sibling should be scouted with a null window.");
        }

        /// <summary>Always returns 0 — deterministic weighted-pick for a benchmark that only cares
        /// about wall-clock cost, not which candidate the book picks.</summary>
        private sealed class ZeroRandomSource : IRandomSource
        {
            public bool NextBool() => false;
            public int NextInt(int maxExclusive) => 0;
            public float NextFloat() => 0f;
        }

        [Test]
        public void OpeningBookLookup_Hit_IsOrdersOfMagnitudeFasterThanDepth7Search()
        {
            // The whole point of the opening book is to skip the search entirely for known
            // theory — this proves the lookup path actually delivers that, not just that it
            // returns a legal move. A generous 200ms ceiling (vs. seconds for a real search)
            // leaves headroom for CI/dev-machine jitter while still catching a regression where
            // the book lookup accidentally falls through to a full search.
            OpeningBookAsset book = CompileStartingBook();
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();

            var stopwatch = Stopwatch.StartNew();
            MoveCommand? bookMove = OpeningBookLookup.TryGetBookMove(book, board, _engine, new ZeroRandomSource());
            stopwatch.Stop();

            System.Console.WriteLine($"Opening-book lookup: {stopwatch.Elapsed.TotalMilliseconds:F2}ms, move={bookMove}");

            Assert.That(bookMove, Is.Not.Null);
            Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.LessThan(200),
                $"Opening-book lookup took {stopwatch.Elapsed.TotalMilliseconds:F2}ms — expected well under 200ms; " +
                "exceeding this suggests the lookup fell through to a real search instead of answering instantly.");
        }

        private static OpeningBookAsset CompileStartingBook()
        {
            var (keys, packedMoves, weights, schemeVersion) = OpeningBookCompiler.Compile("e2e4 e7e5 g1f3 b8c6");
            var asset = ScriptableObject.CreateInstance<OpeningBookAsset>();
            asset.SetEntries(keys, packedMoves, weights, schemeVersion);
            return asset;
        }
    }
}
