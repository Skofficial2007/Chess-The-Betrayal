using System;
using UnityEngine;
using UnityEngine.UI;
using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.UI
{
    public class PromotionUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button queenButton;
        [SerializeField] private Button rookButton;
        [SerializeField] private Button bishopButton;
        [SerializeField] private Button knightButton;

        public event Action<ChessPieceType> OnPieceSelected;

        private void Awake()
        {
            if (queenButton != null)
            {
                queenButton.onClick.AddListener(() => OnPieceSelected?.Invoke(ChessPieceType.Queen));
            }
            if (rookButton != null)
            {
                rookButton.onClick.AddListener(() => OnPieceSelected?.Invoke(ChessPieceType.Rook));
            }
            if (bishopButton != null)
            {
                bishopButton.onClick.AddListener(() => OnPieceSelected?.Invoke(ChessPieceType.Bishop));
            }
            if (knightButton != null)
            {
                knightButton.onClick.AddListener(() => OnPieceSelected?.Invoke(ChessPieceType.Knight));
            }
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }
    }
}