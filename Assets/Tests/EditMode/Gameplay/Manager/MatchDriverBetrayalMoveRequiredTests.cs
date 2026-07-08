using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// Pins MatchDriver.OnBetrayalMoveRequired — the event that lets an autonomous driver (the AI)
    /// continue its own forced Betrayal sub-sequence. Act and Defection do NOT flip the side to
    /// move (the turn-flip invariant), so no TurnChangedEvent fires after them; without this event
    /// the AI would Act and then hang forever in RetributionPending, never playing the Retribution
    /// it owes. This bug was invisible to the AI-11 integration tests because those drove the
    /// Retribution ply by hand instead of through the live event flow.
    /// </summary>
    [TestFixture]
    public class MatchDriverBetrayalMoveRequiredTests
    {
        private ChessEngineAdapter _engine;
        private BoardState _board;
        private MatchDriver _matchDriver;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White);

            _matchDriver = new MatchDriver(_engine, _board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            _matchDriver.TransitionToPhase(TurnPhase.Normal);
        }

        [Test]
        public void Act_LeavingRetributionPending_RaisesOnBetrayalMoveRequiredForTheSameSide()
        {
            // White Acts (Knight b1 betrays own pawn a3); a legal Retribution exists (Rook a1),
            // so the turn stays open in RetributionPending and White still owes the Retribution.
            _board.WithPiece("b1", Team.White, ChessPieceType.Knight);
            _board.WithPiece("a1", Team.White, ChessPieceType.Rook);
            _board.WithPiece("a3", Team.White, ChessPieceType.Pawn); // Betrayal victim
            _board.WithBetrayalRight(true);
            _board.ComputeFullZobristHash();

            Team? owedBy = null;
            _matchDriver.OnBetrayalMoveRequired += team => owedBy = team;

            var actMoves = new List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(_board, TestBoardSetupUtility.AlgebraicToVector("b1"), actMoves);
            _matchDriver.PlayMove(actMoves[0]);

            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.RetributionPending));
            Assert.That(owedBy, Is.EqualTo(Team.White),
                "After an Act that leaves Retribution pending, the same side (White) must be announced as owing the forced move.");
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White),
                "Act must not flip the side to move — the announced team must match _board.CurrentTurn.");
        }

        [Test]
        public void OrdinaryMove_NeverRaisesOnBetrayalMoveRequired()
        {
            _board.WithPiece("a2", Team.White, ChessPieceType.Pawn);
            _board.ComputeFullZobristHash();

            bool fired = false;
            _matchDriver.OnBetrayalMoveRequired += _ => fired = true;

            MoveCommand push = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("a2"),
                TestBoardSetupUtility.AlgebraicToVector("a3"),
                _board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a2")),
                _board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a3")),
                _board);
            _matchDriver.PlayMove(push);

            Assert.That(fired, Is.False, "A plain turn-flipping move must never announce a forced Betrayal follow-up.");
        }

        [Test]
        public void RetributionCompletingTheTurn_DoesNotRaiseAgain()
        {
            // Full Act -> Retribution sequence: the event must fire exactly once (after the Act),
            // never after the Retribution that ends the turn (a TurnChangedEvent covers that).
            _board.WithPiece("b1", Team.White, ChessPieceType.Knight);
            _board.WithPiece("a1", Team.White, ChessPieceType.Rook);
            _board.WithPiece("a3", Team.White, ChessPieceType.Pawn);
            _board.WithBetrayalRight(true);
            _board.ComputeFullZobristHash();

            int fireCount = 0;
            _matchDriver.OnBetrayalMoveRequired += _ => fireCount++;

            var actMoves = new List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(_board, TestBoardSetupUtility.AlgebraicToVector("b1"), actMoves);
            _matchDriver.PlayMove(actMoves[0]);

            var retMoves = new List<MoveCommand>();
            _engine.GetRetributionMoves(_board, Team.White, _board.PendingBetrayerSquare.Value, retMoves);
            _matchDriver.PlayMove(retMoves[0]);

            Assert.That(fireCount, Is.EqualTo(1),
                "OnBetrayalMoveRequired must fire once (after the Act), not again after the Retribution ends the turn.");
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black),
                "After the Retribution completes, the turn flips to the opponent as normal.");
        }
    }
}
