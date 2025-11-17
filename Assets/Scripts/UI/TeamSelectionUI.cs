using System;
using UnityEngine;
using UnityEngine.UI;

public class TeamSelectionUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button whiteTeamButton;
    [SerializeField] private Button blackTeamButton;

    // The team chosen by the player (White = 0, Black = 1)
    public static int ChosenTeam { get; private set; } = 0;

    // Is the selection UI open? Other systems should block input while this is true.
    public static bool IsOpen { get; private set; } = true;

    // Notifies subscribers when a team is chosen. Parameter is the chosen team int (0 = white, 1 = black).
    public static event Action<int> OnTeamChosen;

    private void Awake()
    {
        // UI starts open by default if this GameObject is active in the scene.
        IsOpen = gameObject.activeSelf;

        if (whiteTeamButton == null)
        {
            Debug.LogWarning("[TeamSelectionUI] whiteTeamButton reference missing.");
        }
        if (blackTeamButton == null)
        {
            Debug.LogWarning("[TeamSelectionUI] blackTeamButton reference missing.");
        }

        if (whiteTeamButton != null)
            whiteTeamButton.onClick.AddListener(OnWhiteTeamChosen);
        if (blackTeamButton != null)
            blackTeamButton.onClick.AddListener(OnBlackTeamChosen);
    }

    private void OnDestroy()
    {
        if (whiteTeamButton != null)
            whiteTeamButton.onClick.RemoveListener(OnWhiteTeamChosen);
        if (blackTeamButton != null)
            blackTeamButton.onClick.RemoveListener(OnBlackTeamChosen);
    }

    private void OnWhiteTeamChosen()
    {
        ChosenTeam = 0; // White
        CloseAndNotify();
    }

    private void OnBlackTeamChosen()
    {
        ChosenTeam = 1; // Black
        CloseAndNotify();
    }

    private void CloseAndNotify()
    {
        // Mark UI closed so background input can resume.
        IsOpen = false;

        // Notify listeners (Chessboard) so it can spawn/adjust orientation.
        OnTeamChosen?.Invoke(ChosenTeam);

        // Disable UI screen
        gameObject.SetActive(false);
    }
}
