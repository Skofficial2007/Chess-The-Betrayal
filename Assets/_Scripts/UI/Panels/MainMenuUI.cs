using System;
using UnityEngine;
using UnityEngine.UI;

namespace ChessTheMasterPiece.UI
{
    public class MainMenuUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button exitButton;

        public event Action OnPlay;
        public event Action OnExit;

        private void Awake()
        {
            if (playButton != null)
            {
                playButton.onClick.AddListener(() => OnPlay?.Invoke());
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