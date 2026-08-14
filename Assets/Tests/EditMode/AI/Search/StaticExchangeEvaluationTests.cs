using NUnit.Framework;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI.Search
{
    /// <summary>
    /// A small hand-built swap-off suite (the same idea as the classic EPD SEE test sets used to
    /// validate chess engines) plus the one case specific to this codebase: a capture on a square
    /// where a Betrayer is pending, where the ordinary alternating-recapture assumption SEE relies
    /// on simply doesn't hold, and every call site must fall back to plain MVV-LVA instead.
    /// </summary>
    [TestFixture]
    public class StaticExchangeEvaluationTests
    {
        [Test]
        public void Evaluate_UndefendedCapture_ReturnsFullCapturedValue()
        {
            // White Rook takes a lone Black Knight with nothing defending it - straightforward win,
            // no recapture possible, so the result is exactly the Knight's value.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a4", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.Knight)
                .WithComputedHash();

            MoveCommand capture = BuildCapture(board, "a4", "a8", Team.White, ChessPieceType.Rook, ChessPieceType.Knight);

            Assert.That(StaticExchangeEvaluation.Evaluate(board, capture), Is.EqualTo(320));
        }

        [Test]
        public void Evaluate_PawnTakesPawnDefendedByPawn_ReturnsZero()
        {
            // White Pawn takes a Black Pawn that's defended by another Black Pawn of equal value -
            // the exchange nets nothing (100 won, 100 lost back), a textbook "even trade" case.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f6", Team.Black, ChessPieceType.Pawn)
                .WithComputedHash();

            MoveCommand capture = BuildCapture(board, "d4", "e5", Team.White, ChessPieceType.Pawn, ChessPieceType.Pawn);

            Assert.That(StaticExchangeEvaluation.Evaluate(board, capture), Is.EqualTo(0));
        }

        [Test]
        public void Evaluate_QueenTakesPawnDefendedByPawn_ReturnsLosingResult()
        {
            // White Queen captures a Black Pawn that's guarded by another Black Pawn - taking it
            // wins the Pawn but then loses the Queen to the recapture, a textbook SEE-rejects-the-
            // capture case: material result is strongly negative even though the first capture
            // "looks" like a free Pawn by naive MVV-LVA (queen captures a lower-ranked piece).
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Queen)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f6", Team.Black, ChessPieceType.Pawn)
                .WithComputedHash();

            MoveCommand capture = BuildCapture(board, "d4", "e5", Team.White, ChessPieceType.Queen, ChessPieceType.Pawn);

            // Wins the Pawn (100), then loses the Queen (975) to the recapture: 100 - 975 = -875.
            Assert.That(StaticExchangeEvaluation.Evaluate(board, capture), Is.EqualTo(-875));
        }

        [Test]
        public void Evaluate_RookBatteryBehindBishop_DiscoversXRayAttacker()
        {
            // White Bishop takes a Black Knight on d5; a Black Pawn recaptures on d5; but a White
            // Rook sits behind the Bishop on the same file (a1-d5 isn't a file, so use a straight
            // battery instead: Rook and Bishop share no ray - build the battery on an actual shared
            // ray). Correct SEE-battery setup: White Rook on d1, White Bishop... a Bishop can't
            // share the Rook's straight ray, so this proves the SAME-COLOR battery case with two
            // Rooks: White Rook d1 backs up nothing on its own, so instead prove the X-ray with a
            // Rook behind a Rook on the d-file - the front Rook captures, and if the defender
            // recaptures, the back Rook can then recapture in turn only because the front Rook has
            // vacated the file.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Rook)
                .WithPiece("d3", Team.White, ChessPieceType.Rook)
                .WithPiece("d5", Team.Black, ChessPieceType.Knight)
                .WithPiece("d7", Team.Black, ChessPieceType.Rook)
                .WithComputedHash();

            // Front Rook (d3) takes the Knight on d5. Black's Rook on d7 recaptures. White's back
            // Rook on d1 - now unblocked because d3 is empty - recaptures in turn. Without X-ray
            // discovery, the back Rook would never be found as a second attacker, and the exchange
            // would incorrectly stop after Black's recapture.
            MoveCommand capture = BuildCapture(board, "d3", "d5", Team.White, ChessPieceType.Rook, ChessPieceType.Knight);

            // Ply 0: White wins the Knight (320).
            // Ply 1: Black Rook recaptures the White Rook on d5 (500) - net so far 320 - 500 = -180
            //        from White's perspective, but Black "declines" is compared against this.
            // Ply 2: White's back Rook recaptures Black's Rook (500) - only possible via X-ray.
            // Final swap-off (best play both sides): White ends up winning the Knight and Black's
            // Rook while losing one Rook of its own: net = 320 (knight) + 500 (black rook) - 500
            // (white rook lost) = 320.
            Assert.That(StaticExchangeEvaluation.Evaluate(board, capture), Is.EqualTo(320));
        }

        [Test]
        public void IsApplicable_NoPendingBetrayer_ReturnsTrue()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a4", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.Knight)
                .WithComputedHash();

            MoveCommand capture = BuildCapture(board, "a4", "a8", Team.White, ChessPieceType.Rook, ChessPieceType.Knight);

            Assert.That(StaticExchangeEvaluation.IsApplicable(board, capture), Is.True);
        }

        [Test]
        public void IsApplicable_BetrayerPendingAnywhereOnBoard_ReturnsFalse()
        {
            // A Betrayer is pending on a totally different square (d4) from the capture being
            // evaluated (a4xa8) - IsApplicable must still return false, because the whole board is
            // mid a forced Betrayal sub-sequence, not just the pending square itself.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a4", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.Knight)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();

            MoveCommand capture = BuildCapture(board, "a4", "a8", Team.White, ChessPieceType.Rook, ChessPieceType.Knight);

            Assert.That(StaticExchangeEvaluation.IsApplicable(board, capture), Is.False);
        }

        [Test]
        public void IsApplicable_RetributionCaptureOnPendingSquare_ReturnsFalse()
        {
            // The capture under evaluation IS the Retribution move itself - an ally executing the
            // Betrayer. This is exactly the case the class doc warns about: "whoever's turn is
            // next" and "whoever benefits" have come apart, so ordinary SEE math would misjudge it.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();

            MoveCommand retribution = BuildCapture(board, "a1", "d4", Team.White, ChessPieceType.Rook, ChessPieceType.Knight)
                .WithStage(BetrayalStage.Retribution);

            Assert.That(StaticExchangeEvaluation.IsApplicable(board, retribution), Is.False);
        }

        [Test]
        public void IsApplicable_ActMove_ReturnsFalse()
        {
            // An Act isn't a material capture at all - it stages one. There is no exchange on the
            // target square yet, so there is nothing for the swap-off algorithm to be right about,
            // and scoring it as though there were would invent material that hasn't been won.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a4", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.Knight)
                .WithComputedHash();

            MoveCommand act = BuildCapture(board, "a4", "a8", Team.White, ChessPieceType.Rook, ChessPieceType.Knight)
                .WithStage(BetrayalStage.Act);

            Assert.That(StaticExchangeEvaluation.IsApplicable(board, act), Is.False);
        }

        [Test]
        public void CaptureOrdering_UsesExchangeResult_WhenNoBetrayerPending()
        {
            // The ordering nudge is only worth having if it actually separates two captures that
            // the coarse piece-rank comparison rates identically. Both moves below are Knight
            // takes Knight - an even trade by piece rank, so both land on exactly the same tier
            // score - but only one of them loses the Knight straight back to a defender.
            //
            // Equal captures specifically, because the nudge is deliberately confined to the
            // winning and equal capture bands: a losing capture sits close enough to the tiers
            // below it that even a small nudge could push it out of its own band.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("c3", Team.White, ChessPieceType.Knight)
                .WithPiece("a4", Team.Black, ChessPieceType.Knight)
                .WithPiece("f3", Team.White, ChessPieceType.Knight)
                .WithPiece("h4", Team.Black, ChessPieceType.Knight)
                .WithPiece("h5", Team.Black, ChessPieceType.Rook)
                .WithComputedHash();

            AlphaBetaSearch search = new AlphaBetaSearch(new ChessEngineAdapter(), new BetrayalAwareEvaluator());

            MoveCommand safeCapture = BuildCapture(board, "c3", "a4", Team.White, ChessPieceType.Knight, ChessPieceType.Knight);
            MoveCommand defendedCapture = BuildCapture(board, "f3", "h4", Team.White, ChessPieceType.Knight, ChessPieceType.Knight);

            int safeScore = search.OrderScoreForTest(safeCapture, ttMove: 0, plyFromRoot: -1, board: board);
            int defendedScore = search.OrderScoreForTest(defendedCapture, ttMove: 0, plyFromRoot: -1, board: board);

            Assert.That(safeScore, Is.GreaterThan(defendedScore),
                "A capture that survives the recapture should order ahead of one that loses the piece back.");
        }

        [Test]
        public void CaptureOrdering_FallsBackToPieceRank_WhenBetrayerPending()
        {
            // Same two captures as above, but with a Betrayer pending elsewhere on the board. The
            // exchange result can no longer be trusted (an ally, not the opponent, may be the one
            // recapturing), so both captures must fall back to the same coarse piece-rank score
            // rather than one being nudged ahead of the other on exchange math that doesn't apply.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("c3", Team.White, ChessPieceType.Knight)
                .WithPiece("a4", Team.Black, ChessPieceType.Knight)
                .WithPiece("f3", Team.White, ChessPieceType.Knight)
                .WithPiece("h4", Team.Black, ChessPieceType.Knight)
                .WithPiece("h5", Team.Black, ChessPieceType.Rook)
                .WithPiece("d5", Team.Black, ChessPieceType.Bishop)
                .WithPendingBetrayer("d5", Team.Black)
                .WithComputedHash();

            AlphaBetaSearch search = new AlphaBetaSearch(new ChessEngineAdapter(), new BetrayalAwareEvaluator());

            MoveCommand safeCapture = BuildCapture(board, "c3", "a4", Team.White, ChessPieceType.Knight, ChessPieceType.Knight);
            MoveCommand defendedCapture = BuildCapture(board, "f3", "h4", Team.White, ChessPieceType.Knight, ChessPieceType.Knight);

            int safeScore = search.OrderScoreForTest(safeCapture, ttMove: 0, plyFromRoot: -1, board: board);
            int defendedScore = search.OrderScoreForTest(defendedCapture, ttMove: 0, plyFromRoot: -1, board: board);

            Assert.That(safeScore, Is.EqualTo(defendedScore),
                "With a Betrayer pending, ordering must ignore the exchange result entirely.");
        }

        private static MoveCommand BuildCapture(BoardState board, string from, string to, Team team,
                                                  ChessPieceType pieceType, ChessPieceType capturedType)
        {
            Vector2Int start = Algebraic(from);
            Vector2Int end = Algebraic(to);
            PieceData piece = board.GetPiece(start.x, start.y);
            PieceData captured = board.GetPiece(end.x, end.y);

            Assert.That(piece.Type, Is.EqualTo(pieceType), "Test setup error: piece at 'from' doesn't match.");
            Assert.That(captured.Type, Is.EqualTo(capturedType), "Test setup error: piece at 'to' doesn't match.");

            return MoveCommand.CreateStandardMove(start, end, piece, captured, board);
        }

        private static Vector2Int Algebraic(string square)
        {
            int x = square[0] - 'a';
            int y = square[1] - '1';
            return new Vector2Int(x, y);
        }
    }
}
