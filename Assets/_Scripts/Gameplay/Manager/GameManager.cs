using System;
using System.Collections.Generic;
using UnityEngine;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;
using ChessTheMasterPiece.UI;

namespace ChessTheMasterPiece.Controllers
{
    /// <summary>
    /// The central orchestrator of the game. 
    /// Bridges the pure C# logic (BoardState/ChessEngine) with the Unity environment.
    /// Manages game flow, validates moves, and broadcasts state changes to other systems.
    /// GC-optimized with buffer-passing pattern.
    /// </summary>
    public class GameManager : MonoBehaviour
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
        /// The single source of truth for the game state.
        /// All game logic operates on this pure C# data structure.
        /// </summary>
        public BoardState LiveBoard { get; private set; }

        /// <summary>
        /// Which team the human player is controlling.
        /// </summary>
        public Team PlayerTeam { get; private set; } = Team.White;

        /// <summary>
        /// Current phase of the turn state machine.
        /// Controls game flow and locks out actions during special phases.
        /// </summary>
        public TurnPhase CurrentPhase { get; private set; } = TurnPhase.GameOver;

        /// <summary>
        /// Quick check if the game has started.
        /// Derived property keeps existing UI/Input checks from breaking.
        /// </summary>
        public bool IsGameActive => CurrentPhase != TurnPhase.GameOver;

        #endregion

        #region Events

        // Events for other systems (Visuals, Input, UI) to listen to
        public event Action<BoardState> OnGameStarted;
        public event Action<MoveCommand> OnMoveExecuted;
        public event Action OnTurnChanged;
        public event Action<Team> OnCheck;
        public event Action OnGameReset;
        
        // Command Pattern events for async move handling
        public event Action<ChessTheMasterPiece.Data.Vector2Int, ChessTheMasterPiece.Data.Vector2Int> OnMoveRejected;
        public event Action<ChessTheMasterPiece.Data.Vector2Int> OnPromotionRequested;

        #endregion

        #region Private Fields

        // Command Pattern executor - handles all move validation logic
        private IMoveExecutor _moveExecutor;

        // GC-optimized buffer for legal move queries
        private readonly List<MoveCommand> _legalMovesBuffer = new List<MoveCommand>(32);

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Initialize the pure C# data structure
            LiveBoard = new BoardState(boardSizeX, boardSizeY);
        }

        private void Start()
        {
            if (UIManager.Instance == null)
            {
                Debug.LogError("[GameManager] UIManager.Instance is null! Make sure UIManager exists in the scene.");
                return;
            }

            // Subscribe to UI events
            UIManager.Instance.OnTeamSelected += HandleTeamSelected;
            UIManager.Instance.OnGameReset += HandleGameReset;
            UIManager.Instance.OnPromotionSelected += HandlePromotionChoice;

            if (logMoves)
            {
                Debug.Log("[GameManager] Initialized and ready.");
            }
        }

        private void OnDestroy()
        {
            // Unsubscribe from UI events
            if (UIManager.Instance != null)
            {
                UIManager.Instance.OnTeamSelected -= HandleTeamSelected;
                UIManager.Instance.OnGameReset -= HandleGameReset;
                UIManager.Instance.OnPromotionSelected -= HandlePromotionChoice;
            }
        }

        // Executor callbacks (named so we can unsubscribe cleanly)
        private void OnExecutorMoveRejected(ChessTheMasterPiece.Data.Vector2Int from, ChessTheMasterPiece.Data.Vector2Int to)
        {
            OnMoveRejected?.Invoke(from, to);
        }

        private void OnExecutorPromotionRequired(ChessTheMasterPiece.Data.Vector2Int pos)
        {
            OnPromotionRequested?.Invoke(pos);
            UIManager.Instance?.ShowPromotionUI();
        }

        #endregion

        #region Game Flow

        /// <summary>
        /// Called when the player selects White or Black from the team selection UI.
        /// Sets up the board and starts a new game.
        /// </summary>
        private void HandleTeamSelected(int teamIndex)
        {
            PlayerTeam = (Team)teamIndex;
            LiveBoard.Clear();

            SetupStandardPieces();

            // Clean up old executor to prevent memory leaks on game restart
            if (_moveExecutor != null)
            {
                _moveExecutor.OnMoveConfirmed -= ExecuteValidatedMove;
                _moveExecutor.OnMoveRejected -= OnExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= OnExecutorPromotionRequired;
                _moveExecutor = null;
            }

            // Initialize the Command Pattern executor
            _moveExecutor = new LocalMoveExecutor(LiveBoard, logMoves);
            
            // Wire executor responses to the GameManager state machine
            _moveExecutor.OnMoveConfirmed += ExecuteValidatedMove;
            _moveExecutor.OnMoveRejected += OnExecutorMoveRejected;
            _moveExecutor.OnPromotionRequired += OnExecutorPromotionRequired;

            // Enter the Normal phase to start the game
            CurrentPhase = TurnPhase.Normal;

            // Broadcast to BoardVisuals and other systems
            OnGameStarted?.Invoke(LiveBoard);

            if (logMoves)
            {
                Debug.Log($"[GameManager] New game started. Player is {PlayerTeam}. Phase: {CurrentPhase}");
            }
        }

        /// <summary>
        /// Called when the player clicks the reset button.
        /// Clears the board and returns to team selection.
        /// </summary>
        private void HandleGameReset()
        {
            LiveBoard.Clear();
            if (_moveExecutor != null)
            {
                _moveExecutor.OnMoveConfirmed -= ExecuteValidatedMove;
                _moveExecutor.OnMoveRejected -= OnExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= OnExecutorPromotionRequired;
                _moveExecutor = null;
            }

            // Shift to GameOver state
            CurrentPhase = TurnPhase.GameOver;

            OnGameReset?.Invoke();

            if (logMoves)
            {
                Debug.Log("[GameManager] Game reset. Phase: GameOver");
            }
        }

        /// <summary>
        /// Populates the pure C# BoardState with initial piece configuration.
        /// Does NOT spawn Unity GameObjects - that's the Visuals layer's job.
        /// </summary>
        private void SetupStandardPieces()
        {
            ChessPieceType[] backRank = new ChessPieceType[]
            {
                ChessPieceType.Rook, ChessPieceType.Knight, ChessPieceType.Bishop, ChessPieceType.Queen,
                ChessPieceType.King, ChessPieceType.Bishop, ChessPieceType.Knight, ChessPieceType.Rook
            };

            // Setup White Team (Always logical bottom, moving UP)
            for (int x = 0; x < boardSizeX; x++)
            {
                // White Majors (Rank 0)
                LiveBoard.SetPiece(new PieceData(Team.White, backRank[x], x, 0, direction: 1), x, 0);
                // White Pawns (Rank 1)
                LiveBoard.SetPiece(new PieceData(Team.White, ChessPieceType.Pawn, x, 1, direction: 1), x, 1);
            }

            // Setup Black Team (Always logical top, moving DOWN)
            for (int x = 0; x < boardSizeX; x++)
            {
                // Black Pawns (Rank 6)
                LiveBoard.SetPiece(new PieceData(Team.Black, ChessPieceType.Pawn, x, boardSizeY - 2, direction: -1), x, boardSizeY - 2);
                // Black Majors (Rank 7)
                LiveBoard.SetPiece(new PieceData(Team.Black, backRank[x], x, boardSizeY - 1, direction: -1), x, boardSizeY - 1);
            }

            // Compute the initial Zobrist hash from the starting position
            LiveBoard.ComputeFullZobristHash();
        }

        #endregion

        #region Move Execution

        /// <summary>
        /// Entry point for move requests. 
        /// Now uses the Command Pattern for async-ready execution.
        /// Fire-and-forget - result comes back via events.
        /// </summary>
        public void RequestMove(ChessTheMasterPiece.Data.Vector2Int from, ChessTheMasterPiece.Data.Vector2Int to)
        {
            // Gate physical piece selection globally based on game phase
            if (CurrentPhase != TurnPhase.Normal || LiveBoard.IsGameOver)
            {
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            // Hand off to the executor (Fire and Forget!)
            _moveExecutor?.RequestMove(from, to);
        }

        /// <summary>
        /// Called by the UI when a player chooses their promotion piece.
        /// Now delegates to the executor for validation.
        /// </summary>
        private void HandlePromotionChoice(ChessPieceType chosenType)
        {
            _moveExecutor?.RequestPromotion(chosenType);
        }

        /// <summary>
        /// Final execution gate. Sequences the transition from Logic to Visuals.
        /// </summary>
        private void ExecuteValidatedMove(MoveCommand move)
        {
            if (logMoves) Debug.Log($"[GameManager] Executing: {move}");

            // ApplyMoveToBoard now handles RecordMove internally for proper Make/Unmake simulation
            ChessEngine.ApplyMoveToBoard(LiveBoard, move);

            // BoardVisuals hears this and triggers the animations based on the move metadata.
            OnMoveExecuted?.Invoke(move);

            LiveBoard.NextTurn();
            
            EvaluateGameStatus();
        }

        /// <summary>
        /// Checks for Check, Checkmate, or Stalemate after a turn ends.
        /// </summary>
        private void EvaluateGameStatus()
        {
            Team currentTeam = LiveBoard.CurrentTurn;
            GameState state = ChessEngine.EvaluateGameState(LiveBoard, currentTeam);

            switch (state)
            {
                case GameState.Checkmate:
                    // Winner is the person who JUST MOVED, not the current team.
                    Team winner = currentTeam == Team.White ? Team.Black : Team.White;
                    EndGame(winner);
                    break;

                case GameState.Stalemate:
                    EndGame(null); // Draw
                    break;

                case GameState.Check:
                    OnCheck?.Invoke(currentTeam);
                    OnTurnChanged?.Invoke();
                    break;

                case GameState.Normal:
                    OnTurnChanged?.Invoke();
                    break;
            }
        }

        private void EndGame(Team? winner)
        {
            LiveBoard.IsGameOver = true;
            LiveBoard.Winner = winner;

            // Lock the state machine
            CurrentPhase = TurnPhase.GameOver;

            int result = winner.HasValue ? (int)winner.Value : -1;
            UIManager.Instance?.TriggerGameOver(result);

            if (logMoves) Debug.Log($"[GameManager] Game Over. Winner: {(winner.HasValue ? winner.ToString() : "Draw")}");
        }

        #endregion

        #region Public Query Methods

        /// <summary>
        /// Returns all legal moves for a piece at the given position.
        /// Used by UI/Input systems to show move highlights.
        /// GC-optimized - returns internal buffer as readonly to prevent external modification.
        /// </summary>
        public IReadOnlyList<MoveCommand> GetLegalMovesAt(ChessTheMasterPiece.Data.Vector2Int position)
        {
            _legalMovesBuffer.Clear();

            // Gate visual highlights based on the current phase
            if (CurrentPhase == TurnPhase.Normal && !LiveBoard.IsGameOver)
            {
                ChessEngine.GetLegalMoves(LiveBoard, position, _legalMovesBuffer);
            }

            // Return as interface to prevent external scripts from modifying the buffer
            return _legalMovesBuffer;
        }

        /// <summary>
        /// Checks if a piece at the given position can move (belongs to current player).
        /// </summary>
        public bool CanSelectPiece(ChessTheMasterPiece.Data.Vector2Int position)
        {
            // Gate physical piece selection
            if (CurrentPhase != TurnPhase.Normal || LiveBoard.IsGameOver)
            {
                return false;
            }

            PieceData piece = LiveBoard.GetPiece(position);
            return piece != null && piece.Team == LiveBoard.CurrentTurn;
        }

        #endregion
    }
}