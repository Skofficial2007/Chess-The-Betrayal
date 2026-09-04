using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// A Defection that mates the side who started the Betrayal.
    ///
    /// Entering the forced-Save sub-phase used to announce that a Save was owed without ever asking
    /// whether one existed. When none did, the match had no way to end and no way out but
    /// abandoning it — Undo is disabled in that phase as well. The search already scored these
    /// positions as mate while it was thinking about them; the live match was the only thing not
    /// asking.
    ///
    /// The trap worth knowing: EvaluateGameState deliberately answers Normal for any open Betrayal
    /// sub-phase, so routing this through CheckForGameEnd reports a healthy game about a dead one.
    /// The question has to go to GetForcedSaveMoves directly.
    /// </summary>
    [TestFixture]
    public class MatchDriverForcedSaveMateTests
    {
        private ChessEngineAdapter _engine;
        private BoardState _board;
        private MatchDriver _matchDriver;
        private readonly List<MoveCommand> _acts = new List<MoveCommand>();

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _board = BoardSetup.CreateEmpty();
        }

        private void StartDriving()
        {
            _board.ComputeFullZobristHash();
            _matchDriver = new MatchDriver(_engine, _board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            _matchDriver.TransitionToPhase(TurnPhase.Normal);
        }

        /// <summary>Plays the Act that betrays whatever stands on <paramref name="victimSquare"/>.</summary>
        private void Betray(string betrayerSquare, string victimSquare)
        {
            ChessEngine.GetBetrayalTargets(_board, BoardSetup.AlgebraicToVector(betrayerSquare), _acts);

            Vector2Int victim = BoardSetup.AlgebraicToVector(victimSquare);
            foreach (MoveCommand act in _acts)
            {
                if (act.EndPosition == victim)
                {
                    _matchDriver.PlayMove(act);
                    return;
                }
            }

            Assert.Fail($"No Act from {betrayerSquare} onto {victimSquare} was available to play.");
        }

        /// <summary>Sets up the smothered king and the knight that will betray the pawn beside it.</summary>
        private void SmotheredKingAboutToBeBetrayed()
        {
            _board.WithPiece("a1", Team.White, ChessPieceType.King)
                  .WithPiece("b1", Team.White, ChessPieceType.Rook)
                  .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                  .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                  .WithPiece("a3", Team.White, ChessPieceType.Knight)
                  .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                  .WithPiece("h8", Team.Black, ChessPieceType.King)
                  .WithTurn(Team.White)
                  .WithBetrayalRight(true);
            StartDriving();
        }

        /// <summary>
        /// White's king is smothered by its own pieces on a1. A knight betrays the pawn on c2,
        /// nothing white can reach c2 to answer it, so the knight defects — and a black knight on
        /// c2 gives a check that cannot be blocked, captured or stepped away from.
        /// </summary>
        [Test]
        public void ADefectionThatMatesTheInitiatorEndsTheMatch()
        {
            SmotheredKingAboutToBeBetrayed();

            bool aForcedMoveWasAnnounced = false;
            _matchDriver.OnBetrayalMoveRequired += _ => aForcedMoveWasAnnounced = true;

            Betray("a3", "c2");

            Assert.That(_board.IsGameOver, Is.True, "There is no Save to make, so the match is over.");
            Assert.That(_board.Winner, Is.EqualTo(Team.Black), "The side that cannot answer loses.");
            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.GameOver));
            Assert.That(aForcedMoveWasAnnounced, Is.False,
                "Announcing a forced move nobody can make is what left the match waiting forever.");
        }

        /// <summary>
        /// The control. The same shape of Defection — one that checks its own king — but with the
        /// king free to step off the line. A guard that ended the match here would be worse than
        /// the bug it fixes, so this is the half that says it only fires when there is genuinely
        /// nothing left to play.
        /// </summary>
        [Test]
        public void ADefectionThatOnlyChecksTheInitiatorStillWaitsForTheSave()
        {
            _board.WithPiece("e1", Team.White, ChessPieceType.King)
                  .WithPiece("e4", Team.White, ChessPieceType.Rook)
                  .WithPiece("e5", Team.White, ChessPieceType.Pawn)
                  .WithPiece("h8", Team.Black, ChessPieceType.King)
                  .WithTurn(Team.White)
                  .WithBetrayalRight(true);
            StartDriving();

            Team? owedBy = null;
            _matchDriver.OnBetrayalMoveRequired += team => owedBy = team;

            Betray("e4", "e5");

            Assert.That(_board.IsGameOver, Is.False, "The king can step off the file, so the game goes on.");
            Assert.That(_matchDriver.CurrentPhase, Is.EqualTo(TurnPhase.ForcedSave));
            Assert.That(owedBy, Is.EqualTo(Team.White), "White still owes the Save.");
        }

        /// <summary>
        /// The turn has to be handed over even when it ended in mate: a takeback reads the plies a
        /// turn applied, and Undo is offered from the game-over screen. Without this the last turn
        /// of the match is missing from the record and a takeback lands short.
        /// </summary>
        [Test]
        public void TheMatingTurnIsStillHandedToTheTakebackRecord()
        {
            SmotheredKingAboutToBeBetrayed();

            var turnPlies = new List<MoveCommand>();
            _matchDriver.OnTurnCompleted += plies => turnPlies.AddRange(plies);

            Betray("a3", "c2");

            Assert.That(turnPlies, Is.Not.Empty, "The turn that ended the match was never recorded.");
            Assert.That(turnPlies[0].Stage, Is.EqualTo(BetrayalStage.Act));
            Assert.That(turnPlies[turnPlies.Count - 1].Stage, Is.EqualTo(BetrayalStage.Defection),
                "The Defection is the ply the match ended on.");
        }
    }
}
