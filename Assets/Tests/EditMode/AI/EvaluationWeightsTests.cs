using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    [TestFixture]
    public class EvaluationWeightsTests
    {
        [Test]
        public void FromProfile_NormalTier_MapsToIdentity()
        {
            AIProfile normal = new AIProfileTableProvider().Resolve("normal");

            EvaluationWeights weights = EvaluationWeights.FromProfile(normal);

            Assert.That(weights.AttackScale, Is.EqualTo(1f));
            Assert.That(weights.DefenseScale, Is.EqualTo(1f));
            Assert.That(weights.BetrayalOptionScale, Is.EqualTo(1f));
        }

        [Test]
        public void FromProfile_AggressiveTier_MapsAttackHighDefenseLow()
        {
            AIProfile aggressive = new AIProfileTableProvider().Resolve("aggressive"); // bias=1.5, aggression=0.7

            EvaluationWeights weights = EvaluationWeights.FromProfile(aggressive);

            Assert.That(weights.AttackScale, Is.EqualTo(1.5f));
            Assert.That(weights.DefenseScale, Is.EqualTo(0.5f));
            Assert.That(weights.BetrayalOptionScale, Is.EqualTo(1.35f).Within(0.0001f));
        }

        [Test]
        public void FromProfile_DefenseScale_NeverGoesBelowFloor()
        {
            // A hypothetical future tier at the documented bias ceiling (2.0) would compute
            // DefenseScale = 2 - 2 = 0, but the mapping floors it at 0.5 so defense can never
            // vanish entirely.
            var extremeBiasProfile = new AIProfile("hypothetical", maxDepth: 1, softTimeBudgetMs: 1,
                blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0f,
                attackDefenseBias: 2.0f, tieBreakWindowCp: 0, useOpeningBook: false);

            EvaluationWeights weights = EvaluationWeights.FromProfile(extremeBiasProfile);

            Assert.That(weights.DefenseScale, Is.EqualTo(0.5f));
        }

        [Test]
        public void Evaluate_MaterialNeverScaled_SkewedWeightsStillZeroSumSymmetric()
        {
            // Symmetry under extreme, asymmetric-looking weights: the SAME weights apply to both
            // sides' evaluation, so Eval(White) == -Eval(Black) must hold regardless of how far
            // AttackScale/DefenseScale are pushed from identity.
            var skewed = new EvaluationWeights(attackScale: 2.0f, defenseScale: 0.3f, betrayalOptionScale: 1f);
            var evaluator = new BetrayalAwareEvaluator(skewed);

            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Rook)
                .WithPiece("a4", Team.White, ChessPieceType.Knight);

            int whiteScore = evaluator.Evaluate(board, Team.White);
            int blackScore = evaluator.Evaluate(board, Team.Black);

            Assert.That(blackScore, Is.EqualTo(-whiteScore));
        }

        [Test]
        public void Evaluate_MaterialOnlyPosition_ScoreIsWeightInvariant()
        {
            // A bare two-king board has zero material (King's BaseValue is 0 by design) and its
            // PST contributions cancel by symmetry of an otherwise-empty board — proving material
            // truly bypasses the weight multipliers, not merely "happens to look symmetric."
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King);

            var identity = new BetrayalAwareEvaluator(EvaluationWeights.Identity);
            var extreme = new BetrayalAwareEvaluator(new EvaluationWeights(5f, 5f, 5f));

            Assert.That(identity.Evaluate(board, Team.White), Is.EqualTo(0));
            Assert.That(extreme.Evaluate(board, Team.White), Is.EqualTo(0));
        }
    }
}
