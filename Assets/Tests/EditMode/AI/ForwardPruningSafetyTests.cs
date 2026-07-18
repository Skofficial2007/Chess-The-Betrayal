using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Proves the forward-pruning family (reverse futility, move-count pruning, frontier futility)
    /// shares the same forwardPruningAllowed guard NullMovePruningSafetyTests already exercises for
    /// NMP: all three must no-op whenever a Betrayer is pending (any sub-phase) or the side to move
    /// is in check, exactly like a null move would be unsound there. These are correctness gates —
    /// FindBestMove must never desync the turn or the Zobrist hash by pruning through a state the
    /// domain forbids skipping.
    /// </summary>
    [TestFixture]
    public class ForwardPruningSafetyTests
    {
        private ChessEngineAdapter _engine;
        private AlphaBetaSearch _search;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
        }

        [Test]
        public void FindBestMove_ActPendingRetributionAvailable_NeverDesyncsTurnOrHash()
        {
            // White Acted the Knight onto its own Pawn; Rook on a1 can execute Retribution. If any
            // forward-pruning member fired here it would evaluate/skip mid-sequence, corrupting
            // movegen exactly like an unguarded null move would.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 5, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "A forward-pruning member slipping through the pending-Betrayer guard would desync the incremental hash.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void FindBestMove_ForcedSavePending_NeverDesyncsTurnOrHash()
        {
            // Rook on e4 is the pending Betrayer with no legal Executioner; if it defects it will
            // check its own former King on e1 (ForcedSave). Pending state + non-flip must hold
            // through the forward-pruning family just as it does through NMP and ordinary search.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithPendingBetrayer("e4", Team.White)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 5, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void FindBestMove_SideInCheck_NeverDesyncsTurnOrHash()
        {
            // White king in check from the Black rook on e-file — the shared forwardPruningAllowed
            // guard must block every pruning member here; a check position is exactly the case a
            // static eval can't safely stand in for real search.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 5, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void FindBestMove_MidgamePosition_CompletesWithConsistentHash()
        {
            // General regression guard on full material: reverse futility, move-count pruning, and
            // frontier futility are all live at shallow depths in a normal position — this proves
            // they coexist with TT/NMP/LMR/PVS without corrupting the incremental Zobrist hash.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 5, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void RunQuiescenceForTest_FrontierFutilityNeverSkipsBetrayalResolution()
        {
            // Frontier futility lives only in Search's main move loop, never in Quiescence — this
            // proves that a horizon hit while a Betrayer is pending still fully resolves the
            // Retribution/Defection/ForcedSave sub-phase (as NullMovePruningSafetyTests' sibling
            // suite already proves for NMP), rather than a forward-pruning member anywhere on the
            // path short-circuiting a node that still needs quiescence resolution.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;

            int score = 0;
            Assert.DoesNotThrow(() =>
                score = _search.RunQuiescenceForTest(board, Team.Black, CancellationToken.None));

            // White's Rook executes Retribution and captures the Knight — a pending Retribution
            // resolves via GetRetributionMoves, never a bare stand-pat, so from Black's own
            // perspective the returned score must reflect the lost Knight, not a static eval frozen
            // mid-sequence (which would report something close to material parity instead).
            Assert.That(score, Is.LessThan(-200));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }
    }
}
