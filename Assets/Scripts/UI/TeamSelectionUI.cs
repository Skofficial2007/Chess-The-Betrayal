using System;
using UnityEngine;
using UnityEngine.UI;

namespace ChessTheMasterPiece.UI
{
    public class TeamSelectionUI : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Team Buttons")]
        [SerializeField] private Button whiteTeamButton;
        [SerializeField] private Button blackTeamButton;

        #endregion

        #region Public State

        // 0 = White, 1 = Black
        public static int ChosenTeam { get; private set; } = 0;

        // Blocks game input when true
        public static bool IsOpen { get; private set; } = true;

        public static event Action<int> OnTeamChosen;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Determine start state based on whether the object is active in the scene
            IsOpen = gameObject.activeSelf;

            RegisterListeners();
        }

        private void OnDestroy()
        {
            UnregisterListeners();
        }

        #endregion

        #region Internal Logic

        private void RegisterListeners()
        {
            if (whiteTeamButton != null)
                whiteTeamButton.onClick.AddListener(OnWhiteTeamChosen);
            else
                Debug.LogWarning("[TeamSelectionUI] White Team Button is not assigned.");

            if (blackTeamButton != null)
                blackTeamButton.onClick.AddListener(OnBlackTeamChosen);
            else
                Debug.LogWarning("[TeamSelectionUI] Black Team Button is not assigned.");
        }

        private void UnregisterListeners()
        {
            if (whiteTeamButton != null)
                whiteTeamButton.onClick.RemoveListener(OnWhiteTeamChosen);

            if (blackTeamButton != null)
                blackTeamButton.onClick.RemoveListener(OnBlackTeamChosen);
        }

        private void OnWhiteTeamChosen()
        {
            ConfirmSelection(0);
        }

        private void OnBlackTeamChosen()
        {
            ConfirmSelection(1);
        }

        private void ConfirmSelection(int teamIndex)
        {
            ChosenTeam = teamIndex;

            // Release input lock
            IsOpen = false;

            // Notify Chessboard
            OnTeamChosen?.Invoke(ChosenTeam);

            // Hide UI
            gameObject.SetActive(false);
        }

        #endregion
    }
}