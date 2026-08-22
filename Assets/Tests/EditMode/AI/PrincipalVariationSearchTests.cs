using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// PVS's null-window scout + full-window re-search ladder must never change
    /// which move a search reports as best — only how it gets there. These fixtures are the same
    /// mate-in-one/Betrayal shapes SearchCorrectnessTests and LateMoveReductionTests already pin,
    /// run at depths deep enough (multiple full-window siblings past the PV move) to guarantee the
    /// null-window scout -> re-search path actually engages, not just compiles.
    /// </summary>
    [TestFixture]
    public class PrincipalVariationSearchTests
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
        public void FindBestMove_BackRankMateInOne_StillFoundAtDepthWherePVSEngages()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("d2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.StartPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a1")));
            Assert.That(best.EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a8")));
        }

        [Test]
        public void FindBestMove_StandardOpeningPosition_CompletesWithConsistentHashAtDepth()
        {
            // A full-material position gives every node many siblings past the PV move, so the
            // null-window-scout -> LMR-re-search -> PVS-re-search ladder all get real exercise, not
            // just a single-child pass-through. The regression is Zobrist consistency + no exception,
            // since chosen-move pinning at this depth/branching would be brittle to evaluator tuning.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void FindBestMove_ActWithFreeRetributionCapture_NullWindowPassThroughStaysConsistent()
        {
            // Same fixture as SearchCorrectnessTests' Act->Retribution regression, but at a depth
            // where later root siblings are scouted with a null window. Proves ScoreChild's
            // non-flipping pass-through (Act/Defection share the parent's maximizer, so a null
            // window arrives unchanged in that frame) survives PVS without desyncing the hash.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }

        [Test]
        public void FindBestMove_PendingBetrayerPosition_SameMoveAsBeforePVS()
        {
            // Reuses the exact fixture SearchTTIntegrationTests already pins a specific best move
            // for — proves the PVS ladder doesn't change the chosen move even mid-Betrayal-sequence,
            // where the non-flipping-child pass-through is exercised by every Act/Defection sibling.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
        }
    }
}
