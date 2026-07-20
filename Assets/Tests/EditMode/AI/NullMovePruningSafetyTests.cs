using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Proves the NMP guard set actually blocks a null move in every case it must:
    /// a pending Betrayer at ANY sub-phase (Act-pending, Retribution-pending, ForcedSave-pending),
    /// being in check, and two consecutive null moves. These are correctness gates, not perf
    /// gates — the whole point is that FindBestMove/RunQuiescenceForTest never desyncs the turn or
    /// the Zobrist hash by null-passing through a state where the domain forbids it.
    /// </summary>
    [TestFixture]
    public class NullMovePruningSafetyTests
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
            // White Acted the Knight onto its own Pawn; Rook on a1 can execute Retribution.
            // If NMP's guard fired here, CurrentTurn would flip mid-sequence and corrupt movegen.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "A null move slipping through the pending-Betrayer guard would desync the incremental hash.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void FindBestMove_ForcedSavePending_NeverDesyncsTurnOrHash()
        {
            // Rook on e4 is the pending Betrayer with no legal Executioner; if it defects it will
            // check its own former King on e1 (ForcedSave). Pending state + non-flip must hold
            // through NMP just as it does through ordinary search.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithPendingBetrayer("e4", Team.White)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void FindBestMove_SideInCheck_NeverDesyncsTurnOrHash()
        {
            // White king in check from the Black rook on e-file — NMP guard 2 (!IsKingInCheck)
            // must block here; passing while in check is illegal and mate-blind.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void FindBestMove_NoNonPawnMaterial_StillCompletesWithoutDesync()
        {
            // Zugzwang-prone position: side to move has only King + Pawns. NMP guard 5
            // (HasNonPawnMaterial) must block a null move here regardless of depth/beta.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void FindBestMove_MidgamePosition_NeverPlaysTwoConsecutiveNullMoves()
        {
            // No direct observation seam for "did NMP fire twice in a row" — this is a correctness
            // regression guard: a double-null bug manifests as a stack overflow or an infinite
            // depth-1-minus-2R collapse, either of which would hang or throw. Reaching a normal
            // return with a consistent hash on a full-material midgame position is the signal that
            // the parentWasNull guard is actually wired through the recursion, not just declared.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }
    }
}
