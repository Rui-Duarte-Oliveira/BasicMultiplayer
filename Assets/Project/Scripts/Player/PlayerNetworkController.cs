using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BasicMultiplayer.Player
{
    /// <summary>
    /// Manages player input and network sync.
    /// 
    /// USAGE NOTE:
    /// Relies on Owner Authority to avoid the overhead of server-side prediction/reconciliation.
    /// While this provides the best "game feel" for this scope, it trusts the client position, 
    /// making it vulnerable to teleport cheats.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerNetworkController : NetworkBehaviour
    {
        [Header("Visual Feedback")]
        [SerializeField] private MeshRenderer _meshRenderer;
        [SerializeField] private Material _player1Material;
        [SerializeField] private Material _player2Material;

        [Header("Spawn Points (Set by GameManager)")]
        [SerializeField] private Transform[] _spawnPoints;

        /// <summary>
        /// Networked player index (0/1). 
        /// Driven by the server to ensure consistent team assignment across all clients.
        /// </summary>
        public NetworkVariable<int> PlayerIndex = new NetworkVariable<int>(
            -1, // Default: unassigned
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        /// <summary>
        /// Tracks if this player is ready/active in the game.
        /// </summary>
        public NetworkVariable<bool> IsReady = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private PlayerMotor _motor;
        private PlayerInput _playerInput;
        private Rigidbody _rb;
        private Vector2 _currentInput;
        private bool _dashPressed;

        //Input Action references (cached for performance)
        private InputAction _moveAction;
        private InputAction _dashAction;

        /// <summary>
        /// Entry point for network initialization. Handles logic that requires 
        /// valid NetworkVariables and ownership status.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            _motor = GetComponent<PlayerMotor>();
            _playerInput = GetComponent<PlayerInput>();
            _rb = GetComponent<Rigidbody>();

            //Subscribe to PlayerIndex changes to update visuals
            PlayerIndex.OnValueChanged += OnPlayerIndexChanged;

            //Apply current value
            UpdatePlayerVisuals(PlayerIndex.Value);

            //Only the owning client should process input
            if (IsOwner)
            {
                SetupInput();
                Debug.Log($"[PlayerNetworkController] Local player spawned. OwnerClientId: {OwnerClientId}");
            }
            else
            {
                //Disable PlayerInput for non-owners to prevent input conflicts
                DisableInput();
            }

            //Server assigns player index
            if (IsServer)
            {
                AssignPlayerIndex();
            }
        }

        /// <summary>
        /// Called when the NetworkObject despawns.
        /// Always clean up subscriptions to prevent memory leaks.
        /// </summary>
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            PlayerIndex.OnValueChanged -= OnPlayerIndexChanged;

            if (IsOwner)
            {
                CleanupInput();
            }
        }

        /// <summary>
        /// Configures input handling for the local player instance.
        /// Expects a PlayerInput component configured for C# events.
        /// </summary>
        private void SetupInput()
        {
            if (_playerInput == null)
            {
                Debug.LogError("[PlayerNetworkController] PlayerInput component not found!");
                return;
            }

            //Enable the PlayerInput component
            _playerInput.enabled = true;

            //Cache references to actions for performance
            _moveAction = _playerInput.actions["Move"];
            _dashAction = _playerInput.actions["Dash"];

            //Subscribe to dash action
            if (_dashAction != null)
            {
                _dashAction.performed += OnDashPerformed;
            }
            else
            {
                Debug.LogWarning("[PlayerNetworkController] Dash action not found in Input Actions!");
            }

            if (_moveAction == null)
            {
                Debug.LogWarning("[PlayerNetworkController] Move action not found in Input Actions!");
            }

            Debug.Log("[PlayerNetworkController] Input setup complete using PlayerInput component");
        }

        /// <summary>
        /// Disables input for non-owner players.
        /// This prevents input from being processed on remote player instances.
        /// </summary>
        private void DisableInput()
        {
            if (_playerInput != null)
            {
                _playerInput.enabled = false;
            }
        }

        /// <summary>
        /// Cleans up input subscriptions.
        /// </summary>
        private void CleanupInput()
        {
            if (_dashAction != null)
            {
                _dashAction.performed -= OnDashPerformed;
            }
        }

        /// <summary>
        /// Called when the Dash action is performed.
        /// </summary>
        private void OnDashPerformed(InputAction.CallbackContext context)
        {
            _dashPressed = true;
        }

        private void Update()
        {
            //Only process input for the local player
            if (!IsOwner) 
                return;

            //Read movement input from the cached action
            if (_moveAction != null)
            {
                _currentInput = _moveAction.ReadValue<Vector2>();
            }
        }

        private void FixedUpdate()
        {
            //Only the owner processes movement
            if (!IsOwner) 
                return;

            //Delegate movement to the motor
            _motor.Move(_currentInput);

            //Handle dash
            if (_dashPressed)
            {
                if (_motor.TryDash())
                {
                    //Notify other clients about the dash for visual effects
                    NotifyDashServerRpc(transform.forward);
                }
                _dashPressed = false;
            }
        }

        /// <summary>
        /// Handles collision logic for the server-authoritative ball. 
        /// Since clients cannot directly affect the ball's velocity, this sends the 
        /// calculated impact force to the server to ensure synchronized physics.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            //Only the owner (local player) processes collisions
            if (!IsOwner) 
                return;

            //Check if we hit the ball
            if (collision.gameObject.CompareTag("Ball"))
            {
                //Calculate collision data
                Vector3 hitPoint = collision.contacts[0].point;
                Vector3 hitNormal = collision.contacts[0].normal;
                Vector3 relativeVelocity = collision.relativeVelocity;

                //Calculate force direction (away from player)
                Vector3 forceDirection = (collision.transform.position - transform.position).normalized;

                //Calculate force magnitude based on relative velocity and player velocity
                float forceMagnitude = relativeVelocity.magnitude + _rb.linearVelocity.magnitude;

                //Send to server to apply force
                ApplyBallForceServerRpc(collision.gameObject.GetComponent<NetworkObject>().NetworkObjectId,
                                       forceDirection * forceMagnitude,
                                       hitPoint);

                Debug.Log($"[PlayerNetworkController] Hit ball with force: {forceMagnitude}");
            }
        }

        /// <summary>
        /// Server receives collision data and applies force to the ball.
        /// This ensures the force is applied on the authoritative server instance.
        /// </summary>
        [ServerRpc]
        private void ApplyBallForceServerRpc(ulong ballNetworkObjectId, Vector3 force, Vector3 hitPoint)
        {
            //Find the ball by NetworkObjectId
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(ballNetworkObjectId, out NetworkObject ballNetworkObject))
            {
                Rigidbody ballRb = ballNetworkObject.GetComponent<Rigidbody>();
                if (ballRb != null)
                {
                    //Apply force at hit point for realistic physics
                    ballRb.AddForceAtPosition(force, hitPoint, ForceMode.Impulse);

                    Debug.Log($"[PlayerNetworkController SERVER] Applied force {force.magnitude} to ball");
                }
            }
        }

        /// <summary>
        /// Triggers dash effects across the network. 
        /// Using an RPC as this is a transient event rather than persistent state.
        /// </summary>
        [ServerRpc]
        private void NotifyDashServerRpc(Vector3 direction)
        {
            //Server receives this, then broadcasts to all clients
            NotifyDashClientRpc(direction);
        }

        /// <summary>
        /// All clients receive this to play dash effects.
        /// </summary>
        [ClientRpc]
        private void NotifyDashClientRpc(Vector3 direction)
        {
            //Don't play effects for the owner (they already see it)
            if (IsOwner) 
                return;

            //TODO: Trigger particle effects, sound, etc.
            Debug.Log($"[PlayerNetworkController] Player {PlayerIndex.Value} dashed in direction {direction}");
        }

        /// <summary>
        /// Called by GameManager to reset player position at round start.
        /// Server-only operation that uses NetworkVariable sync for position.
        /// </summary>
        [ClientRpc]
        public void ResetPositionClientRpc(Vector3 position)
        {
            _motor.TeleportTo(position);
            _motor.StopMovement();
        }

        /// <summary>
        /// Assigns a player index (0 or 1) based on connection order.
        /// Server-only operation.
        /// </summary>
        private void AssignPlayerIndex()
        {
            //Simple assignment: first player = 0, second = 1
            //In production, use a proper team assignment system
            int index = (int)(OwnerClientId % 2);
            PlayerIndex.Value = index;
            IsReady.Value = true;

            Debug.Log($"[PlayerNetworkController] Assigned PlayerIndex {index} to ClientId {OwnerClientId}");
        }

        private void OnPlayerIndexChanged(int previousValue, int newValue)
        {
            UpdatePlayerVisuals(newValue);
        }

        private void UpdatePlayerVisuals(int playerIndex)
        {
            if (_meshRenderer == null) 
                return;

            //Assign team color based on player index
            Material targetMaterial = playerIndex switch
            {
                0 => _player1Material,
                1 => _player2Material,
                _ => null
            };

            if (targetMaterial != null)
            {
                _meshRenderer.material = targetMaterial;
            }

            //Update GameObject name for debugging
            gameObject.name = $"Player_{playerIndex}_{(IsOwner ? "(Local)" : "(Remote)")}";
        }

        /// <summary>
        /// Returns the spawn point for this player based on their index.
        /// </summary>
        public Vector3 GetSpawnPosition()
        {
            if (_spawnPoints != null && PlayerIndex.Value >= 0 && PlayerIndex.Value < _spawnPoints.Length)
            {
                return _spawnPoints[PlayerIndex.Value].position;
            }

            //Fallback positions if spawn points not set
            return PlayerIndex.Value == 0
                ? new Vector3(-5f, 1f, 0f)
                : new Vector3(5f, 1f, 0f);
        }
    }
}