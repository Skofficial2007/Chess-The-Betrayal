using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// BetrayalAwareEvaluator scales its non-material terms through an EvaluationWeights struct.
    /// This is the safety net proving that default/identity weights (every tier except Aggressive
    /// and Extreme, since their AttackDefenseBias == 1 and BetrayalAggression == 0) score
    /// bit-identically to an unweighted evaluator — weighting must be genuinely inert at identity,
    /// not merely close. Reuses EvaluatorTests.cs's exact positions plus one shelter-pawn position.
    /// </summary>
    [TestFixture]
    public class EvaluatorWeightsRegressionTests
    {
        private static BoardState SymmetricQueens() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen);

        private static BoardState MirroredRookKnight() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Rook)
                .WithPiece("a4", Team.White, ChessPieceType.Knight);

        private static BoardState ExtraQueen() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen);

        private static BoardState ShelteredKing() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d2", Team.White, ChessPieceType.Pawn)
                .WithPiece("e2", Team.White, ChessPieceType.Pawn)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn);

        private static void AssertIdenticalAcrossConstructions(BoardState board, Team perspective)
        {
            var bareCtor = new BetrayalAwareEvaluator();
            var explicitIdentity = new BetrayalAwareEvaluator(new EvaluationWeights(1f, 1f, 1f));
            var fromNormalProfile = new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(new AIProfileTableProvider().Resolve("normal"))); // bias=1, aggression=0

            int a = bareCtor.Evaluate(board, perspective);
            int b = explicitIdentity.Evaluate(board, perspective);
            int c = fromNormalProfile.Evaluate(board, perspective);

            Assert.That(b, Is.EqualTo(a), "Explicit identity weights must match the bare (no-arg) constructor exactly.");
            Assert.That(c, Is.EqualTo(a), "The 'normal' profile (bias=1, aggression=0) must map to identity weights exactly.");
        }

        [Test]
        public void SymmetricQueens_IdenticalAcrossConstructions()
        {
            BoardState board = SymmetricQueens();
            AssertIdenticalAcrossConstructions(board, Team.White);
            AssertIdenticalAcrossConstructions(board, Team.Black);
            Assert.That(new BetrayalAwareEvaluator().Evaluate(board, Team.White), Is.EqualTo(0));
        }

        [Test]
        public void MirroredRookKnight_IdenticalAcrossConstructions()
        {
            BoardState board = MirroredRookKnight();
            AssertIdenticalAcrossConstructions(board, Team.White);
            AssertIdenticalAcrossConstructions(board, Team.Black);
        }

        [Test]
        public void ExtraQueen_IdenticalAcrossConstructions()
        {
            BoardState board = ExtraQueen();
            AssertIdenticalAcrossConstructions(board, Team.White);
            Assert.That(new BetrayalAwareEvaluator().Evaluate(board, Team.White), Is.GreaterThan(900));
        }

        [Test]
        public void ShelteredKing_IdenticalAcrossConstructions()
        {
            BoardState board = ShelteredKing();
            AssertIdenticalAcrossConstructions(board, Team.White);
            AssertIdenticalAcrossConstructions(board, Team.Black);
        }
    }
}
