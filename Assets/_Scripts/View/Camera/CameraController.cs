using System.Collections;
using UnityEngine;
using Unity.Cinemachine;
using ChessTheBetrayal.UI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;

namespace ChessTheBetrayal.View
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

        [Header("Event Channels")]
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _matchStartRequestedChannel;
        [SerializeField] private ChessTheBetrayal.Events.TeamSelectedEventChannel _teamSelectedChannel;
        [SerializeField] private ChessTheBetrayal.Events.GameEventChannel _gameResetChannel;

        private void Awake()
        {
            ValidateRequiredFields();
        }

        private void ValidateRequiredFields()
        {
            InspectorGuard.Require(menuCam, nameof(menuCam), this);
            InspectorGuard.Require(whiteTeamCam, nameof(whiteTeamCam), this);
            InspectorGuard.Require(blackTeamCam, nameof(blackTeamCam), this);
            InspectorGuard.Require(_matchStartRequestedChannel, nameof(_matchStartRequestedChannel), this);
            InspectorGuard.Require(_teamSelectedChannel, nameof(_teamSelectedChannel), this);
            InspectorGuard.Require(_gameResetChannel, nameof(_gameResetChannel), this);
        }

        private void OnEnable()
        {
            _teamSelectedChannel?.Register(HandleTeamSelected);
            _gameResetChannel?.Register(HandleGameReset);
        }

        private void OnDisable()
        {
            _teamSelectedChannel?.Unregister(HandleTeamSelected);
            _gameResetChannel?.Unregister(HandleGameReset);
        }

        private void Start()
        {
            // Set initial state to the Menu profile shot
            ActivateCamera(menuCam);
        }

        public void HandleTeamSelected(Team selectedTeam)
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
            
            _matchStartRequestedChannel?.Raise();
        }

        public void HandleGameReset()
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