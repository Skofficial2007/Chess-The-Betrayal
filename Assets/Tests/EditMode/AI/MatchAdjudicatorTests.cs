using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins MatchAdjudicator's own decision logic in isolation from any real search or engine —
    /// each test hand-drives a small sequence of positions/moves/scores and checks exactly when
    /// (and only when) an early-stopping verdict fires. MatchSimulatorTests separately proves the
    /// adjudicator is actually wired into a real game and shortens it.
    /// </summary>
    [TestFixture]
    public class MatchAdjudicatorTests
    {
        private static MoveCommand QuietKnightMove() =>
            MoveCommand.CreateStandardMove(
                new Vector2Int(1, 0), new Vector2Int(2, 2),
                new PieceData(Team.White, ChessPieceType.Knight, 1, 0, true));

        private static MoveCommand PawnMove() =>
            MoveCommand.CreateStandardMove(
                new Vector2Int(4, 1), new Vector2Int(4, 3),
                new PieceData(Team.White, ChessPieceType.Pawn, 1, 1, false));

        private static MoveCommand CaptureMove() =>
            MoveCommand.CreateStandardMove(
                new Vector2Int(2, 2), new Vector2Int(4, 3),
                new PieceData(Team.White, ChessPieceType.Knight, 1, 0, true),
                new PieceData(Team.Black, ChessPieceType.Pawn, -1, 6, true));

        private static BoardState BoardWithHash(ulong hash)
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty();
            board.ZobristHash = hash;
            return board;
        }

        [Test]
        public void RecordPly_SamePositionThreeTimes_AdjudicatesDraw()
        {
            var adjudicator = new MatchAdjudicator(AdjudicationRules.Standard);
            BoardState repeated = BoardWithHash(12345UL);

            adjudicator.RecordStartingPosition(repeated); // occurrence 1
            MatchOutcome? afterSecond = adjudicator.RecordPly(repeated, QuietKnightMove(), plyIndex: 0, scoreForWhiteCp: 0); // occurrence 2
            MatchOutcome? afterThird = adjudicator.RecordPly(repeated, QuietKnightMove(), plyIndex: 1, scoreForWhiteCp: 0); // occurrence 3

            Assert.That(afterSecond, Is.Null, "two occurrences of a position must not adjudicate — only the third does.");
            Assert.That(afterThird, Is.EqualTo(MatchOutcome.Draw));
        }

        [Test]
        public void RecordPly_DistinctPositionsEveryPly_NeverAdjudicatesOnRepetition()
        {
            var adjudicator = new MatchAdjudicator(AdjudicationRules.Standard);
            adjudicator.RecordStartingPosition(BoardWithHash(1UL));

            for (ulong hash = 2; hash <= 10; hash++)
            {
                MatchOutcome? result = adjudicator.RecordPly(BoardWithHash(hash), QuietKnightMove(), plyIndex: (int)hash, scoreForWhiteCp: 0);
                Assert.That(result, Is.Null, $"hash {hash} is seen for the first time — must never adjudicate on repetition alone.");
            }
        }

        [Test]
        public void RecordPly_FiftyMovePliesWithNoCaptureOrPawnMove_AdjudicatesDraw()
        {
            var rules = new AdjudicationRules(
                threefoldRepetitionCount: int.MaxValue, fiftyMoveRulePlies: 10, minPlyForScoreAdjudication: int.MaxValue,
                winAdjudicationMarginCp: int.MaxValue, winAdjudicationConsecutivePlies: int.MaxValue,
                drawAdjudicationMarginCp: -1, drawAdjudicationConsecutivePlies: int.MaxValue);
            var adjudicator = new MatchAdjudicator(rules);
            adjudicator.RecordStartingPosition(BoardWithHash(1UL));

            MatchOutcome? result = null;
            for (int ply = 0; ply < 10; ply++)
            {
                // A distinct hash per ply so repetition (disabled here anyway) can't be the cause.
                result = adjudicator.RecordPly(BoardWithHash((ulong)(ply + 2)), QuietKnightMove(), ply, scoreForWhiteCp: 0);
                if (result.HasValue) break;
            }

            Assert.That(result, Is.EqualTo(MatchOutcome.Draw));
        }

        [Test]
        public void RecordPly_CaptureResetsTheFiftyMoveCounter()
        {
            var rules = new AdjudicationRules(
                threefoldRepetitionCount: int.MaxValue, fiftyMoveRulePlies: 5, minPlyForScoreAdjudication: int.MaxValue,
                winAdjudicationMarginCp: int.MaxValue, winAdjudicationConsecutivePlies: int.MaxValue,
                drawAdjudicationMarginCp: -1, drawAdjudicationConsecutivePlies: int.MaxValue);
            var adjudicator = new MatchAdjudicator(rules);
            adjudicator.RecordStartingPosition(BoardWithHash(1UL));

            // 3 quiet plies (short of the 5-ply threshold), then a capture, then 3 more quiet
            // plies. If the capture didn't reset the counter, ply count 3+3=6 would exceed the
            // threshold of 5; since it does reset, the total quiet run after the capture is only 3.
            for (int ply = 0; ply < 3; ply++)
                Assert.That(adjudicator.RecordPly(BoardWithHash((ulong)(ply + 2)), QuietKnightMove(), ply, 0), Is.Null);

            Assert.That(adjudicator.RecordPly(BoardWithHash(100UL), CaptureMove(), 3, 0), Is.Null);

            MatchOutcome? result = null;
            for (int ply = 4; ply < 7; ply++)
            {
                result = adjudicator.RecordPly(BoardWithHash((ulong)(ply + 200)), QuietKnightMove(), ply, 0);
                if (result.HasValue) break;
            }

            Assert.That(result, Is.Null, "the capture must have reset the fifty-move counter — 3 quiet plies after it is not enough to trigger a 5-ply threshold.");
        }

        [Test]
        public void RecordPly_PawnMoveAlsoResetsTheFiftyMoveCounter()
        {
            var rules = new AdjudicationRules(
                threefoldRepetitionCount: int.MaxValue, fiftyMoveRulePlies: 3, minPlyForScoreAdjudication: int.MaxValue,
                winAdjudicationMarginCp: int.MaxValue, winAdjudicationConsecutivePlies: int.MaxValue,
                drawAdjudicationMarginCp: -1, drawAdjudicationConsecutivePlies: int.MaxValue);
            var adjudicator = new MatchAdjudicator(rules);
            adjudicator.RecordStartingPosition(BoardWithHash(1UL));

            Assert.That(adjudicator.RecordPly(BoardWithHash(2UL), PawnMove(), 0, 0), Is.Null);
            Assert.That(adjudicator.RecordPly(BoardWithHash(3UL), QuietKnightMove(), 1, 0), Is.Null);
            Assert.That(adjudicator.RecordPly(BoardWithHash(4UL), QuietKnightMove(), 2, 0), Is.Null,
                "only 2 quiet plies since the pawn move — must not yet hit a 3-ply threshold.");
        }

        [Test]
        public void RecordPly_LargeScoreBelowConsecutivePlyThreshold_DoesNotAdjudicate()
        {
            var rules = new AdjudicationRules(
                threefoldRepetitionCount: int.MaxValue, fiftyMoveRulePlies: int.MaxValue, minPlyForScoreAdjudication: 0,
                winAdjudicationMarginCp: 500, winAdjudicationConsecutivePlies: 8,
                drawAdjudicationMarginCp: -1, drawAdjudicationConsecutivePlies: int.MaxValue);
            var adjudicator = new MatchAdjudicator(rules);
            adjudicator.RecordStartingPosition(BoardWithHash(1UL));

            MatchOutcome? result = null;
            // Only 4 plies of a big White-favoring score — short of the 8-ply requirement.
            for (int ply = 0; ply < 4; ply++)
                result = adjudicator.RecordPly(BoardWithHash((ulong)(ply + 2)), QuietKnightMove(), ply, scoreForWhiteCp: 900);

            Assert.That(result, Is.Null,
                "a big score sustained for FEWER plies than the consecutive-ply requirement must not adjudicate — this pins that a merely-better-but-still-contested position is never mistaken for a decided one.");
        }

        [Test]
        public void RecordPly_LargeWhiteFavoringScoreSustained_AdjudicatesWhiteWon()
        {
            var rules = new AdjudicationRules(
                threefoldRepetitionCount: int.MaxValue, fiftyMoveRulePlies: int.MaxValue, minPlyForScoreAdjudication: 0,
                winAdjudicationMarginCp: 500, winAdjudicationConsecutivePlies: 4,
                drawAdjudicationMarginCp: -1, drawAdjudicationConsecutivePlies: int.MaxValue);
            var adjudicator = new MatchAdjudicator(rules);
            adjudicator.RecordStartingPosition(BoardWithHash(1UL));

            MatchOutcome? result = null;
            for (int ply = 0; ply < 4; ply++)
                result = adjudicator.RecordPly(BoardWithHash((ulong)(ply + 2)), QuietKnightMove(), ply, scoreForWhiteCp: 900);

            Assert.That(result, Is.EqualTo(MatchOutcome.WhiteWon));
        }

        [Test]
        public void RecordPly_LargeBlackFavoringScoreSustained_AdjudicatesBlackWon()
        {
            var rules = new AdjudicationRules(
                threefoldRepetitionCount: int.MaxValue, fiftyMoveRulePlies: int.MaxValue, minPlyForScoreAdjudication: 0,
                winAdjudicationMarginCp: 500, winAdjudicationConsecutivePlies: 4,
                drawAdjudicationMarginCp: -1, drawAdjudicationConsecutivePlies: int.MaxValue);
            var adjudicator = new MatchAdjudicator(rules);
            adjudicator.RecordStartingPosition(BoardWithHash(1UL));

            MatchOutcome? result = null;
            for (int ply = 0; ply < 4; ply++)
                result = adjudicator.RecordPly(BoardWithHash((ulong)(ply + 2)), QuietKnightMove(), ply, scoreForWhiteCp: -900);

            Assert.That(result, Is.EqualTo(MatchOutcome.BlackWon));
        }

        [Test]
        public void RecordPly_ScoreFlipsSidesMidStreak_ResetsTheStreakInstead_OfAdjudicating()
        {
            var rules = new AdjudicationRules(
                threefoldRepetitionCount: int.MaxValue, fiftyMoveRulePlies: int.MaxValue, minPlyForScoreAdjudication: 0,
                winAdjudicationMarginCp: 500, winAdjudicationConsecutivePlies: 3,
                drawAdjudicationMarginCp: -1, drawAdjudicationConsecutivePlies: int.MaxValue);
            var adjudicator = new MatchAdjudicator(rules);
            adjudicator.RecordStartingPosition(BoardWithHash(1UL));

            // White favored for 2 plies (short of 3), then Black favored for 2 plies (also short
            // of 3) — neither streak alone should reach the threshold if the flip properly resets
            // the counter rather than letting it keep accumulating across a sign change.
            adjudicator.RecordPly(BoardWithHash(2UL), QuietKnightMove(), 0, scoreForWhiteCp: 900);
            adjudicator.RecordPly(BoardWithHash(3UL), QuietKnightMove(), 1, scoreForWhiteCp: 900);
            MatchOutcome? afterFlip1 = adjudicator.RecordPly(BoardWithHash(4UL), QuietKnightMove(), 2, scoreForWhiteCp: -900);
            MatchOutcome? afterFlip2 = adjudicator.RecordPly(BoardWithHash(5UL), QuietKnightMove(), 3, scoreForWhiteCp: -900);

            Assert.That(afterFlip1, Is.Null);
            Assert.That(afterFlip2, Is.Null,
                "the streak must restart when the favored side changes — 2 White-favoring plies followed by 2 Black-favoring plies never reaches a 3-ply-same-side threshold.");
        }

        [Test]
        public void RecordPly_SmallScoreSustained_AdjudicatesDraw()
        {
            var rules = new AdjudicationRules(
                threefoldRepetitionCount: int.MaxValue, fiftyMoveRulePlies: int.MaxValue, minPlyForScoreAdjudication: 0,
                winAdjudicationMarginCp: int.MaxValue, winAdjudicationConsecutivePlies: int.MaxValue,
                drawAdjudicationMarginCp: 20, drawAdjudicationConsecutivePlies: 4);
            var adjudicator = new MatchAdjudicator(rules);
            adjudicator.RecordStartingPosition(BoardWithHash(1UL));

            MatchOutcome? result = null;
            for (int ply = 0; ply < 4; ply++)
                result = adjudicator.RecordPly(BoardWithHash((ulong)(ply + 2)), QuietKnightMove(), ply, scoreForWhiteCp: 5);

            Assert.That(result, Is.EqualTo(MatchOutcome.Draw));
        }

        [Test]
        public void RecordPly_BeforeMinPlyForScoreAdjudication_NeverAdjudicatesOnScoreAlone()
        {
            var rules = new AdjudicationRules(
                threefoldRepetitionCount: int.MaxValue, fiftyMoveRulePlies: int.MaxValue, minPlyForScoreAdjudication: 20,
                winAdjudicationMarginCp: 500, winAdjudicationConsecutivePlies: 2,
                drawAdjudicationMarginCp: 20, drawAdjudicationConsecutivePlies: 2);
            var adjudicator = new MatchAdjudicator(rules);
            adjudicator.RecordStartingPosition(BoardWithHash(1UL));

            MatchOutcome? result = null;
            for (int ply = 0; ply < 10; ply++)
                result = adjudicator.RecordPly(BoardWithHash((ulong)(ply + 2)), QuietKnightMove(), ply, scoreForWhiteCp: 900);

            Assert.That(result, Is.Null,
                "every ply here is below MinPlyForScoreAdjudication (20) — a huge sustained score must still not adjudicate this early.");
        }

        [Test]
        public void Disabled_NeverAdjudicatesAnything()
        {
            var adjudicator = new MatchAdjudicator(AdjudicationRules.Disabled);
            adjudicator.RecordStartingPosition(BoardWithHash(1UL));

            MatchOutcome? result = null;
            for (int ply = 0; ply < 200; ply++)
                result = adjudicator.RecordPly(BoardWithHash(1UL), QuietKnightMove(), ply, scoreForWhiteCp: 0);

            Assert.That(result, Is.Null,
                "AdjudicationRules.Disabled must be a genuine off switch — 200 plies of the same repeated position with zero progress must still never adjudicate.");
        }
    }
}
