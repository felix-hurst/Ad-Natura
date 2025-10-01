using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 10f;
    
    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;
    
    private Rigidbody2D rb;
    private bool isGrounded;
    private Vector2 moveInput;
    private Raycast raycast;
    
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        
        // Create and initialize raycast system
        raycast = gameObject.AddComponent<Raycast>();
        raycast.Initialize(transform);
    }
    
    void Update()
    {
        // Check if grounded
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        
        // Call raycast system
        raycast.DrawLineAndCheckHits();
    }
    
    void FixedUpdate()
    {
        // Move player
        rb.linearVelocity = new Vector2(moveInput.x * moveSpeed, rb.linearVelocity.y);
    }
    
    // Called by Unity's Input System
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }
    
    // Called by Unity's Input System
    public void OnJump(InputValue value)
    {
        if (isGrounded && value.isPressed)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        }
    }
    
    // Visualize ground check in editor
    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
    
    void OnDestroy()
    {
        // Clean up raycast system
        if (raycast != null)
        {
            raycast.Cleanup();
        }
    }
}