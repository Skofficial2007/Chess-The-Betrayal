using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
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
        // depth-1 override, so these tests don't have to wait out a full deep search.
        private static AISearchSettings ShallowSettings(BetrayalUsage usage, AIProfile profile) =>
            new AISearchSettings(maxDepth: 1, softTimeBudgetMs: 5000, usage);

        // Deep/slow settings, used only where a test needs a wide cancellation window.
        private static AISearchSettings SlowSettings(BetrayalUsage usage, AIProfile profile) =>
            new AISearchSettings(maxDepth: 32, softTimeBudgetMs: 30_000, usage);

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
        public void IsAiMode_FalseUntilSetAIModeCalled()
        {
            Assert.That(_coordinator.IsAiMode, Is.False);

            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            Assert.That(_coordinator.IsAiMode, Is.True);
        }

        [Test]
        public void TryRequestMove_NotAiTeamsTurn_NeverPlaysAMove()
        {
            _board.CurrentTurn = Team.White;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);
            Thread.Sleep(200);
            _coordinator.Tick();

            Assert.That(_lastPlayedMove, Is.Null, "AI must not move when it isn't its team's turn.");
        }

        [Test]
        public void TryRequestMove_GameNotActive_NeverPlaysAMove()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: false);
            Thread.Sleep(200);
            _coordinator.Tick();

            Assert.That(_lastPlayedMove, Is.Null, "AI must not move once the game is no longer active.");
        }

        [Test]
        public void TryRequestMove_AiTeamsTurnAndGameActive_EventuallyPlaysAMoveThroughTheDelegate()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);
            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_lastPlayedMove, Is.Not.Null, "AI must deliver a move through the playMove delegate once the search completes.");
            Assert.That(_lastPlayedMove.Value.PieceTeam, Is.EqualTo(Team.Black));
        }

        [Test]
        public void IsSearchInFlight_TrueWhileSearching_FalseAfterDelivery()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);
            Assert.That(_coordinator.IsSearchInFlight, Is.True);

            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_coordinator.IsSearchInFlight, Is.False);
        }

        [Test]
        public void CancelInFlightSearch_PreventsTheInFlightSearchFromEverPlayingAMove()
        {
            // Deep/slow search settings so cancellation has a wide window to land inside.
            var slowCoordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, SlowSettings, ProfileProvider);
            _board.CurrentTurn = Team.Black;

            try
            {
                slowCoordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
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
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            _coordinator.TryRequestMove(isGameActive: true);

            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            Assert.That(_coordinator.IsSearchInFlight, Is.False,
                "Reconfiguring via SetAIMode must cancel/replace the prior agent, not run both.");
        }

        [Test]
        public void TryRequestMove_ThenDelivery_EmitsSearchRequestedAndMoveDecided()
        {
            var logger = new CapturingLogger();
            var coordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, ShallowSettings, ProfileProvider, logger);
            _board.CurrentTurn = Team.Black;

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.TryRequestMove(isGameActive: true);

                Assert.That(logger.Codes, Contains.Item(DomainEventCode.AI_SearchRequested),
                    "Requesting a move must log AI_SearchRequested so the human can see the AI start thinking.");

                var stopwatch = Stopwatch.StartNew();
                while (!_lastPlayedMove.HasValue && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    coordinator.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                Assert.That(_lastPlayedMove, Is.Not.Null);
                Assert.That(logger.Codes, Contains.Item(DomainEventCode.AI_MoveDecided),
                    "Delivering a move must log AI_MoveDecided (with elapsed ms) so the search cost is visible.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void CancelInFlightSearch_WhileSearching_EmitsSearchCancelled()
        {
            var logger = new CapturingLogger();
            // Slow settings so the search is genuinely still in flight when we cancel it.
            var coordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, SlowSettings, ProfileProvider, logger);
            _board.CurrentTurn = Team.Black;

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.TryRequestMove(isGameActive: true);
                coordinator.CancelInFlightSearch();

                Assert.That(logger.Codes, Contains.Item(DomainEventCode.AI_SearchCancelled),
                    "Cancelling an in-flight search (the Undo path) must log AI_SearchCancelled.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void CancelInFlightSearch_WithNoSearchRunning_LogsNothing()
        {
            var logger = new CapturingLogger();
            var coordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, ShallowSettings, ProfileProvider, logger);

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.CancelInFlightSearch(); // nothing is searching

                Assert.That(logger.Codes, Has.No.Member(DomainEventCode.AI_SearchCancelled),
                    "Cancelling when no search is in flight must be a silent no-op, not a spurious cancel log.");
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        [Test]
        public void Dispose_StopsFurtherMoveDelivery()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            _coordinator.TryRequestMove(isGameActive: true);

            _coordinator.Dispose();

            Thread.Sleep(200);
            _coordinator.Tick();

            Assert.That(_lastPlayedMove, Is.Null, "A disposed coordinator must never deliver a move.");
            Assert.That(_coordinator.IsAiMode, Is.False);
        }

        /// <summary>
        /// Verbose-on IDomainLogger that records every event code it's handed, so lifecycle-logging
        /// assertions can check what the coordinator emitted. IsVerbose is true because the
        /// coordinator gates its LogInfo calls behind it. Locked because a search delivered via
        /// Tick() and a cancel from the test thread can both call in.
        /// </summary>
        private sealed class CapturingLogger : IDomainLogger
        {
            private readonly object _lock = new object();
            private readonly List<DomainEventCode> _codes = new List<DomainEventCode>();

            public bool IsVerbose => true;

            public IReadOnlyList<DomainEventCode> Codes
            {
                get { lock (_lock) { return new List<DomainEventCode>(_codes); } }
            }

            private void Add(DomainLogEvent evt) { lock (_lock) { _codes.Add(evt.Code); } }

            public void LogInfo(DomainLogEvent evt) => Add(evt);
            public void LogWarning(DomainLogEvent evt) => Add(evt);
            public void LogError(DomainLogEvent evt) => Add(evt);
        }
    }
}
