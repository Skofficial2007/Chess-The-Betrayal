using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The King piece-square table now blends a midgame and an endgame value by MaterialPhase's
    /// weight. EvaluatorWeightsRegressionTests.GoldenScores_AtFullMaterial_MatchCapturedBaseline
    /// is the real bit-identity pin at full phase (every fixture there still uses full or
    /// near-full material, so the blend reads at or near the midgame end) — this fixture covers
    /// what that one can't: that the endgame end of the blend actually rewards centralization,
    /// and that it does so at identity weights specifically, since the King's central squares
    /// straddle the attack/defense bucket boundary and a personality dial would amplify one side
    /// of that boundary over the other.
    /// </summary>
    [TestFixture]
    public class TaperedEvaluationTests
    {
        [Test]
        public void CentralizedKing_InAKingAndPawnEndgame_ScoresHigherThanABackRankKing()
        {
            // Bare-bones king-and-pawn endgame: both sides down to a king and one pawn each, well
            // inside MaterialPhase's zero-weight (endgame) end. Identity weights only — Aggressive/
            // Extreme's AttackDefenseBias would amplify d4 (attack bucket) differently from d1/e1
            // (defense bucket), which would measure the personality dial, not the taper.
            var evaluator = new BetrayalAwareEvaluator();

            BoardState backRankKing = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn);

            BoardState centralizedKing = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.King)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn);

            int backRankScore = evaluator.Evaluate(backRankKing, Team.White);
            int centralizedScore = evaluator.Evaluate(centralizedKing, Team.White);

            Assert.That(centralizedScore, Is.GreaterThan(backRankScore));
        }

        [Test]
        public void CentralizedKing_AtFullMaterial_DoesNotOutscoreABackRankKing()
        {
            // The same king move, but at full material on both sides — the blend should sit close
            // enough to the midgame table (which still penalizes the center) that walking the king
            // out is not rewarded. Confirms the blend is actually reading the phase, not just
            // always applying the endgame table.
            var evaluator = new BetrayalAwareEvaluator();

            BoardState backRankKing = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("g1", Team.White, ChessPieceType.Knight)
                .WithPiece("c1", Team.White, ChessPieceType.Bishop)
                .WithPiece("f1", Team.White, ChessPieceType.Bishop)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook)
                .WithPiece("b8", Team.Black, ChessPieceType.Knight)
                .WithPiece("g8", Team.Black, ChessPieceType.Knight)
                .WithPiece("c8", Team.Black, ChessPieceType.Bishop)
                .WithPiece("f8", Team.Black, ChessPieceType.Bishop)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen);

            BoardState centralizedKing = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d4", Team.White, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("g1", Team.White, ChessPieceType.Knight)
                .WithPiece("c1", Team.White, ChessPieceType.Bishop)
                .WithPiece("f1", Team.White, ChessPieceType.Bishop)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook)
                .WithPiece("b8", Team.Black, ChessPieceType.Knight)
                .WithPiece("g8", Team.Black, ChessPieceType.Knight)
                .WithPiece("c8", Team.Black, ChessPieceType.Bishop)
                .WithPiece("f8", Team.Black, ChessPieceType.Bishop)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen);

            int backRankScore = evaluator.Evaluate(backRankKing, Team.White);
            int centralizedScore = evaluator.Evaluate(centralizedKing, Team.White);

            Assert.That(centralizedScore, Is.LessThan(backRankScore));
        }

        [Test]
        public void MidgameKingTable_AtFullPhaseWeight_MatchesTheOriginalTableEntryForEntry()
        {
            // Table-level pin, independent of any board: at phaseWeight == FullPhaseWeight the
            // blend must return EXACTLY the pre-taper table's values, entry for entry — this is
            // the guarantee the whole-evaluator golden-score pin in EvaluatorWeightsRegressionTests
            // depends on. Values copied verbatim from PieceSquareTables.King (the original, still
            // midgame-only table) so a future edit to the array has to also update this pin
            // deliberately, not drift silently.
            int[] originalKingMidgameTable =
            {
                 20,  30,  10,   0,   0,  10,  30,  20,
                 20,  20,   0,   0,   0,   0,  20,  20,
                -10, -20, -20, -20, -20, -20, -20, -10,
                -20, -30, -30, -40, -40, -30, -30, -20,
                -30, -40, -40, -50, -50, -40, -40, -30,
                -30, -40, -40, -50, -50, -40, -40, -30,
                -30, -40, -40, -50, -50, -40, -40, -30,
                -30, -40, -40, -50, -50, -40, -40, -30,
            };

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int blended = PieceSquareTables.Bonus(
                        ChessPieceType.King, x, y, Team.White, 8, 8, MaterialPhase.FullPhaseWeight);
                    int expected = originalKingMidgameTable[(y * 8) + x];

                    Assert.That(blended, Is.EqualTo(expected), $"square ({x},{y})");
                }
            }
        }

        [Test]
        public void EndgameKingTable_AtZeroPhaseWeight_RewardsCentralizationOverCorner()
        {
            // Table-level pin for the endgame end of the blend, independent of any board or
            // material bucketing: d4/e4/d5/e5 (the center) must score higher than a1/h1/a8/h8
            // (the corners) at phaseWeight == 0.
            int centerScore = PieceSquareTables.Bonus(ChessPieceType.King, 3, 3, Team.White, 8, 8, 0);
            int cornerScore = PieceSquareTables.Bonus(ChessPieceType.King, 0, 0, Team.White, 8, 8, 0);

            Assert.That(centerScore, Is.GreaterThan(cornerScore));
        }
    }
}
