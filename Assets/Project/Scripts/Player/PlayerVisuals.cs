using UnityEngine;

namespace BasicMultiplayer.Player
{
    /// <summary>
    /// Manages local visual feedback and particle systems. 
    /// Decoupled from networking logic; effects are triggered based on 
    /// state changes observed by the controller.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerVisuals : MonoBehaviour
    {
        [Header("Dash Effect")]
        [SerializeField] private ParticleSystem _dashParticles;
        [SerializeField] private TrailRenderer _speedTrail;
        [SerializeField] private float _trailSpeedThreshold = 5f;
        
        [Header("Movement Effect")]
        [SerializeField] private ParticleSystem _dustParticles;
        [SerializeField] private float _dustSpeedThreshold = 2f;
        
        [Header("Cooldown Indicator")]
        [SerializeField] private MeshRenderer _meshRenderer;
        [SerializeField] private Color _cooldownColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        
        private PlayerMotor _motor;
        private Rigidbody _rb;
        private Color _baseColor;
        private bool _wasGrounded;
        
        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _rb = GetComponent<Rigidbody>();
            
            if (_meshRenderer != null)
            {
                _baseColor = _meshRenderer.material.color;
            }
        }

        private void Update()
        {
            UpdateSpeedTrail();
            UpdateDustParticles();
            UpdateCooldownIndicator();
            CheckLanding();
        }

        private void UpdateSpeedTrail()
        {
            if (_speedTrail == null) 
                return;
            
            float speed = _rb.linearVelocity.magnitude;
            _speedTrail.emitting = speed > _trailSpeedThreshold;
        }

        private void UpdateDustParticles()
        {
            if (_dustParticles == null) 
                return;
            
            float speed = _rb.linearVelocity.magnitude;
            var emission = _dustParticles.emission;
            
            if (speed > _dustSpeedThreshold && _motor.IsGrounded)
            {
                if (!_dustParticles.isPlaying)
                {
                    _dustParticles.Play();
                }
            }
            else
            {
                if (_dustParticles.isPlaying)
                {
                    _dustParticles.Stop();
                }
            }
        }

        private void UpdateCooldownIndicator()
        {
            if (_meshRenderer == null) 
                return;
            
            //Subtle color shift to indicate dash availability
            if (_motor.CanDash)
            {
                _meshRenderer.material.color = _baseColor;
            }
            else
            {
                //Lerp towards cooldown color based on remaining time
                float t = _motor.DashCooldownRemaining / 1.5f; // Assuming 1.5s cooldown
                _meshRenderer.material.color = Color.Lerp(_baseColor, _cooldownColor, t * 0.3f);
            }
        }

        private void CheckLanding()
        {
            //Play landing effect when hitting ground after being airborne
            if (_motor.IsGrounded && !_wasGrounded)
            {
                PlayLandingEffect();
            }
            _wasGrounded = _motor.IsGrounded;
        }

        /// <summary>
        /// Call this when a dash is performed to trigger visual effects.
        /// </summary>
        public void PlayDashEffect()
        {
            if (_dashParticles != null)
            {
                _dashParticles.Play();
            }
            
            //Brief scale punch effect
            StartCoroutine(DashPunchCoroutine());
        }

        /// <summary>
        /// Call when player lands after being airborne.
        /// </summary>
        public void PlayLandingEffect()
        {
            if (_dustParticles != null)
            {
                _dustParticles.Emit(10);
            }
        }

        /// <summary>
        /// Call when player is hit/bumped by the ball.
        /// </summary>
        public void PlayHitEffect()
        {
            StartCoroutine(HitFlashCoroutine());
        }
        private System.Collections.IEnumerator DashPunchCoroutine()
        {
            Vector3 originalScale = transform.localScale;
            Vector3 punchScale = originalScale * 1.2f;
            
            float duration = 0.15f;
            float elapsed = 0f;
            
            //Scale up
            while (elapsed < duration / 2)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / (duration / 2);
                transform.localScale = Vector3.Lerp(originalScale, punchScale, t);
                yield return null;
            }
            
            //Scale back
            elapsed = 0f;
            while (elapsed < duration / 2)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / (duration / 2);
                transform.localScale = Vector3.Lerp(punchScale, originalScale, t);
                yield return null;
            }
            
            transform.localScale = originalScale;
        }

        private System.Collections.IEnumerator HitFlashCoroutine()
        {
            if (_meshRenderer == null) 
                yield break;
            
            Color originalColor = _meshRenderer.material.color;
            _meshRenderer.material.color = Color.white;
            
            yield return new WaitForSeconds(0.05f);
            
            _meshRenderer.material.color = originalColor;
        }
    }
}
