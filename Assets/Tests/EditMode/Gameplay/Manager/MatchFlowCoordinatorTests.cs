using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Gameplay.Manager;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// MatchFlowCoordinator is the AI-13 extraction of GameManager's match setup/teardown/mode
    /// slice — the deepest of the three collaborators, since it orchestrates AIMatchCoordinator +
    /// ClockCoordinator + MatchDriver + UndoService + GameSetup. UIManager-touching operations
    /// (trigger roulette, show team/mode selection) are constructor delegates, exercised here as
    /// simple call-count/argument-capturing stand-ins — this coordinator's job is orchestration,
    /// not UI navigation, so these tests never construct a real UIManager.
    /// </summary>
    [TestFixture]
    public class MatchFlowCoordinatorTests
    {
        private GameObject _host;
        private BoardState _board;
        private MatchDriver _matchDriver;
        private AIMatchCoordinator _aiCoordinator;
        private ClockCoordinator _clockCoordinator;
        private UndoService _undoService;
        private MatchFlowCoordinator _matchFlow;

        private Team? _triggeredRouletteTeam;
        private int _showTeamSelectionCount;
        private int _showGameModeSelectionCount;
        private (Vector2Int from, Vector2Int to)? _rejectedMove;
        private GameModeConfig? _raisedGameModeConfigured;
        private int _raisedGameStartedCount;
        private BoardState _lastSetSharedBoardState;
        private int _clearedSharedBoardStateCount;
        private int _raisedGameResetCount;

        [SetUp]
        public void Setup()
        {
            _host = new GameObject("MatchFlowCoordinatorTests.Host");

            var engine = new ChessEngineAdapter();
            _board = new BoardState(8, 8);
            _matchDriver = new MatchDriver(engine, _board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);

            _undoService = new UndoService(engine, _board, _matchDriver);
            _matchDriver.OnTurnCompleted += _undoService.RecordTurn;

            _aiCoordinator = new AIMatchCoordinator(engine, _board, _matchDriver.PlayMove);
            _clockCoordinator = new ClockCoordinator(new GameSetup(logMoves: false), _ => { }, (_, __) => { });

            _triggeredRouletteTeam = null;
            _showTeamSelectionCount = 0;
            _showGameModeSelectionCount = 0;
            _rejectedMove = null;
            _raisedGameModeConfigured = null;
            _raisedGameStartedCount = 0;
            _lastSetSharedBoardState = null;
            _clearedSharedBoardStateCount = 0;
            _raisedGameResetCount = 0;

            _matchFlow = new MatchFlowCoordinator(
                _board, new GameSetup(logMoves: false), _matchDriver, engine, _undoService, _aiCoordinator, _clockCoordinator,
                _host, boardSizeX: 8, boardSizeY: 8, logMoves: false, BetrayalUsage.Full,
                triggerTeamRoulette: team => _triggeredRouletteTeam = team,
                showTeamSelection: () => _showTeamSelectionCount++,
                showGameModeSelection: () => _showGameModeSelectionCount++,
                onExecutorMoveRejected: (from, to) => _rejectedMove = (from, to),
                onExecutorPromotionRequired: (_, __, ___) => { },
                raiseGameModeConfigured: mode => _raisedGameModeConfigured = mode,
                raiseGameStarted: () => _raisedGameStartedCount++,
                setSharedBoardState: board => _lastSetSharedBoardState = board,
                clearSharedBoardState: () => _clearedSharedBoardStateCount++,
                raiseGameReset: () => _raisedGameResetCount++);
        }

        [TearDown]
        public void TearDown()
        {
            _aiCoordinator.Dispose();
            if (_host != null) Object.DestroyImmediate(_host);
        }

        [Test]
        public void HandleTeamRollRequested_SetsPlayerTeamAndBoardTurnAndTriggersRoulette()
        {
            _matchFlow.HandleTeamRollRequested();

            Assert.That(_triggeredRouletteTeam, Is.Not.Null);
            Assert.That(_matchFlow.PlayerTeam, Is.EqualTo(_triggeredRouletteTeam));
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.White),
                "White always moves first regardless of which seat the human draws.");
        }

        [Test]
        public void HandleTeamAnimationComplete_PlacesStandardPositionAndTransitionsToStarting()
        {
            _matchFlow.HandleTeamRollRequested();

            _matchFlow.HandleTeamAnimationComplete();

            Assert.That(_matchFlow.CurrentPhase, Is.EqualTo(TurnPhase.Starting));
            Assert.That(_board.GetPiece(new Vector2Int(4, 0)).Type, Is.EqualTo(ChessPieceType.King));
            Assert.That(_raisedGameModeConfigured, Is.Not.Null);
            Assert.That(_lastSetSharedBoardState, Is.SameAs(_board));
            Assert.That(_raisedGameStartedCount, Is.EqualTo(1));
        }

        [Test]
        public void HandleTeamAnimationComplete_UnlimitedMode_EnablesAiForTheOppositeTeam()
        {
            _matchFlow.HandleTeamRollRequested();

            _matchFlow.HandleTeamAnimationComplete();

            Assert.That(_matchFlow.IsAiMode, Is.True,
                "The TEMP debug hook enables AI whenever SelectedMode is Unlimited (default at construction).");
        }

        [Test]
        public void StartMatch_FromStartingPhase_TransitionsToNormal()
        {
            _matchFlow.HandleTeamRollRequested();
            _matchFlow.HandleTeamAnimationComplete();
            Assert.That(_matchFlow.CurrentPhase, Is.EqualTo(TurnPhase.Starting));

            _matchFlow.StartMatch();

            Assert.That(_matchFlow.CurrentPhase, Is.EqualTo(TurnPhase.Normal));
        }

        [Test]
        public void StartMatch_NotInStartingPhase_IsANoOp()
        {
            Assert.That(_matchFlow.CurrentPhase, Is.EqualTo(TurnPhase.GameOver));

            _matchFlow.StartMatch();

            Assert.That(_matchFlow.CurrentPhase, Is.EqualTo(TurnPhase.GameOver));
        }

        [Test]
        public void RequestMove_GameOverPhase_RejectsAndNeverReachesTheExecutor()
        {
            var from = new Vector2Int(0, 1);
            var to = new Vector2Int(0, 2);

            _matchFlow.RequestMove(from, to);

            Assert.That(_rejectedMove, Is.EqualTo((from, to)));
        }

        [Test]
        public void RequestMove_NormalPhase_LegalMove_UpdatesTheBoard()
        {
            _matchFlow.HandleTeamRollRequested();
            _matchFlow.HandleTeamAnimationComplete();
            _matchFlow.StartMatch();

            var from = new Vector2Int(0, 1);
            var to = new Vector2Int(0, 3);
            _matchFlow.RequestMove(from, to);

            Assert.That(_rejectedMove, Is.Null, "A legal opening pawn push must not be rejected.");
            Assert.That(_board.GetPiece(to).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black));
        }

        [Test]
        public void HandleGameReset_TearsDownAndBroadcastsPresentationResetAndResetsMode()
        {
            _matchFlow.HandleTeamRollRequested();
            _matchFlow.HandleTeamAnimationComplete();
            _matchFlow.StartMatch();

            _matchFlow.HandleGameReset();

            Assert.That(_matchFlow.CurrentPhase, Is.EqualTo(TurnPhase.GameOver));
            Assert.That(_matchFlow.SelectedMode.IsUnlimited, Is.True);
            Assert.That(_matchFlow.IsAiMode, Is.False);
            Assert.That(_clearedSharedBoardStateCount, Is.EqualTo(1));
            Assert.That(_raisedGameResetCount, Is.EqualTo(1));
        }

        [Test]
        public void StartNewMatch_SetsSelectedModeAndShowsTeamSelection()
        {
            var mode = new GameModeConfig("Blitz 5|0", baseTimeMs: 5 * 60_000L, incrementMs: 0);

            _matchFlow.StartNewMatch(mode);

            Assert.That(_matchFlow.SelectedMode.Label, Is.EqualTo(mode.Label));
            Assert.That(_showTeamSelectionCount, Is.EqualTo(1));
        }

        [Test]
        public void ReturnToModeSelect_ShowsGameModeSelection()
        {
            _matchFlow.ReturnToModeSelect();

            Assert.That(_showGameModeSelectionCount, Is.EqualTo(1));
        }

        [Test]
        public void RecordMatchResult_ThenAcknowledgeReadableViaLastMatchResult()
        {
            _matchFlow.RecordMatchResult(Team.White, isTimeout: false);

            Assert.That(_matchFlow.LastMatchResult.WinningTeam, Is.EqualTo(Team.White));
            Assert.That(_matchFlow.LastMatchResult.IsTimeout, Is.False);
        }

        [Test]
        public void SetAIMode_EnablesAiModeAndForcesUnlimited()
        {
            var mode = new GameModeConfig("Blitz 5|0", baseTimeMs: 5 * 60_000L, incrementMs: 0);
            _matchFlow.HandleGameModeReceived(mode);

            _matchFlow.SetAIMode(Team.Black, BetrayalUsage.Full);

            Assert.That(_matchFlow.IsAiMode, Is.True);
            Assert.That(_matchFlow.SelectedMode.IsUnlimited, Is.True,
                "AI sessions always force Unlimited mode, overriding whatever mode was previously selected.");
        }
    }
}
