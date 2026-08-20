using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine
{
    /// <summary>
    /// A request names a move by four fields and nothing else, which is all an outside caller can be
    /// trusted to supply. Two of those fields are the ones a careless match would drop, and both
    /// decide which move gets played: the same two squares are an ordinary capture or a Betrayal Act
    /// depending on the stage asked for, and a pawn arriving at the last rank offers four moves that
    /// differ in nothing but what it turns into.
    /// </summary>
    [TestFixture]
    public class MoveRequestTests
    {
        private IChessEngine _engine;

        [SetUp]
        public void Setup() => _engine = new ChessEngineAdapter();

        private static Vector2Int Square(string algebraic) =>
            TestBoardSetupUtility.AlgebraicToVector(algebraic);

        private DomainResult<MoveCommand> Ask(
            BoardState board, string from, string to,
            BetrayalStage stage = BetrayalStage.None,
            ChessPieceType promotedTo = ChessPieceType.None) =>
            MoveRequest.Resolve(_engine, board, Square(from), Square(to), stage, promotedTo);

        private static BoardState TwoKingsAnd(params (string Square, Team Team, ChessPieceType Type)[] pieces)
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King);

            foreach ((string square, Team team, ChessPieceType type) in pieces)
            {
                board = board.WithPiece(square, team, type);
            }

            return board.WithTurn(Team.White).WithComputedHash();
        }

        [Test]
        public void AMoveThatIsLegal_ComesBackAsTheMoveTheEngineGenerated()
        {
            BoardState board = TwoKingsAnd(("d4", Team.White, ChessPieceType.Rook));

            DomainResult<MoveCommand> result = Ask(board, "d4", "d7");

            Assert.That(result.IsSuccess, Is.True, "A rook on an open file can reach d7.");
            Assert.That(result.Value.StartPosition, Is.EqualTo(Square("d4")));
            Assert.That(result.Value.EndPosition, Is.EqualTo(Square("d7")));
        }

        [Test]
        public void AMoveThatIsNotLegal_IsRefusedAndSaysSo()
        {
            BoardState board = TwoKingsAnd(("d4", Team.White, ChessPieceType.Rook));

            DomainResult<MoveCommand> result = Ask(board, "d4", "e5");

            Assert.That(result.IsSuccess, Is.False, "A rook does not move diagonally.");
            Assert.That(result.ErrorCode, Is.EqualTo(DomainEventCode.Engine_IllegalMoveRequested));
            Assert.That(result.ErrorDetail, Is.Not.Null,
                "A refusal a server would have to explain to a client needs to carry the reason.");
        }

        [Test]
        public void AskingFromASquareWithNothingOnIt_IsRefused()
        {
            BoardState board = TwoKingsAnd(("d4", Team.White, ChessPieceType.Rook));

            Assert.That(Ask(board, "e5", "e6").IsSuccess, Is.False);
        }

        [Test]
        public void AskingForTheOtherSidesPiece_IsRefused()
        {
            BoardState board = TwoKingsAnd(("d5", Team.Black, ChessPieceType.Rook));

            Assert.That(Ask(board, "d5", "d7").IsSuccess, Is.False,
                "It is White to move, so Black's rook cannot be asked to move.");
        }

        [Test]
        public void APromotionRequest_GetsTheVariantItAskedFor()
        {
            BoardState board = TwoKingsAnd(("e7", Team.White, ChessPieceType.Pawn));

            DomainResult<MoveCommand> knight =
                Ask(board, "e7", "e8", promotedTo: ChessPieceType.Knight);

            Assert.That(knight.IsSuccess, Is.True);
            Assert.That(knight.Value.PromotedTo, Is.EqualTo(ChessPieceType.Knight),
                "Four moves end on e8 and only what the pawn becomes tells them apart.");
        }

        [Test]
        public void APromotionAskedForWithoutSayingWhatItBecomes_IsRefused()
        {
            BoardState board = TwoKingsAnd(("e7", Team.White, ChessPieceType.Pawn));

            Assert.That(Ask(board, "e7", "e8").IsSuccess, Is.False,
                "Every move to e8 promotes, so a request naming no piece matches none of them and "
                + "must not quietly get whichever one the generator happened to produce first.");
        }

        [Test]
        public void ABetrayalAct_IsFoundWhenTheRequestSaysItIsOne()
        {
            BoardState board = TwoKingsAnd(("d1", Team.White, ChessPieceType.Queen),
                                           ("d2", Team.White, ChessPieceType.Pawn))
                .WithBetrayalRight(true);

            DomainResult<MoveCommand> act = Ask(board, "d1", "d2", BetrayalStage.Act);

            Assert.That(act.IsSuccess, Is.True, "A queen may turn on the pawn in front of it.");
            Assert.That(act.Value.Stage, Is.EqualTo(BetrayalStage.Act));
        }

        [Test]
        public void ABetrayalActAskedForAsAnOrdinaryMove_IsRefused()
        {
            BoardState board = TwoKingsAnd(("d1", Team.White, ChessPieceType.Queen),
                                           ("d2", Team.White, ChessPieceType.Pawn))
                .WithBetrayalRight(true);

            Assert.That(Ask(board, "d1", "d2").IsSuccess, Is.False,
                "Turning on your own piece is never something you do by accident, so a request that "
                + "does not name the Act must not be answered with one.");
        }

        [Test]
        public void TheScratchListIsReusedRatherThanRequiringAFreshOne()
        {
            BoardState board = TwoKingsAnd(("d4", Team.White, ChessPieceType.Rook));
            var scratch = new List<MoveCommand>();

            MoveRequest.Resolve(_engine, board, Square("d4"), Square("d7"), scratch: scratch);
            DomainResult<MoveCommand> second =
                MoveRequest.Resolve(_engine, board, Square("d4"), Square("d6"), scratch: scratch);

            Assert.That(second.IsSuccess, Is.True,
                "A caller passing its own buffer twice must get the same answer as one that did not.");
        }
    }
}
