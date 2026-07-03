using System;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Gameplay;
using ChessTheBetrayal.Events.Payloads;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Manages the in-game heads-up display, including clock visibility, exit controls, and the
    /// Retribution Skip button (visible only while the player is sitting in RetributionPending
    /// with a legal Executioner they're choosing not to use).
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button exitButton;

        [Header("Clock")]
        [SerializeField] private ClockDisplayWidget _clockWidget;

        [Header("Retribution Skip")]
        [SerializeField] private ChessTheBetrayal.Events.BetrayalEventChannel _betrayalChannel;
        [SerializeField] private RectTransform skipButtonRoot;
        [SerializeField] private Button skipButton;
        [SerializeField] private float _skipShowScale = 1f;
        [SerializeField] private float _skipHiddenScale = 0f;
        [SerializeField] private float _skipShowDuration = 0.25f;
        [SerializeField] private float _skipHideDuration = 0.15f;
        [SerializeField] private Ease _skipShowEase = Ease.OutBack;
        [SerializeField] private Ease _skipHideEase = Ease.InBack;

        public event Action OnExitToMenu;
        public event Action OnRetributionSkipClicked;

        private Tweener _skipButtonTween;
        private bool _skipButtonVisible;

        private void Awake()
        {
            ValidateRequiredFields();

            if (exitButton != null)
            {
                exitButton.onClick.AddListener(() => OnExitToMenu?.Invoke());
            }

            if (skipButton != null)
            {
                skipButton.onClick.AddListener(() => OnRetributionSkipClicked?.Invoke());
            }

            // Start hidden and inert — no flash-of-visible-button before the first BetrayalPhase event.
            if (skipButtonRoot != null)
            {
                skipButtonRoot.localScale = Vector3.one * _skipHiddenScale;
                skipButtonRoot.gameObject.SetActive(false);
            }
        }

        private void ValidateRequiredFields()
        {
            InspectorGuard.Require(exitButton, nameof(exitButton), this);
            InspectorGuard.Require(_clockWidget, nameof(_clockWidget), this);
            InspectorGuard.Require(_betrayalChannel, nameof(_betrayalChannel), this);
            InspectorGuard.Require(skipButtonRoot, nameof(skipButtonRoot), this);
            InspectorGuard.Require(skipButton, nameof(skipButton), this);
        }

        private void OnEnable()
        {
            _betrayalChannel?.Register(HandleBetrayalPhaseChanged);
        }

        private void OnDisable()
        {
            _betrayalChannel?.Unregister(HandleBetrayalPhaseChanged);
            _skipButtonTween?.Kill();
        }

        private void Start()
        {
            // No direct GameManager subscriptions - all events come through the Inspector-wired channels
        }

        private void OnDestroy()
        {
            // Cleanup if needed
        }

        /// <summary>
        /// The Skip button is only ever a legal action while resting in RetributionPending — every
        /// other BetrayalPhase (Resolved, DefectionOccurred, ForcedSaveActive) means the sub-machine
        /// has already moved on, so it hides. Initiated is the split-second before RetributionPending
        /// is raised (see MatchDriver.PlayMove's Act branch), so it hides too.
        /// </summary>
        private void HandleBetrayalPhaseChanged(BetrayalPayload payload)
        {
            SetSkipButtonVisible(payload.Phase == BetrayalPhase.RetributionPending);
        }

        private void SetSkipButtonVisible(bool visible)
        {
            if (skipButtonRoot == null || visible == _skipButtonVisible) return;
            _skipButtonVisible = visible;

            _skipButtonTween?.Kill();

            if (visible)
            {
                skipButtonRoot.gameObject.SetActive(true);
                skipButtonRoot.localScale = Vector3.one * _skipHiddenScale;
                _skipButtonTween = skipButtonRoot
                    .DOScale(_skipShowScale, _skipShowDuration)
                    .SetEase(_skipShowEase);
            }
            else
            {
                _skipButtonTween = skipButtonRoot
                    .DOScale(_skipHiddenScale, _skipHideDuration)
                    .SetEase(_skipHideEase)
                    .OnComplete(() => skipButtonRoot.gameObject.SetActive(false));
            }
        }

        public void SetActive(bool active)
        {
            if (!active)
            {
                // Snap the Skip button back to hidden (no animation) so re-activating the HUD for a
                // fresh match never briefly shows it before the next BetrayalPhase event arrives.
                _skipButtonTween?.Kill();
                _skipButtonVisible = false;
                if (skipButtonRoot != null)
                {
                    skipButtonRoot.localScale = Vector3.one * _skipHiddenScale;
                    skipButtonRoot.gameObject.SetActive(false);
                }
            }

            gameObject.SetActive(active);
        }

        /// <summary>
        /// Configures the HUD display elements based on the active game mode.
        /// </summary>
        public void ConfigureForMode(GameModeConfig config)
        {
            if (_clockWidget != null)
            {
                _clockWidget.gameObject.SetActive(!config.IsUnlimited);
            }
        }

        /// <summary>
        /// Called when a player's clock time drops below the urgency threshold.
        /// </summary>
        public void HandleLowTimeWarning(ChessTheBetrayal.Events.Payloads.LowTimeAlertPayload payload)
        {
            // v1: The ClockDisplayWidget handles color changes internally based on state.
            // This event hook is reserved for future audio cues or HUD screen-shake polish.
            // E.g., if (payload.AffectedTeam == PlayerTeam) { PlayUrgencySound(); }
        }

        /// <summary>
        /// Called when the turn changes. Update turn indicators or any per-turn UI elements here.
        /// </summary>
        public void HandleTurnChanged(ChessTheBetrayal.Events.Payloads.TurnChangedPayload payload)
        {
            // Update your UI text or indicators here based on payload.CurrentTeam
            // Example: turnIndicatorText.text = $"{payload.CurrentTeam}'s Turn";
            Debug.Log($"[HUD] Turn changed to {payload.CurrentTeam}");
        }

        /// <summary>
        /// Called when a check is detected. Trigger visual feedback like screen flash or animation.
        /// </summary>
        public void HandleCheckDetected()
        {
            // Trigger your check flash animation or particle effect here
            // Example: _animator.SetTrigger("PlayCheckFlash");
            Debug.Log("[HUD] CHECK detected! Flashing screen...");
        }
    }
}