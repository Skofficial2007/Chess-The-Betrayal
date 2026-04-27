using System;
using System.Collections.Generic;
using UnityEngine;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;
using ChessTheMasterPiece.UI;

namespace ChessTheMasterPiece.Controllers
{
    /// <summary>
    /// The main conductor of the game. It connects the chess logic (BoardState, ChessEngine) to the Unity world (visuals, UI, input).
    /// Everything that needs to happen in sequence — a move, a check, game over — flows through here.
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
        private readonly List<MoveCommand> _legalMoves = new List<MoveCommand>(32);

        // The standard piece order for the back rank, left to right.
        private static readonly ChessPieceType[] StandardBackRank = new ChessPieceType[]
        {
            ChessPieceType.Rook,   ChessPieceType.Knight, ChessPieceType.Bishop,
            ChessPieceType.Queen,  ChessPieceType.King,   ChessPieceType.Bishop,
            ChessPieceType.Knight, ChessPieceType.Rook
        };

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
        private void HandleTeamSelected(Team selectedTeam)
        {
            PlayerTeam = selectedTeam;
            LiveBoard.Clear();

            SetupStandardPieces();

            // Clean up old executor to prevent memory leaks on game restart
            if (_moveExecutor != null)
            {
                _moveExecutor.OnMoveConfirmed -= PlayMove;
                _moveExecutor.OnMoveRejected -= OnExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= OnExecutorPromotionRequired;
                _moveExecutor = null;
            }

            // Initialize the Command Pattern executor
            _moveExecutor = new LocalMoveExecutor(LiveBoard, logMoves);
            
            // Wire executor responses to the GameManager state machine
            _moveExecutor.OnMoveConfirmed += PlayMove;
            _moveExecutor.OnMoveRejected += OnExecutorMoveRejected;
            _moveExecutor.OnPromotionRequired += OnExecutorPromotionRequired;

            // Enter the Normal phase to start the game
            TransitionToPhase(TurnPhase.Normal);

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
                _moveExecutor.OnMoveConfirmed -= PlayMove;
                _moveExecutor.OnMoveRejected -= OnExecutorMoveRejected;
                _moveExecutor.OnPromotionRequired -= OnExecutorPromotionRequired;
                _moveExecutor = null;
            }

            // Shift to GameOver state
            TransitionToPhase(TurnPhase.GameOver);

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
            // Setup White Team (Always logical bottom, moving UP)
            for (int x = 0; x < boardSizeX; x++)
            {
                // White Majors (Rank 0)
                LiveBoard.SetPiece(new PieceData(Team.White, StandardBackRank[x], x, 0, direction: 1), x, 0);
                // White Pawns (Rank 1)
                LiveBoard.SetPiece(new PieceData(Team.White, ChessPieceType.Pawn, x, 1, direction: 1), x, 1);
            }

            // Setup Black Team (Always logical top, moving DOWN)
            for (int x = 0; x < boardSizeX; x++)
            {
                // Black Pawns (Rank 6)
                LiveBoard.SetPiece(new PieceData(Team.Black, ChessPieceType.Pawn, x, boardSizeY - 2, direction: -1), x, boardSizeY - 2);
                // Black Majors (Rank 7)
                LiveBoard.SetPiece(new PieceData(Team.Black, StandardBackRank[x], x, boardSizeY - 1, direction: -1), x, boardSizeY - 1);
            }

            // Compute the initial Zobrist hash from the starting position
            LiveBoard.ComputeFullZobristHash();
        }

        #endregion

        #region Move Execution

        /// <summary>
        /// Passes a move request to the executor. The result comes back via events, not a return value.
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
        /// Applies a validated move to the board and notifies everyone who needs to know.
        /// </summary>
        private void PlayMove(MoveCommand move)
        {
            if (logMoves) Debug.Log($"[GameManager] Executing: {move}");

            // ApplyMoveToBoard handles move history internally.
            ChessEngine.ApplyMoveToBoard(LiveBoard, move);

            // BoardVisuals hears this and triggers the animations based on the move metadata.
            OnMoveExecuted?.Invoke(move);

            LiveBoard.NextTurn();
            
            CheckForGameEnd();
        }

        /// <summary>
        /// Checks for Check, Checkmate, or Stalemate after a turn ends.
        /// </summary>
        private void CheckForGameEnd()
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

            // Lock the state machine
            TransitionToPhase(TurnPhase.GameOver);

            UIManager.Instance?.TriggerGameOver(winner);

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
            _legalMoves.Clear();

            // Gate visual highlights based on the current phase
            if (CurrentPhase == TurnPhase.Normal && !LiveBoard.IsGameOver)
            {
                ChessEngine.GetLegalMoves(LiveBoard, position, _legalMoves);
            }

            // Return as interface to prevent external scripts from modifying the buffer
            return _legalMoves;
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

        #region State Machine

        /// <summary>
        /// Centralized state transition method. 
        /// Logs phase changes for easier debugging during complex Betrayal scenarios.
        /// </summary>
        private void TransitionToPhase(TurnPhase nextPhase)
        {
            if (logMoves && CurrentPhase != nextPhase)
            {
                Debug.Log($"[GameManager] Phase Transition: {CurrentPhase} -> {nextPhase}");
            }
            CurrentPhase = nextPhase;
        }

        #endregion
    }
}