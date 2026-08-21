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

        public event Action OnPlay;
        public event Action OnPracticeMatch;
        public event Action OnExit;

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
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }
    }
}