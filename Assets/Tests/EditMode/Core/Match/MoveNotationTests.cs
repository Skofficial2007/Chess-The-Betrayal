using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Match
{
    /// <summary>
    /// MoveNotation is the single source of truth for "what actually happened this ply" in bug
    /// reports and replay dumps — if its formatting silently drifts (wrong square, wrong move
    /// number, a misleading "e5-e5" for a same-square Defection), a bug report built from it
    /// becomes actively misleading instead of just incomplete. These tests pin the exact string
    /// output for every move shape the formatter branches on.
    /// </summary>
    [TestFixture]
    public class MoveNotationTests
    {
        [Test]
        public void Format_WhiteStandardMove_UsesDotSeparatorAndDash()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece("e2", Team.White, ChessPieceType.Pawn);
            PieceData pawn = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e2"));
            MoveCommand move = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e2"), TestBoardSetupUtility.AlgebraicToVector("e4"), pawn, default, board);

            string result = MoveNotation.Format(move, 1);

            Assert.AreEqual("1. e2-e4", result);
        }

        [Test]
        public void Format_BlackStandardMove_UsesEllipsisSeparator()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece("e7", Team.Black, ChessPieceType.Pawn);
            PieceData pawn = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e7"));
            MoveCommand move = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e7"), TestBoardSetupUtility.AlgebraicToVector("e5"), pawn, default, board);

            string result = MoveNotation.Format(move, 1);

            Assert.AreEqual("1... e7-e5", result, "Black's half of a full move must render with the ellipsis, not a period.");
        }

        [Test]
        public void Format_PieceMove_PrefixesWithPieceLetter()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece("g1", Team.White, ChessPieceType.Knight);
            PieceData knight = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("g1"));
            MoveCommand move = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("g1"), TestBoardSetupUtility.AlgebraicToVector("f3"), knight, default, board);

            string result = MoveNotation.Format(move, 1);

            Assert.AreEqual("1. Ng1-f3", result);
        }

        [Test]
        public void Format_PawnMove_OmitsPieceLetter()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece("e2", Team.White, ChessPieceType.Pawn);
            PieceData pawn = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e2"));
            MoveCommand move = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e2"), TestBoardSetupUtility.AlgebraicToVector("e4"), pawn, default, board);

            string result = MoveNotation.Format(move, 1);

            StringAssert.DoesNotContain("P", result.Replace("1. ", ""), "Pawn moves must never carry a piece letter, per SAN convention.");
        }

        [Test]
        public void Format_Capture_UsesXSeparator()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithPiece("d5", Team.Black, ChessPieceType.Pawn);
            PieceData attacker = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e4"));
            PieceData victim = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("d5"));
            MoveCommand move = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e4"), TestBoardSetupUtility.AlgebraicToVector("d5"), attacker, victim, board);

            string result = MoveNotation.Format(move, 3);

            Assert.AreEqual("3. e4xd5", result);
        }

        [Test]
        public void Format_KingsideCastling_RendersOO()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece("e1", Team.White, ChessPieceType.King);
            PieceData king = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e1"));
            MoveCommand move = MoveCommand.CreateCastlingMove(
                TestBoardSetupUtility.AlgebraicToVector("e1"), TestBoardSetupUtility.AlgebraicToVector("g1"), king,
                TestBoardSetupUtility.AlgebraicToVector("h1"), TestBoardSetupUtility.AlgebraicToVector("f1"), board);

            string result = MoveNotation.Format(move, 5);

            Assert.AreEqual("5. O-O", result);
        }

        [Test]
        public void Format_QueensideCastling_RendersOOO()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece("e1", Team.White, ChessPieceType.King);
            PieceData king = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e1"));
            MoveCommand move = MoveCommand.CreateCastlingMove(
                TestBoardSetupUtility.AlgebraicToVector("e1"), TestBoardSetupUtility.AlgebraicToVector("c1"), king,
                TestBoardSetupUtility.AlgebraicToVector("a1"), TestBoardSetupUtility.AlgebraicToVector("d1"), board);

            string result = MoveNotation.Format(move, 5);

            Assert.AreEqual("5. O-O-O", result);
        }

        [Test]
        public void Format_EnPassant_AppendsEpMarker()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e5", Team.White, ChessPieceType.Pawn)
                .WithPiece("d5", Team.Black, ChessPieceType.Pawn);
            PieceData pawn = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e5"));
            PieceData captured = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("d5"));
            MoveCommand move = MoveCommand.CreateEnPassantMove(
                TestBoardSetupUtility.AlgebraicToVector("e5"), TestBoardSetupUtility.AlgebraicToVector("d6"),
                pawn, captured, TestBoardSetupUtility.AlgebraicToVector("d5"), board);

            string result = MoveNotation.Format(move, 4);

            Assert.AreEqual("4. e5xd6 e.p.", result);
        }

        [Test]
        public void Format_Promotion_AppendsPromotedPieceLetter()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece("e7", Team.White, ChessPieceType.Pawn);
            PieceData pawn = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e7"));
            MoveCommand move = MoveCommand.CreatePromotionMove(
                TestBoardSetupUtility.AlgebraicToVector("e7"), TestBoardSetupUtility.AlgebraicToVector("e8"),
                pawn, ChessPieceType.Queen, default, board);

            string result = MoveNotation.Format(move, 20);

            Assert.AreEqual("20. e7-e8=Q", result);
        }

        [Test]
        public void Format_ActMove_AppendsActTag()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("c3", Team.White, ChessPieceType.Knight)
                .WithPiece("d5", Team.White, ChessPieceType.Pawn);
            PieceData knight = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("c3"));
            PieceData victim = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("d5"));
            MoveCommand move = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("c3"), TestBoardSetupUtility.AlgebraicToVector("d5"), knight, victim, board)
                .WithStage(BetrayalStage.Act);

            string result = MoveNotation.Format(move, 12);

            Assert.AreEqual("12. Nc3xd5 [Act]", result);
        }

        [Test]
        public void Format_Defection_RendersInPlaceNotAsSameSquareMove()
        {
            // A Defection's StartPosition == EndPosition (see MoveCommand.CreateDefectionMove) —
            // this is the exact bug caught in production: naively formatting it produced the
            // misleading "e5-e5" instead of describing what actually happened.
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece("e5", Team.White, ChessPieceType.Pawn);
            PieceData betrayer = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e5"));
            MoveCommand move = MoveCommand.CreateDefectionMove(TestBoardSetupUtility.AlgebraicToVector("e5"), betrayer, board);

            string result = MoveNotation.Format(move, 7);

            Assert.AreEqual("7. e5 defects [Defection]", result);
            StringAssert.DoesNotContain("e5-e5", result, "Defection must never render as a same-square dash move.");
        }

        [Test]
        public void Format_DefectionOfNonPawn_IncludesPieceLetter()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece("d5", Team.White, ChessPieceType.Knight);
            PieceData betrayer = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("d5"));
            MoveCommand move = MoveCommand.CreateDefectionMove(TestBoardSetupUtility.AlgebraicToVector("d5"), betrayer, board);

            string result = MoveNotation.Format(move, 7);

            Assert.AreEqual("7. Nd5 defects [Defection]", result);
        }

        [Test]
        public void Format_RetributionMove_AppendsRetributionTag()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d5", Team.Black, ChessPieceType.Knight)
                .WithPiece("d1", Team.White, ChessPieceType.Rook);
            PieceData rook = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("d1"));
            PieceData betrayer = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("d5"));
            MoveCommand move = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("d1"), TestBoardSetupUtility.AlgebraicToVector("d5"), rook, betrayer, board)
                .WithStage(BetrayalStage.Retribution);

            string result = MoveNotation.Format(move, 8);

            Assert.AreEqual("8. Rd1xd5 [Retribution]", result);
        }

        [Test]
        public void WithResultSuffix_Checkmate_AppendsHash()
        {
            string result = MoveNotation.WithResultSuffix("8. Qh5xf7", GameState.Checkmate);

            Assert.AreEqual("8. Qh5xf7#", result);
        }

        [Test]
        public void WithResultSuffix_Check_AppendsPlus()
        {
            string result = MoveNotation.WithResultSuffix("4. Bb5", GameState.Check);

            Assert.AreEqual("4. Bb5+", result);
        }

        [Test]
        public void WithResultSuffix_Normal_AppendsNothing()
        {
            string result = MoveNotation.WithResultSuffix("1. e4", GameState.Normal);

            Assert.AreEqual("1. e4", result);
        }

        [Test]
        public void WithResultSuffix_Stalemate_AppendsNothing()
        {
            string result = MoveNotation.WithResultSuffix("40. Kf1", GameState.Stalemate);

            Assert.AreEqual("40. Kf1", result, "Stalemate has no standard SAN suffix — unlike checkmate's '#'.");
        }
    }
}
