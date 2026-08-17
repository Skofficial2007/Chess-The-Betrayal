using System.Collections;
using PrimeTween;
using UnityEngine;
using Unity.Cinemachine;
using ChessTheBetrayal.Infrastructure;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.View
{
    /// <summary>
    /// Listens to UI events and shifts the Cinemachine priorities to 
    /// orchestrate smooth camera transitions using Cinemachine 3.
    /// </summary>
    public class CameraController : MonoBehaviour, ICameraShake
    {
        // The knock a capture gives the camera. Deliberately small: the camera is looking at a
        // chessboard from a fixed seat, so a shake reads as impact at a fraction of the movement an
        // action game would use — much more than this and it stops looking like the board was hit
        // and starts looking like the camera came loose.
        private const float ShakeDuration = 0.22f;
        private const float ShakeMaxOffset = 0.055f;
        private const float ShakeFrequency = 38f;
        private const float ShakeTiltFrequency = 31f;
        private const float ShakeMaxTiltDegrees = 0.5f;

        private Tween _shakeTween;
        private CinemachineCamera _shakenCam;
        private Vector3 _shakeRestPosition;
        private Quaternion _shakeRestRotation;

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

            // Registered here rather than wired in the inspector on purpose. A shake that only
            // happens when somebody remembered to drag this object into a field is a shake that
            // will eventually be missing from a scene, and this project has already shipped a build
            // missing a visual for exactly that reason. Consumers resolve it optionally, so a scene
            // without a camera controller simply does not shake.
            ServiceLocator.Instance.Register<ICameraShake>(this);
        }

        /// <summary>
        /// Knocks the live virtual camera and settles it back exactly where it started.
        ///
        /// The virtual camera is moved, not the real one: the Brain owns the real camera's transform
        /// and overwrites anything written to it every frame. These virtual cameras carry no body or
        /// aim components, so the Brain tracks them one to one and the knock arrives intact.
        ///
        /// Everything runs off one driver on unscaled time, decaying to zero by the end — the same
        /// arrangement the pieces' own check-shake uses, and for the same reason: the camera has to
        /// land back on its exact framing, and a decaying envelope guarantees that without a second
        /// tween to put it there.
        /// </summary>
        public void Shake(float strength)
        {
            strength = Mathf.Clamp01(strength);

            CinemachineCamera target = LiveCamera();
            if (target == null) return;

            // A knock arriving while the last one is still going would otherwise read the displaced
            // pose as the new resting one and leave the camera off its framing for good. Restore
            // before re-reading, and check before Stop() since stopping clears isAlive.
            if (_shakeTween.isAlive && _shakenCam != null)
            {
                _shakenCam.transform.localPosition = _shakeRestPosition;
                _shakenCam.transform.localRotation = _shakeRestRotation;
            }
            _shakeTween.Stop();

            _shakenCam = target;
            _shakeRestPosition = target.transform.localPosition;
            _shakeRestRotation = target.transform.localRotation;

            _shakeTween = Tween.Custom(this, 0f, 1f, ShakeDuration,
                (self, t) => self.ApplyShake(t, strength), Ease.Linear, useUnscaledTime: true);
        }

        private void ApplyShake(float t, float strength)
        {
            // The camera can be destroyed or the scene torn down mid-shake.
            if (_shakenCam == null) return;

            float decay = (1f - t) * (1f - t);
            float sway = Mathf.Sin(t * ShakeFrequency) * ShakeMaxOffset * strength * decay;
            float heave = Mathf.Sin(t * ShakeFrequency * 0.5f) * ShakeMaxOffset * 0.6f * strength * decay;
            float tilt = Mathf.Sin(t * ShakeTiltFrequency) * ShakeMaxTiltDegrees * strength * decay;

            Transform cam = _shakenCam.transform;
            cam.localPosition = _shakeRestPosition + cam.right * sway + cam.up * heave;
            cam.localRotation = _shakeRestRotation * Quaternion.AngleAxis(tilt, Vector3.forward);
        }

        /// <summary>The camera currently being blended to, which is the one holding top priority.</summary>
        private CinemachineCamera LiveCamera()
        {
            if (whiteTeamCam != null && whiteTeamCam.Priority >= 20) return whiteTeamCam;
            if (blackTeamCam != null && blackTeamCam.Priority >= 20) return blackTeamCam;
            return menuCam;
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