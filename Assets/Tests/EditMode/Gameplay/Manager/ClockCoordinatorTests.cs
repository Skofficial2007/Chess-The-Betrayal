using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Logic;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// ClockCoordinator is the AI-13 extraction of GameManager's clock-lifecycle slice. It
    /// implements IClockEventHandler/IClockSnapshotSource directly rather than being forwarded
    /// through GameManager — these tests exercise it exactly as GameManager composes it (ctor
    /// takes GameSetup + two narrow routing delegates), including the real GameClockController
    /// MonoBehaviour it constructs via GameSetup.InitializeClock's AddComponent call.
    ///
    /// ChessClock's own timeout/low-time-warning behavior (when it invokes IClockEventHandler) is
    /// already covered by ChessClockTests — these tests only pin ClockCoordinator's half of the
    /// contract: that it wires itself as the handler and routes each callback to the constructor
    /// delegate GameManager supplied, exercised by calling the explicit interface methods directly.
    /// </summary>
    [TestFixture]
    public class ClockCoordinatorTests
    {
        private GameObject _host;
        private ClockCoordinator _coordinator;
        private MatchDriver _matchDriver;

        private Team? _timedOutTeam;
        private (Team team, long remainingMs)? _lowTimeWarning;

        [SetUp]
        public void Setup()
        {
            _host = new GameObject("ClockCoordinatorTests.Host");

            var engine = new ChessEngineAdapter();
            var board = new BoardState(8, 8);
            _matchDriver = new MatchDriver(engine, board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);

            _timedOutTeam = null;
            _lowTimeWarning = null;

            _coordinator = new ClockCoordinator(
                new GameSetup(logMoves: false),
                team => _timedOutTeam = team,
                (team, remainingMs) => _lowTimeWarning = (team, remainingMs));
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        [Test]
        public void Current_NoClockInitialized_IsNull()
        {
            Assert.That(_coordinator.Current, Is.Null);
        }

        [Test]
        public void Initialize_TimedModeNotAi_ConstructsAClockAndAttachesItToMatchDriver()
        {
            var mode = new GameModeConfig("Blitz 5|0", baseTimeMs: 5 * 60_000L, incrementMs: 0);

            _coordinator.Initialize(mode, isAiMode: false, Team.White, _host, _matchDriver);

            Assert.That(_coordinator.Current, Is.Not.Null, "A timed, non-AI session must construct a real clock.");
            Assert.That(_coordinator.Current.Value.WhiteRemainingMs, Is.EqualTo(mode.BaseTimeMs));
            Assert.That(_coordinator.Current.Value.ActiveSide, Is.EqualTo(Team.White));
            Assert.That(_matchDriver.GetCurrentClockSnapshot(), Is.Not.Null,
                "Initialize must attach the clock to MatchDriver, not just construct it locally.");
        }

        [Test]
        public void Initialize_AiMode_NeverConstructsAClockEvenWithATimedModeConfig()
        {
            var mode = new GameModeConfig("Blitz 5|0", baseTimeMs: 5 * 60_000L, incrementMs: 0);

            _coordinator.Initialize(mode, isAiMode: true, Team.White, _host, _matchDriver);

            Assert.That(_coordinator.Current, Is.Null,
                "AI sessions must bypass the clock entirely to preserve search performance, even under a timed mode config.");
        }

        [Test]
        public void Initialize_UnlimitedMode_NeverConstructsAClock()
        {
            _coordinator.Initialize(GameModeConfig.Unlimited, isAiMode: false, Team.White, _host, _matchDriver);

            Assert.That(_coordinator.Current, Is.Null);
        }

        [Test]
        public void Deactivate_ClearsTheClockEvenAfterATimedSessionWasInitialized()
        {
            var mode = new GameModeConfig("Blitz 5|0", baseTimeMs: 5 * 60_000L, incrementMs: 0);
            _coordinator.Initialize(mode, isAiMode: false, Team.White, _host, _matchDriver);
            Assert.That(_coordinator.Current, Is.Not.Null);

            _coordinator.Deactivate();

            Assert.That(_coordinator.Current, Is.Null);
        }

        [Test]
        public void Deactivate_WithNoClockEverInitialized_IsSafeNoOp()
        {
            Assert.DoesNotThrow(() => _coordinator.Deactivate());
        }

        [Test]
        public void PushSnapshotTo_NoClockActive_LeavesSharedStateUntouched()
        {
            var sharedState = ScriptableObject.CreateInstance<ChessTheBetrayal.Events.SharedClockStateSO>();
            try
            {
                _coordinator.PushSnapshotTo(sharedState);

                Assert.That(sharedState.Value, Is.Null,
                    "With no active clock, the shared bridge must be left as-is (not force-cleared or force-set).");
            }
            finally
            {
                Object.DestroyImmediate(sharedState);
            }
        }

        [Test]
        public void PushSnapshotTo_ClockActive_WritesTheCurrentSnapshot()
        {
            var mode = new GameModeConfig("Blitz 5|0", baseTimeMs: 5 * 60_000L, incrementMs: 0);
            _coordinator.Initialize(mode, isAiMode: false, Team.White, _host, _matchDriver);

            var sharedState = ScriptableObject.CreateInstance<ChessTheBetrayal.Events.SharedClockStateSO>();
            try
            {
                _coordinator.PushSnapshotTo(sharedState);

                Assert.That(sharedState.Value, Is.Not.Null);
                Assert.That(sharedState.Value.Value.WhiteRemainingMs, Is.EqualTo(mode.BaseTimeMs));
            }
            finally
            {
                Object.DestroyImmediate(sharedState);
            }
        }

        [Test]
        public void IClockEventHandler_OnClockTimeout_RoutesToTheConstructorDelegate()
        {
            var handler = (IClockEventHandler)_coordinator;

            handler.OnClockTimeout(Team.Black);

            Assert.That(_timedOutTeam, Is.EqualTo(Team.Black));
        }

        [Test]
        public void IClockEventHandler_OnLowTimeWarning_RoutesToTheConstructorDelegate()
        {
            var handler = (IClockEventHandler)_coordinator;

            handler.OnLowTimeWarning(Team.White, 4200L);

            Assert.That(_lowTimeWarning, Is.EqualTo((Team.White, 4200L)));
        }
    }
}
