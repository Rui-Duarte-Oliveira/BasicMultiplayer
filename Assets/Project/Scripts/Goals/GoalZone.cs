using UnityEngine;
using UnityEngine.Events;

namespace BasicMultiplayer.Goals
{
    /// <summary>
    /// Detects when the ball enters a goal zone.
    /// 
    /// NETWORKING DECISION: This script does NOT inherit from NetworkBehaviour.
    /// Goal detection happens on the SERVER only (via ArenaBall's collision).
    /// This is just a marker/trigger zone that the server-side ball checks against.
    /// 
    /// Why server-side only?
    /// - Prevents clients from falsely reporting goals
    /// - Single source of truth for scoring
    /// - Reduces network traffic (no need to sync trigger events)
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class GoalZone : MonoBehaviour
    {
        [Header("Goal Settings")]
        [Tooltip("Which player owns this goal (0 or 1). When ball enters, the OTHER player scores.")]
        [SerializeField] private int _ownerPlayerIndex;
        
        [Header("Visual Feedback")]
        [SerializeField] private Color _goalColor = Color.yellow;
        [SerializeField] private float _flashDuration = 0.5f;
        
        [Header("Events")]
        [Tooltip("Fired when ball enters this goal zone. Parameter is the scoring player index.")]
        public UnityEvent<int> OnGoalScored;
        
        /// <summary>
        /// The player index that owns this goal (0 or 1).
        /// When the ball enters this goal, the OTHER player scores.
        /// </summary>
        public int OwnerPlayerIndex => _ownerPlayerIndex;
        
        /// <summary>
        /// Returns the player index that scores when ball enters this goal.
        /// </summary>
        public int ScoringPlayerIndex => _ownerPlayerIndex == 0 ? 1 : 0;
        
        private MeshRenderer _meshRenderer;
        private Color _originalColor;
        
        private void Awake()
        {
            //Ensure collider is a trigger
            Collider collider = GetComponent<Collider>();
            collider.isTrigger = true;
            
            //Cache renderer for visual feedback
            _meshRenderer = GetComponent<MeshRenderer>();
            if (_meshRenderer != null)
            {
                _originalColor = _meshRenderer.material.color;
            }
        }

        private void OnValidate()
        {
            // Clamp player index to valid range
            _ownerPlayerIndex = Mathf.Clamp(_ownerPlayerIndex, 0, 1);
        }

        /// <summary>
        /// Called by ArenaBall when it enters this goal zone.
        /// This method should only be called on the server.
        /// </summary>
        public void RegisterGoal()
        {
            // Invoke event for any listeners (primarily GameManager)
            OnGoalScored?.Invoke(ScoringPlayerIndex);
            
            Debug.Log($"[GoalZone] Goal scored! Player {ScoringPlayerIndex} scores (ball entered Player {_ownerPlayerIndex}'s goal)");
        }

        /// <summary>
        /// Triggers visual feedback for the goal.
        /// Can be called via ClientRpc from GameManager.
        /// </summary>
        public void PlayGoalEffect()
        {
            if (_meshRenderer != null)
            {
                StartCoroutine(FlashGoalCoroutine());
            }
        }

        private System.Collections.IEnumerator FlashGoalCoroutine()
        {
            //Flash the goal zone
            _meshRenderer.material.color = _goalColor;
            
            float elapsed = 0f;
            while (elapsed < _flashDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / _flashDuration;
                _meshRenderer.material.color = Color.Lerp(_goalColor, _originalColor, t);
                yield return null;
            }
            
            _meshRenderer.material.color = _originalColor;
        }

        private void OnDrawGizmos()
        {
            //Draw goal zone in editor
            Gizmos.color = _ownerPlayerIndex == 0 
                ? new Color(1f, 0.3f, 0.3f, 0.5f)  //Red-ish for Player 1
                : new Color(0.3f, 0.3f, 1f, 0.5f); //Blue-ish for Player 2
            
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                
                if (collider is BoxCollider box)
                {
                    Gizmos.DrawCube(box.center, box.size);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            //Show label when selected
            #if UNITY_EDITOR
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 2f,
                $"Goal Zone\nOwner: P{_ownerPlayerIndex}\nScorer: P{ScoringPlayerIndex}"
            );
            #endif
        }
    }
}