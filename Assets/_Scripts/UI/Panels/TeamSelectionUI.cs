using System;
using UnityEngine;
using UnityEngine.UI;
using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.UI
{
    /// <summary>
    /// Lets the player pick a team and emits the choice.
    /// </summary>
    public class TeamSelectionUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button whiteTeamButton;
        [SerializeField] private Button blackTeamButton;

        public event Action<Team> OnTeamSelected;

        private void Awake()
        {
            if (whiteTeamButton != null)
            {
                whiteTeamButton.onClick.AddListener(() => OnTeamSelected?.Invoke(Team.White));
            }

            if (blackTeamButton != null)
            {
                blackTeamButton.onClick.AddListener(() => OnTeamSelected?.Invoke(Team.Black));
            }
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }
    }
}