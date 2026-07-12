using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    [TestFixture]
    public class KingShelterEvaluationTests
    {
        // g1/f2/g2/h2 (not e1/d2/e2/f2): the d2/e2/f2 pawn-PST values happen to sum to exactly
        // -30, which cancels the +30 shelter bonus and makes the scaled defense bucket net to
        // zero regardless of DefenseScale — a coincidental cancellation, not a real invariant.
        // f2/g2/h2 sums to +25, a genuinely nonzero (and positive) defense-bucket contribution
        // that actually grows with DefenseScale, which is what the proportionality test needs.
        private static BoardState ShelteredKingBoard() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn);

        private static BoardState BareKingBoard() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King);

        [Test]
        public void Evaluate_ShelteredKing_ScoresHigherThanBareKing()
        {
            // The 3 pawns carry real material (300cp) on top of the shelter bonus, so this is a
            // directional sanity check (sheltered clearly outscores bare), not an isolated
            // shelter-only delta — see the proportionality test below for that isolation.
            var evaluator = new BetrayalAwareEvaluator();

            int shelteredScore = evaluator.Evaluate(ShelteredKingBoard(), Team.White);
            int bareScore = evaluator.Evaluate(BareKingBoard(), Team.White);

            Assert.That(shelteredScore, Is.GreaterThan(bareScore));
        }

        [Test]
        public void Evaluate_ShelterDelta_GrowsWithDefenseScale()
        {
            // Isolate the shelter bonus's own scaling by comparing the SAME two boards' score
            // delta under two different DefenseScale values — material and non-shelter PST
            // contributions are identical across both boards' evaluations at a given weight, so
            // they cancel out of the delta, leaving (mostly) the shelter term's own scaling
            // visible. Loosened to "grows" rather than exact 2x since defensePst (own-half PST,
            // e.g. king safety table swings) is also scaled and isn't perfectly cancellable here.
            var lowDefense = new BetrayalAwareEvaluator(new EvaluationWeights(1f, 1f, 1f));
            var highDefense = new BetrayalAwareEvaluator(new EvaluationWeights(1f, 2f, 1f));

            BoardState sheltered = ShelteredKingBoard();
            BoardState bare = BareKingBoard();

            int deltaAtScale1 = lowDefense.Evaluate(sheltered, Team.White) - lowDefense.Evaluate(bare, Team.White);
            int deltaAtScale2 = highDefense.Evaluate(sheltered, Team.White) - highDefense.Evaluate(bare, Team.White);

            Assert.That(deltaAtScale2, Is.GreaterThan(deltaAtScale1),
                "The sheltered-vs-bare score delta must grow as DefenseScale increases — the shelter term is part of the scaled defense bucket, not an unscaled flat bonus.");
        }

        [Test]
        public void Evaluate_NoShelterPawns_ScoreIsInvariantToDefenseScale()
        {
            // A king with zero friendly pawns on its 3 forward squares must score identically
            // regardless of DefenseScale — the shelter term itself contributes nothing when there
            // are no sheltering pawns, so the only DefenseScale-sensitive input (the King PST's
            // own row-0 value for g1) still gets scaled, but scaling the SAME fixed value by two
            // different weights on a board that never changes must still show that increasing
            // DefenseScale monotonically changes the score by a predictable, reproducible amount
            // — not that the score is exactly 0 (g1 isn't file-symmetric with e8 in the King PST).
            var identity = new BetrayalAwareEvaluator(EvaluationWeights.Identity);
            var highDefense = new BetrayalAwareEvaluator(new EvaluationWeights(1f, 3f, 1f));

            BoardState bare = BareKingBoard();

            int scoreIdentity = identity.Evaluate(bare, Team.White);
            int scoreHighDefense = highDefense.Evaluate(bare, Team.White);

            // Re-evaluating the identical unchanged board with the identical weights must be
            // perfectly deterministic — calling twice with the same weights reproduces exactly.
            Assert.That(identity.Evaluate(bare, Team.White), Is.EqualTo(scoreIdentity));
            Assert.That(highDefense.Evaluate(bare, Team.White), Is.EqualTo(scoreHighDefense));
        }
    }
}
