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

        public event Action<ChessTheMasterPiece.Data.Vector2Int, ChessTheMasterPiece.Data.Vector2Int> OnMoveRejected;
        public event Action<ChessTheMasterPiece.Data.Vector2Int> OnPromotionRequested;

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
            }
        }

        // Named methods (rather than lambdas) so we can unsubscribe cleanly.
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

            OnGameStarted?.Invoke(LiveBoard);

            if (logMoves)
            {
                Debug.Log($"[GameManager] New game started. Player is {PlayerTeam}. Phase: {CurrentPhase}");
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

            TransitionToPhase(TurnPhase.GameOver);

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
            // White starts at the bottom (rank 0 and 1), moving up.
            for (int x = 0; x < boardSizeX; x++)
            {
                LiveBoard.SetPiece(new PieceData(Team.White, StandardBackRank[x], x, 0, direction: 1), x, 0);
                LiveBoard.SetPiece(new PieceData(Team.White, ChessPieceType.Pawn, x, 1, direction: 1), x, 1);
            }

            // Black starts at the top (rank 6 and 7), moving down.
            for (int x = 0; x < boardSizeX; x++)
            {
                LiveBoard.SetPiece(new PieceData(Team.Black, ChessPieceType.Pawn, x, boardSizeY - 2, direction: -1), x, boardSizeY - 2);
                LiveBoard.SetPiece(new PieceData(Team.Black, StandardBackRank[x], x, boardSizeY - 1, direction: -1), x, boardSizeY - 1);
            }

            LiveBoard.ComputeFullZobristHash();
        }

        #endregion

        #region Move Execution

        /// <summary>
        /// Sends a move request to the executor. The result comes back through events —
        /// either OnMoveExecuted (if legal) or OnMoveRejected (if not).
        /// </summary>
        public void RequestMove(ChessTheMasterPiece.Data.Vector2Int from, ChessTheMasterPiece.Data.Vector2Int to)
        {
            // Only accept moves during normal play.
            if (CurrentPhase != TurnPhase.Normal || LiveBoard.IsGameOver)
            {
                OnMoveRejected?.Invoke(from, to);
                return;
            }

            _moveExecutor?.RequestMove(from, to);
        }

        /// <summary>
        /// Called by the UI when the player picks a piece to promote to.
        /// </summary>
        private void HandlePromotionChoice(ChessPieceType chosenType)
        {
            _moveExecutor?.RequestPromotion(chosenType);
        }

        /// <summary>
        /// Applies a validated move to the board and tells everyone who needs to know about it.
        /// BoardVisuals will animate the move; then we check if the game is over.
        /// </summary>
        private void PlayMove(MoveCommand move)
        {
            if (logMoves) Debug.Log($"[GameManager] Executing: {move}");

            ChessEngine.ApplyMoveToBoard(LiveBoard, move);

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
            GameState state = ChessEngine.EvaluateGameState(LiveBoard, currentTeam);

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
        public IReadOnlyList<MoveCommand> GetLegalMovesAt(ChessTheMasterPiece.Data.Vector2Int position)
        {
            _legalMoves.Clear();

            if (CurrentPhase == TurnPhase.Normal && !LiveBoard.IsGameOver)
            {
                ChessEngine.GetLegalMoves(LiveBoard, position, _legalMoves);
            }

            return _legalMoves;
        }

        /// <summary>
        /// Returns true if the piece at this position belongs to the player whose turn it is.
        /// </summary>
        public bool CanSelectPiece(ChessTheMasterPiece.Data.Vector2Int position)
        {
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
        /// The only place in the codebase that changes CurrentPhase.
        /// Keeping all transitions here makes it much easier to trace the game flow — especially
        /// once the Betrayal sub-phases are added.
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