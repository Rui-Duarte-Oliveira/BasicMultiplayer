using BasicMultiplayer.Goals;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace BasicMultiplayer.Ball
{
    /// <summary>
    /// Server-authoritative physics ball. 
    /// Handles synchronized movement and collision detection. Since this is a shared 
    /// object interacted with by multiple players, logic is restricted to the server 
    /// to ensure physics consistency and prevent scoring cheats.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkRigidbody))]
    public class ArenaBall : NetworkBehaviour
    {
        [Header("Physics Settings")]
        [SerializeField] private float _maxSpeed = 20f;
        [SerializeField] private float _bounciness = 0.8f;

        [Header("Reset Settings")]
        [SerializeField] private Vector3 _spawnPosition = new Vector3(0f, 1f, 0f);

        [Header("Visual Effects")]
        [SerializeField] private TrailRenderer _trailRenderer;
        [SerializeField] private float _trailSpeedThreshold = 5f;

        /// <summary>
        /// Synchronized state tracking whether the ball is currently in-play.
        /// Disables physics/interactions when false (e.g., during round resets).
        /// </summary>
        public NetworkVariable<bool> IsActive = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        /// <summary>
        /// The ClientId of the last player to touch the ball.
        /// Useful for "last touch" scoring rules or analytics.
        /// </summary>
        public NetworkVariable<ulong> LastTouchedBy = new NetworkVariable<ulong>(
            ulong.MaxValue, // Default: no one
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public delegate void GoalScoredHandler(int scoringPlayerIndex);
        /// <summary>
        /// Event fired when ball enters a goal zone.
        /// GameManager subscribes to this for score updates.
        /// </summary>
        public event GoalScoredHandler OnGoalScored;

        private Rigidbody _rb;
        private bool _goalScoredThisRound;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            ConfigureRigidbody();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            //Subscribe to state changes
            IsActive.OnValueChanged += OnActiveStateChanged;

            //Apply current state (for late joiners)
            SetPhysicsActive(IsActive.Value);

            //Server initializes ball position
            if (IsServer)
            {
                ResetBallPosition();
            }

            Debug.Log($"[ArenaBall] Spawned. IsServer: {IsServer}");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            IsActive.OnValueChanged -= OnActiveStateChanged;
        }

        private void FixedUpdate()
        {
            //Only server processes ball physics logic
            if (!IsServer) 
                return;

            if (!IsActive.Value) 
                return;

            ClampVelocity();
        }

        private void Update()
        {
            //Update visual effects (runs on all clients)
            UpdateTrailEffect();
        }

        /// <summary>
        /// Detects goal and player collisions. 
        /// Only processed on the server instance to maintain authoritative state.
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            //ONLY process on server to prevent duplicate events
            if (!IsServer)
                return;

            if (!IsActive.Value)
                return;

            //Check for goal zone
            if (other.TryGetComponent(out GoalZone goalZone))
            {
                HandleGoalScored(goalZone);
            }
        }

        /// <summary>
        /// Tracks which player last touched the ball.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            //Server only - track last touch
            if (!IsServer)
                return;

            //Check if collided with a player
            if (collision.gameObject.TryGetComponent(out NetworkObject networkObject))
            {
                if (collision.gameObject.CompareTag("Player"))
                {
                    LastTouchedBy.Value = networkObject.OwnerClientId;
                }
            }
        }

        private void HandleGoalScored(GoalZone goalZone)
        {
            //Prevent multiple goal triggers
            if (_goalScoredThisRound) return;
            _goalScoredThisRound = true;

            int scoringPlayer = goalZone.ScoringPlayerIndex;

            Debug.Log($"[ArenaBall] GOAL! Player {scoringPlayer} scores!");

            //Notify listeners (GameManager)
            OnGoalScored?.Invoke(scoringPlayer);

            //Trigger visual effects on all clients
            PlayGoalEffectClientRpc(scoringPlayer);

            //Deactivate ball
            IsActive.Value = false;
        }

        /// <summary>
        /// Resets the ball to center for a new round.
        /// Called by GameManager - SERVER ONLY.
        /// </summary>
        public void ResetForNewRound()
        {
            if (!IsServer)
            {
                Debug.LogWarning("[ArenaBall] ResetForNewRound called on client - ignored");
                return;
            }

            _goalScoredThisRound = false;
            ResetBallPosition();
            IsActive.Value = true;
            LastTouchedBy.Value = ulong.MaxValue;

            //Notify clients to reset their trail effects
            ResetVisualsClientRpc();
        }

        /// <summary>
        /// Gives the ball an initial push in a random direction.
        /// Called at round start for gameplay variety.
        /// </summary>
        public void ApplyStartingImpulse(float force = 5f)
        {
            if (!IsServer)
                return;

            //Random direction (mostly horizontal)
            Vector3 randomDirection = new Vector3(
                Random.Range(-1f, 1f),
                0.1f,
                Random.Range(-1f, 1f)
            ).normalized;

            _rb.AddForce(randomDirection * force, ForceMode.Impulse);
        }

        /// <summary>
        /// Freezes the ball in place. Useful for round end.
        /// </summary>
        public void Freeze()
        {
            if (!IsServer)
                return;

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            IsActive.Value = false;
        }

        /// <summary>
        /// Broadcasts goal-related visual and audio effects to all connected clients.
        /// </summary>
        [ClientRpc]
        private void PlayGoalEffectClientRpc(int scoringPlayer)
        {
            //TODO: Spawn particle effects, play sound
            Debug.Log($"[ArenaBall] Playing goal effect for Player {scoringPlayer}");

            //Flash the ball or other visual feedback
            StartCoroutine(GoalFlashCoroutine());
        }

        [ClientRpc]
        private void ResetVisualsClientRpc()
        {
            //Clear trail
            if (_trailRenderer != null)
            {
                _trailRenderer.Clear();
            }
        }

        private void ConfigureRigidbody()
        {
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            //Configure physics material for bounciness
            Collider collider = GetComponent<Collider>();
            if (collider != null)
            {
                PhysicsMaterial physicsMaterial = new PhysicsMaterial("BallPhysics")
                {
                    bounciness = _bounciness,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Maximum
                };
                collider.material = physicsMaterial;
            }
        }

        private void ResetBallPosition()
        {
            _rb.position = _spawnPosition;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        private void SetPhysicsActive(bool active)
        {
            _rb.isKinematic = !active;
        }

        private void OnActiveStateChanged(bool previousValue, bool newValue)
        {
            SetPhysicsActive(newValue);
        }

        private void ClampVelocity()
        {
            if (_rb.linearVelocity.magnitude > _maxSpeed)
            {
                _rb.linearVelocity = _rb.linearVelocity.normalized * _maxSpeed;
            }
        }

        private void UpdateTrailEffect()
        {
            if (_trailRenderer == null)
                return;

            //Only show trail when moving fast
            float speed = _rb.linearVelocity.magnitude;
            _trailRenderer.emitting = speed > _trailSpeedThreshold && IsActive.Value;
        }

        private System.Collections.IEnumerator GoalFlashCoroutine()
        {
            MeshRenderer renderer = GetComponent<MeshRenderer>();

            if (renderer == null) 
                yield break;

            Color originalColor = renderer.material.color;

            //Flash effect
            for (int i = 0; i < 3; i++)
            {
                renderer.material.color = Color.white;
                yield return new WaitForSeconds(0.1f);
                renderer.material.color = originalColor;
                yield return new WaitForSeconds(0.1f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            //Draw spawn position
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_spawnPosition, 0.5f);
            Gizmos.DrawLine(transform.position, _spawnPosition);
        }
    }
}