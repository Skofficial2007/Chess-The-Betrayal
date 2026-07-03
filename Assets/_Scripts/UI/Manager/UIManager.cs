using System;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Gameplay;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// The traffic controller for all UI panels. It knows which panels should be open at any given time and listens to UI events to pass player choices (team selection, promotions) up to GameManager.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Panel References")]
        [SerializeField] private GameModeSelectorUI gameModeSelectionUI;
        [SerializeField] private TeamSelectionUI teamSelectionUI;
        [SerializeField] private PromotionUI promotionUI;
        [SerializeField] private GameOverUI gameOverUI;
        [SerializeField] private MainMenuUI mainMenuUI;
        [SerializeField] private GameHUD gameHUD;

        [Header("Event Channels")]
        [SerializeField] private ChessTheBetrayal.Events.TeamSelectedEventChannel _teamSelectedChannel;

        // Events
        public event Action<GameModeConfig> OnGameModeSelected;
        public event Action OnTeamRollRequested;
        public event Action OnTeamAnimationComplete;
        public event Action<Team> OnTeamSelected;
        public event Action<ChessPieceType> OnPromotionSelected;
        public event Action OnGameReset;
        public event Action OnRetributionSkipRequested;

        private Team _assignedTeam;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            ValidateRequiredFields();
            RegisterPanelEvents();
        }

        private void ValidateRequiredFields()
        {
            InspectorGuard.Require(gameModeSelectionUI, nameof(gameModeSelectionUI), this);
            InspectorGuard.Require(teamSelectionUI, nameof(teamSelectionUI), this);
            InspectorGuard.Require(promotionUI, nameof(promotionUI), this);
            InspectorGuard.Require(gameOverUI, nameof(gameOverUI), this);
            InspectorGuard.Require(mainMenuUI, nameof(mainMenuUI), this);
            InspectorGuard.Require(gameHUD, nameof(gameHUD), this);
            InspectorGuard.Require(_teamSelectedChannel, nameof(_teamSelectedChannel), this);
        }

        private void Start()
        {
            if (gameModeSelectionUI != null)
            {
                gameModeSelectionUI.SetActive(false);
            }

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

            if (gameHUD != null)
            {
                gameHUD.SetActive(false);
            }

            if (mainMenuUI != null)
            {
                mainMenuUI.SetActive(true);
            }
        }

        private void OnDestroy()
        {
            UnregisterPanelEvents();
        }

        #region Setup

        private void RegisterPanelEvents()
        {
            if (gameModeSelectionUI != null)
            {
                gameModeSelectionUI.OnModeSelected += HandleGameModeSelected;
            }

            if (teamSelectionUI != null)
            {
                teamSelectionUI.OnRollRequested += () => OnTeamRollRequested?.Invoke();
                teamSelectionUI.OnRouletteComplete += HandleRouletteComplete;
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
                gameHUD.OnRetributionSkipClicked += HandleRetributionSkipClicked;
            }

            if (gameOverUI != null)
            {
                gameOverUI.OnReplay += HandleReplay;
                gameOverUI.OnExit += HandleGameExit;
            }
        }

        private void UnregisterPanelEvents()
        {
            if (gameModeSelectionUI != null)
            {
                gameModeSelectionUI.OnModeSelected -= HandleGameModeSelected;
            }

            if (teamSelectionUI != null)
            {
                teamSelectionUI.OnRollRequested -= () => OnTeamRollRequested?.Invoke();
                teamSelectionUI.OnRouletteComplete -= HandleRouletteComplete;
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
                gameHUD.OnRetributionSkipClicked -= HandleRetributionSkipClicked;
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
            if (gameModeSelectionUI != null && gameModeSelectionUI.gameObject.activeSelf)
            {
                return true;
            }

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

            if (gameModeSelectionUI != null)
            {
                gameModeSelectionUI.SetActive(false);
            }

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

        public void ShowGameModeSelection()
        {
            if (gameModeSelectionUI != null)
            {
                gameModeSelectionUI.SetActive(true);
            }

            if (mainMenuUI != null)
            {
                mainMenuUI.SetActive(false);
            }

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

            if (gameModeSelectionUI != null)
            {
                gameModeSelectionUI.SetActive(false);
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

        public void TriggerGameOver(Team? winningTeam, bool byTimeout = false)
        {
            if (gameOverUI != null)
            {
                gameOverUI.SetWinnerText(winningTeam, byTimeout);
                gameOverUI.SetActive(true);
            }

            if (gameHUD != null)
            {
                gameHUD.SetActive(true);
            }
        }

        /// <summary>
        /// Called when the game ends via the event bus. Unpacks the payload and triggers the game over UI.
        /// </summary>
        public void HandleGameOver(ChessTheBetrayal.Events.Payloads.GameOverPayload payload)
        {
            // Unpack the struct and pass it to your existing method
            TriggerGameOver(payload.WinningTeam, payload.IsTimeout);
        }

        public void ConfigureHUDForMode(GameModeConfig config)
        {
            gameHUD?.ConfigureForMode(config);
        }

        public void TriggerTeamRoulette(Team assignedTeam)
        {
            _assignedTeam = assignedTeam;
            
            if (teamSelectionUI != null)
            {
                teamSelectionUI.PlayRoulette(assignedTeam);
            }
        }

        #endregion

        #region Internal Handlers

        private void HandlePlayGame()
        {
            ShowGameModeSelection();
        }

        private void HandleExitGame()
        {
            Application.Quit();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void HandleGameModeSelected(GameModeConfig config)
        {
            if (gameModeSelectionUI != null)
            {
                gameModeSelectionUI.SetActive(false);
            }
            
            OnGameModeSelected?.Invoke(config);
            ShowTeamSelection();
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
            OnGameReset?.Invoke();
            ShowMainMenu();
        }

        private void HandleReplay()
        {
            // Delegates to GameManager's bound IPostGameAction (BackToModeSelectAction in the
            // prototype), which tears down the finished match and decides what screen comes next.
            // UIManager never decides the mode or the destination screen itself.
            GameManager.Instance?.HandleGameOverAcknowledged();
        }

        private void HandleRetributionSkipClicked()
        {
            OnRetributionSkipRequested?.Invoke();
        }

        private void HandleRouletteComplete()
        {
            // Hide team selection, show game HUD
            if (teamSelectionUI != null)
            {
                teamSelectionUI.SetActive(false);
            }

            if (gameHUD != null)
            {
                gameHUD.SetActive(true);
            }

            OnTeamSelected?.Invoke(_assignedTeam);
            _teamSelectedChannel?.Raise(_assignedTeam);
            OnTeamAnimationComplete?.Invoke();
        }

        #endregion
    }
}