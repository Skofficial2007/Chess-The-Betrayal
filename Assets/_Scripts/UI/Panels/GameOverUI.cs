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

        public void SetWinnerText(Team? winnerTeam)
        {
            if (winnerText != null)
            {
                if (winnerTeam == Team.White)
                {
                    winnerText.text = "White Team Won!";
                }
                else if (winnerTeam == Team.Black)
                {
                    winnerText.text = "Black Team Won!";
                }
                else
                {
                    winnerText.text = "Stalemate! Draw.";
                }
            }
        }
    }
}