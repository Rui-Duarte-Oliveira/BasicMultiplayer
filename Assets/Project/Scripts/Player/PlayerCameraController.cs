using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

namespace BasicMultiplayer.Player
{
    /// <summary>
    /// Configures the camera to follow the local player.
    /// Ensures that Cinemachine targets only the owned player instance on the local client.
    /// </summary>
    public class PlayerCameraController : NetworkBehaviour
    {
        [Header("Camera Settings")]
        [Tooltip("Offset from the player position for the camera to look at")]
        [SerializeField] private Vector3 _lookAtOffset = new Vector3(0f, 0f, 0f);

        [Header("Optional - Manual Camera Reference")]
        [Tooltip("Leave empty to auto-find. Only set if you have multiple virtual cameras.")]
        [SerializeField] private CinemachineCamera _virtualCamera;

        private Transform _cameraFollowTarget;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            //Only the local player should control the camera
            if (!IsOwner) 
                return;

            SetupCameraFollow();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            if (!IsOwner) 
                return;

            //Clean up - release camera target
            if (_virtualCamera != null)
            {
                _virtualCamera.Follow = null;
                _virtualCamera.LookAt = null;
            }

            //Destroy the follow target helper
            if (_cameraFollowTarget != null)
            {
                Destroy(_cameraFollowTarget.gameObject);
            }
        }

        private void SetupCameraFollow()
        {
            //Find the virtual camera if not assigned
            if (_virtualCamera == null)
            {
                _virtualCamera = FindFirstObjectByType<CinemachineCamera>();

                if (_virtualCamera == null)
                {
                    Debug.LogError("[PlayerCameraController] No CinemachineCamera found in scene! " +
                                   "Please add a Cinemachine Camera to your scene.");
                    return;
                }
            }

            //Create a follow target (child object) for smoother camera behavior
            CreateFollowTarget();

            //Assign this player as the camera's follow target
            _virtualCamera.Follow = _cameraFollowTarget;
            _virtualCamera.LookAt = _cameraFollowTarget;

            Debug.Log($"[PlayerCameraController] Camera now following local player (ClientId: {OwnerClientId})");
        }

        /// <summary>
        /// Creates a child transform for the camera to follow.
        /// This allows for offset adjustments without modifying the player's actual position.
        /// </summary>
        private void CreateFollowTarget()
        {
            GameObject followTargetGO = new GameObject("CameraFollowTarget");
            _cameraFollowTarget = followTargetGO.transform;
            _cameraFollowTarget.SetParent(transform);
            _cameraFollowTarget.localPosition = _lookAtOffset;
        }

        /// <summary>
        /// Triggers a camera shake effect. Call this on goal scored, dash impact, etc.
        /// Requires CinemachineImpulseSource on the player or in the scene.
        /// </summary>
        public void TriggerCameraShake(float intensity = 1f)
        {
            if (!IsOwner) 
                return;

            CinemachineImpulseSource impulseSource = GetComponent<CinemachineImpulseSource>();
            if (impulseSource != null)
            {
                impulseSource.GenerateImpulse(intensity);
            }
        }
    }
}