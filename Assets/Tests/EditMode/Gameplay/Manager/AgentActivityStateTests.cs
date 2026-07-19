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
    /// AgentActivity is AIMatchCoordinator's local-AI-only presentation state machine (Idle,
    /// Searching, ResultReady — see the enum's doc comment). These tests exercise every legal
    /// transition directly through the coordinator's public lifecycle methods, mirroring
    /// AIMatchCoordinatorTests' fixture construction.
    /// </summary>
    [TestFixture]
    public class AgentActivityStateTests
    {
        private const int PollTimeoutMs = 5000;
        private const int PollIntervalMs = 10;

        private static AISearchSettings ShallowSettings(BetrayalUsage usage, AIProfile profile) =>
            new AISearchSettings(maxDepth: 1, TestTimeBudgets.Generous, usage);

        private static AISearchSettings SlowSettings(BetrayalUsage usage, AIProfile profile) =>
            new AISearchSettings(maxDepth: 32, TestTimeBudgets.Generous, usage);

        private static readonly IAIProfileProvider ProfileProvider = new AIProfileTableProvider();

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

            _coordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, ShallowSettings, ProfileProvider);
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
        public void Activity_StartsIdle()
        {
            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle));
        }

        [Test]
        public void Activity_SetAIMode_StaysIdle()
        {
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle));
        }

        [Test]
        public void Activity_TryRequestMove_TransitionsToSearching()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Searching));
        }

        [Test]
        public void Activity_TryRequestMove_NotAiTurn_StaysIdle()
        {
            _board.CurrentTurn = Team.White;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle),
                "A no-op request (wrong team's turn) must never leave Idle.");
        }

        [Test]
        public void Activity_SearchDelivered_FallsBackToIdleAfterMovePlayed()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);
            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle),
                "ResultReady is a one-tick pulse; by the time Tick() returns the move has already been played and the machine has fallen back to Idle.");
        }

        [Test]
        public void Activity_CancelInFlightSearch_TransitionsStraightBackToIdle()
        {
            var slowCoordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, SlowSettings, ProfileProvider);
            _board.CurrentTurn = Team.Black;

            try
            {
                slowCoordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                slowCoordinator.TryRequestMove(isGameActive: true);
                Assert.That(slowCoordinator.Activity, Is.EqualTo(AgentActivity.Searching));

                slowCoordinator.CancelInFlightSearch();

                Assert.That(slowCoordinator.Activity, Is.EqualTo(AgentActivity.Idle),
                    "Cancellation must never visit ResultReady — it drops straight back to Idle.");
            }
            finally
            {
                slowCoordinator.Dispose();
            }
        }

        [Test]
        public void Activity_SetAIModeCalledAgainWhileSearching_ResetsToIdle()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            _coordinator.TryRequestMove(isGameActive: true);
            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Searching));

            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle),
                "Reconfiguring mid-search must tear down the old agent and reset the state machine.");
        }

        [Test]
        public void Activity_Dispose_ResetsToIdle()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            _coordinator.TryRequestMove(isGameActive: true);

            _coordinator.Dispose();

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle));
        }

        [Test]
        public void IsSearchInFlight_MirrorsSearchingState()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            Assert.That(_coordinator.IsSearchInFlight, Is.False);

            _coordinator.TryRequestMove(isGameActive: true);
            Assert.That(_coordinator.IsSearchInFlight, Is.True);

            PumpTickUntil(() => _lastPlayedMove.HasValue);
            Assert.That(_coordinator.IsSearchInFlight, Is.False);
        }
    }
}
