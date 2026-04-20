using System;
using System.Collections.Generic;
using UnityEngine;
using ChessTheMasterPiece.Data;
using ChessTheMasterPiece.Logic;
using ChessTheMasterPiece.UI;
using UnityEditor.Experimental.GraphView;

namespace ChessTheMasterPiece.Controllers
{
    /// <summary>
    /// The central orchestrator of the game. 
    /// Bridges the pure C# logic (BoardState/ChessEngine) with the Unity environment.
    /// Manages game flow, validates moves, and broadcasts state changes to other systems.
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
        /// Quick check if the game has started.
        /// </summary>
        public bool IsGameActive { get; private set; }

        #endregion

        #region Events

        // Events for other systems (Visuals, Input, UI) to listen to
        public event Action<BoardState> OnGameStarted;
        public event Action<MoveCommand> OnMoveExecuted;
        public event Action OnTurnChanged;
        public event Action<Team> OnCheck;
        public event Action OnGameReset;

        #endregion

        #region Private Fields

        private MoveCommand pendingPromotionMove;

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

            IsGameActive = true;

            // Broadcast to BoardVisuals and other systems
            OnGameStarted?.Invoke(LiveBoard);

            if (logMoves)
            {
                Debug.Log($"[GameManager] New game started. Player is {PlayerTeam}.");
            }
        }

        /// <summary>
        /// Called when the player clicks the reset button.
        /// Clears the board and returns to team selection.
        /// </summary>
        private void HandleGameReset()
        {
            LiveBoard.Clear();
            IsGameActive = false;
            pendingPromotionMove = default;

            OnGameReset?.Invoke();

            if (logMoves)
            {
                Debug.Log("[GameManager] Game reset.");
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
        }

        #endregion

        #region Move Execution

        /// <summary>
        /// Entry point for move requests. 
        /// Handles the transition from player intent to engine validation.
        /// </summary>
        public bool RequestMove(ChessTheMasterPiece.Data.Vector2Int from, ChessTheMasterPiece.Data.Vector2Int to)
        {
            if (!IsGameActive || LiveBoard.IsGameOver)
            {
                return false;
            }

            PieceData movingPiece = LiveBoard.GetPiece(from);

            if (movingPiece == null || movingPiece.Team != LiveBoard.CurrentTurn)
            {
                return false;
            }

            // If the player dragged the King onto a friendly Rook, remap the 'to' target to the standard 2-step castling square.
            PieceData targetPiece = LiveBoard.GetPiece(to);
            if (movingPiece.Type == ChessPieceType.King && targetPiece != null && 
                targetPiece.Type == ChessPieceType.Rook && targetPiece.Team == movingPiece.Team)
            {
                // If Rook is at X=7 (Kingside), Target is X=6. If Rook is at X=0 (Queenside), Target is X=2.
                int castlingX = to.x > from.x ? 6 : 2;
                to = new ChessTheMasterPiece.Data.Vector2Int(castlingX, from.y);
            }

            // Fetch ALL legal moves for this specific piece
            List<MoveCommand> legalMoves = ChessEngine.GetLegalMoves(LiveBoard, from);

            // Filter moves that match our target destination
            // We use FindAll because a single square can have multiple valid MoveCommands (Promotions)
            List<MoveCommand> destinationMatches = legalMoves.FindAll(m => m.EndPosition == to);

            // Illegal Move Safety
            if (destinationMatches.Count == 0)
            {
                if (logMoves) Debug.Log($"[GameManager] Illegal move attempted: {from} -> {to}");
                return false;
            }

            // AMBIGUITY RESOLUTION: Promotion
            // If the engine has generated multiple moves for one square, it's a promotion choice.
            bool isPromotionChoice = destinationMatches.Exists(m => m.IsPromotion);

            if (isPromotionChoice)
            {
                // Store the first match as a template for position/capture data
                pendingPromotionMove = destinationMatches[0];

                if (logMoves) Debug.Log($"[GameManager] Promotion detected at {to}. Halting for UI choice.");

                // Show the UI. The InputController receives 'true', so the piece snaps to the tile,
                // but the turn DOES NOT switch yet.
                UIManager.Instance?.ShowPromotionUI();
                return true;
            }

            // SINGLE-MOVE RESOLUTION (Standard, Castling, or En Passant)
            // If it's not a promotion, there is only ever one logical command.
            MoveCommand validMove = destinationMatches[0];
            ExecuteValidatedMove(validMove);
            return true;
        }

        /// <summary>
        /// Called by the UI when a player chooses their promotion piece.
        /// </summary>
        private void HandlePromotionChoice(ChessPieceType chosenType)
        {
            // Ensure we are in a valid promotion state
            if (pendingPromotionMove.PieceMoved == null)
            {
                Debug.LogWarning("[GameManager] Received promotion choice but no move was pending.");
                return;
            }

            // RE-VALIDATION: Ask the engine for the options again to get the exact validated command
            // This is safer than manual construction and protects against metadata loss.
            List<MoveCommand> options = ChessEngine.GetLegalMoves(LiveBoard, pendingPromotionMove.StartPosition);

            foreach (var move in options)
            {
                if (move.EndPosition == pendingPromotionMove.EndPosition &&
                    move.PromotedTo == chosenType)
                {
                    // Found the specific command matching the player's choice. Execute it.
                    pendingPromotionMove = default; // Reset state
                    ExecuteValidatedMove(move);
                    return;
                }
            }

            Debug.LogError($"[GameManager] Could not find valid {chosenType} promotion in engine results.");
            pendingPromotionMove = default;
        }

        /// <summary>
        /// Final execution gate. Sequences the transition from Logic to Visuals.
        /// </summary>
        private void ExecuteValidatedMove(MoveCommand move)
        {
            if (logMoves) Debug.Log($"[GameManager] Executing: {move}");

            // This moves the piece in the PieceData[,] and handles captures/state changes.
            ChessEngine.ApplyMoveToBoard(LiveBoard, move);

            // Update move history (essential for En Passant and Castling checks)
            LiveBoard.RecordMove(move.StartPosition, move.EndPosition);

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

            int result = winner.HasValue ? (int)winner.Value : -1;
            UIManager.Instance?.TriggerGameOver(result);

            if (logMoves) Debug.Log($"[GameManager] Game Over. Winner: {(winner.HasValue ? winner.ToString() : "Draw")}");
        }

        #endregion

        #region Public Query Methods

        /// <summary>
        /// Returns all legal moves for a piece at the given position.
        /// Used by UI/Input systems to show move highlights.
        /// </summary>
        public List<MoveCommand> GetLegalMovesAt(ChessTheMasterPiece.Data.Vector2Int position)
        {
            if (!IsGameActive || LiveBoard.IsGameOver)
            {
                return new List<MoveCommand>();
            }

            return ChessEngine.GetLegalMoves(LiveBoard, position);
        }

        /// <summary>
        /// Checks if a piece at the given position can move (belongs to current player).
        /// </summary>
        public bool CanSelectPiece(ChessTheMasterPiece.Data.Vector2Int position)
        {
            if (!IsGameActive || LiveBoard.IsGameOver)
            {
                return false;
            }

            PieceData piece = LiveBoard.GetPiece(position);
            return piece != null && piece.Team == LiveBoard.CurrentTurn;
        }

        #endregion
    }
}