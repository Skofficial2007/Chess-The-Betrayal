using System;
using UnityEngine;
using UnityEngine.UI;
using ChessTheMasterPiece.ChessPiece;

namespace ChessTheMasterPiece.UI
{
    public class PromotionUI : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Piece Selection Buttons")]
        [SerializeField] private Button queenButton;
        [SerializeField] private Button rookButton;
        [SerializeField] private Button bishopButton;
        [SerializeField] private Button knightButton;

        #endregion

        #region Static Accessors

        // Singleton instance for global access
        public static PromotionUI Instance { get; private set; }

        // Blocks game input when true
        public static bool IsOpen { get; private set; } = false;

        // Event fired when the player makes a selection
        public static event Action<ChessPieceType> OnPieceChosen;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Singleton Pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Default State: Hidden
            gameObject.SetActive(false);
            IsOpen = false;

            RegisterListeners();
        }

        private void OnDestroy()
        {
            UnregisterListeners();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        #endregion

        #region Public Interface

        /// <summary>
        /// Activates the Promotion UI and pauses game interaction.
        /// </summary>
        public void EnableUI()
        {
            gameObject.SetActive(true);
            IsOpen = true;
        }

        #endregion

        #region Internal Logic

        private void RegisterListeners()
        {
            if (queenButton != null) queenButton.onClick.AddListener(() => SelectPiece(ChessPieceType.Queen));
            if (rookButton != null) rookButton.onClick.AddListener(() => SelectPiece(ChessPieceType.Rook));
            if (bishopButton != null) bishopButton.onClick.AddListener(() => SelectPiece(ChessPieceType.Bishop));
            if (knightButton != null) knightButton.onClick.AddListener(() => SelectPiece(ChessPieceType.Knight));
        }

        private void UnregisterListeners()
        {
            if (queenButton != null) queenButton.onClick.RemoveAllListeners();
            if (rookButton != null) rookButton.onClick.RemoveAllListeners();
            if (bishopButton != null) bishopButton.onClick.RemoveAllListeners();
            if (knightButton != null) knightButton.onClick.RemoveAllListeners();
        }

        private void SelectPiece(ChessPieceType type)
        {
            // Close the UI immediately
            IsOpen = false;
            gameObject.SetActive(false);

            // Notify the Chessboard
            OnPieceChosen?.Invoke(type);
        }

        #endregion
    }
}