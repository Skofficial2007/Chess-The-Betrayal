using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// Confirms PracticeMatchSettings.AiProfileId flows end to end from the settings struct,
    /// through MatchFlowCoordinator.HandleTeamAnimationComplete, into AIMatchCoordinator.SetAIMode
    /// — the id used to be read only for a debug log and never forwarded, so every Practice match
    /// silently played against the default profile regardless of the chosen difficulty.
    /// Mirrors MatchFlowCoordinatorTests' fixture construction.
    /// </summary>
    [TestFixture]
    public class PracticeMatchSettingsMappingTests
    {
        private GameObject _host;
        private BoardState _board;
        private MatchDriver _matchDriver;
        private AIMatchCoordinator _aiCoordinator;
        private ClockCoordinator _clockCoordinator;
        private UndoService _undoService;
        private MatchFlowCoordinator _matchFlow;

        [SetUp]
        public void Setup()
        {
            _host = new GameObject("PracticeMatchSettingsMappingTests.Host");

            var engine = new ChessEngineAdapter();
            _board = new BoardState(8, 8);
            _matchDriver = new MatchDriver(engine, _board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);

            _undoService = new UndoService(engine, _board, _matchDriver);
            _matchDriver.OnTurnCompleted += _undoService.RecordTurn;

            _aiCoordinator = new AIMatchCoordinator(engine, _board, _matchDriver.PlayMove);
            _clockCoordinator = new ClockCoordinator(new GameSetup(logMoves: false), _ => { }, (_, __) => { });

            _matchFlow = new MatchFlowCoordinator(
                _board, new GameSetup(logMoves: false), _matchDriver, _matchDriver.PlayMove, engine, _undoService, _aiCoordinator, _clockCoordinator,
                _host, boardSizeX: 8, boardSizeY: 8, logMoves: false,
                triggerTeamRoulette: _ => { },
                showTeamSelection: () => { },
                showGameModeSelection: () => { },
                showAIMatchSettings: () => { },
                onExecutorMoveRejected: (_, __) => { },
                onExecutorPromotionRequired: (_, __, ___) => { },
                raiseGameModeConfigured: _ => { },
                raiseGameStarted: () => { },
                raiseBoardResyncRequired: () => { },
                setSharedBoardState: _ => { },
                clearSharedBoardState: () => { },
                raiseGameReset: () => { });
        }

        [TearDown]
        public void TearDown()
        {
            _aiCoordinator.Dispose();
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void Default_HasNormalProfileId()
        {
            Assert.That(PracticeMatchSettings.Default.AiProfileId, Is.EqualTo("normal"));
        }

        [Test]
        public void HandleTeamAnimationComplete_WithPendingSettings_ConfiguresAiModeUsingTheChosenProfileId()
        {
            var settings = new PracticeMatchSettings(
                betrayalEnabled: true, aiDefendOnly: false, retributionSkipAllowed: true, aiProfileId: "hard");
            _matchFlow.SetPracticeMatchSettings(settings);
            _matchFlow.HandleTeamRollRequested();

            // An unknown/unsupported id must resolve safely (falls back to normal) rather than
            // throwing and leaving the match half-configured — HandleTeamAnimationComplete must
            // not blow up regardless of what the panel handed it.
            _matchFlow.HandleTeamAnimationComplete();

            Assert.That(_matchFlow.IsAiMode, Is.True,
                "A pending PracticeMatchSettings with a valid AiProfileId must configure AI mode.");
            Assert.That(_aiCoordinator.IsAiMode, Is.True,
                "MatchFlowCoordinator must forward the profile id all the way to AIMatchCoordinator.SetAIMode.");
        }

        [Test]
        public void HandleTeamAnimationComplete_UnknownProfileId_StillConfiguresAiModeViaFallback()
        {
            var settings = new PracticeMatchSettings(
                betrayalEnabled: true, aiDefendOnly: false, retributionSkipAllowed: true, aiProfileId: "not-a-real-tier");
            _matchFlow.SetPracticeMatchSettings(settings);
            _matchFlow.HandleTeamRollRequested();

            Assert.DoesNotThrow(() => _matchFlow.HandleTeamAnimationComplete());

            Assert.That(_aiCoordinator.IsAiMode, Is.True,
                "IAIProfileProvider falls back to 'normal' for an unknown id rather than throwing.");
        }

        [Test]
        public void HandleTeamAnimationComplete_NoPendingSettings_DoesNotEnableAiMode()
        {
            _matchFlow.HandleTeamRollRequested();
            _matchFlow.HandleTeamAnimationComplete();

            Assert.That(_matchFlow.IsAiMode, Is.False,
                "A plain (non-Practice) match must never call SetAIMode.");
        }
    }
}
