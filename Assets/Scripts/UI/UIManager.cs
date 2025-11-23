using System;
using UnityEngine;
using ChessTheMasterPiece.ChessPiece;

namespace ChessTheMasterPiece.UI
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Panel References")]
        [SerializeField] private TeamSelectionUI teamSelectionUI;
        [SerializeField] private PromotionUI promotionUI;
        [SerializeField] private GameOverUI gameOverUI;
        [SerializeField] private MainMenuUI mainMenuUI;
        [SerializeField] private GameHUD gameHUD;

        // Events
        public event Action<int> OnTeamSelected;
        public event Action<ChessPieceType> OnPromotionSelected;
        public event Action OnGameReset;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            RegisterPanelEvents();
        }

        private void Start()
        {
            // Initial State: Hide all panels
            if (teamSelectionUI != null)
            {
                teamSelectionUI.SetActive(false);
            }

            if (promotionUI != null)
            {
                promotionUI.SetActive(false);
            }

            if (gameOverUI != null)
            {
                gameOverUI.SetActive(false);
            }

            if (mainMenuUI != null)
            {
                mainMenuUI.SetActive(false);
            }

            if (gameHUD != null)
            {
                gameHUD.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            UnregisterPanelEvents();
        }

        #region Setup

        private void RegisterPanelEvents()
        {
            if (teamSelectionUI != null)
            {
                teamSelectionUI.OnTeamSelected += HandleTeamSelected;
            }

            if (promotionUI != null)
            {
                promotionUI.OnPieceSelected += HandlePromotionSelected;
            }

            if (mainMenuUI != null)
            {
                mainMenuUI.OnPlay += HandlePlayGame;
                mainMenuUI.OnExit += HandleExitGame;
            }

            if (gameHUD != null)
            {
                gameHUD.OnExitToMenu += HandleGameExit;
            }

            if (gameOverUI != null)
            {
                gameOverUI.OnReplay += HandleReplay;
                gameOverUI.OnExit += HandleGameExit;
            }
        }

        private void UnregisterPanelEvents()
        {
            if (teamSelectionUI != null)
            {
                teamSelectionUI.OnTeamSelected -= HandleTeamSelected;
            }

            if (promotionUI != null)
            {
                promotionUI.OnPieceSelected -= HandlePromotionSelected;
            }

            if (mainMenuUI != null)
            {
                mainMenuUI.OnPlay -= HandlePlayGame;
                mainMenuUI.OnExit -= HandleExitGame;
            }

            if (gameHUD != null)
            {
                gameHUD.OnExitToMenu -= HandleGameExit;
            }

            if (gameOverUI != null)
            {
                gameOverUI.OnReplay -= HandleReplay;
                gameOverUI.OnExit -= HandleGameExit;
            }
        }

        #endregion

        #region State Checks

        public bool IsUIBlocking()
        {
            if (teamSelectionUI != null && teamSelectionUI.gameObject.activeSelf)
            {
                return true;
            }

            if (promotionUI != null && promotionUI.gameObject.activeSelf)
            {
                return true;
            }

            if (gameOverUI != null && gameOverUI.gameObject.activeSelf)
            {
                return true;
            }

            if (mainMenuUI != null && mainMenuUI.gameObject.activeSelf)
            {
                return true;
            }

            return false;
        }

        #endregion

        #region Control Methods

        public void ShowMainMenu()
        {
            if (mainMenuUI != null)
            {
                mainMenuUI.SetActive(true);
            }

            // Ensure other panels are closed
            if (teamSelectionUI != null)
            {
                teamSelectionUI.SetActive(false);
            }

            if (gameHUD != null)
            {
                gameHUD.SetActive(false);
            }

            if (gameOverUI != null)
            {
                gameOverUI.SetActive(false);
            }

            if (promotionUI != null)
            {
                promotionUI.SetActive(false);
            }
        }

        public void ShowTeamSelection()
        {
            if (teamSelectionUI != null)
            {
                teamSelectionUI.SetActive(true);
            }

            if (mainMenuUI != null)
            {
                mainMenuUI.SetActive(false);
            }

            if (gameHUD != null)
            {
                gameHUD.SetActive(false);
            }

            if (gameOverUI != null)
            {
                gameOverUI.SetActive(false);
            }

            if (promotionUI != null)
            {
                promotionUI.SetActive(false);
            }
        }

        public void ShowPromotionUI()
        {
            if (promotionUI != null)
            {
                promotionUI.SetActive(true);
            }

            if (gameHUD != null)
            {
                gameHUD.SetActive(true);
            }
        }

        public void TriggerGameOver(int winningTeam)
        {
            if (gameOverUI != null)
            {
                gameOverUI.SetWinnerText(winningTeam);
                gameOverUI.SetActive(true);
            }

            if (gameHUD != null)
            {
                gameHUD.SetActive(true);
            }
        }

        #endregion

        #region Internal Handlers

        private void HandlePlayGame()
        {
            ShowTeamSelection();
        }

        private void HandleExitGame()
        {
            Application.Quit();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void HandleTeamSelected(int teamIndex)
        {
            if (teamSelectionUI != null)
            {
                teamSelectionUI.SetActive(false);
            }

            if (gameHUD != null)
            {
                gameHUD.SetActive(true);
            }

            OnTeamSelected?.Invoke(teamIndex);
        }

        private void HandlePromotionSelected(ChessPieceType type)
        {
            if (promotionUI != null)
            {
                promotionUI.SetActive(false);
            }

            OnPromotionSelected?.Invoke(type);
        }

        private void HandleGameExit()
        {
            // Notify Chessboard to clear pieces
            OnGameReset?.Invoke();

            // Return to Main Menu
            ShowMainMenu();
        }

        private void HandleReplay()
        {
            OnGameReset?.Invoke();
            ShowTeamSelection();
        }

        #endregion
    }
}