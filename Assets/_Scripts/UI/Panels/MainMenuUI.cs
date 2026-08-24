using System;
using UnityEngine;
using UnityEngine.UI;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Main menu controller exposing Play and Exit events.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button practiceMatchButton;
        [SerializeField] private Button exitButton;

        [Header("QA (optional)")]
        [SerializeField] private Button qaButton;

        public event Action OnPlay;
        public event Action OnPracticeMatch;
        public event Action OnExit;
        public event Action OnQARequested;

        private void Awake()
        {
            if (playButton != null)
            {
                playButton.onClick.AddListener(() => OnPlay?.Invoke());
            }

            if (practiceMatchButton != null)
            {
                practiceMatchButton.onClick.AddListener(() => OnPracticeMatch?.Invoke());
            }

            if (exitButton != null)
            {
                exitButton.onClick.AddListener(() => OnExit?.Invoke());
            }

            if (qaButton != null)
            {
                qaButton.onClick.AddListener(() => OnQARequested?.Invoke());
            }

            // Hidden until whoever owns the setting (GameManager) says otherwise, so a build never
            // shows this to a player just because the scene happened to leave it active.
            SetQAButtonVisible(false);
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }

        public void SetQAButtonVisible(bool visible)
        {
            if (qaButton != null)
            {
                qaButton.gameObject.SetActive(visible);
            }
        }
    }
}