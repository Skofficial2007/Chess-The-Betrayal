using System.Collections.Generic;
using UnityEngine;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Logic;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Gameplay.Flow;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.UI;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Infrastructure;

namespace ChessTheBetrayal.App
{
    /// <summary>
    /// The Unity entry point for a match. Owns the MonoBehaviour lifecycle, the Inspector-serialized
    /// configuration (event channels, board size, time-bounty schedule), and composition-root
    /// wiring — nothing else. Every actual rules/orchestration responsibility is delegated:
    ///
    /// - <see cref="GameSetup"/> builds a new match: rolls player/first-mover team, places the
    ///   standard starting position, and constructs the clock.
    /// - <see cref="MatchDriver"/> runs an in-progress match: applies moves through
    ///   <see cref="IChessEngine"/>, translates the result into event-channel raises and clock
    ///   calls, evaluates end-of-game conditions, and owns the <see cref="TurnPhase"/> state machine.
    /// - <see cref="MatchFlowCoordinator"/> owns match setup/teardown, mode/AI-flag state, and the
    ///   move executor — the collaborator this class composes and forwards IMatchFlow/IBoardQuery to.
    /// - <see cref="AIMatchCoordinator"/> owns the background AI agent's lifecycle.
    /// - <see cref="ClockCoordinator"/> owns the match clock's lifecycle.
    ///
    /// If the board looks wrong before move one, the bug is in GameSetup. If a move resolves
    /// incorrectly once the game is running, the bug is in MatchDriver. If a match won't start,
    /// tear down, or the AI/clock won't engage, the bug is in one of the four collaborators above.
    /// If none of those, it's here — most likely a Unity wiring issue (a missing event
    /// subscription, a null Inspector reference).
    ///
    /// Betrayal Phase Contract (see MatchDriver.PlayMove for where these are raised):
    /// Domain `TurnPhase`    | Presentation `BetrayalPhase` Raised
    /// ----------------------|------------------------------------
    /// RetributionPending    | Initiated, then RetributionPending
    /// Normal (post-success) | Resolved
    /// Normal/ForcedSave (result.DidDefect) | DefectionOccurred
    /// ForcedSave            | ForcedSaveActive
    /// </summary>
    public class GameManager : MonoBehaviour, IMatchFlow, IBoardQuery
    {
        #region Inspector Fields

        [Header("Board Configuration")]
        [SerializeField, Min(2)] private int boardSizeX = 8;
        [SerializeField, Min(2)] private int boardSizeY = 8;

        [Header("Debug")]
        [SerializeField] private bool logMoves = true;

        [Header("Betrayal Time Bounty (milliseconds)")]
        [SerializeField] private long _betrayalBountyBulletMs = 3_000L;   // Bullet 1|0
        [SerializeField] private long _betrayalBountyBullet2Ms = 5_000L;   // Bullet 2|1
        [SerializeField] private long _betrayalBountyBlitzMs = 8_000L;   // Blitz 3|0
        [SerializeField] private long _betrayalBountyBlitz5Ms = 12_000L;  // Blitz 5|5
        [SerializeField] private long _betrayalBountyRapidMs = 20_000L;  // Rapid 10|0
        [SerializeField] private long _betrayalBountyRapid15Ms = 30_000L;  // Rapid 15|10

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

        /// <summary>The board. All game logic reads from and writes to this.</summary>
        public BoardState LiveBoard { get; private set; }

        /// <summary>Which team the human player is controlling. Delegated to MatchFlowCoordinator.</summary>
        public Team PlayerTeam => _matchFlow.PlayerTeam;

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

        // Owns match setup/teardown, mode/AI-flag state, and the move executor — the deepest of
        // the three AI-13 collaborators, since it orchestrates the other two below plus
        // MatchDriver/UndoService/GameSetup. Constructed in Awake().
        private MatchFlowCoordinator _matchFlow;

        // AI turn-triggering, background search lifecycle, and Undo's cancel-before-pop ordering.
        // Constructed in Awake() (see AIMatchCoordinator's class doc for why it takes a playMove
        // delegate instead of a MatchDriver reference). Null-agent-safe internally — most sessions
        // (human vs human) never call SetAIMode and every other method is then a no-op.
        private AIMatchCoordinator _aiCoordinator;

        // Instance-scoped rules engine (move generation, check detection, the Betrayal state
        // machine). Shared by GameSetup and MatchDriver so the exact same seam can be handed to
        // a server or an AI without a Unity singleton.
        private readonly IChessEngine _engine = new ChessEngineAdapter();

        // Constructed in Awake(), not as a field initializer — [SerializeField] Inspector values
        // (like logMoves) aren't populated yet when field initializers run.
        private GameSetup _setup;
        private MatchDriver _matchDriver;

        // Practice-mode (AI) Undo. Subscribes to _matchDriver.OnTurnCompleted to record each
        // finished turn; null-safe everywhere since human-vs-human sessions never touch it.
        private UndoService _undoService;

        // Reused across calls so we're not allocating a new list every time someone hovers over a piece.
        private readonly List<MoveCommand> _legalMoves = new List<MoveCommand>(32);

        // Clock lifecycle (construct/attach/deactivate/snapshot). Implements IClockEventHandler/
        // IClockSnapshotSource directly — see ClockCoordinator's class doc for why the interfaces
        // live there instead of being forwarded through GameManager.
        private ClockCoordinator _clockCoordinator;

        private UnityDomainLogger _domainLogger;

        // Composition root binding: the pair that changes between game-contexts. Picked per-match
        // in AcknowledgeGameOver based on MatchFlowCoordinator.IsAiMode, since a single session can
        // play both a plain match (Mode Select origin) and a practice match (AI Settings origin) —
        // it's a runtime fact, not something fixed once at construction like the prototype's old
        // single _postGameAction binding.
        private readonly IPostGameAction _backToModeSelect = new BackToModeSelectAction();
        private readonly IPostGameAction _backToAIMatchSettings = new BackToAIMatchSettingsAction();

        // Resolved once in Start() — every consumer here reads it from an event callback or a
        // later lifecycle method, all of which run after every MonoBehaviour's Awake() (see
        // Bootstrap's doc comment for why no explicit execution order is required).
        private UIManager _uiManager;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            ServiceLocator.Instance.Register(this);

            // Register under the Core-owned seams the presentation layer resolves. The locator keys
            // strictly on the static generic type, so each interface needs its own explicit call —
            // this is what lets UI/View/interaction code resolve the match host without referencing
            // the concrete GameManager (which would recreate the assembly cycle this rework removed).
            ServiceLocator.Instance.Register<IBoardQuery>(this);
            ServiceLocator.Instance.Register<IMatchFlow>(this);

            ValidateRequiredFields();

            LiveBoard = new BoardState(boardSizeX, boardSizeY);
            _setup = new GameSetup(logMoves);

            // Construct and inject the domain logger before any engine method fires.
            _domainLogger = new UnityDomainLogger(
                verbose: logMoves,
                onFatalError: evt =>
                {
                    // TODO: route fatal domain errors to a UI toast, e.g.
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

            _undoService = new UndoService(_engine, LiveBoard, _matchDriver);
            _matchDriver.OnTurnCompleted += _undoService.RecordTurn;
            _matchDriver.OnTurnCompleted += OnTurnCompletedForUndo;

            _aiCoordinator = new AIMatchCoordinator(_engine, LiveBoard, _matchDriver.PlayMove, _domainLogger);
            _aiCoordinator.OnSearchException += HandleAISearchException;

            // Continue the AI through its own forced Betrayal sub-sequence (Act -> Retribution, or
            // Defection -> DefensiveOverride): these transitions don't flip the side to move, so no
            // TurnChangedEvent fires — this event is what re-prompts the AI to play the owed move.
            _matchDriver.OnBetrayalMoveRequired += OnBetrayalMoveRequiredForAI;

            _clockCoordinator = new ClockCoordinator(_setup, OnClockTimeout, OnLowTimeWarning);

            _matchFlow = new MatchFlowCoordinator(
                LiveBoard, _setup, _matchDriver, _engine, _undoService, _aiCoordinator, _clockCoordinator,
                gameObject, boardSizeX, boardSizeY, logMoves,
                triggerTeamRoulette: team => _uiManager.TriggerTeamRoulette(team),
                showTeamSelection: () => _uiManager.ShowTeamSelection(),
                showGameModeSelection: () => _uiManager.ShowGameModeSelection(),
                showAIMatchSettings: () => _uiManager.ShowAIMatchSettings(),
                onExecutorMoveRejected: OnExecutorMoveRejected,
                onExecutorPromotionRequired: OnExecutorPromotionRequired,
                raiseGameModeConfigured: mode => _gameModeConfiguredChannel?.Raise(mode),
                raiseGameStarted: () => _gameStartedChannel?.Raise(),
                setSharedBoardState: board => _sharedBoardState?.Set(board),
                clearSharedBoardState: () => _sharedBoardState?.Clear(),
                raiseGameReset: () => _gameResetChannel?.Raise());

            _gameOverChannel?.Register(OnGameOverRaised);
            _matchStartRequestedChannel?.Register(StartMatch);
            _turnChangedChannel?.Register(OnTurnChangedForAI);
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
            _uiManager.OnPracticeMatchSettingsConfirmed += HandlePracticeMatchSettingsConfirmed;
            _uiManager.OnRetributionSkipRequested += RequestRetributionSkip;
            _uiManager.OnUndoRequested += HandleUndoRequested;

            if (logMoves)
            {
                Debug.Log("[GameManager] Initialized and ready.");
            }
        }

        private void OnDestroy()
        {
            if (_aiCoordinator != null)
            {
                _aiCoordinator.OnSearchException -= HandleAISearchException;
                _aiCoordinator.Dispose();
            }

            if (_matchDriver != null && _undoService != null)
            {
                _matchDriver.OnTurnCompleted -= _undoService.RecordTurn;
                _matchDriver.OnTurnCompleted -= OnTurnCompletedForUndo;
            }

            if (_matchDriver != null)
            {
                _matchDriver.OnBetrayalMoveRequired -= OnBetrayalMoveRequiredForAI;
            }

            if (_uiManager != null)
            {
                _uiManager.OnTeamRollRequested -= HandleTeamRollRequested;
                _uiManager.OnTeamAnimationComplete -= HandleTeamAnimationComplete;
                _uiManager.OnGameReset -= HandleGameReset;
                _uiManager.OnPromotionSelected -= HandlePromotionChoice;
                _uiManager.OnGameModeSelected -= HandleGameModeReceived;
                _uiManager.OnPracticeMatchSettingsConfirmed -= HandlePracticeMatchSettingsConfirmed;
                _uiManager.OnRetributionSkipRequested -= RequestRetributionSkip;
                _uiManager.OnUndoRequested -= HandleUndoRequested;
            }

            _gameOverChannel?.Unregister(OnGameOverRaised);
            _matchStartRequestedChannel?.Unregister(StartMatch);
            _turnChangedChannel?.Unregister(OnTurnChangedForAI);

            // Reset the static engine logger to the safe default to prevent scene-reload issues.
            ChessEngine.Initialize(NullDomainLogger.Instance);
        }

        private void OnGameOverRaised(ChessTheBetrayal.Events.Payloads.GameOverPayload payload) =>
            _matchFlow.RecordMatchResult(payload.WinningTeam, payload.IsTimeout);

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
            // Flush queued domain-log lines before any other per-frame work, so they reach
            // the console ahead of anything else this frame produces.
            _domainLogger?.FlushToUnityConsole();

            // Write the latest clock state to the shared bridge every frame.
            // Skipped in unlimited/AI mode, where there's no controller and nothing to report.
            _clockCoordinator.PushSnapshotTo(_sharedClockState);

            // Pump the AI coordinator so a completed background search hands its move back to us
            // on the main thread. No-op until SetAIMode() constructs an agent and something starts
            // calling TryRequestAIMove() at the right turn boundary.
            _aiCoordinator.Tick();
        }

        // TEMP DEBUG (AI-08 manual verification): surface any worker-thread exception.
        private void HandleAISearchException(string exception) =>
            Debug.LogError($"[GameManager][DEBUG] AI search threw:\n{exception}");

        // Named methods (rather than lambdas) so we can unsubscribe cleanly.
        private void OnExecutorMoveRejected(Vector2Int from, Vector2Int to) =>
            _moveRejectedChannel?.Raise(new ChessTheBetrayal.Events.Payloads.MoveRejectedPayload(from, to));

        private void OnExecutorPromotionRequired(Vector2Int from, Vector2Int to, bool isCapture)
        {
            _promotionRequiredChannel?.Raise(new ChessTheBetrayal.Events.Payloads.PromotionRequiredPayload(from, to, isCapture));
        }

        #endregion

        #region Game Flow

        private void HandleGameModeReceived(GameModeConfig config) => _matchFlow.HandleGameModeReceived(config);

        /// <summary>
        /// UI entry point for the Practice Match Setup panel's Done button. Stashes the confirmed
        /// choices on MatchFlowCoordinator — consumed the moment the very next
        /// HandleTeamAnimationComplete runs (see MatchFlowCoordinator.SetPracticeMatchSettings),
        /// which UIManager's flow always sequences after this (OnSettingsConfirmed fires before
        /// ShowTeamSelection, and only then can the roulette/animation sequence reach
        /// OnTeamAnimationComplete).
        /// </summary>
        private void HandlePracticeMatchSettingsConfirmed(PracticeMatchSettings settings) =>
            _matchFlow.SetPracticeMatchSettings(settings);

        private void HandleTeamRollRequested() => _matchFlow.HandleTeamRollRequested();

        private void HandleTeamAnimationComplete()
        {
            _matchFlow.HandleTeamAnimationComplete();

            // Push the settled per-match HUD state now that MatchFlowCoordinator has consumed any
            // pending Practice Match settings — IsAiMode/RetributionSkipAllowed/CanUndo are only
            // meaningful after HandleTeamAnimationComplete returns.
            _uiManager.SetRetributionSkipAllowed(_matchFlow.RetributionSkipAllowed);
            _uiManager.SetUndoVisible(_matchFlow.IsAiMode);
            RefreshUndoInteractable();
        }

        private void HandleGameReset() => _matchFlow.HandleGameReset();

        /// <summary>
        /// Called by GameOverUI (via UIManager, through IMatchFlow) when the player dismisses the
        /// Game Over screen. Picks the post-game action by what kind of match just ended: an AI
        /// practice match returns to AI Settings (it never went through Mode Select), everything
        /// else returns to Mode Select — never a hidden default either way.
        /// </summary>
        public void AcknowledgeGameOver()
        {
            IPostGameAction postGameAction = _matchFlow.IsAiMode ? _backToAIMatchSettings : _backToModeSelect;
            postGameAction.Execute(this, _matchFlow.LastMatchResult);
        }

        #region IMatchFlow

        void IMatchFlow.TearDownCurrentMatch() => _matchFlow.TearDownCurrentMatchAndBroadcastReset();

        void IMatchFlow.StartNewMatch(GameModeConfig mode) => _matchFlow.StartNewMatch(mode);

        void IMatchFlow.ReturnToModeSelect() => _matchFlow.ReturnToModeSelect();

        void IMatchFlow.ReturnToAIMatchSettings() => _matchFlow.ReturnToAIMatchSettings();

        #endregion

        /// <summary>
        /// Called by the presentation layer when all intro animations are finished.
        /// Unlocks the board, allowing pieces to move and starting the active player's clock.
        /// </summary>
        public void StartMatch() => _matchFlow.StartMatch();

        #endregion

        #region Move Execution

        /// <summary>
        /// Sends a move request to the executor. The result comes back through events —
        /// either OnMoveExecuted (if legal) or OnMoveRejected (if not).
        /// </summary>
        public void RequestMove(Vector2Int from, Vector2Int to) => _matchFlow.RequestMove(from, to);

        private void HandlePromotionChoice(ChessPieceType chosenType) => _matchFlow.HandlePromotionChoice(chosenType);

        /// <summary>
        /// UI entry point for the HUD's Skip button (visible only during RetributionPending).
        /// Sends intent to the executor, which validates the phase before forwarding to
        /// MatchDriver — GameManager never resolves the Betrayal sub-machine itself.
        /// </summary>
        public void RequestRetributionSkip() => _matchFlow.RequestRetributionSkip();

        /// <summary>
        /// UI entry point for the HUD's Undo button (visible only in AI practice matches). AI-only
        /// (per UndoService's own gating): a no-op in human-vs-human or any future networked
        /// session. Cancels an in-flight AI search BEFORE popping the board — see
        /// UndoService.RequestUndo's ordering contract.
        /// </summary>
        public void RequestUndo() => _matchFlow.RequestUndo();

        /// <summary>
        /// Wired to UIManager.OnUndoRequested. Performs the undo, then immediately refreshes the
        /// button's interactable state — CanUndo can flip false the moment the stack empties out.
        /// </summary>
        private void HandleUndoRequested()
        {
            RequestUndo();
            RefreshUndoInteractable();

            if (logMoves)
            {
                // One clean line per press: whose turn it is now and how many undos remain
                // (2 recorded turns = 1 more press). Easy to grep as "[Undo]".
                Debug.Log($"[Undo] Undone. Now {LiveBoard.CurrentTurn} to move. {_matchFlow.UndoTurnCount} turn(s) left on stack.");
            }
        }

        /// <summary>
        /// Pushes MatchFlowCoordinator.CanUndo to the HUD. Called after every turn completes (a
        /// turn was just recorded, so CanUndo may have flipped true) and after Undo itself runs (the
        /// stack may have just emptied out). A no-op push in non-AI matches: SetUndoVisible(false)
        /// already hid the button, so the interactable flag underneath is inert.
        /// </summary>
        private void RefreshUndoInteractable() => _uiManager.SetUndoInteractable(_matchFlow.CanUndo);

        /// <summary>
        /// MatchDriver.OnTurnCompleted handler, subscribed alongside UndoService.RecordTurn in
        /// Awake(). _uiManager is only resolved in Start(), but OnTurnCompleted never fires before a
        /// match is actually running (long after Start()), so this ordering is safe.
        /// </summary>
        private void OnTurnCompletedForUndo(IReadOnlyList<MoveCommand> turnMoves) => RefreshUndoInteractable();

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

        public bool CanSelectPiece(Vector2Int position) => _matchFlow.CanSelectPiece(position);

        /// <summary>
        /// Configures the session for AI play. AI sessions always run untimed (see
        /// MatchFlowCoordinator.InitializeClock). Call this — and only this — before
        /// HandleTeamAnimationComplete/StartMatch run their course; the AI coordinator's agent
        /// being set is what makes TryRequestMove (fired from StartMatch and every TurnChangedEvent)
        /// not a no-op. Calling it late (after StartMatch) simply means the AI won't move until the
        /// next turn change — there's no unsafe half-configured state in between.
        /// </summary>
        public void SetAIMode(Team aiTeam = Team.Black, BetrayalUsage betrayalUsage = BetrayalUsage.Full) =>
            _matchFlow.SetAIMode(aiTeam, betrayalUsage);

        /// <summary>
        /// Fires whenever a turn-ending move completes (see MatchDriver.CheckForGameEnd's
        /// GameState.Normal/Check branches). This is what triggers the AI's move after every
        /// human reply — the very first ply (when the AI draws White) has no preceding
        /// TurnChangedEvent, so StartMatch() calls TryRequestAIMove directly for that case.
        /// </summary>
        private void OnTurnChangedForAI(ChessTheBetrayal.Events.Payloads.TurnChangedPayload payload) =>
            _aiCoordinator.TryRequestMove(IsGameActive);

        /// <summary>
        /// Fires when the match enters a forced Betrayal sub-phase (RetributionPending/ForcedSave)
        /// where a side owes a mandatory follow-up move without a turn flip — the domain-level
        /// counterpart to a human being prompted by the UI reacting to the same phase change. Routes
        /// to the same TryRequestMove gate as a normal turn change; the gate's own
        /// _board.CurrentTurn == _aiTeam check means this is a no-op whenever the owed move belongs
        /// to the human, so no team argument needs to be threaded through here.
        /// </summary>
        private void OnBetrayalMoveRequiredForAI(Team owedBy) => _aiCoordinator.TryRequestMove(IsGameActive);

        #endregion

        #region Clock Event Routing

        // Passed to ClockCoordinator's constructor as narrow delegates — GameManager's only
        // remaining clock-shaped responsibility is routing these two events into match-flow/UI,
        // not owning clock lifecycle itself (see ClockCoordinator, which implements
        // IClockEventHandler directly).
        private void OnClockTimeout(Team timedOutTeam) => _matchDriver.HandleClockTimeout(timedOutTeam);

        private void OnLowTimeWarning(Team team, long remainingMs)
        {
            _lowTimeAlertChannel?.Raise(new ChessTheBetrayal.Events.Payloads.LowTimeAlertPayload(team, remainingMs));
        }

        #endregion
    }
}
