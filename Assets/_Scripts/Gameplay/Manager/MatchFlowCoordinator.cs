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
        private readonly Action<MoveCommand> _playMove;
        private readonly IChessEngine _engine;
        private readonly UndoService _undoService;
        private readonly AIMatchCoordinator _aiCoordinator;
        private readonly ClockCoordinator _clockCoordinator;
        private readonly GameObject _clockHost;

        private readonly int _boardSizeX;
        private readonly int _boardSizeY;
        private readonly bool _logMoves;

        private readonly Action<Team> _triggerTeamRoulette;
        private readonly Action _showTeamSelection;
        private readonly Action _showGameModeSelection;
        private readonly Action _showAIMatchSettings;
        private readonly Action<Vector2Int, Vector2Int> _onExecutorMoveRejected;
        private readonly Action<Vector2Int, Vector2Int, bool> _onExecutorPromotionRequired;
        private readonly Action<GameModeConfig> _raiseGameModeConfigured;
        private readonly Action _raiseGameStarted;
        private readonly Action<BoardState> _setSharedBoardState;
        private readonly Action _clearSharedBoardState;
        private readonly Action _raiseGameReset;

        private IMoveExecutor _moveExecutor;

        // One-shot: set by GameManager (via SetPracticeMatchSettings) after the player confirms the
        // AI Settings panel, consumed and cleared by the very next HandleTeamAnimationComplete. Null
        // means "plain match" — Play from the main menu never touches this, so BetrayalRightAvailable
        // stays at BoardState's own default (true) and SetAIMode is never called.
        private PracticeMatchSettings? _pendingPracticeSettings;

        public GameModeConfig SelectedMode { get; private set; } = GameModeConfig.Unlimited;
        public bool IsAiMode { get; private set; }
        public Team PlayerTeam { get; private set; } = Team.White;
        public MatchResult LastMatchResult { get; private set; }

        // True when the human drew the first-mover seat this match. When false in an AI match, the AI
        // played the opening move — which, like chess.com, must NOT be undoable (there's no human
        // move beneath it to take back), so the last Undo lands on the human's first turn.
        private bool _humanMovesFirst = true;

        /// <summary>True when the AI made the forced opening move this match — its opening is protected from Undo. See UndoService.</summary>
        private bool AiMovesFirst => IsAiMode && !_humanMovesFirst;

        /// <summary>Whether the HUD should ever offer the Retribution Skip button this match. True
        /// for every non-practice match (the setting doesn't apply); mirrors the practice match's
        /// confirmed choice otherwise. Read by GameManager right after HandleTeamAnimationComplete.</summary>
        public bool RetributionSkipAllowed { get; private set; } = true;

        public TurnPhase CurrentPhase => _matchDriver.CurrentPhase;
        public bool IsGameActive => CurrentPhase != TurnPhase.GameOver;

        /// <summary>True once there's at least one full player turn to undo back to. AI-practice-only — see UndoService.CanUndo.</summary>
        public bool CanUndo => _undoService != null && _undoService.CanUndo(IsAiMode, CurrentPhase, AiMovesFirst);

        /// <summary>How many recorded turns remain on the undo stack (debug logging only; 2 turns = 1 undo press).</summary>
        public int UndoTurnCount => _undoService?.TurnCount ?? 0;

        public MatchFlowCoordinator(
            BoardState board, GameSetup setup, MatchDriver matchDriver, Action<MoveCommand> playMove, IChessEngine engine,
            UndoService undoService, AIMatchCoordinator aiCoordinator, ClockCoordinator clockCoordinator,
            GameObject clockHost, int boardSizeX, int boardSizeY, bool logMoves,
            Action<Team> triggerTeamRoulette, Action showTeamSelection, Action showGameModeSelection,
            Action showAIMatchSettings,
            Action<Vector2Int, Vector2Int> onExecutorMoveRejected,
            Action<Vector2Int, Vector2Int, bool> onExecutorPromotionRequired,
            Action<GameModeConfig> raiseGameModeConfigured, Action raiseGameStarted,
            Action<BoardState> setSharedBoardState, Action clearSharedBoardState, Action raiseGameReset)
        {
            _board = board;
            _setup = setup;
            _matchDriver = matchDriver;
            _playMove = playMove;
            _engine = engine;
            _undoService = undoService;
            _aiCoordinator = aiCoordinator;
            _clockCoordinator = clockCoordinator;
            _clockHost = clockHost;

            _boardSizeX = boardSizeX;
            _boardSizeY = boardSizeY;
            _logMoves = logMoves;

            _triggerTeamRoulette = triggerTeamRoulette;
            _showTeamSelection = showTeamSelection;
            _showGameModeSelection = showGameModeSelection;
            _showAIMatchSettings = showAIMatchSettings;
            _onExecutorMoveRejected = onExecutorMoveRejected;
            _onExecutorPromotionRequired = onExecutorPromotionRequired;
            _raiseGameModeConfigured = raiseGameModeConfigured;
            _raiseGameStarted = raiseGameStarted;
            _setSharedBoardState = setSharedBoardState;
            _clearSharedBoardState = clearSharedBoardState;
            _raiseGameReset = raiseGameReset;
        }

        public void HandleGameModeReceived(GameModeConfig config) => SelectedMode = config;

        /// <summary>
        /// Records the player's confirmed Practice Match Setup choices for the next match to pick
        /// up. Must be called before HandleTeamRollRequested/HandleTeamAnimationComplete run their
        /// course for that match — UIManager's flow already guarantees this ordering, since
        /// AIMatchSettingsUI.OnSettingsConfirmed fires before ShowTeamSelection().
        /// </summary>
        public void SetPracticeMatchSettings(PracticeMatchSettings settings) => _pendingPracticeSettings = settings;

        /// <summary>Records the outcome of the match that just ended, read back by AcknowledgeGameOver via LastMatchResult.</summary>
        public void RecordMatchResult(Team? winningTeam, bool isTimeout) =>
            LastMatchResult = new MatchResult(winningTeam, isTimeout, SelectedMode);

        /// <summary>UI requested a team. We do the domain math and tell the UI what to animate.</summary>
        public void HandleTeamRollRequested()
        {
            (Team playerTeam, Team firstMover) = _setup.RollTeams();
            PlayerTeam = playerTeam;
            _board.CurrentTurn = firstMover;
            _humanMovesFirst = playerTeam == firstMover;

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
                _moveExecutor.OnMoveConfirmed -= _playMove;
                _moveExecutor.OnMoveRejected -= _onExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= _onExecutorPromotionRequired;
                _moveExecutor.OnRetributionSkipConfirmed -= _matchDriver.RequestRetributionSkip;
                _moveExecutor = null;
            }

            _moveExecutor = new LocalMoveExecutor(_board, _engine, () => CurrentPhase, _clockCoordinator, _logMoves);

            _moveExecutor.OnMoveConfirmed += _playMove;
            _moveExecutor.OnMoveRejected += _onExecutorMoveRejected;
            _moveExecutor.OnPromotionRequired += _onExecutorPromotionRequired;
            _moveExecutor.OnRetributionSkipConfirmed += _matchDriver.RequestRetributionSkip;

            // Practice Match Setup was confirmed for this match: apply every board/AI-level choice
            // now, at the one true match-init seam, then consume the one-shot settings so a
            // subsequent Replay/plain match never inherits them by accident.
            if (_pendingPracticeSettings.HasValue)
            {
                PracticeMatchSettings settings = _pendingPracticeSettings.Value;
                _pendingPracticeSettings = null;

                _board.BetrayalRightAvailable = settings.BetrayalEnabled;
                RetributionSkipAllowed = settings.RetributionSkipAllowed;

                Team aiTeam = PlayerTeam == Team.White ? Team.Black : Team.White;
                BetrayalUsage aiBetrayalUsage = settings.AiDefendOnly ? BetrayalUsage.DefendOnly : BetrayalUsage.Full;
                SetAIMode(aiTeam, aiBetrayalUsage);

                if (_logMoves) Debug.Log($"[MatchFlowCoordinator] Practice match started. AI={aiTeam}, BetrayalEnabled={settings.BetrayalEnabled}, AiBetrayalUsage={aiBetrayalUsage}, SkipAllowed={settings.RetributionSkipAllowed}, Difficulty={settings.Difficulty}. Human plays {PlayerTeam}.");
            }
            else
            {
                RetributionSkipAllowed = true;
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
            RetributionSkipAllowed = true;
            _pendingPracticeSettings = null;

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

        /// <summary>Replay destination for AI practice matches — see BackToAIMatchSettingsAction.</summary>
        public void ReturnToAIMatchSettings() => _showAIMatchSettings();

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
                _moveExecutor.OnMoveConfirmed -= _playMove;
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

        /// <summary>
        /// Whether the human may select the piece at this square. Wraps MatchDriver's phase/turn/
        /// ownership check with the Practice-mode rule: in an AI match the human controls ONLY their
        /// own team, so a piece belonging to the AI's side is never selectable even on the AI's turn.
        /// In non-AI matches this is a straight passthrough (hot-seat: whoever's turn it is may move),
        /// so nothing changes for plain Play mode. MatchDriver already confirmed the piece belongs to
        /// the side to move, so comparing that piece's team to PlayerTeam is sufficient — no need to
        /// re-read the board here.
        /// </summary>
        public bool CanSelectPiece(Vector2Int position)
        {
            if (!_matchDriver.CanSelectPiece(position)) return false;
            if (!IsAiMode) return true;

            return _board.GetPiece(position).Team == PlayerTeam;
        }

        public void HandlePromotionChoice(ChessPieceType chosenType) => _moveExecutor?.RequestPromotion(chosenType);

        public void RequestRetributionSkip() => _moveExecutor?.RequestRetributionSkip();

        public void RequestUndo()
        {
            if (_undoService == null) return;

            // Read CanUndo BEFORE popping so we only re-broadcast the board (an expensive full View
            // rebuild) when an undo actually happened — a press with nothing to undo is a no-op both
            // in UndoService and here. Note the search-cancel below must NOT gate on this: it's fine
            // to run either way, and CanUndo doesn't depend on whether a search is in flight.
            if (!CanUndo) return;

            bool aiSearchInFlight = _aiCoordinator.IsSearchInFlight;
            if (aiSearchInFlight)
            {
                _aiCoordinator.CancelInFlightSearch();
            }

            _undoService.RequestUndo(IsAiMode, CurrentPhase, aiSearchInFlight, AiMovesFirst);

            // The undo mutated only the domain board (pieces unmade, captures restored). BoardVisuals
            // is a purely incremental animator driven by per-move events — it has no idea an undo
            // happened — so without this it keeps showing the post-move position. Re-point the shared
            // board bridge at the reverted board and re-raise game-started, which drives BoardVisuals'
            // one full-rebuild path (HandleGameStarted: ClearAllVisuals + SpawnAllPieces). Pieces snap
            // to the reverted position; there's no reverse animation, which is acceptable for undo.
            _setSharedBoardState(_board);
            _raiseGameStarted();
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
