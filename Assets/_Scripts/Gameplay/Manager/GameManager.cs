using System;
using System.Collections.Generic;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Logic;
using ChessTheBetrayal.UI;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

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
        }

        private void Start()
        {
            if (UIManager.Instance == null)
            {
                Debug.LogError("[GameManager] UIManager.Instance is null! Make sure UIManager exists in the scene.");
                return;
            }

            UIManager.Instance.OnTeamSelected += HandleTeamSelected;
            UIManager.Instance.OnGameReset += HandleGameReset;
            UIManager.Instance.OnPromotionSelected += HandlePromotionChoice;

            // [PHASE 4 WIRING] - We will uncomment this when Phase 4 updates UIManager.
            // UIManager.Instance.OnGameModeSelected += HandleGameModeReceived;

            if (logMoves)
            {
                Debug.Log("[GameManager] Initialized and ready.");
            }
        }

        private void OnDestroy()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.OnTeamSelected -= HandleTeamSelected;
                UIManager.Instance.OnGameReset -= HandleGameReset;
                UIManager.Instance.OnPromotionSelected -= HandlePromotionChoice;

                // [PHASE 4 WIRING]
                // UIManager.Instance.OnGameModeSelected -= HandleGameModeReceived;
            }
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
        /// Called when the player picks White or Black from the team selection screen.
        /// Clears the board, places all the pieces, and starts the game.
        /// </summary>
        private void HandleTeamSelected(Team selectedTeam)
        {
            PlayerTeam = selectedTeam;
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

            TransitionToPhase(TurnPhase.Normal);

            InitializeClock();

            // [PHASE 5 WIRING] - We will uncomment this when Phase 5 builds the HUD.
            // UIManager.Instance?.ConfigureHUDForMode(_selectedMode);

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

            // [PHASE 5 WIRING]
            // UIManager.Instance?.ConfigureHUDForMode(GameModeConfig.Unlimited);

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

            _clock = new ChessClock(_selectedMode, this);

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
        /// BoardVisuals will animate the move; then we check if the game is over.
        /// </summary>
        private void PlayMove(MoveCommand move)
        {
            if (logMoves) Debug.Log($"[GameManager] Executing: {move}");

            ChessEngine.ApplyMoveToBoard(LiveBoard, move);

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

        private void EndGame(Team? winner)
        {
            LiveBoard.IsGameOver = true;
            LiveBoard.Winner = winner;

            TransitionToPhase(TurnPhase.GameOver);
            UIManager.Instance?.TriggerGameOver(winner);

            if (logMoves) Debug.Log($"[GameManager] Game Over. Winner: {(winner.HasValue ? winner.ToString() : "Draw")}");
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

            bool shouldPause = nextPhase == TurnPhase.RetributionPending
                            || nextPhase == TurnPhase.ResolutionFailed
                            || nextPhase == TurnPhase.ForcedSave
                            || nextPhase == TurnPhase.GameOver;

            if (shouldPause) _clock?.Pause();
            else             _clock?.Resume();
        }

        #endregion

        #region IClockEventHandler

        public void OnClockTimeout(Team timedOutTeam)
        {
            // Stub implementation for compilation.
            // Full resolution (including FIDE insufficient material rules) occurs in Phase 6.
        }

        public void OnLowTimeWarning(Team team, long remainingMs)
        {
            OnLowTimeAlert?.Invoke(team, remainingMs);
        }

        #endregion
    }
}