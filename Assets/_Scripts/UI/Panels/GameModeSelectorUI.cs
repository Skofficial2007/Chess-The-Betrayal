using System;
using UnityEngine;
using UnityEngine.UI;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Manages the custom 2-column UI for Game Mode selection.
    /// Handles visual states (greying out inactive options) and mapping UI selections to GameModeConfigs.
    /// </summary>
    public class GameModeSelectorUI : MonoBehaviour
    {
        public event Action<GameModeConfig> OnModeSelected;

        [Header("Main Mode Toggles")]
        [SerializeField] private Toggle _toggleBullet;
        [SerializeField] private Toggle _toggleBlitz;
        [SerializeField] private Toggle _toggleRapid;
        [SerializeField] private Toggle _toggleUltimate;

        [Header("Time Row Canvas Groups")]
        [SerializeField] private CanvasGroup _cgBulletTimes;
        [SerializeField] private CanvasGroup _cgBlitzTimes;
        [SerializeField] private CanvasGroup _cgRapidTimes;

        [Header("Time Option Toggles")]
        [SerializeField] private Toggle _toggleBullet1_0;
        [SerializeField] private Toggle _toggleBullet2_1;
        [SerializeField] private Toggle _toggleBlitz3_0;
        [SerializeField] private Toggle _toggleBlitz5_5;
        [SerializeField] private Toggle _toggleRapid10_0;
        [SerializeField] private Toggle _toggleRapid15_10;

        [Header("Controls")]
        [SerializeField] private Button _doneButton;

        private void Start()
        {
            RegisterToggleListeners();
            _doneButton.onClick.AddListener(HandleStartClicked);
            
            // Force an initial update so the UI starts in the correct visual state
            UpdateVisualStates();
        }

        private void RegisterToggleListeners()
        {
            // Listen for Main Mode changes to update the CanvasGroups
            _toggleBullet.onValueChanged.AddListener((isOn) => { if (isOn) UpdateVisualStates(); });
            _toggleBlitz.onValueChanged.AddListener((isOn) => { if (isOn) UpdateVisualStates(); });
            _toggleRapid.onValueChanged.AddListener((isOn) => { if (isOn) UpdateVisualStates(); });
            _toggleUltimate.onValueChanged.AddListener((isOn) => { if (isOn) UpdateVisualStates(); });

            // Listen for Time Option changes to re-evaluate the Start button
            _toggleBullet1_0.onValueChanged.AddListener((_) => ValidateDoneButton());
            _toggleBullet2_1.onValueChanged.AddListener((_) => ValidateDoneButton());
            _toggleBlitz3_0.onValueChanged.AddListener((_) => ValidateDoneButton());
            _toggleBlitz5_5.onValueChanged.AddListener((_) => ValidateDoneButton());
            _toggleRapid10_0.onValueChanged.AddListener((_) => ValidateDoneButton());
            _toggleRapid15_10.onValueChanged.AddListener((_) => ValidateDoneButton());
        }

        /// <summary>
        /// Updates the interactability and opacity of the time rows based on the active Main Mode.
        /// </summary>
        private void UpdateVisualStates()
        {
            SetCanvasGroupState(_cgBulletTimes, _toggleBullet.isOn);
            SetCanvasGroupState(_cgBlitzTimes, _toggleBlitz.isOn);
            SetCanvasGroupState(_cgRapidTimes, _toggleRapid.isOn);

            ValidateDoneButton();
        }

        private void SetCanvasGroupState(CanvasGroup cg, bool isActive)
        {
            if (cg == null) return;
            cg.interactable = isActive;
            cg.alpha = isActive ? 1f : 0.4f; 
        }

        /// <summary>
        /// Checks if a valid combination is selected. If so, enables the Done button.
        /// </summary>
        private void ValidateDoneButton()
        {
            bool isValid = false;

            if (_toggleBullet.isOn)
            {
                isValid = _toggleBullet1_0.isOn || _toggleBullet2_1.isOn;
            }
            else if (_toggleBlitz.isOn)
            {
                isValid = _toggleBlitz3_0.isOn || _toggleBlitz5_5.isOn;
            }
            else if (_toggleRapid.isOn)
            {
                isValid = _toggleRapid10_0.isOn || _toggleRapid15_10.isOn;
            }
            else if (_toggleUltimate.isOn)
            {
                isValid = true; // Ultimate requires no time selection
            }

            _doneButton.interactable = isValid;
        }

        /// <summary>
        /// Maps the current UI state back to the concrete GameModePresets and fires the event.
        /// </summary>
        private void HandleStartClicked()
        {
            GameModeConfig selectedConfig = GameModePresets.Unlimited; // Default fallback

            if (_toggleBullet.isOn)
            {
                selectedConfig = _toggleBullet1_0.isOn ? GameModePresets.Bullet1_0 : GameModePresets.Bullet2_1;
            }
            else if (_toggleBlitz.isOn)
            {
                selectedConfig = _toggleBlitz3_0.isOn ? GameModePresets.Blitz3_0 : GameModePresets.Blitz5_5;
            }
            else if (_toggleRapid.isOn)
            {
                selectedConfig = _toggleRapid10_0.isOn ? GameModePresets.Rapid10_0 : GameModePresets.Rapid15_10;
            }
            else if (_toggleUltimate.isOn)
            {
                selectedConfig = GameModePresets.Unlimited;
            }

            OnModeSelected?.Invoke(selectedConfig);
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }
    }
}
