using System;
using System.Collections.Generic;
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
    /// through a match's lifecycle, and answering move-execution/query requests. Split out of
    /// GameManager — the deepest of the three collaborators, since it is the one
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
        private readonly Action<Vector2Int, Vector2Int> _onExecutorBetrayalActConfirmationRequired;
        private readonly Action<GameModeConfig> _raiseGameModeConfigured;
        private readonly Action _raiseGameStarted;
        private readonly Action _raiseBoardResyncRequired;
        private readonly Action<BoardState> _setSharedBoardState;
        private readonly Action _clearSharedBoardState;
        private readonly Action _raiseGameReset;

        // Drops any move that has been decided but hasn't reached the board yet — see RequestUndo.
        // A delegate rather than a MoveVisualPacingGate reference for the same reason playMove is
        // one: this class orchestrates a match, and "forget what you were about to play" is the
        // whole of what it needs to say to whatever is doing the pacing.
        private readonly Action _abandonQueuedMoves;

        // Shows a takeback ply by ply. Optional: without one, an undo falls back to rebuilding the
        // position instead of playing it backwards.
        private readonly UndoPlaybackSequencer _undoPlayback;

        // Reused across presses to hold the plies an undo just removed, newest first. Only ever
        // read straight back out by RequestUndo before the next press can touch it.
        private readonly List<MoveCommand> _unmadePlies = new List<MoveCommand>(8);

        private IMoveExecutor _moveExecutor;

        // One-shot: set by GameManager (via SetPracticeMatchSettings) after the player confirms the
        // AI Settings panel, consumed and cleared by the very next HandleTeamAnimationComplete. Null
        // means "plain match" — Play from the main menu never touches this, so BetrayalRightAvailable
        // stays at BoardState's own default (true) and SetAIMode is never called.
        private PracticeMatchSettings? _pendingPracticeSettings;

        public GameModeConfig SelectedMode { get; private set; } = GameModeConfig.Unlimited;
        public bool IsAiMode { get; private set; }

        /// <summary>Set once by GameManager after construction from its Inspector-serialized field.
        /// Null skips opening-book play entirely, so the AI searches from move one. Read by both
        /// SetAIMode call sites (the public one below and the Practice-match auto-configure path
        /// in BeginPlay) so either entry point into AI play gets book support.</summary>
        public ChessTheBetrayal.AI.OpeningBook.OpeningBookAsset OpeningBook { get; set; }
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

        /// <summary>True while a takeback is still playing out on the board, during which the position on screen is behind the real one.</summary>
        public bool IsShowingUndo => _undoPlayback != null && _undoPlayback.IsPlayingBack;

        public MatchFlowCoordinator(
            BoardState board, GameSetup setup, MatchDriver matchDriver, Action<MoveCommand> playMove, IChessEngine engine,
            UndoService undoService, AIMatchCoordinator aiCoordinator, ClockCoordinator clockCoordinator,
            GameObject clockHost, int boardSizeX, int boardSizeY, bool logMoves,
            Action<Team> triggerTeamRoulette, Action showTeamSelection, Action showGameModeSelection,
            Action showAIMatchSettings,
            Action<Vector2Int, Vector2Int> onExecutorMoveRejected,
            Action<Vector2Int, Vector2Int, bool> onExecutorPromotionRequired,
            Action<Vector2Int, Vector2Int> onExecutorBetrayalActConfirmationRequired,
            Action<GameModeConfig> raiseGameModeConfigured, Action raiseGameStarted, Action raiseBoardResyncRequired,
            Action<BoardState> setSharedBoardState, Action clearSharedBoardState, Action raiseGameReset,
            Action abandonQueuedMoves = null, UndoPlaybackSequencer undoPlayback = null)
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
            _onExecutorBetrayalActConfirmationRequired = onExecutorBetrayalActConfirmationRequired;
            _raiseGameModeConfigured = raiseGameModeConfigured;
            _raiseGameStarted = raiseGameStarted;
            _raiseBoardResyncRequired = raiseBoardResyncRequired;
            _setSharedBoardState = setSharedBoardState;
            _clearSharedBoardState = clearSharedBoardState;
            _raiseGameReset = raiseGameReset;
            _abandonQueuedMoves = abandonQueuedMoves;
            _undoPlayback = undoPlayback;
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

        /// <summary>
        /// The View finished its 4-second animation. Now we actually build the game state, then
        /// tell the view about it. Thin view-triggered adapter over ConfigureMatch: forwards the
        /// one-shot pending Practice settings and performs the two presentation-facing raises that
        /// only the local-play flow needs (a future server-authoritative caller invokes
        /// ConfigureMatch directly and decides its own readiness/broadcast timing instead).
        /// </summary>
        public void HandleTeamAnimationComplete()
        {
            PracticeMatchSettings? settings = _pendingPracticeSettings;
            _pendingPracticeSettings = null;

            ConfigureMatch(settings);

            _raiseGameModeConfigured(SelectedMode);

            // Populate the shared board bridge before raising the event, so listeners that
            // read the board from the event callback see the live position and not stale data.
            _setSharedBoardState(_board);
            _raiseGameStarted();
        }

        /// <summary>
        /// Pure domain half of match initialization: rolls the board back to the standard
        /// position, rebuilds the move executor, applies the given Practice settings (if any),
        /// and boots the turn state machine into Starting (clock paused, waiting for
        /// BeginPlay). Raises no view-facing events and touches no shared-state bridge — a
        /// future server-authoritative caller can invoke this directly and decide its own
        /// readiness/broadcast timing instead of inheriting the local-play UI sequencing that
        /// HandleTeamAnimationComplete layers on top.
        /// </summary>
        public void ConfigureMatch(PracticeMatchSettings? settings)
        {
            _board.Clear();
            _setup.PlaceStandardPieces(_board, _boardSizeX, _boardSizeY);
            _matchDriver.MoveLog.Clear();
            _matchDriver.ResetTurnAccumulator();
            _undoService?.Clear();
            _undoPlayback?.Clear();

            // Tear down the previous executor if one exists (e.g. the player hit Replay).
            if (_moveExecutor != null)
            {
                _moveExecutor.OnMoveConfirmed -= _playMove;
                _moveExecutor.OnMoveRejected -= _onExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= _onExecutorPromotionRequired;
                _moveExecutor.OnBetrayalActConfirmationRequired -= _onExecutorBetrayalActConfirmationRequired;
                _moveExecutor.OnRetributionSkipConfirmed -= _matchDriver.RequestRetributionSkip;
                _moveExecutor = null;
            }

            _moveExecutor = new LocalMoveExecutor(_board, _engine, () => CurrentPhase, _clockCoordinator, _logMoves);

            _moveExecutor.OnMoveConfirmed += _playMove;
            _moveExecutor.OnMoveRejected += _onExecutorMoveRejected;
            _moveExecutor.OnPromotionRequired += _onExecutorPromotionRequired;
            _moveExecutor.OnBetrayalActConfirmationRequired += _onExecutorBetrayalActConfirmationRequired;
            _moveExecutor.OnRetributionSkipConfirmed += _matchDriver.RequestRetributionSkip;

            // Every match starts with no AI configured, unconditionally, before either branch
            // below runs — only the Practice-settings branch opts back in. This is deliberately
            // NOT "reset in the else branch": a match kind added later (multiplayer, or anything
            // else that reaches this method with no PracticeMatchSettings) inherits a clean slate
            // for free, rather than needing to remember the same reset the plain-match branch
            // needed today. Harmless for the AI branch too — SetAIMode below immediately rebuilds
            // everything this clears.
            IsAiMode = false;
            _aiCoordinator.ClearAIMode();

            // Practice Match Setup was confirmed for this match: apply every board/AI-level choice
            // now, at the one true match-init seam.
            if (settings.HasValue)
            {
                PracticeMatchSettings confirmedSettings = settings.Value;

                _board.BetrayalRightAvailable = confirmedSettings.BetrayalEnabled;
                RetributionSkipAllowed = confirmedSettings.RetributionSkipAllowed;

                Team aiTeam = PlayerTeam == Team.White ? Team.Black : Team.White;
                BetrayalUsage aiBetrayalUsage = confirmedSettings.AiDefendOnly ? BetrayalUsage.DefendOnly : BetrayalUsage.Full;
                SetAIMode(aiTeam, aiBetrayalUsage, confirmedSettings.AiProfileId);

                if (_logMoves) Debug.Log($"[MatchFlowCoordinator] Practice match started. AI={aiTeam}, BetrayalEnabled={confirmedSettings.BetrayalEnabled}, AiBetrayalUsage={aiBetrayalUsage}, SkipAllowed={confirmedSettings.RetributionSkipAllowed}, AiProfileId={confirmedSettings.AiProfileId}. Human plays {PlayerTeam}.");
            }
            else
            {
                RetributionSkipAllowed = true;
            }

            // The clock has to exist before TransitionToPhase runs — the phase transition
            // is what resumes it.
            InitializeClock();

            // Boot into Starting so the clock stays paused until the presentation layer
            // signals ready (see BeginPlay).
            _matchDriver.TransitionToPhase(TurnPhase.Starting);

            if (_logMoves)
            {
                Debug.Log($"[MatchFlowCoordinator] New game configured. Player: {PlayerTeam}. Mode: {SelectedMode.Label}. Phase: {CurrentPhase}");
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
            _undoPlayback?.Clear();

            if (_moveExecutor != null)
            {
                _moveExecutor.OnMoveConfirmed -= _playMove;
                _moveExecutor.OnMoveRejected -= _onExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= _onExecutorPromotionRequired;
                _moveExecutor.OnBetrayalActConfirmationRequired -= _onExecutorBetrayalActConfirmationRequired;
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
        public void BeginPlay()
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
            if (!AcceptsPlayerInput)
            {
                _onExecutorMoveRejected(from, to);
                return;
            }

            _moveExecutor?.RequestMove(from, to);
        }

        /// <summary>
        /// Whether anything the player does can reach the board right now: a phase that takes moves
        /// (standard play, Retribution, Forced Save), a match still running, and no takeback playing
        /// out — while one is, the position on screen is several plies behind the real one.
        /// </summary>
        private bool AcceptsPlayerInput =>
            (CurrentPhase == TurnPhase.Normal
                || CurrentPhase == TurnPhase.RetributionPending
                || CurrentPhase == TurnPhase.ForcedSave)
            && !_board.IsGameOver
            && !IsShowingUndo;

        /// <summary>
        /// The player agreed to the Betrayal Act they were asked about. A question can sit on screen
        /// for as long as they like, and a timed match can run out underneath it — so if the board
        /// has stopped taking input in the meantime, the parked Act is dropped rather than played.
        /// The executor checks the position again for itself either way.
        /// </summary>
        public void ConfirmBetrayalAct()
        {
            if (!AcceptsPlayerInput)
            {
                _moveExecutor?.CancelBetrayalAct();
                return;
            }

            _moveExecutor?.ConfirmBetrayalAct();
        }

        /// <summary>The player backed out. Unconditional — dropping a move needs no permission.</summary>
        public void CancelBetrayalAct() => _moveExecutor?.CancelBetrayalAct();

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
            // The board is showing a takeback in progress, so what is on screen is behind where the
            // pieces actually are. Selecting anything here would be selecting a square by what used
            // to be on it.
            if (IsShowingUndo) return false;

            if (!_matchDriver.CanSelectPiece(position)) return false;
            if (!IsAiMode) return true;

            return _board.GetPiece(position).Team == PlayerTeam;
        }

        public void HandlePromotionChoice(ChessPieceType chosenType) => _moveExecutor?.RequestPromotion(chosenType);

        public void RequestRetributionSkip() => _moveExecutor?.RequestRetributionSkip();

        public void RequestUndo()
        {
            if (_undoService == null) return;

            // Read CanUndo BEFORE popping so we only re-broadcast the board when an undo actually
            // happened — a press with nothing to undo is a no-op both in UndoService and here.
            if (!CanUndo) return;

            // A takeback already being shown owns the board until it finishes. Starting a second
            // one on top would interleave two rewinds, and captured pieces come back in the order
            // they were taken — so the presses have to stay one at a time.
            if (_undoPlayback != null && _undoPlayback.IsPlayingBack) return;

            // Both halves matter, and both must happen before the board moves. Cancelling stops a
            // search (or an already-decided reply) from being delivered; abandoning the queue drops
            // a reply that was delivered but is still waiting out the previous move's animation.
            // Either one left behind gets played against a position that no longer exists.
            _aiCoordinator.CancelInFlightSearch();
            _abandonQueuedMoves?.Invoke();

            _undoService.RequestUndo(IsAiMode, CurrentPhase, PlayerTeam, AiMovesFirst, _unmadePlies);

            // The board has just been rewound, so its ply count is now the last ply that really
            // happened — anything the match report numbered above that describes a move the player
            // took back. Read here rather than before the pop, for the obvious reason that before
            // the pop it still counts the plies we are about to remove.
            _aiCoordinator.NotePliesUnmade(_board.PliesPlayed);

            // The undo mutated only the domain board (pieces unmade, captures restored).
            // BoardVisuals is an incremental animator driven by per-move events, so without being
            // told something it would keep showing the post-move position. Re-point the shared board
            // bridge at the reverted board first, so anything reading it sees the real position.
            _setSharedBoardState(_board);

            if (_undoPlayback != null && _unmadePlies.Count > 0)
            {
                // Whether the position lands in check has to be read here, from the board, while
                // the view is still several plies behind it.
                _undoPlayback.Begin(_unmadePlies, _engine.IsKingInCheck(_board, _board.CurrentTurn));
                return;
            }

            // Nothing to play the takeback with, so fall back to rebuilding the position. The
            // player sees the result rather than the move coming back, which is worse to watch but
            // never wrong.
            _raiseBoardResyncRequired();
        }

        /// <summary>
        /// Configures the session for AI play. AI sessions always run untimed (see InitializeClock).
        /// Call this — and only this — before HandleTeamAnimationComplete/BeginPlay run their
        /// course; IsAiMode/the coordinator's agent being set is what makes TryRequestMove (fired
        /// from BeginPlay and every TurnChangedEvent) not a no-op. Calling it late (after
        /// BeginPlay) simply means the AI won't move until the next turn change — there's no
        /// unsafe half-configured state in between.
        /// </summary>
        public void SetAIMode(Team aiTeam, BetrayalUsage betrayalUsage, string aiProfileId)
        {
            IsAiMode = true;
            SelectedMode = GameModeConfig.Unlimited;

            _aiCoordinator.SetAIMode(aiTeam, betrayalUsage, aiProfileId, OpeningBook);
        }
    }
}
