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

            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
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

            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
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

            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

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

            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            MoveCommand best = default;
            Assert.DoesNotThrow(() => best = _search.FindBestMove(board, settings, CancellationToken.None),
                "FindBestMove must not throw when handed a board that's already ForcedSave-pending on entry.");

            Assert.That(best.Stage, Is.EqualTo(BetrayalStage.DefensiveOverride),
                "The only legal root move on a ForcedSave-pending board is a DefensiveOverride.");
        }

        [Test]
        public void FindBestMove_ActNobodyCanAnswer_IsNotMistakenForStalemateDraw()
        {
            // White is slightly behind (knight + pawn vs rook). The knight on b1 can Act onto its
            // own a3 pawn, and NO White piece can then capture a3 — so the knight defects to Black,
            // leaving White down a knight AND the pawn it just betrayed. The search used to score
            // that empty-Retribution position as stalemate (0), which made the Act look like a free
            // draw to any side that was behind — the deeper the search, the better it got at
            // steering into that phantom escape hatch, which inverted the whole difficulty ladder.
            // Search must resolve the forced Defection and keep searching instead.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            // The wide rescore margin forces an exact full-window score for every root move, so the
            // Act's entry in RootScores below is its true value, not an alpha-beta bound.
            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None,
                candidateRescoreMarginCp: 2000);

            Assert.That(best.Stage, Is.Not.EqualTo(BetrayalStage.Act),
                "A losing side must not play an unanswerable Act in the belief it forces a draw.");

            int actIndex = -1;
            for (int i = 0; i < _search.RootMoveCount; i++)
            {
                if (_search.RootMoves[i].Stage == BetrayalStage.Act) actIndex = i;
            }

            Assert.That(actIndex, Is.GreaterThanOrEqualTo(0),
                "The b1xa3 Act must exist as a root move for this test to mean anything.");
            Assert.That(_search.RootScores[actIndex], Is.LessThan(-300),
                "The unanswerable Act loses the betrayed pawn and hands Black the defected knight — " +
                "its score must reflect that material reality, not read as a draw.");
            Assert.That(_search.Stats.ForcedDefectionResolutions, Is.GreaterThan(0),
                "The search must have explored the Act line by resolving the forced Defection in-line.");

            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "Resolving Defections inside the main search must keep the incremental hash consistent.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore),
                "The live board must be fully restored after searching through in-line Defection resolutions.");
        }

        [Test]
        public void FindBestMove_ActWhoseDefectionChecksOwnKing_ScoresTheForcedSaveContinuation()
        {
            // The self-check flavor of the same rule: the rook on e5 can Act onto its own e4 pawn
            // with no possible Retribution, and the defected (now Black) rook then checks the White
            // King along the open e-file — so White owes a mandatory king-save before play resumes.
            // The search must play out that whole forced continuation (Defection, then the save)
            // rather than scoring the empty-Retribution position as a draw.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e5", Team.White, ChessPieceType.Rook)
                .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            var settings = new AISearchSettings(maxDepth: 4, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            _search.FindBestMove(board, settings, CancellationToken.None, candidateRescoreMarginCp: 2000);

            int actIndex = -1;
            for (int i = 0; i < _search.RootMoveCount; i++)
            {
                if (_search.RootMoves[i].Stage == BetrayalStage.Act) actIndex = i;
            }

            Assert.That(actIndex, Is.GreaterThanOrEqualTo(0),
                "The e5xe4 Act must exist as a root move for this test to mean anything.");
            Assert.That(_search.RootScores[actIndex], Is.LessThan(-300),
                "Betraying the pawn gifts Black a whole rook (with check) — the Act's score must " +
                "reflect the ForcedSave continuation, not read as a draw.");
            Assert.That(_search.Stats.ForcedDefectionResolutions, Is.GreaterThan(0),
                "The search must have resolved the self-checking Defection in-line to score this line.");

            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "The Defection + king-save make/unmake inside the main search must keep the hash consistent.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore),
                "The live board must be fully restored after the ForcedSave continuation unwinds.");
        }

        [Test]
        public void FindBestMove_RootScoresExactForSelection_ReportsWhenCandidateScoresAreTrustworthy()
        {
            // Non-best root scores start life as tightened alpha-beta bounds, and a bound can sit
            // arbitrarily close to the best score while its move is far worse — so anything that
            // picks among "near-best" candidates (tie-break windows, deliberate blunders) must
            // know whether the exact-rescore pass actually ran. Trusting bounds is how a
            // time-capped profile once ended up playing near-random moves: its budget expired
            // before the rescore pass, and the selection window happily treated leftover bounds
            // as real scores.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            // Uncancelled search with a rescore margin: the pass completes, scores are exact.
            _search.FindBestMove(board, settings, CancellationToken.None, candidateRescoreMarginCp: 100);
            Assert.That(_search.RootScoresExactForSelection, Is.True,
                "An uncancelled search that requested a rescore margin must report exact candidate scores.");

            // No rescore margin requested: non-best scores stay bounds, and must be reported as such.
            _search.FindBestMove(board, settings, CancellationToken.None);
            Assert.That(_search.RootScoresExactForSelection, Is.False,
                "Without a rescore margin the non-best root scores are alpha-beta bounds — never exact.");

            // Budget already spent (the token fired before the search could even start): whatever
            // scores are left over must never be presented as selection-worthy.
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                _search.FindBestMove(board, settings, cts.Token, candidateRescoreMarginCp: 100);
                Assert.That(_search.RootScoresExactForSelection, Is.False,
                    "A cancelled search cannot have rescored its candidates — selection must fall back to the best move.");
            }
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

            var settings = new AISearchSettings(maxDepth: 2, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.DefendOnly);
            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.Stage, Is.Not.EqualTo(BetrayalStage.Act),
                "DefendOnly must never let the agent choose an Act move as its own root decision.");
        }
    }
}
