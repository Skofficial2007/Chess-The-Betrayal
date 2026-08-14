using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the one property that makes MaterialOnlyEvaluator usable as a proof standard: it must
    /// be blind to everything the production evaluator has an opinion about. If this evaluator ever
    /// started reacting to pawn structure or king safety, every position proven with it would
    /// quietly become circular — proven correct by the same judgement being measured — and nothing
    /// would visibly break. These tests are the alarm for that.
    /// </summary>
    [TestFixture]
    public class MaterialOnlyEvaluatorTests
    {
        private static readonly MaterialOnlyEvaluator Evaluator = new MaterialOnlyEvaluator();

        [Test]
        public void Score_IsIdentical_ForWildlyDifferentPawnStructures()
        {
            // Same material for both sides in both positions: three white pawns and three black.
            // The only difference is structure — White's pawns are a healthy connected phalanx in
            // one and a tripled, isolated wreck in the other. The production evaluator separates
            // these by a wide margin; a material-only judgement must not tell them apart at all.
            BoardState healthy = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("b4", Team.White, ChessPieceType.Pawn)
                .WithPiece("c4", Team.White, ChessPieceType.Pawn)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

            BoardState wrecked = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b3", Team.White, ChessPieceType.Pawn)
                .WithPiece("b4", Team.White, ChessPieceType.Pawn)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

            Assert.That(Evaluator.Evaluate(wrecked, Team.White),
                Is.EqualTo(Evaluator.Evaluate(healthy, Team.White)),
                "Pawn structure changed the material-only score — this evaluator is no longer a neutral proof standard.");
        }

        [Test]
        public void Score_IsIdentical_ForASafeKingAndAnExposedOne()
        {
            // Identical material again. One white king sits behind an intact pawn shield; the other
            // is stripped bare with an enemy rook bearing down an open file. King safety is exactly
            // the kind of term a proof must not consult.
            BoardState sheltered = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithComputedHash();

            BoardState exposed = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.King)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a4", Team.White, ChessPieceType.Pawn)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithComputedHash();

            Assert.That(Evaluator.Evaluate(exposed, Team.White),
                Is.EqualTo(Evaluator.Evaluate(sheltered, Team.White)),
                "King exposure changed the material-only score — this evaluator is no longer a neutral proof standard.");
        }

        [Test]
        public void Score_IsIdentical_WhenOnlyTheKingsDistanceChanges()
        {
            // The bare-king mating scenario the king-approach term exists to reward. Moving the
            // attacking king from the far corner to right beside the defender is a large positional
            // gain and no material change whatsoever.
            BoardState distant = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("e5", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithComputedHash();

            BoardState closingIn = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e3", Team.White, ChessPieceType.King)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("e5", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithComputedHash();

            Assert.That(Evaluator.Evaluate(closingIn, Team.White),
                Is.EqualTo(Evaluator.Evaluate(distant, Team.White)),
                "King approach changed the material-only score — this evaluator is no longer a neutral proof standard.");
        }

        [Test]
        public void Score_IsIdentical_WhetherOrNotTheBetrayalRightIsStillAvailable()
        {
            // The production evaluator pays a bonus for holding an unspent Betrayal right. A proof
            // standard must not, or a position could be admitted on the strength of an option that
            // was never exercised in the proving line.
            BoardState withRight = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            BoardState withoutRight = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(false)
                .WithComputedHash();

            Assert.That(Evaluator.Evaluate(withoutRight, Team.White),
                Is.EqualTo(Evaluator.Evaluate(withRight, Team.White)),
                "The Betrayal option value leaked into the material-only score.");
        }

        [Test]
        public void Score_CountsMaterial_AndReportsItFromEachSidesPointOfView()
        {
            // The other half of the contract: being blind to position is only useful if it still
            // sees material correctly. White is up exactly a rook here.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Rook)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithComputedHash();

            Assert.That(Evaluator.Evaluate(board, Team.White), Is.EqualTo(500));
            Assert.That(Evaluator.Evaluate(board, Team.Black), Is.EqualTo(-500),
                "The score must negate cleanly for the other side, the convention the search's negamax frame depends on.");
        }

        [Test]
        public void CheapScore_MatchesTheFullScore_BecauseNoTermIsHeldBack()
        {
            // The interface allows a cheap score to be an approximation the full score refines.
            // Here there is nothing to refine, and callers may rely on that.
            BoardState board = TestBoardSetupUtility.CreateStandard();

            Assert.That(Evaluator.EvaluateCheap(board, Team.White),
                Is.EqualTo(Evaluator.Evaluate(board, Team.White)));
        }
    }
}
