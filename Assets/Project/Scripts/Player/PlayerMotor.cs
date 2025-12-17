using UnityEngine;

namespace BasicMultiplayer.Player
{
    /// <summary>
    /// Handles physics-based movement and dash logic. 
    /// Driven by the player controller; kept independent of networking for easier 
    /// local testing and reuse.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float _moveForce = 50f;
        [SerializeField] private float _maxSpeed = 10f;
        [SerializeField] private float _drag = 3f;

        [Header("Dash Settings")]
        [SerializeField] private float _dashForce = 25f;
        [SerializeField] private float _dashCooldown = 1.5f;

        [Header("Ground Check")]
        [SerializeField] private float _groundCheckDistance = 0.6f;
        [SerializeField] private LayerMask _groundLayer = ~0; //Default to all layers

        private Rigidbody _rb;
        private float _lastDashTime = -999f;
        private Vector3 _lastMoveDirection;

        /// <summary>
        /// Returns true if the dash ability is ready to use.
        /// </summary>
        public bool CanDash => Time.time >= _lastDashTime + _dashCooldown;

        /// <summary>
        /// Returns the remaining cooldown time for dash (0 if ready).
        /// </summary>
        public float DashCooldownRemaining => Mathf.Max(0, (_lastDashTime + _dashCooldown) - Time.time);

        /// <summary>
        /// Returns current velocity magnitude as a percentage of max speed (0-1).
        /// Useful for UI or visual effects.
        /// </summary>
        public float SpeedPercent => _rb != null ? _rb.linearVelocity.magnitude / _maxSpeed : 0f;

        /// <summary>
        /// Returns true if the player is grounded.
        /// </summary>
        public bool IsGrounded { get; private set; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            ConfigureRigidbody();
        }

        private void FixedUpdate()
        {
            CheckGrounded();
            ClampVelocity();
        }

        #region PUBLIC MOVEMENT METHODS
        /// <summary>
        /// Applies movement force based on input direction.
        /// Call this from FixedUpdate for physics consistency.
        /// </summary>
        /// <param name="inputDirection">Normalized input vector (x = horizontal, z = vertical)</param>
        public void Move(Vector2 inputDirection)
        {
            if (!IsGrounded)
                return;

            //Convert 2D input to 3D world direction
            Vector3 moveDirection = new Vector3(inputDirection.x, 0f, inputDirection.y).normalized;

            if (moveDirection.sqrMagnitude > 0.01f)
            {
                _lastMoveDirection = moveDirection;
                _rb.AddForce(moveDirection * _moveForce, ForceMode.Force);
            }
        }

        /// <summary>
        /// Executes a dash in the current movement direction.
        /// Returns true if dash was executed, false if on cooldown.
        /// </summary>
        /// <returns>Whether the dash was successfully executed</returns>
        public bool TryDash()
        {
            if (!CanDash || !IsGrounded)
                return false;

            //Use last move direction, or forward if stationary
            Vector3 dashDirection = _lastMoveDirection.sqrMagnitude > 0.01f
                ? _lastMoveDirection
                : transform.forward;

            _rb.AddForce(dashDirection * _dashForce, ForceMode.Impulse);
            _lastDashTime = Time.time;

            return true;
        }

        /// <summary>
        /// Executes a dash in a specific direction (for network-synced dashes).
        /// </summary>
        /// <param name="direction">World-space direction to dash</param>
        public void ForceDash(Vector3 direction)
        {
            _rb.AddForce(direction.normalized * _dashForce, ForceMode.Impulse);
            _lastDashTime = Time.time;
        }

        /// <summary>
        /// Immediately stops all movement. Useful for round resets.
        /// </summary>
        public void StopMovement()
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Teleports the player to a position. Useful for spawning/respawning.
        /// </summary>
        public void TeleportTo(Vector3 position)
        {
            _rb.position = position;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        #endregion

        #region PRIVATE HELPER METHODS
        private void ConfigureRigidbody()
        {
            //Configure rigidbody for responsive arcade-style movement
            _rb.linearDamping = _drag;
            _rb.angularDamping = 2f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            //Freeze rotation to prevent tumbling (we're a cube pushing a ball)
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        private void CheckGrounded()
        {
            //Simple raycast ground check
            IsGrounded = Physics.Raycast(
                transform.position,
                Vector3.down,
                _groundCheckDistance,
                _groundLayer
            );
        }

        private void ClampVelocity()
        {
            //Clamp horizontal velocity to max speed (allow vertical for physics)
            Vector3 horizontalVelocity = new Vector3(_rb.linearVelocity.x, 0, _rb.linearVelocity.z);

            if (horizontalVelocity.magnitude > _maxSpeed)
            {
                horizontalVelocity = horizontalVelocity.normalized * _maxSpeed;
                _rb.linearVelocity = new Vector3(
                    horizontalVelocity.x,
                    _rb.linearVelocity.y,
                    horizontalVelocity.z
                );
            }
        }
        #endregion

        private void OnDrawGizmosSelected()
        {
            // Visualize ground check
            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position, transform.position + Vector3.down * _groundCheckDistance);

            // Visualize last move direction
            if (_lastMoveDirection.sqrMagnitude > 0.01f)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(transform.position, _lastMoveDirection * 2f);
            }
        }
    }
}