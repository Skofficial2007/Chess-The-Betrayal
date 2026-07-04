using System.Collections.Generic;
using UnityEngine;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Logic;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Gameplay.Flow;
using ChessTheBetrayal.UI;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Infrastructure;

namespace ChessTheBetrayal.Gameplay
{
    /// <summary>
    /// The Unity entry point for a match. Owns the MonoBehaviour lifecycle, the Inspector-serialized
    /// configuration (event channels, board size, time-bounty schedule), and the UIManager event
    /// wiring — nothing else. Every actual rules/orchestration responsibility is delegated:
    ///
    /// - <see cref="GameSetup"/> builds a new match: rolls player/first-mover team, places the
    ///   standard starting position, and constructs the clock.
    /// - <see cref="MatchDriver"/> runs an in-progress match: applies moves through
    ///   <see cref="IChessEngine"/>, translates the result into event-channel raises and clock
    ///   calls, evaluates end-of-game conditions, and owns the <see cref="TurnPhase"/> state machine.
    ///
    /// If the board looks wrong before move one, the bug is in GameSetup. If a move resolves
    /// incorrectly once the game is running, the bug is in MatchDriver. If neither, it's here —
    /// most likely a Unity wiring issue (a missing event subscription, a null Inspector reference).
    ///
    /// Betrayal Phase Contract (see MatchDriver.PlayMove for where these are raised):
    /// Domain `TurnPhase`    | Presentation `BetrayalPhase` Raised
    /// ----------------------|------------------------------------
    /// RetributionPending    | Initiated, then RetributionPending
    /// Normal (post-success) | Resolved
    /// Normal/ForcedSave (result.DidDefect) | DefectionOccurred
    /// ForcedSave            | ForcedSaveActive
    /// </summary>
    public class GameManager : MonoBehaviour, IClockEventHandler, IClockSnapshotSource, IMatchFlow
    {
        #region Inspector Fields

        [Header("Board Configuration")]
        [SerializeField, Min(2)] private int boardSizeX = 8;
        [SerializeField, Min(2)] private int boardSizeY = 8;

        [Header("Debug")]
        [SerializeField] private bool logMoves = true;

        [Header("Betrayal Time Bounty (milliseconds)")]
        [SerializeField] private long _betrayalBountyBulletMs = 3_000L;   // Bullet 1|0: +3s
        [SerializeField] private long _betrayalBountyBullet2Ms = 5_000L;   // Bullet 2|1: +5s
        [SerializeField] private long _betrayalBountyBlitzMs = 8_000L;   // Blitz 3|0: +8s
        [SerializeField] private long _betrayalBountyBlitz5Ms = 12_000L;  // Blitz 5|5: +12s
        [SerializeField] private long _betrayalBountyRapidMs = 20_000L;  // Rapid 10|0: +20s
        [SerializeField] private long _betrayalBountyRapid15Ms = 30_000L;  // Rapid 15|10: +30s

        [Header("Shared State")]
        [SerializeField] private ChessTheBetrayal.Events.SharedBoardStateSO _sharedBoardState;
        [SerializeField] private ChessTheBetrayal.Events.SharedClockStateSO _sharedClockState;

        [Header("Event Channels")]
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _gameStartedChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _gameResetChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameOverEventChannel _gameOverChannel;
        [SerializeField] private ChessTheBetrayal.Events.TurnChangedEventChannel _turnChangedChannel;
        [SerializeField] private ChessTheBetrayal.Events.MoveExecutedEventChannel _moveExecutedChannel;
        [SerializeField] private ChessTheBetrayal.Events.MoveRejectedEventChannel _moveRejectedChannel;
        [SerializeField] private ChessTheBetrayal.Events.PromotionRequiredEventChannel _promotionRequiredChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _checkDetectedChannel;
        [SerializeField] private ChessTheBetrayal.Events.LowTimeAlertEventChannel _lowTimeAlertChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameModeConfiguredEventChannel _gameModeConfiguredChannel;
        [SerializeField] private ChessTheBetrayal.Events.BetrayalEventChannel _betrayalChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _matchStartRequestedChannel;

        #endregion

        #region Public Properties

        /// <summary>
        /// The board. All game logic reads from and writes to this.
        /// </summary>
        public BoardState LiveBoard { get; private set; }

        /// <summary>
        /// Which team the human player is controlling.
        /// </summary>
        public Team PlayerTeam { get; private set; } = Team.White;

        /// <summary>
        /// Ordered ply-by-ply record of the current match, including Betrayal sub-phase moves.
        /// Call MoveLog.DumpToString() to get a paste-ready bug report of every move played —
        /// this is the authoritative source for "what actually happened," independent of whatever
        /// scattered Debug.Log lines fired along the way.
        /// </summary>
        public ChessTheBetrayal.Core.Match.MatchMoveLog MoveLog => _matchDriver.MoveLog;

        /// <summary>
        /// The current phase of the turn (Normal, Betrayal sub-phases, GameOver, etc.).
        /// Delegated to MatchDriver, which owns the actual state machine.
        /// </summary>
        public TurnPhase CurrentPhase => _matchDriver.CurrentPhase;

        /// <summary>
        /// True if a game is in progress. Used by UI and input scripts to know whether to respond to player actions.
        /// </summary>
        public bool IsGameActive => CurrentPhase != TurnPhase.GameOver;

        #endregion

        #region Private Fields

        // Handles move validation. Swap this out for NetworkMoveExecutor to go online.
        private IMoveExecutor _moveExecutor;

        // Background-thread search agent. Null until SetAIMode() constructs it — most sessions
        // (human vs human) never touch this at all. Ticked from Update() so OnMoveDecided always
        // fires on the main thread (see AsyncAIAgent's class doc for why that's mandatory).
        // Not yet wired to actually request/play moves — see TryRequestAIMove().
        private IAIAgent _aiAgent;
        private Team _aiTeam;

        // Instance-scoped rules engine (move generation, check detection, the Betrayal state
        // machine). Shared by GameSetup and MatchDriver so the exact same seam can be handed to
        // a server or an AI without a Unity singleton.
        private readonly IChessEngine _engine = new ChessEngineAdapter();

        // Constructed in Awake(), not as a field initializer — [SerializeField] Inspector values
        // (like logMoves) aren't populated yet when field initializers run.
        private GameSetup _setup;
        private MatchDriver _matchDriver;

        // Reused across calls so we're not allocating a new list every time someone hovers over a piece.
        private readonly List<MoveCommand> _legalMoves = new List<MoveCommand>(32);

        // ── Clock System ──────────────────────────────────────────────────────────────
        private GameModeConfig      _selectedMode = GameModeConfig.Unlimited;
        private bool                _isAIMode     = false;
        private ChessClock          _clock;
        private GameClockController _clockController;

        private UnityDomainLogger _domainLogger;

        // Composition root binding: this is the only line that changes between the prototype,
        // AI, and multiplayer game-contexts. UI never branches on which one is active.
        private readonly IPostGameAction _postGameAction = new BackToModeSelectAction();
        private MatchResult _lastMatchResult;

        // Resolved once in Start() — every consumer here reads it from an event callback or a
        // later lifecycle method, all of which run after every MonoBehaviour's Awake() (see
        // Bootstrap's doc comment for why no explicit execution order is required).
        private UIManager _uiManager;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            ServiceLocator.Instance.Register(this);

            ValidateRequiredFields();

            LiveBoard = new BoardState(boardSizeX, boardSizeY);
            _setup = new GameSetup(logMoves);

            // Construct and inject the domain logger before any engine method fires.
            _domainLogger = new UnityDomainLogger(
                verbose: logMoves,
                onFatalError: evt =>
                {
                    // Reserved: Route fatal domain errors to a UI toast in a future sprint.
                    // UIManager.Instance?.ShowDomainErrorToast(evt.Code.ToString());
                });

            ChessEngine.Initialize(_domainLogger);

            _matchDriver = new MatchDriver(
                _engine,
                LiveBoard,
                logMoves,
                _domainLogger,
                _gameOverChannel,
                _turnChangedChannel,
                _moveExecutedChannel,
                _moveRejectedChannel,
                _checkDetectedChannel,
                _betrayalChannel);

            _matchDriver.SetBountyConfig(new BetrayalBountyConfig(
                _betrayalBountyBulletMs,
                _betrayalBountyBullet2Ms,
                _betrayalBountyBlitzMs,
                _betrayalBountyBlitz5Ms,
                _betrayalBountyRapidMs,
                _betrayalBountyRapid15Ms));

            _gameOverChannel?.Register(OnGameOverRaised);
            _matchStartRequestedChannel?.Register(StartMatch);
        }

        private void Start()
        {
            if (!ServiceLocator.Instance.TryResolve(out _uiManager))
            {
                Debug.LogError("[GameManager] UIManager was never registered! Make sure UIManager exists in the scene.");
                return;
            }

            _uiManager.OnTeamRollRequested += HandleTeamRollRequested;
            _uiManager.OnTeamAnimationComplete += HandleTeamAnimationComplete;
            _uiManager.OnGameReset += HandleGameReset;
            _uiManager.OnPromotionSelected += HandlePromotionChoice;
            _uiManager.OnGameModeSelected += HandleGameModeReceived;
            _uiManager.OnRetributionSkipRequested += RequestRetributionSkip;

            if (logMoves)
            {
                Debug.Log("[GameManager] Initialized and ready.");
            }
        }

        private void OnDestroy()
        {
            TearDownAIAgent();

            if (_uiManager != null)
            {
                _uiManager.OnTeamRollRequested -= HandleTeamRollRequested;
                _uiManager.OnTeamAnimationComplete -= HandleTeamAnimationComplete;
                _uiManager.OnGameReset -= HandleGameReset;
                _uiManager.OnPromotionSelected -= HandlePromotionChoice;
                _uiManager.OnGameModeSelected -= HandleGameModeReceived;
                _uiManager.OnRetributionSkipRequested -= RequestRetributionSkip;
            }

            _gameOverChannel?.Unregister(OnGameOverRaised);
            _matchStartRequestedChannel?.Unregister(StartMatch);

            // Reset the static engine logger to the safe default to prevent scene-reload issues.
            ChessEngine.Initialize(NullDomainLogger.Instance);
        }

        private void OnGameOverRaised(ChessTheBetrayal.Events.Payloads.GameOverPayload payload)
        {
            _lastMatchResult = new MatchResult(payload.WinningTeam, payload.IsTimeout, _selectedMode);
        }

        /// <summary>
        /// Loud-fails on any unassigned Inspector reference at Play-mode start instead of letting
        /// a missing channel silently no-op later. See InspectorGuard.
        /// </summary>
        private void ValidateRequiredFields()
        {
            InspectorGuard.Require(_sharedBoardState, nameof(_sharedBoardState), this);
            InspectorGuard.Require(_sharedClockState, nameof(_sharedClockState), this);
            InspectorGuard.Require(_gameStartedChannel, nameof(_gameStartedChannel), this);
            InspectorGuard.Require(_gameResetChannel, nameof(_gameResetChannel), this);
            InspectorGuard.Require(_gameOverChannel, nameof(_gameOverChannel), this);
            InspectorGuard.Require(_turnChangedChannel, nameof(_turnChangedChannel), this);
            InspectorGuard.Require(_moveExecutedChannel, nameof(_moveExecutedChannel), this);
            InspectorGuard.Require(_moveRejectedChannel, nameof(_moveRejectedChannel), this);
            InspectorGuard.Require(_promotionRequiredChannel, nameof(_promotionRequiredChannel), this);
            InspectorGuard.Require(_checkDetectedChannel, nameof(_checkDetectedChannel), this);
            InspectorGuard.Require(_lowTimeAlertChannel, nameof(_lowTimeAlertChannel), this);
            InspectorGuard.Require(_gameModeConfiguredChannel, nameof(_gameModeConfiguredChannel), this);
            InspectorGuard.Require(_betrayalChannel, nameof(_betrayalChannel), this);
            InspectorGuard.Require(_matchStartRequestedChannel, nameof(_matchStartRequestedChannel), this);
        }

        private void Update()
        {
            // NON-NEGOTIABLE: Logger flush must remain first.
            _domainLogger?.FlushToUnityConsole();

            // Write the latest clock state to the shared bridge every frame.
            // Skipped in unlimited/AI mode, where there's no controller and nothing to report.
            if (_clockController != null)
            {
                _sharedClockState?.Set(GetCurrentClockSnapshot());
            }

            // Pump the AI agent so a completed background search hands its move back to us on the
            // main thread. No-op until SetAIMode() constructs _aiAgent and something starts calling
            // TryRequestAIMove() at the right turn boundary.
            if (_aiAgent is AsyncAIAgent asyncAgent)
            {
                asyncAgent.Tick();
            }
        }

        // Named methods (rather than lambdas) so we can unsubscribe cleanly.
        private void OnExecutorMoveRejected(Vector2Int from, Vector2Int to) =>
            _moveRejectedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.MoveRejectedPayload(from, to));

        private void OnExecutorPromotionRequired(Vector2Int from, Vector2Int to)
        {
            _promotionRequiredChannel?.Raise(new ChessTheBetrayal.Events.Payloads.PromotionRequiredPayload(from, to));
        }

        #endregion

        #region Game Flow

        private void HandleGameModeReceived(GameModeConfig config)
        {
            _selectedMode = config;
        }

        /// <summary>
        /// UI requested a team. We do the domain math and tell the UI what to animate.
        /// </summary>
        private void HandleTeamRollRequested()
        {
            (Team playerTeam, Team firstMover) = _setup.RollTeams();
            PlayerTeam = playerTeam;
            LiveBoard.CurrentTurn = firstMover;

            // Pass the decision back to the View to play the blind roulette animation
            _uiManager.TriggerTeamRoulette(PlayerTeam);
        }

        /// <summary>
        /// The View finished its 4-second animation. Now we actually build the game state.
        /// </summary>
        private void HandleTeamAnimationComplete()
        {
            LiveBoard.Clear();
            _setup.PlaceStandardPieces(LiveBoard, boardSizeX, boardSizeY);
            _matchDriver.MoveLog.Clear();

            // Tear down the previous executor if one exists (e.g. the player hit Replay).
            if (_moveExecutor != null)
            {
                _moveExecutor.OnMoveConfirmed -= _matchDriver.PlayMove;
                _moveExecutor.OnMoveRejected -= OnExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= OnExecutorPromotionRequired;
                _moveExecutor.OnRetributionSkipConfirmed -= _matchDriver.RequestRetributionSkip;
                _moveExecutor = null;
            }

            _moveExecutor = new LocalMoveExecutor(LiveBoard, _engine, () => CurrentPhase, this, logMoves);

            _moveExecutor.OnMoveConfirmed += _matchDriver.PlayMove;
            _moveExecutor.OnMoveRejected += OnExecutorMoveRejected;
            _moveExecutor.OnPromotionRequired += OnExecutorPromotionRequired;
            _moveExecutor.OnRetributionSkipConfirmed += _matchDriver.RequestRetributionSkip;

            // FIX: Initialize the clock BEFORE transitioning to normal phase.
            // This ensures TransitionToPhase() successfully calls _clock.Resume() and starts White's timer immediately.
            InitializeClock();

            // FIX: Boot into Starting phase. Clock remains paused until presentation layer signals ready.
            _matchDriver.TransitionToPhase(TurnPhase.Starting);

            _gameModeConfiguredChannel?.Raise(_selectedMode);

            // Write the live board reference to the shared bridge BEFORE raising the event.
            _sharedBoardState?.Set(LiveBoard);
            _gameStartedChannel?.Raise();

            if (logMoves)
            {
                Debug.Log($"[GameManager] New game started. Player: {PlayerTeam}. Mode: {_selectedMode.Label}. Phase: {CurrentPhase}");
            }
        }

        /// <summary>
        /// Called when the player hits Exit. Clears everything and returns to the main menu,
        /// resetting the mode since there's no next match to carry it into.
        /// </summary>
        private void HandleGameReset()
        {
            TearDownCurrentMatch();
            BroadcastPresentationReset();

            _selectedMode = GameModeConfig.Unlimited;
            _isAIMode     = false;

            _gameModeConfiguredChannel?.Raise(GameModeConfig.Unlimited);

            if (logMoves)
            {
                Debug.Log("[GameManager] Game reset. Phase: GameOver");
            }
        }

        /// <summary>
        /// Called by GameOverUI (via UIManager) when the player dismisses the Game Over screen.
        /// Delegates to whichever IPostGameAction is bound for this game-context — the prototype
        /// binds BackToModeSelectAction, so this always lands on Mode Select, never a hidden default.
        /// </summary>
        public void HandleGameOverAcknowledged()
        {
            _postGameAction.Execute(this, _lastMatchResult);
        }

        #region IMatchFlow

        void IMatchFlow.TearDownCurrentMatch()
        {
            TearDownCurrentMatch();
            BroadcastPresentationReset();
        }

        void IMatchFlow.StartNewMatch(GameModeConfig mode)
        {
            _selectedMode = mode;
            _uiManager.ShowTeamSelection();
        }

        void IMatchFlow.ReturnToModeSelect()
        {
            _uiManager.ShowGameModeSelection();
        }

        /// <summary>
        /// Domain-only teardown: unwires the move executor, stops the clock, and drops the
        /// state machine into GameOver. Deliberately does NOT touch presentation (camera, shared
        /// board bridge) — callers that need the view reset too must also call
        /// BroadcastPresentationReset(), so the two concerns stay separately named even when
        /// they're sequenced together.
        /// </summary>
        private void TearDownCurrentMatch()
        {
            LiveBoard.Clear();
            TearDownAIAgent();

            if (_moveExecutor != null)
            {
                _moveExecutor.OnMoveConfirmed -= _matchDriver.PlayMove;
                _moveExecutor.OnMoveRejected -= OnExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= OnExecutorPromotionRequired;
                _moveExecutor.OnRetributionSkipConfirmed -= _matchDriver.RequestRetributionSkip;
                _moveExecutor = null;
            }

            if (_clockController != null)
            {
                _clockController.Deactivate();
                _clockController = null;
            }

            _clock = null;

            _matchDriver.TransitionToPhase(TurnPhase.GameOver);
        }

        /// <summary>
        /// Clears the shared board bridge and raises _gameResetChannel, whose real consumer is
        /// CameraController (wired in the Inspector) snapping back to its neutral/menu shot.
        /// This is presentation cleanup, not domain teardown — kept separate from
        /// TearDownCurrentMatch() so a future domain-only caller (e.g. a headless/server match
        /// flow) doesn't pull in a View-layer side effect by accident.
        /// </summary>
        private void BroadcastPresentationReset()
        {
            _sharedBoardState?.Clear();
            _gameResetChannel?.Raise();
        }

        #endregion

        /// <summary>
        /// Called by the presentation layer when all intro animations are finished.
        /// Unlocks the board, allowing pieces to move and starting the active player's clock.
        /// </summary>
        public void StartMatch()
        {
            if (CurrentPhase == TurnPhase.Starting)
            {
                _matchDriver.TransitionToPhase(TurnPhase.Normal);
                if (logMoves) Debug.Log("[GameManager] Match officially started. Clock running.");
            }
        }

        /// <summary>
        /// Builds the clock via GameSetup and hands it to MatchDriver. Bypassed entirely during
        /// AI sessions to preserve engine search performance.
        /// </summary>
        private void InitializeClock()
        {
            (_clock, _clockController) = _setup.InitializeClock(
                _selectedMode, _isAIMode, LiveBoard.CurrentTurn, this, gameObject, _clockController);

            _matchDriver.AttachClock(_clock, _clockController, _selectedMode);
        }

        #endregion

        #region Move Execution

        /// <summary>
        /// Sends a move request to the executor. The result comes back through events —
        /// either OnMoveExecuted (if legal) or OnMoveRejected (if not).
        /// </summary>
        public void RequestMove(Vector2Int from, Vector2Int to)
        {
            // --- BETRAYAL MECHANIC FIX ---
            // Allow inputs during standard play, Retribution, and Forced Save phases.
            if ((CurrentPhase != TurnPhase.Normal && CurrentPhase != TurnPhase.RetributionPending && CurrentPhase != TurnPhase.ForcedSave) || LiveBoard.IsGameOver)
            {
                _moveRejectedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.MoveRejectedPayload(from, to));
                return;
            }

            _moveExecutor?.RequestMove(from, to);
        }

        private void HandlePromotionChoice(ChessPieceType chosenType) => _moveExecutor?.RequestPromotion(chosenType);

        /// <summary>
        /// UI entry point for the HUD's Skip button (visible only during RetributionPending).
        /// Sends intent to the executor, which validates the phase before forwarding to
        /// MatchDriver — GameManager never resolves the Betrayal sub-machine itself.
        /// </summary>
        public void RequestRetributionSkip() => _moveExecutor?.RequestRetributionSkip();

        #endregion

        #region Public Query Methods

        /// <summary>
        /// Returns all legal moves for a piece at the given position.
        /// SelectionController and MoveHighlightView call this to know which squares to highlight.
        /// Returns an empty list if it's not that team's turn or the game isn't active.
        /// </summary>
        public IReadOnlyList<MoveCommand> GetLegalMovesAt(Vector2Int position)
        {
            _matchDriver.GetLegalMovesAt(position, _legalMoves);
            return _legalMoves;
        }

        public bool CanSelectPiece(Vector2Int position) => _matchDriver.CanSelectPiece(position);

        /// <summary>
        /// Returns a value-type snapshot of the clock state, or null if untimed/AI mode.
        /// </summary>
        public ClockState? GetCurrentClockSnapshot()
        {
            return _clockController != null ? (ClockState?)_clockController.CurrentState : null;
        }

        /// <summary>
        /// IClockSnapshotSource implementation. LocalMoveExecutor is handed `this` instead of
        /// reaching into GameManager.Instance, so it never depends on the singleton directly.
        /// </summary>
        ClockState? IClockSnapshotSource.Current => GetCurrentClockSnapshot();

        /// <summary>
        /// Configures the session for AI participation, enforcing the performance bypass invariant.
        /// Constructs the background-thread search agent, but does not start a search — nothing
        /// calls TryRequestAIMove() yet, so this is safe to call without changing any current
        /// human-vs-human behavior. Wiring the actual turn-trigger is deliberately deferred until
        /// the full AI flow is being implemented.
        /// </summary>
        public void SetAIMode(Team aiTeam = Team.Black, BetrayalUsage betrayalUsage = BetrayalUsage.Full)
        {
            _isAIMode     = true;
            _selectedMode = GameModeConfig.Unlimited;
            _aiTeam       = aiTeam;

            TearDownAIAgent();

            var agent = new AsyncAIAgent(
                _engine,
                new BetrayalAwareEvaluator(),
                AISearchSettings.Ultimate(betrayalUsage));

            agent.OnMoveDecided += HandleAIMoveDecided;
            _aiAgent = agent;
        }

        /// <summary>
        /// Feeds the AI's chosen move through the exact same seam a human move takes
        /// (MatchDriver.PlayMove) — the AI never gets a special-cased execution path.
        /// Runs on the main thread: AsyncAIAgent.Tick() only raises this from Update().
        /// </summary>
        private void HandleAIMoveDecided(MoveCommand move) => _matchDriver.PlayMove(move);

        /// <summary>
        /// Call once it's aiTeam's turn in a live match to kick off a background search. Not
        /// invoked anywhere yet — this is the seam the full AI-turn flow will call into.
        /// </summary>
        private void TryRequestAIMove()
        {
            if (_aiAgent == null || LiveBoard.CurrentTurn != _aiTeam || !IsGameActive) return;
            _aiAgent.RequestBestMove(LiveBoard, _aiTeam);
        }

        private void TearDownAIAgent()
        {
            if (_aiAgent is AsyncAIAgent asyncAgent)
            {
                asyncAgent.OnMoveDecided -= HandleAIMoveDecided;
                asyncAgent.Dispose();
            }
            _aiAgent = null;
        }

        #endregion

        #region IClockEventHandler

        public void OnClockTimeout(Team timedOutTeam) => _matchDriver.HandleClockTimeout(timedOutTeam);

        public void OnLowTimeWarning(Team team, long remainingMs)
        {
            _lowTimeAlertChannel?.Raise(new ChessTheBetrayal.Events.Payloads.LowTimeAlertPayload(team, remainingMs));
        }

        #endregion
    }
}
