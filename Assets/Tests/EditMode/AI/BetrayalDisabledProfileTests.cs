using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Confirms plain-chess mode (BoardState.BetrayalRightAvailable == false) is inert end to end,
    /// even against the most Betrayal-hungry profile. No Act move can be generated, evaluated, or
    /// selected once the board-level right is off, regardless of what an agent's personality dials
    /// would otherwise push it toward.
    /// </summary>
    [TestFixture]
    public class BetrayalDisabledProfileTests
    {
        private const int PollTimeoutMs = 10_000;
        private const int PollIntervalMs = 10;

        [Test]
        public void FullMatch_BetrayalDisabled_AggressiveProfile_NeverProducesAnActMove()
        {
            var engine = new ChessEngineAdapter();
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.CurrentTurn = Team.White;
            board.BetrayalRightAvailable = false;

            var matchDriver = new MatchDriver(engine, board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            matchDriver.TransitionToPhase(TurnPhase.Normal);

            AIProfile aggressive = AIProfileTable.BuiltIn.Single(p => p.Id == "aggressive");
            var rng = new SystemRandomSource(seed: 2026);

            var whiteAgent = new AsyncAIAgent(
                engine, new BetrayalAwareEvaluator(),
                AISearchSettings.FromProfile(BetrayalUsage.Full, aggressive), aggressive, rng);
            var blackAgent = new AsyncAIAgent(
                engine, new BetrayalAwareEvaluator(),
                AISearchSettings.FromProfile(BetrayalUsage.Full, aggressive), aggressive, rng);

            try
            {
                for (int ply = 0; ply < 6 && !board.IsGameOver; ply++)
                {
                    AsyncAIAgent mover = board.CurrentTurn == Team.White ? whiteAgent : blackAgent;
                    Team team = board.CurrentTurn;

                    MoveCommand? decided = null;
                    mover.OnMoveDecided += move => decided = move;

                    mover.RequestBestMove(board, team);

                    var stopwatch = Stopwatch.StartNew();
                    while (!decided.HasValue && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                    {
                        mover.Tick();
                        Thread.Sleep(PollIntervalMs);
                    }

                    Assert.That(decided, Is.Not.Null, $"AI search did not deliver a move for ply {ply}.");
                    Assert.That(decided.Value.Stage, Is.Not.EqualTo(BetrayalStage.Act),
                        "BetrayalRightAvailable == false must make an Act move unreachable, even for the most Betrayal-aggressive profile.");

                    matchDriver.PlayMove(decided.Value);
                    Assert.DoesNotThrow(() => board.AssertZobristConsistency());
                }
            }
            finally
            {
                whiteAgent.Dispose();
                blackAgent.Dispose();
            }

            Assert.That(matchDriver.MoveLog.Entries.Select(e => e.Move.Stage),
                Has.None.EqualTo(BetrayalStage.Act),
                "No ply in the recorded match log may be an Act move while Betrayal is disabled.");
        }

        [Test]
        public void Evaluate_BetrayalDisabled_ScoreIsIdenticalAcrossEveryBetrayalOptionScale()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            board.BetrayalRightAvailable = false;

            var evaluatorAtHalf = new BetrayalAwareEvaluator(new EvaluationWeights(1f, 1f, 0.5f));
            var evaluatorAtIdentity = new BetrayalAwareEvaluator(EvaluationWeights.Identity);
            var evaluatorAtDoubled = new BetrayalAwareEvaluator(new EvaluationWeights(1f, 1f, 2.0f));

            int scoreAtHalf = evaluatorAtHalf.Evaluate(board, Team.White);
            int scoreAtIdentity = evaluatorAtIdentity.Evaluate(board, Team.White);
            int scoreAtDoubled = evaluatorAtDoubled.Evaluate(board, Team.White);

            Assert.That(scoreAtHalf, Is.EqualTo(scoreAtIdentity),
                "BetrayalOptionScale must be inert once the board-level right is unavailable.");
            Assert.That(scoreAtDoubled, Is.EqualTo(scoreAtIdentity),
                "BetrayalOptionScale must be inert once the board-level right is unavailable.");
        }
    }
}
