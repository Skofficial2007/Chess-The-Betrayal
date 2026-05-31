using System;
using System.Collections.Generic;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Logic;
using ChessTheBetrayal.UI;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;
using ChessTheBetrayal.Core.Diagnostics;

namespace ChessTheBetrayal.Gameplay
{
    /// <summary>
    /// The primary conductor of the game loop. Orchestrates domain logic (BoardState, ChessEngine),
    /// timing systems (ChessClock), and presentation (UI, Visuals).
    /// </summary>
    public class GameManager : MonoBehaviour, IClockEventHandler
    {
        #region Singleton

        public static GameManager Instance { get; private set; }

        #endregion

        #region Inspector Fields

        [Header("Board Configuration")]
        [SerializeField, Min(2)] private int boardSizeX = 8;
        [SerializeField, Min(2)] private int boardSizeY = 8;

        [Header("Debug")]
        [SerializeField] private bool logMoves = true;

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
        /// The current phase of the turn (Normal, Betrayal sub-phases, GameOver, etc.).
        /// </summary>
        public TurnPhase CurrentPhase { get; private set; } = TurnPhase.GameOver;

        /// <summary>
        /// True if a game is in progress. Used by UI and input scripts to know whether to respond to player actions.
        /// </summary>
        public bool IsGameActive => CurrentPhase != TurnPhase.GameOver;

        #endregion

        #region Events

        // Other systems (BoardVisuals, BoardInputController, UIManager) subscribe to these.
        public event Action<BoardState> OnGameStarted;
        public event Action<MoveCommand> OnMoveExecuted;
        public event Action OnTurnChanged;
        public event Action<Team> OnCheck;
        public event Action OnGameReset;

        public event Action<Vector2Int, Vector2Int> OnMoveRejected;
        public event Action<Vector2Int, Vector2Int> OnPromotionRequested;

        /// <summary>
        /// Invoked by the clock system when a player's remaining time drops below the urgency threshold.
        /// Payload: (Team, RemainingMilliseconds)
        /// </summary>
        public event Action<Team, long> OnLowTimeAlert;

        #endregion

        #region Private Fields

        // Handles move validation. Swap this out for NetworkMoveExecutor to go online.
        private IMoveExecutor _moveExecutor;

        // Reused across calls so we're not allocating a new list every time someone hovers over a piece.
        private readonly List<MoveCommand> _legalMoves = new List<MoveCommand>(32);

        // The standard piece order for the back rank, left to right.
        private static readonly ChessPieceType[] StandardBackRank = new ChessPieceType[]
        {
            ChessPieceType.Rook,   ChessPieceType.Knight, ChessPieceType.Bishop,
            ChessPieceType.Queen,  ChessPieceType.King,   ChessPieceType.Bishop,
            ChessPieceType.Knight, ChessPieceType.Rook
        };

        // ── Clock System ──────────────────────────────────────────────────────────────
        private GameModeConfig      _selectedMode = GameModeConfig.Unlimited;
        private bool                _isAIMode     = false;
        private ChessClock          _clock;
        private GameClockController _clockController;

        private UnityDomainLogger _domainLogger;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            LiveBoard = new BoardState(boardSizeX, boardSizeY);

            // Construct and inject the domain logger before any engine method fires.
            _domainLogger = new UnityDomainLogger(
                verbose: logMoves,
                onFatalError: evt =>
                {
                    // Reserved: Route fatal domain errors to a UI toast in a future sprint.
                    // UIManager.Instance?.ShowDomainErrorToast(evt.Code.ToString());
                });
            
            ChessEngine.Initialize(_domainLogger);
        }

        private void Start()
        {
            if (UIManager.Instance == null)
            {
                Debug.LogError("[GameManager] UIManager.Instance is null! Make sure UIManager exists in the scene.");
                return;
            }

            UIManager.Instance.OnTeamRollRequested += HandleTeamRollRequested;
            UIManager.Instance.OnTeamAnimationComplete += HandleTeamAnimationComplete;
            UIManager.Instance.OnGameReset += HandleGameReset;
            UIManager.Instance.OnPromotionSelected += HandlePromotionChoice;
            UIManager.Instance.OnGameModeSelected += HandleGameModeReceived;

            if (logMoves)
            {
                Debug.Log("[GameManager] Initialized and ready.");
            }
        }

        private void OnDestroy()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.OnTeamRollRequested -= HandleTeamRollRequested;
                UIManager.Instance.OnTeamAnimationComplete -= HandleTeamAnimationComplete;
                UIManager.Instance.OnGameReset -= HandleGameReset;
                UIManager.Instance.OnPromotionSelected -= HandlePromotionChoice;
                UIManager.Instance.OnGameModeSelected -= HandleGameModeReceived;
            }

            // Reset the static engine logger to the safe default to prevent scene-reload issues.
            ChessEngine.Initialize(NullDomainLogger.Instance);
        }

        private void Update()
        {
            // NON-NEGOTIABLE: Logger flush must remain first.
            _domainLogger?.FlushToUnityConsole();
            
            // Write the latest clock state to the shared bridge every frame.
            _sharedClockState?.Set(GetCurrentClockSnapshot());
        }

        // Named methods (rather than lambdas) so we can unsubscribe cleanly.
        private void OnExecutorMoveRejected(Vector2Int from, Vector2Int to) => OnMoveRejected?.Invoke(from, to);

        private void OnExecutorPromotionRequired(Vector2Int from, Vector2Int to)
        {
            OnPromotionRequested?.Invoke(from, to);
            UIManager.Instance?.ShowPromotionUI();
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
            // 1. Core Logic dictates the random assignments
            PlayerTeam = UnityEngine.Random.value > 0.5f ? Team.White : Team.Black;
            
            // Fulfill the Pitch Doc Rule: First mover is also completely random, forcing players out of book.
            LiveBoard.CurrentTurn = UnityEngine.Random.value > 0.5f ? Team.White : Team.Black;

            if (logMoves) 
            {
                Debug.Log($"[GameManager] Roll Decided -> Player Team: {PlayerTeam} | First Mover: {LiveBoard.CurrentTurn}");
            }

            // 2. Pass the decision back to the View to play the blind roulette animation
            UIManager.Instance.TriggerTeamRoulette(PlayerTeam);
        }

        /// <summary>
        /// The View finished its 4-second animation. Now we actually build the game state.
        /// </summary>
        private void HandleTeamAnimationComplete()
        {
            LiveBoard.Clear();
            SetupStandardPieces();

            // Tear down the previous executor if one exists (e.g. the player hit Replay).
            if (_moveExecutor != null)
            {
                _moveExecutor.OnMoveConfirmed -= PlayMove;
                _moveExecutor.OnMoveRejected -= OnExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= OnExecutorPromotionRequired;
                _moveExecutor = null;
            }

            _moveExecutor = new LocalMoveExecutor(LiveBoard, logMoves);

            _moveExecutor.OnMoveConfirmed += PlayMove;
            _moveExecutor.OnMoveRejected += OnExecutorMoveRejected;
            _moveExecutor.OnPromotionRequired += OnExecutorPromotionRequired;

            // FIX: Initialize the clock BEFORE transitioning to normal phase.
            // This ensures TransitionToPhase() successfully calls _clock.Resume() and starts White's timer immediately.
            InitializeClock();

            // FIX: Boot into Starting phase. Clock remains paused until presentation layer signals ready.
            TransitionToPhase(TurnPhase.Starting);

            UIManager.Instance?.ConfigureHUDForMode(_selectedMode);

            _sharedBoardState?.Set(LiveBoard);
            OnGameStarted?.Invoke(LiveBoard);

            if (logMoves)
            {
                Debug.Log($"[GameManager] New game started. Player: {PlayerTeam}. Mode: {_selectedMode.Label}. Phase: {CurrentPhase}");
            }
        }

        /// <summary>
        /// Called when the player hits the reset button.
        /// Clears everything and waits for a new team selection.
        /// </summary>
        private void HandleGameReset()
        {
            LiveBoard.Clear();

            if (_moveExecutor != null)
            {
                _moveExecutor.OnMoveConfirmed -= PlayMove;
                _moveExecutor.OnMoveRejected -= OnExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= OnExecutorPromotionRequired;
                _moveExecutor = null;
            }

            if (_clockController != null)
            {
                _clockController.Deactivate();
                _clockController = null;
            }
            
            _clock        = null;
            _selectedMode = GameModeConfig.Unlimited;
            _isAIMode     = false;

            TransitionToPhase(TurnPhase.GameOver);

            UIManager.Instance?.ConfigureHUDForMode(GameModeConfig.Unlimited);

            _sharedBoardState?.Clear();
            OnGameReset?.Invoke();

            if (logMoves)
            {
                Debug.Log("[GameManager] Game reset. Phase: GameOver");
            }
        }

        /// <summary>
        /// Fills the board with pieces in the standard starting configuration.
        /// Does not spawn any GameObjects — that's BoardVisuals' job when it receives OnGameStarted.
        /// </summary>
        private void SetupStandardPieces()
        {
            for (int x = 0; x < boardSizeX; x++)
            {
                LiveBoard.SetPiece(new PieceData(Team.White, StandardBackRank[x], moveDirection: 1, startRow: 0), x, 0);
                LiveBoard.SetPiece(new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1), x, 1);
            }

            for (int x = 0; x < boardSizeX; x++)
            {
                LiveBoard.SetPiece(new PieceData(Team.Black, ChessPieceType.Pawn, moveDirection: -1, startRow: boardSizeY - 2), x, boardSizeY - 2);
                LiveBoard.SetPiece(new PieceData(Team.Black, StandardBackRank[x], moveDirection: -1, startRow: boardSizeY - 1), x, boardSizeY - 1);
            }

            LiveBoard.ComputeFullZobristHash();
        }

        /// <summary>
        /// Called by the presentation layer when all intro animations are finished.
        /// Unlocks the board, allowing pieces to move and starting the active player's clock.
        /// </summary>
        public void StartMatch()
        {
            if (CurrentPhase == TurnPhase.Starting)
            {
                TransitionToPhase(TurnPhase.Normal);
                if (logMoves) Debug.Log("[GameManager] Match officially started. Clock running.");
            }
        }

        /// <summary>
        /// Instantiates the pure-C# clock and attaches the MonoBehaviour controller bridge.
        /// Bypassed entirely during AI sessions to preserve engine search performance.
        /// </summary>
        private void InitializeClock()
        {
            if (_isAIMode || _selectedMode.IsUnlimited)
            {
                _clock           = null;
                _clockController = null;
                return;
            }

            _clock = new ChessClock(_selectedMode, this, LiveBoard.CurrentTurn);

            if (_clockController == null)
            {
                _clockController = gameObject.AddComponent<GameClockController>();
            }

            _clockController.Initialize(_clock);
        }

        #endregion

        #region Move Execution

        /// <summary>
        /// Sends a move request to the executor. The result comes back through events —
        /// either OnMoveExecuted (if legal) or OnMoveRejected (if not).
        /// </summary>
        public void RequestMove(Vector2Int from, Vector2Int to)
        {
            // Only accept moves during normal play.
            if (CurrentPhase != TurnPhase.Normal || LiveBoard.IsGameOver)
            {
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            _moveExecutor?.RequestMove(from, to);
        }

        private void HandlePromotionChoice(ChessPieceType chosenType) => _moveExecutor?.RequestPromotion(chosenType);

        /// <summary>
        /// Applies a validated move to the board and tells everyone who needs to know about it.
        /// Wraps execution in exception boundaries to safely catch domain invariant violations.
        /// </summary>
        private void PlayMove(MoveCommand move)
        {
            if (logMoves) Debug.Log($"[GameManager] Executing: {move}");

            try
            {
                ChessEngine.ApplyMoveToBoard(LiveBoard, move);
            }
            catch (BetrayalRuleViolationException ex)
            {
                // A Betrayal rule was violated — this is a bug at the call site.
                // Log it, reject the move visually, and recover gracefully.
                Debug.LogException(ex);
                OnMoveRejected?.Invoke(move.StartPosition, move.EndPosition);
                return;
            }
            catch (DomainException ex)
            {
                // Any other hard domain invariant violation.
                Debug.LogException(ex);
                return;
            }
            // Note: DO NOT catch (Exception) here. Genuine CLR crashes must surface.

            _clock?.OnMoveMade(move.PieceTeam);

            OnMoveExecuted?.Invoke(move);

            LiveBoard.NextTurn();

            CheckForGameEnd();
        }

        /// <summary>
        /// Checks whether the game has ended (checkmate or stalemate) after a turn completes.
        /// Also fires OnCheck if the next player is in check but still has moves.
        /// </summary>
        private void CheckForGameEnd()
        {
            Team currentTeam = LiveBoard.CurrentTurn;
            ClockState? clockSnapshot = GetCurrentClockSnapshot();

            GameState state = ChessEngine.EvaluateGameState(LiveBoard, currentTeam, clockSnapshot);

            switch (state)
            {
                case GameState.Checkmate:
                    // The winner is whoever just moved, not the team currently being evaluated.
                    Team winner = currentTeam == Team.White ? Team.Black : Team.White;
                    EndGame(winner);
                    break;

                case GameState.Stalemate:
                    EndGame(null); // Draw.
                    break;

                case GameState.Check:
                    OnCheck?.Invoke(currentTeam);
                    OnTurnChanged?.Invoke();
                    break;

                case GameState.Normal:
                    OnTurnChanged?.Invoke();
                    break;

                case GameState.Timeout:
                    // Primary resolution path is GameManager.OnClockTimeout() called directly via interface.
                    break;
            }

            // --- BETRAYAL MECHANIC HOOKS ---
            // When you implement The Betrayal, this is where the phase transitions live.
            // After a normal move resolves, check if the Betrayal was triggered and call:
            //   TransitionToPhase(TurnPhase.RetributionPending)
            // If Retribution fails (no valid executioner), call:
            //   TransitionToPhase(TurnPhase.ResolutionFailed)
            // See TurnPhase in Enum.cs for the full state machine map.
        }

        private void EndGame(Team? winner, bool byTimeout = false)
        {
            LiveBoard.IsGameOver = true;
            LiveBoard.Winner = winner;

            TransitionToPhase(TurnPhase.GameOver);
            UIManager.Instance?.TriggerGameOver(winner, byTimeout);

            if (logMoves) 
            {
                Debug.Log($"[GameManager] Game Over. Winner: {(winner.HasValue ? winner.ToString() : "Draw")}. Timeout: {byTimeout}");
            }
        }

        #endregion

        #region Public Query Methods

        /// <summary>
        /// Returns all legal moves for a piece at the given position.
        /// BoardInputController calls this to know which squares to highlight.
        /// Returns an empty list if it's not that team's turn or the game isn't active.
        /// </summary>
        public IReadOnlyList<MoveCommand> GetLegalMovesAt(Vector2Int position)
        {
            _legalMoves.Clear();
            if (CurrentPhase == TurnPhase.Normal && !LiveBoard.IsGameOver)
            {
                ChessEngine.GetLegalMoves(LiveBoard, position, _legalMoves);
            }
            return _legalMoves;
        }

        public bool CanSelectPiece(Vector2Int position)
        {
            if (CurrentPhase != TurnPhase.Normal || LiveBoard.IsGameOver) return false;
            PieceData piece = LiveBoard.GetPiece(position);
            return !piece.IsEmpty && piece.Team == LiveBoard.CurrentTurn;
        }

        /// <summary>
        /// Evaluates if a team has sufficient material to force a checkmate.
        /// v1 implementation: a team can force mate if they have any piece beyond the King.
        /// Uses GetPieceIndices for O(N) traversal to maintain high performance.
        /// </summary>
        private static bool CanForceMate(BoardState board, Team team)
        {
            var indices = board.GetPieceIndices(team);
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                PieceData p = board.GetPiece(idx % board.TileCountX, idx / board.TileCountX);
                
                // PieceData is a readonly struct — use !p.IsEmpty.
                if (!p.IsEmpty && p.Type != ChessPieceType.King)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns a value-type snapshot of the clock state, or null if untimed/AI mode.
        /// </summary>
        public ClockState? GetCurrentClockSnapshot()
        {
            return _clockController != null ? (ClockState?)_clockController.CurrentState : null;
        }

        /// <summary>
        /// Configures the session for AI participation, enforcing the performance bypass invariant.
        /// </summary>
        public void SetAIMode()
        {
            _isAIMode     = true;
            _selectedMode = GameModeConfig.Unlimited;
        }

        #endregion

        #region State Machine

        private void TransitionToPhase(TurnPhase nextPhase)
        {
            if (logMoves && CurrentPhase != nextPhase)
            {
                Debug.Log($"[GameManager] Phase Transition: {CurrentPhase} -> {nextPhase}");
            }
            
            CurrentPhase = nextPhase;

            // FIX: Betrayal sub-phases removed. The clock keeps running to maintain pressure!
            bool shouldPause = nextPhase == TurnPhase.Starting
                            || nextPhase == TurnPhase.GameOver;

            if (shouldPause) _clock?.Pause();
            else             _clock?.Resume();
        }

        #endregion

        #region IClockEventHandler

        public void OnClockTimeout(Team timedOutTeam)
        {
            // If the opponent cannot force checkmate by any legal sequence,
            // the result is a draw even if the losing player's time reaches zero.
            Team opponent = timedOutTeam == Team.White ? Team.Black : Team.White;
            bool opponentCanMate = CanForceMate(LiveBoard, opponent);

            if (opponentCanMate)
            {
                EndGame(opponent, byTimeout: true);
            }
            else
            {
                EndGame(null, byTimeout: true); // Insufficient material draw
            }
        }

        public void OnLowTimeWarning(Team team, long remainingMs)
        {
            OnLowTimeAlert?.Invoke(team, remainingMs);
        }

        #endregion
    }
}