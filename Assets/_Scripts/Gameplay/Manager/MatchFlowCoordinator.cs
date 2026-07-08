using System;
using UnityEngine;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Gameplay.Interaction;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Owns match setup, teardown, and mode/session state: rolling teams, placing the starting
    /// position, constructing/tearing down the move executor, driving the AI/clock coordinators
    /// through a match's lifecycle, and answering move-execution/query requests. Extracted from
    /// GameManager (AI-13) — the deepest and last of the three collaborators, since it is the one
    /// that actually orchestrates the other two (AIMatchCoordinator, ClockCoordinator) plus
    /// MatchDriver/UndoService/GameSetup. GameManager itself shrinks to Unity lifecycle,
    /// Inspector-serialized configuration, and composition-root wiring only.
    ///
    /// Takes UIManager-touching operations as constructor delegates (triggerTeamRoulette,
    /// showTeamSelection, showGameModeSelection) rather than a UIManager reference — this
    /// coordinator's job is match orchestration, not View/UI navigation, so those three calls stay
    /// narrow seams same as every other cross-boundary call in this split (see AIMatchCoordinator's
    /// playMove delegate, ClockCoordinator's onTimeout/onLowTime delegates).
    /// </summary>
    public sealed class MatchFlowCoordinator
    {
        private readonly BoardState _board;
        private readonly GameSetup _setup;
        private readonly MatchDriver _matchDriver;
        private readonly IChessEngine _engine;
        private readonly UndoService _undoService;
        private readonly AIMatchCoordinator _aiCoordinator;
        private readonly ClockCoordinator _clockCoordinator;
        private readonly GameObject _clockHost;

        private readonly int _boardSizeX;
        private readonly int _boardSizeY;
        private readonly bool _logMoves;
        private readonly BetrayalUsage _debugAiBetrayalUsage;

        private readonly Action<Team> _triggerTeamRoulette;
        private readonly Action _showTeamSelection;
        private readonly Action _showGameModeSelection;
        private readonly Action<Vector2Int, Vector2Int> _onExecutorMoveRejected;
        private readonly Action<Vector2Int, Vector2Int, bool> _onExecutorPromotionRequired;
        private readonly Action<GameModeConfig> _raiseGameModeConfigured;
        private readonly Action _raiseGameStarted;
        private readonly Action<BoardState> _setSharedBoardState;
        private readonly Action _clearSharedBoardState;
        private readonly Action _raiseGameReset;

        private IMoveExecutor _moveExecutor;

        public GameModeConfig SelectedMode { get; private set; } = GameModeConfig.Unlimited;
        public bool IsAiMode { get; private set; }
        public Team PlayerTeam { get; private set; } = Team.White;
        public MatchResult LastMatchResult { get; private set; }

        public TurnPhase CurrentPhase => _matchDriver.CurrentPhase;
        public bool IsGameActive => CurrentPhase != TurnPhase.GameOver;

        public MatchFlowCoordinator(
            BoardState board, GameSetup setup, MatchDriver matchDriver, IChessEngine engine,
            UndoService undoService, AIMatchCoordinator aiCoordinator, ClockCoordinator clockCoordinator,
            GameObject clockHost, int boardSizeX, int boardSizeY, bool logMoves, BetrayalUsage debugAiBetrayalUsage,
            Action<Team> triggerTeamRoulette, Action showTeamSelection, Action showGameModeSelection,
            Action<Vector2Int, Vector2Int> onExecutorMoveRejected,
            Action<Vector2Int, Vector2Int, bool> onExecutorPromotionRequired,
            Action<GameModeConfig> raiseGameModeConfigured, Action raiseGameStarted,
            Action<BoardState> setSharedBoardState, Action clearSharedBoardState, Action raiseGameReset)
        {
            _board = board;
            _setup = setup;
            _matchDriver = matchDriver;
            _engine = engine;
            _undoService = undoService;
            _aiCoordinator = aiCoordinator;
            _clockCoordinator = clockCoordinator;
            _clockHost = clockHost;

            _boardSizeX = boardSizeX;
            _boardSizeY = boardSizeY;
            _logMoves = logMoves;
            _debugAiBetrayalUsage = debugAiBetrayalUsage;

            _triggerTeamRoulette = triggerTeamRoulette;
            _showTeamSelection = showTeamSelection;
            _showGameModeSelection = showGameModeSelection;
            _onExecutorMoveRejected = onExecutorMoveRejected;
            _onExecutorPromotionRequired = onExecutorPromotionRequired;
            _raiseGameModeConfigured = raiseGameModeConfigured;
            _raiseGameStarted = raiseGameStarted;
            _setSharedBoardState = setSharedBoardState;
            _clearSharedBoardState = clearSharedBoardState;
            _raiseGameReset = raiseGameReset;
        }

        public void HandleGameModeReceived(GameModeConfig config) => SelectedMode = config;

        /// <summary>Records the outcome of the match that just ended, read back by AcknowledgeGameOver via LastMatchResult.</summary>
        public void RecordMatchResult(Team? winningTeam, bool isTimeout) =>
            LastMatchResult = new MatchResult(winningTeam, isTimeout, SelectedMode);

        /// <summary>UI requested a team. We do the domain math and tell the UI what to animate.</summary>
        public void HandleTeamRollRequested()
        {
            (Team playerTeam, Team firstMover) = _setup.RollTeams();
            PlayerTeam = playerTeam;
            _board.CurrentTurn = firstMover;

            _triggerTeamRoulette(PlayerTeam);
        }

        /// <summary>The View finished its 4-second animation. Now we actually build the game state.</summary>
        public void HandleTeamAnimationComplete()
        {
            _board.Clear();
            _setup.PlaceStandardPieces(_board, _boardSizeX, _boardSizeY);
            _matchDriver.MoveLog.Clear();
            _matchDriver.ResetTurnAccumulator();
            _undoService?.Clear();

            // Tear down the previous executor if one exists (e.g. the player hit Replay).
            if (_moveExecutor != null)
            {
                _moveExecutor.OnMoveConfirmed -= _matchDriver.PlayMove;
                _moveExecutor.OnMoveRejected -= _onExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= _onExecutorPromotionRequired;
                _moveExecutor.OnRetributionSkipConfirmed -= _matchDriver.RequestRetributionSkip;
                _moveExecutor = null;
            }

            _moveExecutor = new LocalMoveExecutor(_board, _engine, () => CurrentPhase, _clockCoordinator, _logMoves);

            _moveExecutor.OnMoveConfirmed += _matchDriver.PlayMove;
            _moveExecutor.OnMoveRejected += _onExecutorMoveRejected;
            _moveExecutor.OnPromotionRequired += _onExecutorPromotionRequired;
            _moveExecutor.OnRetributionSkipConfirmed += _matchDriver.RequestRetributionSkip;

            // TEMP DEBUG HOOK (AI-08 manual verification) — remove once a real Settings screen
            // drives SetAIMode. Ultimate mode only; AI takes whichever side the roulette didn't
            // give the human.
            if (SelectedMode.IsUnlimited)
            {
                Team aiTeam = PlayerTeam == Team.White ? Team.Black : Team.White;
                SetAIMode(aiTeam, _debugAiBetrayalUsage);
                if (_logMoves) Debug.Log($"[MatchFlowCoordinator][DEBUG] AI enabled for {aiTeam}, BetrayalUsage={_debugAiBetrayalUsage}. Human plays {PlayerTeam}.");
            }

            // The clock has to exist before TransitionToPhase runs — the phase transition
            // is what resumes it.
            InitializeClock();

            // Boot into Starting so the clock stays paused until the presentation layer
            // signals ready (see StartMatch).
            _matchDriver.TransitionToPhase(TurnPhase.Starting);

            _raiseGameModeConfigured(SelectedMode);

            // Populate the shared board bridge before raising the event, so listeners that
            // read the board from the event callback see the live position and not stale data.
            _setSharedBoardState(_board);
            _raiseGameStarted();

            if (_logMoves)
            {
                Debug.Log($"[MatchFlowCoordinator] New game started. Player: {PlayerTeam}. Mode: {SelectedMode.Label}. Phase: {CurrentPhase}");
            }
        }

        /// <summary>Called when the player hits Exit. Clears everything and returns to the main menu, resetting the mode since there's no next match to carry it into.</summary>
        public void HandleGameReset()
        {
            TearDownCurrentMatch();
            BroadcastPresentationReset();

            SelectedMode = GameModeConfig.Unlimited;
            IsAiMode = false;

            _raiseGameModeConfigured(GameModeConfig.Unlimited);

            if (_logMoves) Debug.Log("[MatchFlowCoordinator] Game reset. Phase: GameOver");
        }

        public void TearDownCurrentMatchAndBroadcastReset()
        {
            TearDownCurrentMatch();
            BroadcastPresentationReset();
        }

        public void StartNewMatch(GameModeConfig mode)
        {
            SelectedMode = mode;
            _showTeamSelection();
        }

        public void ReturnToModeSelect() => _showGameModeSelection();

        /// <summary>
        /// Domain-only teardown: unwires the move executor, stops the clock, and drops the
        /// state machine into GameOver. Deliberately does NOT touch presentation (camera, shared
        /// board bridge) — callers that need the view reset too must also call
        /// BroadcastPresentationReset(), so the two concerns stay separately named even when
        /// they're sequenced together.
        /// </summary>
        private void TearDownCurrentMatch()
        {
            if (_logMoves && _board != null && !_board.IsGameOver)
            {
                Debug.Log($"[MatchFlowCoordinator] Match exited mid-game. Final position:\n{BoardStateDump.ToAscii(_board)}");
                Debug.Log($"[MatchFlowCoordinator] Move log at exit ({_matchDriver.MoveLog.Entries.Count} plies):\n{_matchDriver.MoveLog.DumpToString()}");
            }

            _board.Clear();
            _aiCoordinator.Dispose();

            if (_moveExecutor != null)
            {
                _moveExecutor.OnMoveConfirmed -= _matchDriver.PlayMove;
                _moveExecutor.OnMoveRejected -= _onExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= _onExecutorPromotionRequired;
                _moveExecutor.OnRetributionSkipConfirmed -= _matchDriver.RequestRetributionSkip;
                _moveExecutor = null;
            }

            _clockCoordinator.Deactivate();

            _matchDriver.TransitionToPhase(TurnPhase.GameOver);
        }

        /// <summary>
        /// Clears the shared board bridge and raises the game-reset event, whose real consumer is
        /// CameraController (wired in the Inspector) snapping back to its neutral/menu shot.
        /// This is presentation cleanup, not domain teardown — kept separate from
        /// TearDownCurrentMatch() so a future domain-only caller (e.g. a headless/server match
        /// flow) doesn't pull in a View-layer side effect by accident.
        /// </summary>
        private void BroadcastPresentationReset()
        {
            _clearSharedBoardState();
            _raiseGameReset();
        }

        /// <summary>Called by the presentation layer when all intro animations are finished. Unlocks the board, allowing pieces to move and starting the active player's clock.</summary>
        public void StartMatch()
        {
            if (CurrentPhase != TurnPhase.Starting) return;

            _matchDriver.TransitionToPhase(TurnPhase.Normal);
            if (_logMoves) Debug.Log("[MatchFlowCoordinator] Match officially started. Clock running.");

            // Human-Black path: no TurnChangedEvent precedes the very first ply, so this is
            // the only place that can kick off the AI's opening move.
            _aiCoordinator.TryRequestMove(IsGameActive);
        }

        /// <summary>Builds the clock via ClockCoordinator and hands it to MatchDriver. Bypassed entirely during AI sessions to preserve engine search performance.</summary>
        private void InitializeClock() =>
            _clockCoordinator.Initialize(SelectedMode, IsAiMode, _board.CurrentTurn, _clockHost, _matchDriver);

        public void RequestMove(Vector2Int from, Vector2Int to)
        {
            // Allow inputs during standard play, Retribution, and Forced Save phases.
            if ((CurrentPhase != TurnPhase.Normal && CurrentPhase != TurnPhase.RetributionPending && CurrentPhase != TurnPhase.ForcedSave) || _board.IsGameOver)
            {
                _onExecutorMoveRejected(from, to);
                return;
            }

            _moveExecutor?.RequestMove(from, to);
        }

        public void HandlePromotionChoice(ChessPieceType chosenType) => _moveExecutor?.RequestPromotion(chosenType);

        public void RequestRetributionSkip() => _moveExecutor?.RequestRetributionSkip();

        public void RequestUndo()
        {
            if (_undoService == null) return;

            bool aiSearchInFlight = _aiCoordinator.IsSearchInFlight;
            if (aiSearchInFlight)
            {
                _aiCoordinator.CancelInFlightSearch();
            }

            _undoService.RequestUndo(IsAiMode, CurrentPhase, aiSearchInFlight);
        }

        /// <summary>
        /// Configures the session for AI play. AI sessions always run untimed (see InitializeClock).
        /// Call this — and only this — before HandleTeamAnimationComplete/StartMatch run their
        /// course; IsAiMode/the coordinator's agent being set is what makes TryRequestMove (fired
        /// from StartMatch and every TurnChangedEvent) not a no-op. Calling it late (after
        /// StartMatch) simply means the AI won't move until the next turn change — there's no
        /// unsafe half-configured state in between.
        /// </summary>
        public void SetAIMode(Team aiTeam, BetrayalUsage betrayalUsage)
        {
            IsAiMode = true;
            SelectedMode = GameModeConfig.Unlimited;

            _aiCoordinator.SetAIMode(aiTeam, betrayalUsage);
        }
    }
}
