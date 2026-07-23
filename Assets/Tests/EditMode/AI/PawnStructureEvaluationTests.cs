using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Passed/isolated/doubled pawn scoring straddles the same attack/defense bucket boundary
    /// PieceSquareTables' King table does — a passed pawn's bonus grows as it advances past the
    /// midline, so every positional assertion here uses PawnStructure.Score directly (which has no
    /// personality-weight scaling to begin with) rather than the fully-weighted Evaluate, isolating
    /// the term itself from both material/PST noise and any attack/defense dial.
    /// </summary>
    [TestFixture]
    public class PawnStructureEvaluationTests
    {
        private static int NetScore(BoardState board, Team team)
        {
            PawnStructure.Score(board, team, out int attack, out int defense);
            return attack + defense;
        }

        [Test]
        public void PassedPawn_CloserToPromotion_ScoresHigherThanFurtherBack()
        {
            BoardState earlyPassedPawn = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e3", Team.White, ChessPieceType.Pawn);

            BoardState advancedPassedPawn = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e6", Team.White, ChessPieceType.Pawn);

            int earlyScore = NetScore(earlyPassedPawn, Team.White);
            int advancedScore = NetScore(advancedPassedPawn, Team.White);

            Assert.That(advancedScore, Is.GreaterThan(earlyScore));
        }

        [Test]
        public void PassedPawn_BlockedByAnEnemyPawnOnItsOwnFileAhead_IsNotRewarded()
        {
            BoardState blocked = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e6", Team.White, ChessPieceType.Pawn)
                .WithPiece("e7", Team.Black, ChessPieceType.Pawn);

            BoardState clear = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e6", Team.White, ChessPieceType.Pawn);

            int blockedScore = NetScore(blocked, Team.White);
            int clearScore = NetScore(clear, Team.White);

            Assert.That(blockedScore, Is.LessThan(clearScore),
                "An enemy pawn directly ahead on the same file must deny the passed bonus.");
        }

        [Test]
        public void PassedPawn_BlockedByAnEnemyPawnOnAnAdjacentFileAhead_IsNotRewarded()
        {
            BoardState blockedByAdjacentFile = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e6", Team.White, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn);

            BoardState blockedDirectly = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e6", Team.White, ChessPieceType.Pawn)
                .WithPiece("e7", Team.Black, ChessPieceType.Pawn);

            // Both boards deny the passed bonus (adjacent-file rule); scores should match since
            // neither pawn is isolated/doubled differently and neither White pawn count changes.
            Assert.That(NetScore(blockedByAdjacentFile, Team.White), Is.EqualTo(NetScore(blockedDirectly, Team.White)));
        }

        [Test]
        public void IsolatedPawn_ScoresLowerThanTheSamePawnWithAFriendlyNeighbor()
        {
            BoardState isolated = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn);

            BoardState notIsolated = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("d3", Team.White, ChessPieceType.Pawn); // supports c2's file, further back so it can't itself be blocked/doubled with c2

            // c2's own contribution only: subtract d3's contribution (computed independently on a
            // board where d3 is alone, so it carries no isolation/doubling interaction with c2).
            BoardState d3Alone = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d3", Team.White, ChessPieceType.Pawn);

            int c2IsolatedContribution = NetScore(isolated, Team.White);
            int combinedContribution = NetScore(notIsolated, Team.White);
            int d3AloneContribution = NetScore(d3Alone, Team.White);
            int c2WithNeighborContribution = combinedContribution - d3AloneContribution;

            Assert.That(c2IsolatedContribution, Is.LessThan(c2WithNeighborContribution),
                "A lone pawn with no friendly neighbor on an adjacent file must be penalized " +
                "relative to the same pawn once it has one.");
        }

        [Test]
        public void DoubledPawns_ScoreLowerThanTheSameCountSpreadAcrossFiles()
        {
            BoardState doubled = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("c3", Team.White, ChessPieceType.Pawn);

            BoardState spread = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("f3", Team.White, ChessPieceType.Pawn); // far enough that neither pawn supports the other's isolation either

            Assert.That(NetScore(doubled, Team.White), Is.LessThan(NetScore(spread, Team.White)),
                "Two pawns sharing a file must score lower than the same two pawns on separate, unrelated files.");
        }

        [Test]
        public void DefectedPawn_IsScoredOnItsNewTeamsStructure_NotItsOldOnes()
        {
            // A lone Black pawn on e5, isolated and passed from Black's point of view (no White
            // pawn anywhere near the d/e/f files). Defect it to White mid-test and confirm the very
            // next evaluation call scores it on WHITE's structure — proving pawn membership is read
            // live off the board every call, never from a structure snapshot taken before the flip.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithComputedHash();

            int blackScoreBefore = NetScore(board, Team.Black);
            int whiteScoreBefore = NetScore(board, Team.White);
            Assert.That(blackScoreBefore, Is.GreaterThan(0), "Before defection, Black's own passed pawn must score in Black's favor.");
            Assert.That(whiteScoreBefore, Is.EqualTo(0), "White has no pawns yet, so White's own pawn score must be zero.");

            board.DefectPiece(new Vector2Int(4, 4)); // e5

            int blackScoreAfter = NetScore(board, Team.Black);
            int whiteScoreAfter = NetScore(board, Team.White);

            Assert.That(blackScoreAfter, Is.EqualTo(0), "After losing the pawn, Black must have no pawn-structure score left.");
            Assert.That(whiteScoreAfter, Is.GreaterThan(0), "The defected pawn must now score on WHITE's structure.");
        }

        [Test]
        public void PawnlessPosition_ScoresIdenticallyBetweenCheapAndFull()
        {
            // No pawn term exists to add with no pawns on the board, so pawn structure alone
            // contributes nothing here. King safety (also full-only, AI-53) is NOT automatically
            // zero on a pawnless board -- open files are about missing PAWNS specifically, and both
            // bare kings on this mirrored setup are equally exposed on all three of their own files,
            // so their king-safety deltas cancel by symmetry rather than by having nothing to add.
            // KingSafetyEvaluationTests covers the asymmetric case where they do NOT cancel.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen);

            var evaluator = new BetrayalAwareEvaluator();

            Assert.That(evaluator.Evaluate(board, Team.White), Is.EqualTo(evaluator.EvaluateCheap(board, Team.White)));
            Assert.That(evaluator.Evaluate(board, Team.Black), Is.EqualTo(evaluator.EvaluateCheap(board, Team.Black)));
        }
    }
}
