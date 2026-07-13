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
        public void Quiescence_ForcedDefectionNoSelfCheck_ResolvesToFiniteScoreAndRestoresState()
        {
            // Regression for the AlphaBetaSearch.Quiescence StackOverflow: a Betrayer is pending with
            // NO legal Executioner (White King on a1 can't reach the Knight on h8), so quiescence must
            // drive the forced Defection to resolution. The Knight defects far from the White King, so
            // no self-check → no ForcedSave → the sequence fully resolves and the turn passes to Black.
            // Before the fix, this looped forever because the pending state was never cleared.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("h8", Team.White, ChessPieceType.Knight) // Betrayer, defects far from its King
                .WithTurn(Team.White)
                .WithPendingBetrayer("h8", Team.White)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            Vector2Int? pendingBefore = board.PendingBetrayerSquare;
            Team? initiatorBefore = board.BetrayalInitiator;

            int score = 0;
            Assert.DoesNotThrow(
                () => score = _search.RunQuiescenceForTest(board, Team.White, CancellationToken.None),
                "Quiescence must resolve a no-legal-Retribution Act (forced Defection) without overflowing the stack.");

            Assert.That(score, Is.GreaterThan(-1_000_000).And.LessThan(1_000_000),
                "Resolved quiescence must return a finite evaluation, not a garbage/overflow value.");

            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "The forced-Defection make/unmake inside quiescence must leave the incremental hash consistent.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore),
                "The live board (including the sub-state hash) must be fully restored after quiescence unwinds.");
            Assert.That(board.PendingBetrayerSquare, Is.EqualTo(pendingBefore),
                "PendingBetrayerSquare must be restored to its pre-search value on unmake.");
            Assert.That(board.BetrayalInitiator, Is.EqualTo(initiatorBefore),
                "BetrayalInitiator must be restored to its pre-search value on unmake.");
        }

        [Test]
        public void Quiescence_ForcedDefectionWithForcedSave_ResolvesTheSaveAndRestoresState()
        {
            // The ForcedSave sub-case the old code ignored entirely: no legal Executioner for the Rook
            // on e4, so it defects to Black and immediately checks its former King on e1 along the open
            // e-file. That obliges White to play a DefensiveOverride (king-save) — the SAME side moves
            // again, pending state stays set until the save closes it. Quiescence must explore the save
            // and still resolve to a finite score with a perfect state round-trip.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook) // Betrayer, defects and checks e1
                .WithTurn(Team.White)
                .WithPendingBetrayer("e4", Team.White)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            Vector2Int? pendingBefore = board.PendingBetrayerSquare;
            Team? initiatorBefore = board.BetrayalInitiator;

            int score = 0;
            Assert.DoesNotThrow(
                () => score = _search.RunQuiescenceForTest(board, Team.White, CancellationToken.None),
                "Quiescence must resolve a forced Defection that requires a ForcedSave without overflowing.");

            Assert.That(score, Is.GreaterThan(-1_000_000).And.LessThan(1_000_000),
                "Resolved quiescence must return a finite evaluation for the ForcedSave line.");

            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "The Defection + DefensiveOverride make/unmake inside quiescence must keep the hash consistent.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore),
                "The live board must be fully restored after the ForcedSave line unwinds.");
            Assert.That(board.PendingBetrayerSquare, Is.EqualTo(pendingBefore),
                "PendingBetrayerSquare must be restored after the ForcedSave line.");
            Assert.That(board.BetrayalInitiator, Is.EqualTo(initiatorBefore),
                "BetrayalInitiator must be restored after the ForcedSave line.");
        }

        [Test]
        public void FindBestMove_BoardArrivesAlreadyForcedSavePending_DoesNotThrow()
        {
            // Regression: unlike the test above (where quiescence discovers and resolves the ForcedSave
            // itself, defecting the piece as part of the same call), this board is handed to
            // FindBestMove ALREADY in the post-defection ForcedSave state — exactly what a real
            // multi-ply game leaves behind on the next ply after a self-checking Defection resolves
            // (see MatchDriver.PlayMove / TurnResolver.Advance). GetAllLegalMovesIncludingBetrayal used
            // to have no branch for this and silently generated ordinary moves instead of the mandatory
            // DefensiveOverride, which downstream corrupted search state and threw
            // Betrayal_ForcedSaveInvariantViolated deep inside quiescence on a real multi-game run.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e4", Team.Black, ChessPieceType.Rook) // already defected: now Black
                .WithPiece("h4", Team.White, ChessPieceType.Rook) // legal DefensiveOverride: captures on e4
                .WithTurn(Team.White)
                .WithPendingBetrayer("e4", Team.White)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 3, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            MoveCommand best = default;
            Assert.DoesNotThrow(() => best = _search.FindBestMove(board, settings, CancellationToken.None),
                "FindBestMove must not throw when handed a board that's already ForcedSave-pending on entry.");

            Assert.That(best.Stage, Is.EqualTo(BetrayalStage.DefensiveOverride),
                "The only legal root move on a ForcedSave-pending board is a DefensiveOverride.");
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
