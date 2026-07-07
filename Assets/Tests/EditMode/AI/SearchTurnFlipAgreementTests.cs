using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Replays Act/Retribution and Act/forced-Defection sequences through the game path
    /// (TurnResolver.Advance, as GameManager drives it) and asserts board.CurrentTurn agrees,
    /// ply for ply, with AlphaBetaSearch.StageFlipsTurn. If the two ever disagree, the search
    /// and the engine have desynced on when a turn actually passes.
    /// </summary>
    [TestFixture]
    public class SearchTurnFlipAgreementTests
    {
        private List<MoveCommand> _moveBuffer;
        private ITurnResolver _turnResolver;

        [SetUp]
        public void Setup()
        {
            _moveBuffer = new List<MoveCommand>();
            _turnResolver = new TurnResolver();
        }

        [Test]
        public void ActThenRetribution_TurnFlipsOnlyOnRetribution()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            board.ComputeFullZobristHash();

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("b1"), _moveBuffer);
            MoveCommand actMove = _moveBuffer[0];
            Team turnBeforeAct = board.CurrentTurn;

            TurnAdvanceResult actResult = _turnResolver.Advance(board, actMove);

            Assert.That(AlphaBetaSearch.StageFlipsTurn(actMove.Stage), Is.False,
                "Act must not flip the turn per the search's flip predicate.");
            Assert.That(actResult.TurnPassedToOpponent, Is.False,
                "Act must not flip the turn per the game path.");
            Assert.That(board.CurrentTurn, Is.EqualTo(turnBeforeAct),
                "board.CurrentTurn must be unchanged immediately after Act.");

            ChessEngine.GetRetributionMoves(board, Team.White, board.PendingBetrayerSquare.Value, _moveBuffer);
            MoveCommand retMove = _moveBuffer[0];

            TurnAdvanceResult retResult = _turnResolver.Advance(board, retMove);

            Assert.That(AlphaBetaSearch.StageFlipsTurn(retMove.Stage), Is.True,
                "Retribution must flip the turn per the search's flip predicate.");
            Assert.That(retResult.TurnPassedToOpponent, Is.True,
                "Retribution must flip the turn per the game path.");
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.Black),
                "board.CurrentTurn must have passed to Black after Retribution.");

            board.AssertZobristConsistency();
        }

        [Test]
        public void ActThenForcedDefection_TurnFlipsOnlyAfterForcedSave()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)
                .WithPiece("e3", Team.White, ChessPieceType.Knight)
                .WithPiece("d3", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            board.ComputeFullZobristHash();

            MoveCommand actMove = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e3"), TestBoardSetupUtility.AlgebraicToVector("d3"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e3")),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("d3")), board)
                .WithStage(BetrayalStage.Act);

            Team turnBeforeAct = board.CurrentTurn;
            TurnAdvanceResult actResult = _turnResolver.Advance(board, actMove);

            Assert.That(AlphaBetaSearch.StageFlipsTurn(actMove.Stage), Is.False);
            Assert.That(actResult.TurnPassedToOpponent, Is.False);
            Assert.That(board.CurrentTurn, Is.EqualTo(turnBeforeAct));

            Assert.That(actResult.RequiresForcedSave, Is.True,
                "Pinned executioner leaves no legal Retribution — the defected Knight now checks the King.");

            MoveCommand defectionMove = actResult.DefectionMove.Value;
            Assert.That(AlphaBetaSearch.StageFlipsTurn(defectionMove.Stage), Is.False,
                "Defection must not flip the turn per the search's flip predicate.");
            Assert.That(board.CurrentTurn, Is.EqualTo(turnBeforeAct),
                "board.CurrentTurn must still be unchanged after the forced Defection.");

            ChessEngine.GetForcedSaveMoves(board, Team.White, _moveBuffer);
            MoveCommand saveMove = _moveBuffer[0];

            TurnAdvanceResult saveResult = _turnResolver.Advance(board, saveMove);

            Assert.That(AlphaBetaSearch.StageFlipsTurn(saveMove.Stage), Is.True,
                "DefensiveOverride must flip the turn per the search's flip predicate.");
            Assert.That(saveResult.TurnPassedToOpponent, Is.True,
                "DefensiveOverride must flip the turn per the game path.");
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.Black),
                "board.CurrentTurn must have passed to Black only after the Defensive Override save.");

            board.AssertZobristConsistency();
        }
    }
}
