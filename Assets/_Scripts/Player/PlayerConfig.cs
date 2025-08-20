using UnityEngine;

namespace MyBFF.Player
{
    /// <summary>
    /// ScriptableObject that holds all player configuration values.
    /// This allows designers to tweak player behavior without touching code.
    /// Create via: Assets > Create > Player > Player Config
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerConfig", menuName = "Player/Player Config")]
    public class PlayerConfig : ScriptableObject
    {
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 3f;
        [SerializeField] private float runSpeed = 6f;
        [SerializeField] private float crouchSpeed = 1.5f;
        [SerializeField] private float acceleration = 10f;
        [SerializeField] private float deceleration = 10f;
        
        [Header("Jumping")]
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float coyoteTime = 0.2f;
        
        [Header("Physics")]
        [SerializeField] private float gravity = 9.81f;
        [SerializeField] private float groundCheckDistance = 0.2f;
        [SerializeField] private LayerMask groundLayers = 1;
        
        [Header("Camera")]
        [SerializeField] private float mouseSensitivity = 2f;
        [SerializeField] private float touchSensitivity = 1f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;
        [SerializeField] private bool invertY = false;
        
        [Header("Interaction")]
        [SerializeField] private float interactRange = 2f;
        [SerializeField] private LayerMask interactLayers = -1;

        // Movement Properties - these provide read-only access to private fields
        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public float CrouchSpeed => crouchSpeed;
        public float Acceleration => acceleration;
        public float Deceleration => deceleration;
        
        // Jump Properties
        public float JumpHeight => jumpHeight;
        public float CoyoteTime => coyoteTime;
        
        // Physics Properties
        public float Gravity => gravity;
        public float GroundCheckDistance => groundCheckDistance;
        public LayerMask GroundLayers => groundLayers;
        
        // Camera Properties
        public float MouseSensitivity => mouseSensitivity;
        public float TouchSensitivity => touchSensitivity;
        public float MinPitch => minPitch;
        public float MaxPitch => maxPitch;
        public bool InvertY => invertY;
        
        // Interaction Properties
        public float InteractRange => interactRange;
        public LayerMask InteractLayers => interactLayers;
    }
}