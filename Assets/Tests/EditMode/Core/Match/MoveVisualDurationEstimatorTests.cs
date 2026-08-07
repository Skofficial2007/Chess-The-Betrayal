using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.Core.Match
{
    /// <summary>
    /// What the move pacing believes each animation costs. Nothing pinned these numbers before, and
    /// the capture figure had drifted a quarter of a second under what the strike actually plays —
    /// which does not fail anything, it just quietly lets the next move start while the last one is
    /// still finishing. So the assertions that matter here are the ones tying an estimate to the
    /// beats it is supposed to cover.
    /// </summary>
    [TestFixture]
    public class MoveVisualDurationEstimatorTests
    {
        // Measured off PrimeTweenPieceAnimator's stamp sequence: 0.09 anticipation, 0.30 leap,
        // 0.06 impact squash, 0.24 recovery, 0.10 settle bob.
        private const float MeasuredStrikeSeconds = 0.79f;

        private static PieceData Pawn() => new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1, hasMoved: true);
        private static PieceData Piece(ChessPieceType type) => new PieceData(Team.White, type, moveDirection: 1, startRow: 0, hasMoved: true);
        private static PieceData Enemy() => new PieceData(Team.Black, ChessPieceType.Pawn, moveDirection: -1, startRow: 6, hasMoved: true);

        private static Vector2Int Sq(int file, int rank) => new Vector2Int(file, rank);

        private static MoveCommand Quiet(Vector2Int from, Vector2Int to, ChessPieceType type = ChessPieceType.Rook)
            => MoveCommand.CreateStandardMove(from, to, Piece(type));

        private static MoveCommand Capture(Vector2Int from, Vector2Int to, ChessPieceType type)
            => MoveCommand.CreateStandardMove(from, to, Piece(type), Enemy());

        #region Captures

        [Test]
        public void ACaptureIsBudgetedForTheWholeStrikeAndNotJustPartOfIt()
        {
            // The defect this fixture exists for: the old flat 0.55s stopped before the recovery
            // overshoot and the settle bob had played, so the next move was released on top of them.
            float estimate = MoveVisualDurationEstimator.EstimateSeconds(Capture(Sq(4, 3), Sq(5, 4), ChessPieceType.Pawn));

            Assert.That(estimate, Is.GreaterThanOrEqualTo(MeasuredStrikeSeconds));
        }

        [Test]
        public void ACaptureFromAcrossTheBoardIsBudgetedForTheRunUpToo()
        {
            MoveCommand nextDoor = Capture(Sq(5, 3), Sq(6, 4), ChessPieceType.Bishop);
            MoveCommand acrossTheBoard = Capture(Sq(2, 0), Sq(6, 4), ChessPieceType.Bishop);

            Assert.That(MoveVisualDurationEstimator.EstimateSeconds(acrossTheBoard),
                Is.GreaterThan(MoveVisualDurationEstimator.EstimateSeconds(nextDoor)),
                "A bishop that walks in from c1 is on screen longer than one already beside its victim.");
        }

        [Test]
        public void ACaptureBesideItsVictimIsNotChargedForATravelLegItNeverPlays()
        {
            // A pawn taking the piece diagonally ahead strikes where it stands. Padding it for a
            // run-up would slow down the most common capture in the game for nothing.
            float estimate = MoveVisualDurationEstimator.EstimateSeconds(Capture(Sq(4, 3), Sq(5, 4), ChessPieceType.Pawn));

            Assert.That(estimate, Is.LessThan(MeasuredStrikeSeconds + 0.1f));
        }

        [Test]
        public void EnPassantIsNotHeldForAStompItNeverPlays()
        {
            // The attacker lands on an empty square beside its victim, so there is no strike to
            // wait out — only the victim's own glide off the board. Budgeting the full stomp here
            // left half a second of dead air after every en passant.
            MoveCommand enPassant = MoveCommand.CreateEnPassantMove(
                Sq(4, 4), Sq(5, 5), Pawn(), Enemy(), Sq(5, 4));

            Assert.That(MoveVisualDurationEstimator.EstimateSeconds(enPassant),
                Is.LessThan(MeasuredStrikeSeconds));
        }

        [Test]
        public void ACapturingPromotionIsNotHeldForAStompItNeverPlays()
        {
            // Same reasoning: the pawn is replaced the moment it arrives, so nothing is left to do
            // the stomping and the victim clears the square on its own.
            MoveCommand capturingPromotion = MoveCommand.CreatePromotionMove(
                Sq(4, 6), Sq(5, 7), Pawn(), ChessPieceType.Queen, Enemy());

            Assert.That(MoveVisualDurationEstimator.EstimateSeconds(capturingPromotion),
                Is.LessThan(MeasuredStrikeSeconds));
        }

        [Test]
        public void AKnightsCaptureIsNotChargedForARunUpItNeverPlays()
        {
            float knight = MoveVisualDurationEstimator.EstimateSeconds(Capture(Sq(1, 0), Sq(2, 2), ChessPieceType.Knight));
            float bishop = MoveVisualDurationEstimator.EstimateSeconds(Capture(Sq(0, 0), Sq(2, 2), ChessPieceType.Bishop));

            Assert.That(knight, Is.LessThan(bishop),
                "A knight leaps the whole way, so it owes only the strike.");
        }

        #endregion

        #region Everything else

        [Test]
        public void ALongQuietSlideIsBudgetedForLongerThanAShortOne()
        {
            float oneSquare = MoveVisualDurationEstimator.EstimateSeconds(Quiet(Sq(0, 0), Sq(0, 1)));
            float wholeBoard = MoveVisualDurationEstimator.EstimateSeconds(Quiet(Sq(0, 0), Sq(0, 7)));

            Assert.That(wholeBoard, Is.GreaterThan(oneSquare));
        }

        [Test]
        public void EveryMoveIsBudgetedForAtLeastAsLongAsItsGlideTakes()
        {
            // The estimate is a ceiling on the animation, so it can never come in under the travel
            // the animator is going to play for the same move.
            for (int squares = 1; squares <= 7; squares++)
            {
                MoveCommand move = Quiet(Sq(0, 0), Sq(0, squares));

                Assert.That(MoveVisualDurationEstimator.EstimateSeconds(move),
                    Is.GreaterThanOrEqualTo(MoveTravelTiming.SecondsForSquares(squares)),
                    $"A {squares}-square glide is budgeted for less time than it takes.");
            }
        }

        [Test]
        public void ACastleIsBudgetedForTheRookTrailingTheKingAndBothSettling()
        {
            MoveCommand castle = MoveCommand.CreateCastlingMove(
                Sq(4, 0), Sq(6, 0), Piece(ChessPieceType.King), Sq(7, 0), Sq(5, 0));

            // 0.06 stagger + 0.24 glide + 0.10 settle bob.
            Assert.That(MoveVisualDurationEstimator.EstimateSeconds(castle), Is.GreaterThanOrEqualTo(0.4f));
        }

        [Test]
        public void APromotionIsBudgetedForTheSwapNotJustTheStepOntoTheBackRank()
        {
            MoveCommand promotion = MoveCommand.CreatePromotionMove(
                Sq(4, 6), Sq(4, 7), Pawn(), ChessPieceType.Queen);

            // 0.12 collapsing out, then the longer of the 0.20 grow-in and the 0.22 pop hop.
            Assert.That(MoveVisualDurationEstimator.EstimateSeconds(promotion), Is.GreaterThanOrEqualTo(0.34f));
        }

        [Test]
        public void APromotionIsBudgetedForTheWalkOntoTheLastRankAsWellAsTheMorph()
        {
            // The pawn is seen arriving before it turns into anything, so the budget owes both
            // legs. Counting only the morph would release the next move while the pawn was still
            // walking.
            MoveCommand promotion = MoveCommand.CreatePromotionMove(
                Sq(4, 6), Sq(4, 7), Pawn(), ChessPieceType.Queen);

            Assert.That(MoveVisualDurationEstimator.EstimateSeconds(promotion),
                Is.GreaterThanOrEqualTo(MoveTravelTiming.SecondsForSquares(1) + 0.34f));
        }

        [Test]
        public void AMoveThatIsBothACaptureAndAPromotionIsBudgetedForWhicheverIsLonger()
        {
            MoveCommand capturingPromotion = MoveCommand.CreatePromotionMove(
                Sq(4, 6), Sq(5, 7), Pawn(), ChessPieceType.Queen, Enemy());

            float plainPromotion = MoveVisualDurationEstimator.EstimateSeconds(
                MoveCommand.CreatePromotionMove(Sq(4, 6), Sq(4, 7), Pawn(), ChessPieceType.Queen));

            Assert.That(MoveVisualDurationEstimator.EstimateSeconds(capturingPromotion),
                Is.GreaterThanOrEqualTo(plainPromotion),
                "Matching two shapes must never come out shorter than matching one of them.");
        }

        #endregion
    }
}
