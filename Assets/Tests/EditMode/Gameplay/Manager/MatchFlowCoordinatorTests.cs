using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Core.Utils;
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
        // Deterministic stand-in for SystemRandomSource — lets a test pin RollTeams' seat/color
        // assignment instead of depending on which side the human happens to draw. See
        // RandomFirstMoverPolicyTests.FakeRandomSource for the identical pattern.
        private sealed class FixedRandomSource : IRandomSource
        {
            private readonly bool _nextBool;
            public FixedRandomSource(bool nextBool) { _nextBool = nextBool; }
            public bool NextBool() => _nextBool;
            public int NextInt(int maxExclusive) => 0;
            public float NextFloat() => 0f;
        }

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
        private int _showAIMatchSettingsCount;
        private (Vector2Int from, Vector2Int to)? _rejectedMove;
        private GameModeConfig? _raisedGameModeConfigured;
        private int _raisedGameStartedCount;
        private int _raisedBoardResyncRequiredCount;
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
            _showAIMatchSettingsCount = 0;
            _rejectedMove = null;
            _raisedGameModeConfigured = null;
            _raisedGameStartedCount = 0;
            _lastSetSharedBoardState = null;
            _clearedSharedBoardStateCount = 0;
            _raisedGameResetCount = 0;
            _raisedBoardResyncRequiredCount = 0;

            _matchFlow = new MatchFlowCoordinator(
                _board, new GameSetup(logMoves: false), _matchDriver, _matchDriver.PlayMove, engine, _undoService, _aiCoordinator, _clockCoordinator,
                _host, boardSizeX: 8, boardSizeY: 8, logMoves: false,
                triggerTeamRoulette: team => _triggeredRouletteTeam = team,
                showTeamSelection: () => _showTeamSelectionCount++,
                showGameModeSelection: () => _showGameModeSelectionCount++,
                showAIMatchSettings: () => _showAIMatchSettingsCount++,
                onExecutorMoveRejected: (from, to) => _rejectedMove = (from, to),
                onExecutorPromotionRequired: (_, __, ___) => { },
                raiseGameModeConfigured: mode => _raisedGameModeConfigured = mode,
                raiseGameStarted: () => _raisedGameStartedCount++,
                raiseBoardResyncRequired: () => _raisedBoardResyncRequiredCount++,
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
        public void HandleTeamAnimationComplete_PlainMatch_NeverEnablesAi()
        {
            _matchFlow.HandleTeamRollRequested();

            _matchFlow.HandleTeamAnimationComplete();

            Assert.That(_matchFlow.IsAiMode, Is.False,
                "Plain Play (no SetPracticeMatchSettings call) must never activate the AI.");
            Assert.That(_matchFlow.RetributionSkipAllowed, Is.True,
                "Skip stays allowed by default outside of Practice matches.");
            Assert.That(_board.BetrayalRightAvailable, Is.True,
                "Board-level Betrayal stays at BoardState's own default when no practice settings were set.");
        }

        [Test]
        public void HandleTeamAnimationComplete_WithPendingPracticeSettings_AppliesEveryChoiceAndConsumesItOnce()
        {
            var settings = new PracticeMatchSettings(
                betrayalEnabled: false, aiDefendOnly: true, retributionSkipAllowed: false, aiProfileId: "hard");
            _matchFlow.SetPracticeMatchSettings(settings);

            _matchFlow.HandleTeamRollRequested();
            _matchFlow.HandleTeamAnimationComplete();

            Assert.That(_matchFlow.IsAiMode, Is.True);
            Assert.That(_board.BetrayalRightAvailable, Is.False);
            Assert.That(_matchFlow.RetributionSkipAllowed, Is.False);

            // One-shot: a second match (e.g. Replay) must not inherit these settings.
            _matchFlow.HandleGameReset();
            _matchFlow.HandleTeamRollRequested();
            _matchFlow.HandleTeamAnimationComplete();

            Assert.That(_matchFlow.IsAiMode, Is.False,
                "SetPracticeMatchSettings is one-shot and must not leak into the next match.");
        }

        [Test]
        public void CanUndo_TrueOnlyAfterAPracticeMatchTurnCompletes()
        {
            // RollTeams randomizes which seat draws White (see HandleTeamRollRequested_..._Test above,
            // which deliberately covers both outcomes) — but this test only cares about the Undo-stack
            // bookkeeping once any legal human move completes, so it pins the roll to "human is White"
            // via a fixed IFirstMoverPolicy/IRandomSource. Without this, the hardcoded White-pawn move
            // below is illegal (and silently rejected) whenever the roll gives the AI White instead.
            var deterministicSetup = new GameSetup(logMoves: false, new FixedRandomSource(nextBool: true), new RandomFirstMoverPolicy());
            var matchFlow = new MatchFlowCoordinator(
                _board, deterministicSetup, _matchDriver, _matchDriver.PlayMove, new ChessEngineAdapter(), _undoService, _aiCoordinator, _clockCoordinator,
                _host, boardSizeX: 8, boardSizeY: 8, logMoves: false,
                triggerTeamRoulette: team => _triggeredRouletteTeam = team,
                showTeamSelection: () => _showTeamSelectionCount++,
                showGameModeSelection: () => _showGameModeSelectionCount++,
                showAIMatchSettings: () => _showAIMatchSettingsCount++,
                onExecutorMoveRejected: (from, to) => _rejectedMove = (from, to),
                onExecutorPromotionRequired: (_, __, ___) => { },
                raiseGameModeConfigured: mode => _raisedGameModeConfigured = mode,
                raiseGameStarted: () => _raisedGameStartedCount++,
                raiseBoardResyncRequired: () => _raisedBoardResyncRequiredCount++,
                setSharedBoardState: board => _lastSetSharedBoardState = board,
                clearSharedBoardState: () => _clearedSharedBoardStateCount++,
                raiseGameReset: () => _raisedGameResetCount++);

            var settings = new PracticeMatchSettings(
                betrayalEnabled: true, aiDefendOnly: false, retributionSkipAllowed: true, aiProfileId: "normal");
            matchFlow.SetPracticeMatchSettings(settings);

            matchFlow.HandleTeamRollRequested();
            Assert.That(matchFlow.PlayerTeam, Is.EqualTo(Team.White), "Test fixture must pin the human to White.");

            matchFlow.HandleTeamAnimationComplete();
            matchFlow.BeginPlay();

            Assert.That(matchFlow.CanUndo, Is.False, "No turn has completed yet.");

            var from = new Vector2Int(0, 1);
            var to = new Vector2Int(0, 3);
            matchFlow.RequestMove(from, to);

            Assert.That(matchFlow.CanUndo, Is.True, "A full turn (the human's opening move) has now completed.");
        }

        [Test]
        public void StartMatch_FromStartingPhase_TransitionsToNormal()
        {
            _matchFlow.HandleTeamRollRequested();
            _matchFlow.HandleTeamAnimationComplete();
            Assert.That(_matchFlow.CurrentPhase, Is.EqualTo(TurnPhase.Starting));

            _matchFlow.BeginPlay();

            Assert.That(_matchFlow.CurrentPhase, Is.EqualTo(TurnPhase.Normal));
        }

        [Test]
        public void StartMatch_NotInStartingPhase_IsANoOp()
        {
            Assert.That(_matchFlow.CurrentPhase, Is.EqualTo(TurnPhase.GameOver));

            _matchFlow.BeginPlay();

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
            _matchFlow.BeginPlay();

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
            _matchFlow.BeginPlay();

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
        public void ReturnToAIMatchSettings_ShowsAIMatchSettings()
        {
            _matchFlow.ReturnToAIMatchSettings();

            Assert.That(_showAIMatchSettingsCount, Is.EqualTo(1));
        }

        [Test]
        public void RecordMatchResult_ThenAcknowledgeReadableViaLastMatchResult()
        {
            _matchFlow.RecordMatchResult(Team.White, isTimeout: false);

            Assert.That(_matchFlow.LastMatchResult.WinningTeam, Is.EqualTo(Team.White));
            Assert.That(_matchFlow.LastMatchResult.IsTimeout, Is.False);
        }

        [Test]
        public void CanSelectPiece_NonAiMatch_AllowsWhicheverSideIsToMove()
        {
            // Plain hot-seat Play: no practice settings, so IsAiMode stays false and the human may
            // move BOTH sides in turn — the prototype behavior that must not regress.
            _matchFlow.HandleTeamRollRequested();
            _matchFlow.HandleTeamAnimationComplete();
            _matchFlow.BeginPlay();

            // White to move: a White pawn is selectable.
            Assert.That(_matchFlow.CanSelectPiece(new Vector2Int(0, 1)), Is.True);

            // Advance to Black's turn; a Black pawn is now selectable too (hot-seat).
            _matchFlow.RequestMove(new Vector2Int(0, 1), new Vector2Int(0, 3));
            Assert.That(_board.CurrentTurn, Is.EqualTo(Team.Black));
            Assert.That(_matchFlow.CanSelectPiece(new Vector2Int(0, 6)), Is.True,
                "In a non-AI match the human controls whichever side is to move.");
        }

        [Test]
        public void CanSelectPiece_AiMatch_HumanControlsOnlyTheirOwnTeam()
        {
            var settings = new PracticeMatchSettings(
                betrayalEnabled: true, aiDefendOnly: false, retributionSkipAllowed: true, aiProfileId: "normal");
            _matchFlow.SetPracticeMatchSettings(settings);
            _matchFlow.HandleTeamRollRequested();
            _matchFlow.HandleTeamAnimationComplete();
            _matchFlow.BeginPlay();

            Assert.That(_matchFlow.IsAiMode, Is.True);

            Team human = _matchFlow.PlayerTeam;
            Team ai = human == Team.White ? Team.Black : Team.White;

            // Put the board on the AI's turn and try to select one of the AI's pieces — must fail,
            // even though it IS that side's turn (MatchDriver alone would have allowed it).
            _board.CurrentTurn = ai;
            Vector2Int aiPawn = ai == Team.White ? new Vector2Int(0, 1) : new Vector2Int(0, 6);
            Assert.That(_matchFlow.CanSelectPiece(aiPawn), Is.False,
                "In a Practice/AI match the human must never be able to select the AI's pieces.");

            // On the human's own turn, their own pawn is selectable as normal.
            _board.CurrentTurn = human;
            Vector2Int humanPawn = human == Team.White ? new Vector2Int(0, 1) : new Vector2Int(0, 6);
            Assert.That(_matchFlow.CanSelectPiece(humanPawn), Is.True,
                "The human must still be able to select their own pieces on their own turn.");
        }

        [Test]
        public void SetAIMode_EnablesAiModeAndForcesUnlimited()
        {
            var mode = new GameModeConfig("Blitz 5|0", baseTimeMs: 5 * 60_000L, incrementMs: 0);
            _matchFlow.HandleGameModeReceived(mode);

            _matchFlow.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            Assert.That(_matchFlow.IsAiMode, Is.True);
            Assert.That(_matchFlow.SelectedMode.IsUnlimited, Is.True,
                "AI sessions always force Unlimited mode, overriding whatever mode was previously selected.");
        }
    }
}
