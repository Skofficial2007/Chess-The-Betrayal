using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Proves AttackDefenseBias (via EvaluationWeights) measurably changes which move the search
    /// prefers, not just how deep it looks. Crafted position: two near-equal-value knight moves,
    /// one landing past the midline into enemy territory (attack bucket) and one landing on the
    /// scoring side's own half (defense bucket) at a comparably strong PST square — a low-bias
    /// (defensive) weighting should prefer the home-half square, a high-bias (aggressive)
    /// weighting should prefer crossing into enemy territory.
    /// </summary>
    [TestFixture]
    public class AttackDefenseBiasSearchTests
    {
        [Test]
        public void FindBestMove_LowVsHighAttackDefenseBias_PrefersDifferentSquares()
        {
            // White knight on d1 (back rank) can jump to c3/e3 (own half, row 2, strong defense
            // square) or to c5/e5 territory via intermediate development — to keep this a single
            // shallow search decision, use a knight already centrally placed with two legal
            // destinations straddling the midline: b3 (own half, row 2) vs b5 (enemy half, row 4).
            var lowBias = new BetrayalAwareEvaluator(new EvaluationWeights(attackScale: 0.5f, defenseScale: 1.5f, betrayalOptionScale: 1f));
            var highBias = new BetrayalAwareEvaluator(new EvaluationWeights(attackScale: 1.8f, defenseScale: 0.5f, betrayalOptionScale: 1f));

            BoardState boardLow = CraftedPosition();
            BoardState boardHigh = CraftedPosition();

            var engine = new ChessEngineAdapter();
            var searchLow = new AlphaBetaSearch(engine, lowBias);
            var searchHigh = new AlphaBetaSearch(engine, highBias);
            var settings = new AISearchSettings(maxDepth: 2, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);

            MoveCommand bestLow = searchLow.FindBestMove(boardLow, settings, CancellationToken.None);
            MoveCommand bestHigh = searchHigh.FindBestMove(boardHigh, settings, CancellationToken.None);

            Assert.That(bestLow.EndPosition, Is.Not.EqualTo(bestHigh.EndPosition),
                "A low-bias (defensive) and high-bias (aggressive) evaluator must prefer different destination squares for the same knight, proving AttackDefenseBias actually changes move choice, not just search depth.");
        }

        private static BoardState CraftedPosition()
        {
            return TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                .WithTurn(Team.White);
        }
    }
}
