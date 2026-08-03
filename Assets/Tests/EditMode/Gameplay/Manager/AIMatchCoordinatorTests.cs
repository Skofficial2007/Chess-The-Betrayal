using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.MatchTelemetry;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// AIMatchCoordinator owns GameManager's AI-coordinator slice: turn
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
            new AISearchSettings(maxDepth: 1, TestTimeBudgets.Generous, usage);

        // Deep/slow settings, used only where a test needs a wide cancellation window.
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
        public void ClearAIMode_TearsDownTheAgentAndClearsTelemetry()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            Assert.That(_coordinator.Telemetry, Is.Not.Null, "Sanity check.");
            Assert.That(_coordinator.IsAiMode, Is.True, "Sanity check.");

            _coordinator.ClearAIMode();

            Assert.That(_coordinator.Telemetry, Is.Null,
                "A coordinator configured for no AI at all must not still be holding a previous " +
                "match's recorded moves — that's what let a plain match offer to share the wrong report.");
            Assert.That(_coordinator.IsAiMode, Is.False);
        }

        [Test]
        public void ClearAIMode_OnACoordinatorThatNeverConfiguredAI_IsASafeNoOp()
        {
            Assert.DoesNotThrow(() => _coordinator.ClearAIMode());
            Assert.That(_coordinator.Telemetry, Is.Null);
        }

        [Test]
        public void SetAIMode_AlwaysConstructsAFreshTelemetry_SoAReplayNeverBlendsWithThePriorMatch()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            AiMatchTelemetry first = _coordinator.Telemetry;

            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            AiMatchTelemetry second = _coordinator.Telemetry;

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(second, Is.Not.SameAs(first),
                "A second match (e.g. Replay) must get its own telemetry object, not keep recording into the first one's.");
        }

        [Test]
        public void RecordTelemetry_OffByDefault_NeverRecordsAMove_EvenAfterDelivery()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            // RecordTelemetry is left at its default (false) — this is the composition root's
            // opt-in feature flag, off unless something explicitly turns it on.

            _coordinator.TryRequestMove(isGameActive: true);
            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_coordinator.Telemetry.MoveCount, Is.Zero,
                "Nothing must be recorded unless RecordTelemetry was explicitly turned on.");
        }

        [Test]
        public void RecordTelemetry_On_RecordsTheSearchedMovesDepthAndElapsed()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            _coordinator.RecordTelemetry = true;

            _coordinator.TryRequestMove(isGameActive: true);
            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_coordinator.Telemetry.MoveCount, Is.EqualTo(1));

            string report = _coordinator.Telemetry.Render();
            Assert.That(report, Does.Contain("1 moves total (0 from the opening book, 1 searched)"));
            Assert.That(report, Does.Contain("depth 1,"));
            Assert.That(report, Does.Not.Contain("(book)"));
        }

        [Test]
        public void RecordTelemetry_On_ReadsThePlyNumberFromTheBoardOnceTheMoveIsActuallyApplied()
        {
            // The shared fixture's playMove stub above only records the move for assertions and
            // never touches the board, so BoardState.FullMoveNumber would never move off zero
            // there. This test uses a playMove that actually applies the move, the way MatchDriver
            // does in production, so the recorded ply number reflects something real.
            var engine = new ChessEngineAdapter();
            var board = TestBoardSetupUtility.CreateStandard();
            board.CurrentTurn = Team.Black;
            var coordinator = new AIMatchCoordinator(
                engine, board, move => new TurnResolver().Advance(board, move), ShallowSettings, ProfileProvider);

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.RecordTelemetry = true;
                coordinator.TryRequestMove(isGameActive: true);

                var stopwatch = Stopwatch.StartNew();
                while (coordinator.Telemetry.MoveCount == 0 && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    coordinator.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                Assert.That(coordinator.Telemetry.Render(), Does.Contain("ply 1:"));
            }
            finally
            {
                coordinator.Dispose();
            }
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
        public void TryRequestMove_ThenDelivery_MoveDecidedMessageNamesTheDepthAndStopReason()
        {
            var logger = new CapturingLogger();
            var coordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, ShallowSettings, ProfileProvider, logger);
            _board.CurrentTurn = Team.Black;

            try
            {
                coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                coordinator.TryRequestMove(isGameActive: true);

                var stopwatch = Stopwatch.StartNew();
                while (!_lastPlayedMove.HasValue && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    coordinator.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                Assert.That(_lastPlayedMove, Is.Not.Null);
                DomainLogEvent moveDecided = logger.Events.First(e => e.Code == DomainEventCode.AI_MoveDecided);

                // ShallowSettings caps MaxDepth at 1, so a search that completed normally must
                // report exactly that depth back through the log line.
                Assert.That(moveDecided.Message, Does.Contain("depth 1,"),
                    "AI_MoveDecided must name the depth the search actually completed, not just that a move was played.");
                Assert.That(moveDecided.Message, Does.Not.Contain("Unset"),
                    "A real search always sets a concrete stop reason; Unset would mean the depth/reason wiring never ran.");
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
            private readonly List<DomainLogEvent> _events = new List<DomainLogEvent>();

            public bool IsVerbose => true;

            public IReadOnlyList<DomainEventCode> Codes
            {
                get { lock (_lock) { return _events.Select(e => e.Code).ToList(); } }
            }

            /// <summary>The full events, message and AuxInt included — Codes above only carries
            /// enough to assert an event fired at all, not what it said.</summary>
            public IReadOnlyList<DomainLogEvent> Events
            {
                get { lock (_lock) { return new List<DomainLogEvent>(_events); } }
            }

            private void Add(DomainLogEvent evt) { lock (_lock) { _events.Add(evt); } }

            public void LogInfo(DomainLogEvent evt) => Add(evt);
            public void LogWarning(DomainLogEvent evt) => Add(evt);
            public void LogError(DomainLogEvent evt) => Add(evt);
        }
    }
}
