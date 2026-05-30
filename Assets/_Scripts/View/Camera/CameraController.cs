using System.Collections;
using UnityEngine;
using Unity.Cinemachine;
using ChessTheBetrayal.UI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Gameplay;

namespace ChessTheBetrayal.UI.Camera
{
    /// <summary>
    /// Listens to UI events and shifts the Cinemachine priorities to 
    /// orchestrate smooth camera transitions using Cinemachine 3.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("Cinemachine 3 Cameras")]
        [SerializeField] private CinemachineCamera menuCam;
        [SerializeField] private CinemachineCamera whiteTeamCam;
        [SerializeField] private CinemachineCamera blackTeamCam;

        [Header("Settings")]
        [Tooltip("How long the game waits for the camera to pan before starting the clock.")]
        [SerializeField] private float introBlendTime = 2f;

        private void Start()
        {
            if (UIManager.Instance == null)
            {
                Debug.LogError("[CameraController] UIManager is missing!");
                return;
            }

            // Subscribe to the existing UI events you already built
            UIManager.Instance.OnTeamSelected += HandleTeamSelected;
            UIManager.Instance.OnGameReset += HandleGameReset;

            // Set initial state to the Menu profile shot
            ActivateCamera(menuCam);
        }

        private void OnDestroy()
        {
            // Unsubscribe to prevent memory leaks
            if (UIManager.Instance != null)
            {
                UIManager.Instance.OnTeamSelected -= HandleTeamSelected;
                UIManager.Instance.OnGameReset -= HandleGameReset;
            }
        }

        private void HandleTeamSelected(Team selectedTeam)
        {
            if (selectedTeam == Team.White)
            {
                ActivateCamera(whiteTeamCam);
            }
            else
            {
                ActivateCamera(blackTeamCam);
            }

            // Start the delay timer
            StartCoroutine(WaitAndStartMatch());
        }

        private IEnumerator WaitAndStartMatch()
        {
            yield return new WaitForSeconds(introBlendTime);
            
            if (GameManager.Instance != null)
            {
                GameManager.Instance.StartMatch();
            }
        }

        private void HandleGameReset()
        {
            // Return to the cinematic side view when exiting to menu
            ActivateCamera(menuCam);
        }

        /// <summary>
        /// Switches to a camera by giving it the highest priority. Cinemachine's Brain handles the smooth blend automatically.
        /// </summary>
        private void ActivateCamera(CinemachineCamera targetCam)
        {
            // Set all to low priority
            menuCam.Priority = 10;
            whiteTeamCam.Priority = 10;
            blackTeamCam.Priority = 10;

            // Elevate the target. The Cinemachine Brain will automatically lerp to this new target.
            targetCam.Priority = 20;
        }
    }
}