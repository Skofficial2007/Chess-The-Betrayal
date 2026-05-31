using System;
using UnityEngine;
using UnityEngine.UI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Gameplay;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Manages the in-game heads-up display, including clock visibility and exit controls.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button exitButton;

        [Header("Clock")]
        [SerializeField] private ClockDisplayWidget _clockWidget;

        public event Action OnExitToMenu;

        private void Awake()
        {
            if (exitButton != null)
            {
                exitButton.onClick.AddListener(() => OnExitToMenu?.Invoke());
            }
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnLowTimeAlert += HandleLowTimeWarning;
            }
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnLowTimeAlert -= HandleLowTimeWarning;
            }
        }

        public void SetActive(bool active)
        {
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

        private void HandleLowTimeWarning(Team team, long remainingMs)
        {
            // v1: The ClockDisplayWidget handles color changes internally based on state.
            // This event hook is reserved for future audio cues or HUD screen-shake polish.
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