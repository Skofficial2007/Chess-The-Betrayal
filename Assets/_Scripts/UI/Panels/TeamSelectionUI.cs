using System;
using UnityEngine;
using UnityEngine.UI;

namespace ChessTheMasterPiece.UI
{
    public class TeamSelectionUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button whiteTeamButton;
        [SerializeField] private Button blackTeamButton;

        public event Action<int> OnTeamSelected;

        private void Awake()
        {
            if (whiteTeamButton != null)
            {
                whiteTeamButton.onClick.AddListener(() => OnTeamSelected?.Invoke(0));
            }

            if (blackTeamButton != null)
            {
                blackTeamButton.onClick.AddListener(() => OnTeamSelected?.Invoke(1));
            }
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }
    }
}