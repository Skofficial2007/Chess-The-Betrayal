using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Infrastructure;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// The traffic controller for all UI panels. It knows which panels should be open at any given time and listens to UI events to pass player choices (team selection, promotions) up to GameManager.
    /// </summary>
    public class UIManager : MonoBehaviour, IUiBlockingState
    {
        [Header("Panel References")]
        [SerializeField] private GameModeSelectorUI gameModeSelectionUI;
        [SerializeField] private AIMatchSettingsUI aiMatchSettingsUI;
        [SerializeField] private TeamSelectionUI teamSelectionUI;
        [SerializeField] private PromotionUI promotionUI;
        [SerializeField] private GameOverUI gameOverUI;
        [SerializeField] private MainMenuUI mainMenuUI;
        [SerializeField] private GameHUD gameHUD;

        [Tooltip("The panel every 'are you sure' in the match is asked on. One panel, many questions — the words come from whoever is asking.")]
        [SerializeField] private ChessTheBetrayal.UI.Controls.WarningPopup warningPopup;

        [Header("Event Channels")]
        [SerializeField] private ChessTheBetrayal.Events.TeamSelectedEventChannel _teamSelectedChannel;
        [SerializeField] private ChessTheBetrayal.Events.PromotionRequiredEventChannel _promotionRequiredChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameOverEventChannel _gameOverChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameModeConfiguredEventChannel _gameModeConfiguredChannel;

        // Events
        public event Action<GameModeConfig> OnGameModeSelected;
        public event Action<PracticeMatchSettings> OnPracticeMatchSettingsConfirmed;
        public event Action OnTeamRollRequested;
        public event Action OnTeamAnimationComplete;
        public event Action<Team> OnTeamSelected;
        public event Action<ChessPieceType> OnPromotionSelected;
        public event Action OnGameReset;
        public event Action OnRetributionSkipRequested;
        public event Action OnUndoRequested;

        private Team _assignedTeam;

        // Anything in the game that needs an answer before it acts goes through this. Built here
        // because this is where the panel it draws on is wired; the rules about when to ask, and the
        // promise that every question is always answered, live in the gate itself.
        private ChessTheBetrayal.UI.Controls.ConfirmationGate _confirmations;

        private void Awake()
        {
            ServiceLocator.Instance.Register(this);
            ServiceLocator.Instance.Register<IUiBlockingState>(this);

            ValidateRequiredFields();

            // The Unity null check has to happen on the popup itself. An unassigned reference stored
            // through an interface loses that check — the engine's own == overload is gone by then,
            // and the field reads as a perfectly good object that turns out to be nothing.
            _confirmations = new ChessTheBetrayal.UI.Controls.ConfirmationGate(
                warningPopup != null ? warningPopup : null);
            ServiceLocator.Instance.Register<ChessTheBetrayal.UI.Controls.IConfirmationGate>(_confirmations);

            RegisterPanelEvents();

            _promotionRequiredChannel?.Register(HandlePromotionRequiredChannel);
            _gameOverChannel?.Register(HandleGameOver);
            _gameModeConfiguredChannel?.Register(ConfigureHUDForMode);
        }

        private void ValidateRequiredFields()
        {
            InspectorGuard.Require(gameModeSelectionUI, nameof(gameModeSelectionUI), this);
            InspectorGuard.Require(aiMatchSettingsUI, nameof(aiMatchSettingsUI), this);
            InspectorGuard.Require(teamSelectionUI, nameof(teamSelectionUI), this);
            InspectorGuard.Require(promotionUI, nameof(promotionUI), this);
            InspectorGuard.Require(gameOverUI, nameof(gameOverUI), this);
            InspectorGuard.Require(mainMenuUI, nameof(mainMenuUI), this);
            InspectorGuard.Require(gameHUD, nameof(gameHUD), this);
            InspectorGuard.Require(warningPopup, nameof(warningPopup), this);
            InspectorGuard.Require(_teamSelectedChannel, nameof(_teamSelectedChannel), this);
            InspectorGuard.Require(_promotionRequiredChannel, nameof(_promotionRequiredChannel), this);
            InspectorGuard.Require(_gameOverChannel, nameof(_gameOverChannel), this);
            InspectorGuard.Require(_gameModeConfiguredChannel, nameof(_gameModeConfiguredChannel), this);
        }

        private void Start()
        {
            if (gameModeSelectionUI != null)
            {
                gameModeSelectionUI.SetActive(false);
            }

            if (aiMatchSettingsUI != null)
            {
                aiMatchSettingsUI.SetActive(false);
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

            _promotionRequiredChannel?.Unregister(HandlePromotionRequiredChannel);
            _gameOverChannel?.Unregister(HandleGameOver);
            _gameModeConfiguredChannel?.Unregister(ConfigureHUDForMode);
        }

        #region Setup

        private void RegisterPanelEvents()
        {
            if (gameModeSelectionUI != null)
            {
                gameModeSelectionUI.OnModeSelected += HandleGameModeSelected;
            }

            if (aiMatchSettingsUI != null)
            {
                aiMatchSettingsUI.OnSettingsConfirmed += HandlePracticeMatchSettingsConfirmed;
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
                mainMenuUI.OnPracticeMatch += HandlePracticeMatchRequested;
                mainMenuUI.OnExit += HandleExitGame;
                mainMenuUI.OnQARequested += HandleQARequested;
            }

            if (gameHUD != null)
            {
                gameHUD.OnExitToMenu += HandleGameExit;
                gameHUD.OnRetributionSkipClicked += HandleRetributionSkipClicked;
                gameHUD.OnUndoClicked += HandleUndoClicked;
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

            if (aiMatchSettingsUI != null)
            {
                aiMatchSettingsUI.OnSettingsConfirmed -= HandlePracticeMatchSettingsConfirmed;
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
                mainMenuUI.OnPracticeMatch -= HandlePracticeMatchRequested;
                mainMenuUI.OnExit -= HandleExitGame;
                mainMenuUI.OnQARequested -= HandleQARequested;
            }

            if (gameHUD != null)
            {
                gameHUD.OnExitToMenu -= HandleGameExit;
                gameHUD.OnRetributionSkipClicked -= HandleRetributionSkipClicked;
                gameHUD.OnUndoClicked -= HandleUndoClicked;
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

            if (aiMatchSettingsUI != null && aiMatchSettingsUI.gameObject.activeSelf)
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

            if (aiMatchSettingsUI != null)
            {
                aiMatchSettingsUI.SetActive(false);
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

            if (aiMatchSettingsUI != null)
            {
                aiMatchSettingsUI.SetActive(false);
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

        /// <summary>
        /// Practice Match's entry screen. Skips the normal Game Mode selector entirely — Practice
        /// matches are hardcoded to Ultimate mode, so the AI Settings panel is the only screen the
        /// player needs before Team Selection.
        /// </summary>
        public void ShowAIMatchSettings()
        {
            if (aiMatchSettingsUI != null)
            {
                aiMatchSettingsUI.SetActive(true);
            }

            if (mainMenuUI != null)
            {
                mainMenuUI.SetActive(false);
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

            if (aiMatchSettingsUI != null)
            {
                aiMatchSettingsUI.SetActive(false);
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

        /// <summary>
        /// Adapter for PromotionRequiredEventChannel — ShowPromotionUI() ignores the payload
        /// (the promotion square is read directly from Inspector-driven state elsewhere), but
        /// Register() requires an exact Action&lt;PromotionRequiredPayload&gt; signature match.
        /// </summary>
        private void HandlePromotionRequiredChannel(ChessTheBetrayal.Events.Payloads.PromotionRequiredPayload payload) =>
            ShowPromotionUI();

        public void TriggerGameOver(Team? winningTeam, bool byTimeout = false,
            ChessTheBetrayal.Events.Payloads.GameEndReason reason =
                ChessTheBetrayal.Events.Payloads.GameEndReason.Checkmate)
        {
            if (gameOverUI != null)
            {
                gameOverUI.SetWinnerText(winningTeam, byTimeout, reason);
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
            TriggerGameOver(payload.WinningTeam, payload.IsTimeout, payload.Reason);
        }

        public void ConfigureHUDForMode(GameModeConfig config)
        {
            gameHUD?.ConfigureForMode(config);
        }

        /// <summary>Passthrough to GameHUD — shows/hides the Undo button. GameManager calls this once per match.</summary>
        public void SetUndoVisible(bool visible) => gameHUD?.SetUndoVisible(visible);

        /// <summary>Passthrough to GameHUD — drives the Undo button's interactable state and color. GameManager calls this whenever UndoService.CanUndo changes.</summary>
        public void SetUndoInteractable(bool interactable) => gameHUD?.SetUndoInteractable(interactable);

        /// <summary>Passthrough to MainMenuUI — shows/hides the QA button. GameManager calls this once at startup from its own enableQAButton toggle.</summary>
        public void SetQAButtonVisible(bool visible) => mainMenuUI?.SetQAButtonVisible(visible);

        /// <summary>Passthrough to GameHUD — governs whether the Retribution Skip button is ever shown this match.</summary>
        public void SetRetributionSkipAllowed(bool allowed) => gameHUD?.SetSkipAllowed(allowed);

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

        private void HandlePracticeMatchRequested()
        {
            ShowAIMatchSettings();
        }

        private void HandlePracticeMatchSettingsConfirmed(PracticeMatchSettings settings)
        {
            if (aiMatchSettingsUI != null)
            {
                aiMatchSettingsUI.SetActive(false);
            }

            OnPracticeMatchSettingsConfirmed?.Invoke(settings);
            ShowTeamSelection();
        }

        private void HandleExitGame()
        {
            Application.Quit();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // Unconditional: Back in DeviceBenchmark.unity always returns here the same way, so there is
        // no decision to make on this end beyond which scene that is.
        private void HandleQARequested() => SceneManager.LoadScene(SceneNames.DeviceBenchmark);

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
            // Delegates through IMatchFlow to the host's bound IPostGameAction (BackToModeSelectAction
            // in the prototype), which tears down the finished match and decides what screen comes
            // next. UIManager resolves the interface, never the concrete GameManager — that's what
            // keeps the UI assembly free of any upward dependency on the App layer.
            if (ServiceLocator.Instance.TryResolve(out IMatchFlow matchFlow))
            {
                matchFlow.AcknowledgeGameOver();
            }
        }

        private void HandleRetributionSkipClicked()
        {
            OnRetributionSkipRequested?.Invoke();
        }

        private void HandleUndoClicked()
        {
            OnUndoRequested?.Invoke();
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
