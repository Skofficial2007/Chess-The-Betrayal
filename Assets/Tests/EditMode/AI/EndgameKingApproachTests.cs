using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Every assertion calls EndgameKingApproach.Score directly, the same isolation
    /// KingSafetyEvaluationTests/PawnStructureEvaluationTests use — BetrayalAwareEvaluator still
    /// scales the result by AttackScale before adding it to the total.
    /// </summary>
    [TestFixture]
    public class EndgameKingApproachTests
    {
        [Test]
        public void BareEnemyKing_CloserAttackingKing_ScoresHigherThanFarther()
        {
            BoardState far = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("b3", Team.White, ChessPieceType.Queen)
                .WithPiece("e5", Team.Black, ChessPieceType.King);
            BoardState close = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.King)
                .WithPiece("b3", Team.White, ChessPieceType.Queen)
                .WithPiece("e5", Team.Black, ChessPieceType.King);

            Assert.That(EndgameKingApproach.Score(close, Team.White), Is.GreaterThan(EndgameKingApproach.Score(far, Team.White)));
        }

        [Test]
        public void BareEnemyKing_AdjacentAttackingKing_ScoresTheHighestReachableValue()
        {
            // Chebyshev distance 1 (adjacent) is the closest two kings can ever legally stand -- they
            // can never share a square -- so this is the highest value the term can actually produce
            // in a real game, even though MaxKingApproachPerSide (the clamp used for
            // MaxPositionalSwing's bound) is deliberately the theoretical distance-0 ceiling, never
            // reached in practice. The bound only needs to be an upper limit, not a tight one.
            BoardState adjacent = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Queen)
                .WithPiece("d5", Team.Black, ChessPieceType.King); // Chebyshev distance 1

            int score = EndgameKingApproach.Score(adjacent, Team.White);
            Assert.That(score, Is.LessThanOrEqualTo(EndgameKingApproach.MaxKingApproachPerSide));
            Assert.That(score, Is.GreaterThan(EndgameKingApproach.Score(
                TestBoardSetupUtility.CreateEmpty()
                    .WithPiece("a1", Team.White, ChessPieceType.King)
                    .WithPiece("b3", Team.White, ChessPieceType.Queen)
                    .WithPiece("e5", Team.Black, ChessPieceType.King), // farther apart
                Team.White)));
        }

        [Test]
        public void EnemyHoldsNonPawnMaterial_ScoresZero_EvenWithMatingMaterialOfItsOwn()
        {
            // Both sides still have real material on the board -- this is an ordinary middlegame-
            // shaped position, not a bare-king mating scenario, so the term must stay silent even
            // though White's own king happens to be close to Black's.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Queen)
                .WithPiece("d5", Team.Black, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook);

            Assert.That(EndgameKingApproach.Score(board, Team.White), Is.EqualTo(0));
        }

        [Test]
        public void ScoringSideHasNoNonPawnMaterial_ScoresZero_EvenAgainstABareKing()
        {
            // A King+Pawn race: neither side gets a king-approach bonus purely from being close to
            // the other king, since neither side holds mating material to finish a chase with -- the
            // relevant lever there is pawn promotion, not king proximity, and this term must not
            // interfere with it.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a5", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King);

            Assert.That(EndgameKingApproach.Score(board, Team.White), Is.EqualTo(0));
        }

        [Test]
        public void DefectedMatingPiece_ScoresOnItsCurrentTeam_NotItsOriginalTeam()
        {
            // The rook started on Black's side of a spent Betrayal right and DefectPiece flipped it
            // to White mid-setup -- the term must read live team membership, not a cached/original
            // army, the same correctness bar every other Defection-aware term in this codebase holds.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.King)
                .WithPiece("b3", Team.Black, ChessPieceType.Rook)
                .WithPiece("d5", Team.Black, ChessPieceType.King)
                .WithBetrayalRight(false);
            board.DefectPiece(TestBoardSetupUtility.AlgebraicToVector("b3"));
            board.ComputeFullZobristHash();

            Assert.That(EndgameKingApproach.Score(board, Team.White), Is.GreaterThan(0),
                "White now owns the defected rook and Black is a bare king -- White should score a king-approach bonus.");
            Assert.That(EndgameKingApproach.Score(board, Team.Black), Is.EqualTo(0),
                "Black no longer owns the rook and has no mating material of its own -- Black must not score an approach bonus.");
        }

        [Test]
        public void Score_KingNotFound_ReturnsZero()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.Black, ChessPieceType.Queen);

            Assert.That(EndgameKingApproach.Score(board, Team.White), Is.EqualTo(0));
        }
    }
}
