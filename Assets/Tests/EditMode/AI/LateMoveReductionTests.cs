using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the quiet-move predicate directly — captures, promotions, the TT move, and every
    /// Betrayal-stage move are never treated as quiet regardless of where they land in the sort
    /// order (an Act still gets reduced, but through its own explicitly-reasoned path in Search's
    /// loop, never by falling into the quiet band) — and regression-tests that reducing moves
    /// never changes which move a full search reports as best (SearchCorrectnessTests already pins
    /// the exact chosen moves; this file adds a couple of positions likely to exercise the
    /// reduce -> fail-high -> re-search path).
    /// </summary>
    [TestFixture]
    public class LateMoveReductionTests
    {
        private static readonly Vector2Int From = TestBoardSetupUtility.AlgebraicToVector("a2");
        private static readonly Vector2Int To = TestBoardSetupUtility.AlgebraicToVector("a3");
        private static readonly Vector2Int OtherTo = TestBoardSetupUtility.AlgebraicToVector("a4");

        private static MoveCommand QuietMove(Vector2Int? to = null)
        {
            PieceData piece = new PieceData(Team.White, ChessPieceType.Knight, moveDirection: 1, startRow: 1);
            return MoveCommand.CreateStandardMove(From, to ?? To, piece);
        }

        private static MoveCommand CaptureMove()
        {
            PieceData piece = new PieceData(Team.White, ChessPieceType.Knight, moveDirection: 1, startRow: 1);
            PieceData captured = new PieceData(Team.Black, ChessPieceType.Pawn, moveDirection: -1, startRow: 6);
            return MoveCommand.CreateStandardMove(From, To, piece, captured);
        }

        private static MoveCommand PromotionMove()
        {
            PieceData pawn = new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1);
            return MoveCommand.CreatePromotionMove(From, To, pawn, ChessPieceType.Queen);
        }

        private static MoveCommand ActMove()
        {
            PieceData piece = new PieceData(Team.White, ChessPieceType.Knight, moveDirection: 1, startRow: 1);
            return MoveCommand.CreateStandardMove(From, To, piece).WithStage(BetrayalStage.Act);
        }

        private static MoveCommand RetributionMove()
        {
            PieceData piece = new PieceData(Team.White, ChessPieceType.Rook, moveDirection: 1, startRow: 1);
            PieceData captured = new PieceData(Team.Black, ChessPieceType.Knight, moveDirection: -1, startRow: 6);
            return MoveCommand.CreateStandardMove(From, To, piece, captured).WithStage(BetrayalStage.Retribution);
        }

        private static MoveCommand DefensiveOverrideMove()
        {
            PieceData piece = new PieceData(Team.White, ChessPieceType.King, moveDirection: 1, startRow: 1);
            return MoveCommand.CreateStandardMove(From, To, piece).WithStage(BetrayalStage.DefensiveOverride);
        }

        private static MoveCommand DefectionMove()
        {
            PieceData piece = new PieceData(Team.White, ChessPieceType.Knight, moveDirection: 1, startRow: 1);
            return MoveCommand.CreateStandardMove(From, From, piece).WithStage(BetrayalStage.Defection);
        }

        [Test]
        public void QuietMove_NotTTMove_IsReducible()
        {
            Assert.That(AlphaBetaSearch.IsReducibleMove(QuietMove(), ttMove: 0), Is.True);
        }

        [Test]
        public void CaptureMove_IsNeverReducible()
        {
            Assert.That(AlphaBetaSearch.IsReducibleMove(CaptureMove(), ttMove: 0), Is.False);
        }

        [Test]
        public void PromotionMove_IsNeverReducible()
        {
            Assert.That(AlphaBetaSearch.IsReducibleMove(PromotionMove(), ttMove: 0), Is.False);
        }

        [Test]
        public void TTMove_IsNeverReducible_EvenIfOtherwiseQuiet()
        {
            MoveCommand quiet = QuietMove();
            uint ttPacked = AlphaBetaSearch.PackMove(quiet);

            Assert.That(AlphaBetaSearch.IsReducibleMove(quiet, ttPacked), Is.False);
        }

        [Test]
        public void QuietMove_NotMatchingTTMove_StillReducible()
        {
            MoveCommand quiet = QuietMove();
            uint unrelatedTTMove = AlphaBetaSearch.PackMove(QuietMove(to: OtherTo));

            Assert.That(AlphaBetaSearch.IsReducibleMove(quiet, unrelatedTTMove), Is.True);
        }

        [Test]
        public void ActMove_IsNeverTreatedAsQuiet()
        {
            Assert.That(AlphaBetaSearch.IsReducibleMove(ActMove(), ttMove: 0), Is.False,
                "An Act is a capture of one's own piece that stages a forced sequence — treating it as a " +
                "quiet move would expose it to move-count pruning and futility skips, which must never " +
                "silently discard a Betrayal line. Its reduction happens through its own dedicated path.");
        }

        [Test]
        public void RetributionMove_IsNeverReducible()
        {
            Assert.That(AlphaBetaSearch.IsReducibleMove(RetributionMove(), ttMove: 0), Is.False);
        }

        [Test]
        public void DefensiveOverrideMove_IsNeverReducible()
        {
            Assert.That(AlphaBetaSearch.IsReducibleMove(DefensiveOverrideMove(), ttMove: 0), Is.False);
        }

        [Test]
        public void DefectionMove_IsNeverReducible()
        {
            Assert.That(AlphaBetaSearch.IsReducibleMove(DefectionMove(), ttMove: 0), Is.False);
        }

        [Test]
        public void FindBestMove_ManyQuietMoves_StillFindsMateDespiteReductions()
        {
            // Same back-rank mate-in-one fixture as SearchCorrectnessTests, but at a depth deep
            // enough (>=3) and with enough quiet sibling moves for LMR to actually fire — proves a
            // reduced-then-refuted quiet move doesn't cause the search to miss the real best line.
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

            var engine = new ChessEngineAdapter();
            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator());
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            MoveCommand best = search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.StartPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a1")));
            Assert.That(best.EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a8")));
        }

        [Test]
        public void FindBestMove_PendingBetrayerPosition_NeverReducesAndStaysConsistent()
        {
            // A pending-Betrayer node must never reduce any child (nodeAllowsReduction guard) — this
            // is a regression guard that the search still completes and the hash stays consistent
            // even at a depth where LMR would otherwise be eligible to fire.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var engine = new ChessEngineAdapter();
            var search = new AlphaBetaSearch(engine, new BetrayalAwareEvaluator());
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => search.FindBestMove(board, settings, CancellationToken.None));

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
        }
    }
}
