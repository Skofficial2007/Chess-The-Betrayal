using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// AIMatchCoordinator is the AI-13 extraction of GameManager's AI-coordinator slice: turn
    /// triggering, search lifecycle, and Undo's cancel-before-pop ordering. Constructed with only
    /// IChessEngine/BoardState/a playMove delegate — no MatchDriver or GameManager reference — so
    /// these tests exercise it exactly the way GameManager composes it, minus the MonoBehaviour.
    /// </summary>
    [TestFixture]
    public class AIMatchCoordinatorTests
    {
        private const int PollTimeoutMs = 5000;
        private const int PollIntervalMs = 10;

        // Shallow/fast settings for delivery/cancellation tests — mirrors AsyncAgentTests' own
        // depth-1 override, so these tests don't have to wait out a full Ultimate (depth-7) search.
        private static AISearchSettings ShallowSettings(BetrayalUsage usage) =>
            new AISearchSettings(maxDepth: 1, softTimeBudgetMs: 5000, usage);

        // Deep/slow settings, used only where a test needs a wide cancellation window.
        private static AISearchSettings SlowSettings(BetrayalUsage usage) =>
            new AISearchSettings(maxDepth: 32, softTimeBudgetMs: 30_000, usage);

        private ChessEngineAdapter _engine;
        private BoardState _board;
        private AIMatchCoordinator _coordinator;
        private MoveCommand? _lastPlayedMove;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _board = TestBoardSetupUtility.CreateStandard();
            _lastPlayedMove = null;

            _coordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, ShallowSettings);
        }

        [TearDown]
        public void TearDown()
        {
            _coordinator.Dispose();
        }

        private void PumpTickUntil(System.Func<bool> isDone)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!isDone() && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
            {
                _coordinator.Tick();
                Thread.Sleep(PollIntervalMs);
            }
        }

        [Test]
        public void IsAiMode_FalseUntilSetAIModeCalled()
        {
            Assert.That(_coordinator.IsAiMode, Is.False);

            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full);

            Assert.That(_coordinator.IsAiMode, Is.True);
        }

        [Test]
        public void TryRequestMove_NotAiTeamsTurn_NeverPlaysAMove()
        {
            _board.CurrentTurn = Team.White;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full);

            _coordinator.TryRequestMove(isGameActive: true);
            Thread.Sleep(200);
            _coordinator.Tick();

            Assert.That(_lastPlayedMove, Is.Null, "AI must not move when it isn't its team's turn.");
        }

        [Test]
        public void TryRequestMove_GameNotActive_NeverPlaysAMove()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full);

            _coordinator.TryRequestMove(isGameActive: false);
            Thread.Sleep(200);
            _coordinator.Tick();

            Assert.That(_lastPlayedMove, Is.Null, "AI must not move once the game is no longer active.");
        }

        [Test]
        public void TryRequestMove_AiTeamsTurnAndGameActive_EventuallyPlaysAMoveThroughTheDelegate()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full);

            _coordinator.TryRequestMove(isGameActive: true);
            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_lastPlayedMove, Is.Not.Null, "AI must deliver a move through the playMove delegate once the search completes.");
            Assert.That(_lastPlayedMove.Value.PieceTeam, Is.EqualTo(Team.Black));
        }

        [Test]
        public void IsSearchInFlight_TrueWhileSearching_FalseAfterDelivery()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full);

            _coordinator.TryRequestMove(isGameActive: true);
            Assert.That(_coordinator.IsSearchInFlight, Is.True);

            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_coordinator.IsSearchInFlight, Is.False);
        }

        [Test]
        public void CancelInFlightSearch_PreventsTheInFlightSearchFromEverPlayingAMove()
        {
            // Deep/slow search settings so cancellation has a wide window to land inside.
            var slowCoordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, SlowSettings);
            _board.CurrentTurn = Team.Black;

            try
            {
                slowCoordinator.SetAIMode(Team.Black, BetrayalUsage.Full);
                slowCoordinator.TryRequestMove(isGameActive: true);
                slowCoordinator.CancelInFlightSearch();

                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < 1000)
                {
                    slowCoordinator.Tick();
                    Thread.Sleep(10);
                }

                Assert.That(_lastPlayedMove, Is.Null, "A cancelled search must never reach the playMove delegate.");
            }
            finally
            {
                slowCoordinator.Dispose();
            }
        }

        [Test]
        public void SetAIMode_CalledAgain_TearsDownThePreviousAgentFirst()
        {
            // Reconfiguring for AI play (e.g. a new match) must not leak the previous agent's
            // OnMoveDecided subscription or leave two agents racing to deliver a move.
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full);
            _coordinator.TryRequestMove(isGameActive: true);

            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full);

            Assert.That(_coordinator.IsSearchInFlight, Is.False,
                "Reconfiguring via SetAIMode must cancel/replace the prior agent, not run both.");
        }

        [Test]
        public void Dispose_StopsFurtherMoveDelivery()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full);
            _coordinator.TryRequestMove(isGameActive: true);

            _coordinator.Dispose();

            Thread.Sleep(200);
            _coordinator.Tick();

            Assert.That(_lastPlayedMove, Is.Null, "A disposed coordinator must never deliver a move.");
            Assert.That(_coordinator.IsAiMode, Is.False);
        }
    }
}
