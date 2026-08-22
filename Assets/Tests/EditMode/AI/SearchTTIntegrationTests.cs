using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// TT-on vs TT-off must choose the SAME best move on fixed positions — the transposition table
    /// changes exploration order and cutoff timing only, never move selection (mirrors OrderScore's
    /// "ordering only" contract). Covers a plain midgame position and a pending-Betrayer position,
    /// since the ADR's load-bearing assumption is that TT cutoffs stay valid mid-Betrayal-sequence.
    /// </summary>
    [TestFixture]
    public class SearchTTIntegrationTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        private AlphaBetaSearch NewSearchWithFreshTT() =>
            new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(), transpositionTable: new TranspositionTable(log2Size: 12));

        // A "TT-off" search still owns a table (there is no code path without one), but a table so
        // tiny every store immediately evicts the last one — its cutoff/ordering influence collapses
        // to noise, giving an effective no-TT baseline without touching AlphaBetaSearch's API.
        private AlphaBetaSearch NewSearchWithNegligibleTT() =>
            new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(), transpositionTable: new TranspositionTable(log2Size: 1));

        [Test]
        public void FindBestMove_BackRankMateInOne_SameMoveWithAndWithoutEffectiveTT()
        {
            BoardState boardTTOn = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();
            BoardState boardTTOff = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            MoveCommand withTT = NewSearchWithFreshTT().FindBestMove(boardTTOn, settings, CancellationToken.None);
            MoveCommand withoutTT = NewSearchWithNegligibleTT().FindBestMove(boardTTOff, settings, CancellationToken.None);

            Assert.That(withTT.StartPosition, Is.EqualTo(withoutTT.StartPosition));
            Assert.That(withTT.EndPosition, Is.EqualTo(withoutTT.EndPosition));
        }

        [Test]
        public void FindBestMove_PendingBetrayerPosition_SameMoveWithAndWithoutEffectiveTT()
        {
            // White to move with a Betrayer already pending (Retribution available via the Rook on
            // a1) — exercises TT probe/store while the hash's Betrayal contribution is live. If the
            // pending-Betrayer state were missing from the hash, two genuinely different positions
            // would collide on one entry and the search could return a move from the wrong one.
            BoardState boardTTOn = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();
            BoardState boardTTOff = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            MoveCommand withTT = NewSearchWithFreshTT().FindBestMove(boardTTOn, settings, CancellationToken.None);
            MoveCommand withoutTT = NewSearchWithNegligibleTT().FindBestMove(boardTTOff, settings, CancellationToken.None);

            Assert.That(withTT.StartPosition, Is.EqualTo(withoutTT.StartPosition));
            Assert.That(withTT.EndPosition, Is.EqualTo(withoutTT.EndPosition));
            Assert.That(withTT.Stage, Is.EqualTo(withoutTT.Stage));
        }

        [Test]
        public void FindBestMove_PoisonedTTEntry_OnlyMisordersNeverChangesResult()
        {
            // A stale entry from an unrelated position collides on this table's tiny index space.
            // Per the ADR: a poisoned/mismatched entry can only mis-order a node (PackedBestMove is
            // matched against the freshly generated legal list, never rehydrated), never inject an
            // illegal move or change which move the search ultimately reports as best.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

            var tt = new TranspositionTable(log2Size: 4); // small enough that garbage below likely collides
            // Seed every slot with a garbage "best move" for an unrelated hash/depth/flag combination.
            for (uint i = 0; i < 16; i++)
            {
                tt.Store(hash: i, score: 123456, packedMove: 0x3FFFF, depth: 1, TTFlag.Exact);
            }

            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(), transpositionTable: tt);
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            MoveCommand best = search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.StartPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a1")));
            Assert.That(best.EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a8")));
        }
    }
}
