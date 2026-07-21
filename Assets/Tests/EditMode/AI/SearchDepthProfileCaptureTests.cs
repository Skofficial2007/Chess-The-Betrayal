using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
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
    /// Three positions on purpose, not one. A single fixed opening understates the real cost: a quiet
    /// position, a tactical one with a live capture chain, and a Betrayal-live one all grow their
    /// trees differently past depth 7, and the deepest tiers meet all three in real games.
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

        /// <summary>A developed middlegame with both sides castled, no captures pending — the "quiet"
        /// baseline. Betrayal right is live so the tree carries the real Act/Defection branching.</summary>
        private static BoardState QuietMidgame() =>
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
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("c7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        /// <summary>A sharp middlegame with pieces en prise and open lines — captures and recaptures
        /// available, so the quiescence tail is far larger than the quiet position's.</summary>
        private static BoardState TacticalMidgame() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("h4", Team.Black, ChessPieceType.Queen)
                .WithPiece("e1", Team.White, ChessPieceType.Rook)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                .WithPiece("e5", Team.Black, ChessPieceType.Knight)
                .WithPiece("c4", Team.White, ChessPieceType.Bishop)
                .WithPiece("c5", Team.Black, ChessPieceType.Bishop)
                .WithPiece("f3", Team.White, ChessPieceType.Pawn)
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("d6", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        /// <summary>A Betrayal-live middlegame with the pieces packed near both kings, where an Act and
        /// its forced follow-up are genuinely on the table — the shape whose tree the earlier
        /// benchmarks understated because they measured a quiet opening instead.</summary>
        private static BoardState BetrayalLiveMidgame() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("e2", Team.White, ChessPieceType.Queen)
                .WithPiece("e7", Team.Black, ChessPieceType.Queen)
                .WithPiece("d1", Team.White, ChessPieceType.Rook)
                .WithPiece("d8", Team.Black, ChessPieceType.Rook)
                .WithPiece("f1", Team.White, ChessPieceType.Rook)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook)
                .WithPiece("e3", Team.White, ChessPieceType.Bishop)
                .WithPiece("e6", Team.Black, ChessPieceType.Bishop)
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                .WithPiece("d5", Team.Black, ChessPieceType.Knight)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("c7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

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
        public void CaptureDepthProfile_QuietMidgame() => CaptureProfile("quiet-midgame", QuietMidgame());

        [Test]
        public void CaptureDepthProfile_TacticalMidgame() => CaptureProfile("tactical-midgame", TacticalMidgame());

        [Test]
        public void CaptureDepthProfile_BetrayalLiveMidgame() => CaptureProfile("betrayal-live-midgame", BetrayalLiveMidgame());
    }
}
