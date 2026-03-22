using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ChessTheMasterPiece.UI
{
    public class GameOverUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI winnerText;
        [SerializeField] private Button replayButton;
        [SerializeField] private Button exitButton;

        public event Action OnReplay;
        public event Action OnExit;

        private void Awake()
        {
            if (replayButton != null)
            {
                replayButton.onClick.AddListener(() => OnReplay?.Invoke());
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

        public void SetWinnerText(int winnerTeam)
        {
            if (winnerText != null)
            {
                winnerText.text = (winnerTeam == 0) ? "White Team Won!" : "Black Team Won!";
            }
        }
    }
}