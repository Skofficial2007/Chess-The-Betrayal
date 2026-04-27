using System;
using UnityEngine;
using UnityEngine.UI;

namespace ChessTheMasterPiece.UI
{
    /// <summary>
    /// Simple HUD controller exposing gameplay HUD controls (exit).
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button exitButton;

        public event Action OnExitToMenu;

        private void Awake()
        {
            if (exitButton != null)
            {
                exitButton.onClick.AddListener(() => OnExitToMenu?.Invoke());
            }
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }
    }
}