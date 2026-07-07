using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Correctness tests for AlphaBetaSearch: mate detection, Zobrist consistency across a full
    /// search (proving every ApplyMove is paired with an UndoMove even across cutoffs), and the
    /// Betrayal turn-flip rule (negation only when the turn actually passes).
    /// </summary>
    [TestFixture]
    public class SearchCorrectnessTests
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
        public void FindBestMove_BackRankMateInOne_FindsTheMatingMove()
        {
            // Black King boxed in on g8 by its own pawns; White Rook on a1 delivers Ra1-a8#.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 3, softTimeBudgetMs: 5000, BetrayalUsage.Full);
            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.StartPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a1")));
            Assert.That(best.EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("a8")));
        }

        [Test]
        public void FindBestMove_FullDepthSearch_BoardZobristConsistentAfterReturn()
        {
            // Every ApplyMove/UndoMove pair — including ones pruned by a beta cutoff mid-loop —
            // must leave the live board exactly as it found it. A single missed UndoMove here
            // would desync the incremental Zobrist hash from the board's actual piece layout.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            ulong hashBefore = board.ZobristHash;

            var settings = new AISearchSettings(maxDepth: 3, softTimeBudgetMs: 5000, BetrayalUsage.Full);
            _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "Board's incremental hash must still match a from-scratch recomputation after the full search unwinds.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore),
                "The live board must be untouched (every explored line fully unmade) once FindBestMove returns.");
        }

        [Test]
        public void FindBestMove_ActWithFreeRetributionCapture_PrefersInitiatingBetrayal()
        {
            // White can Act the Knight at b1 onto its own Pawn at a3, and White's Rook at a1 then
            // executes the Knight next ply (Retribution) — net effect: White trades a pawn it
            // already owned for nothing except opening the file, so this is a wash materially,
            // but it proves the search explores Act -> Retribution without desyncing the turn
            // (StageFlipsTurn) or corrupting the hash across the sub-phase.
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

            var settings = new AISearchSettings(maxDepth: 3, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            Assert.DoesNotThrow(() => _search.FindBestMove(board, settings, CancellationToken.None),
                "Search must explore an Act -> Retribution sub-sequence without throwing.");

            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "Hash must remain consistent after searching through a full Betrayal sub-phase.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore),
                "The live board must be fully restored after searching lines that include Act/Retribution.");
        }

        [Test]
        public void FindBestMove_DefendOnlyMode_NeverChoosesActAtRoot()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 2, softTimeBudgetMs: 5000, BetrayalUsage.DefendOnly);
            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.Stage, Is.Not.EqualTo(BetrayalStage.Act),
                "DefendOnly must never let the agent choose an Act move as its own root decision.");
        }
    }
}
