using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.UI
{
    /// <summary>
    /// Handles the game-over panel UI (winner text, replay and exit actions).
    /// </summary>
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

        public void SetWinnerText(Team? winnerTeam, bool byTimeout = false)
        {
            if (winnerText == null) return;

            string prefix = byTimeout ? "Time Out!\n" : string.Empty;

            winnerText.text = winnerTeam switch
            {
                Team.White => $"{prefix}White Team Won!",
                Team.Black => $"{prefix}Black Team Won!",
                _          => byTimeout ? "Time Out!\nDraw (Insufficient Material)" : "Stalemate! Draw."
            };
        }
    }
}