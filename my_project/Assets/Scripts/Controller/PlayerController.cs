using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 10f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    // [SerializeField] private float groundCheckRadius = 0.2f; // <-- REMOVE THIS
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.5f, 0.1f);
    [SerializeField] private float groundCheckAngle = 0f;
    [SerializeField] private Vector2 groundCheckOffset = new Vector2(0f, 0f); // <-- ADD THIS
    [SerializeField] private LayerMask groundLayer;

    private Animator anim;
    
    private Rigidbody2D rb;
    private bool isGrounded;
    private bool isRunning;
    private Vector2 moveInput = Vector2.zero;
    private Raycast raycast;

    private float runningBufferTime = 0.1f; // Time (in seconds) to keep running animation active
    private float currentRunningBuffer = 0f;

    //private float velocityY;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        
        // Create and initialize raycast system
        raycast = gameObject.AddComponent<Raycast>();
        raycast.Initialize(transform);

        // Create and initialize animator
        anim = GetComponent<Animator>();
    }
    
    void Update()
    {
        // Check for running
        if (Mathf.Abs(moveInput.x) > 0.01f)
        {
            // 1. If input is active, the character is running, and the timer is reset.
            isRunning = true;
            currentRunningBuffer = runningBufferTime;
        }
        else
        {
            // 2. If no input, start the buffer countdown.
            currentRunningBuffer -= Time.deltaTime;

            // 3. Keep running animation active until the buffer runs out.
            isRunning = currentRunningBuffer > 0f;
        }

        // Check if grounded
        Vector2 checkPosition = (Vector2)groundCheck.position + groundCheckOffset;
        isGrounded = Physics2D.OverlapBox(checkPosition, groundCheckSize, groundCheckAngle, groundLayer);
        //velocityY = rb.linearVelocity.y;

        // Call raycast system
        raycast.DrawLineAndCheckHits();

        if (anim != null)
        {
            anim.SetBool("isRunning", isRunning);
            anim.SetBool("isGrounded", isGrounded);
            //anim.SetFloat("VelocityY", velocityY);
        }
    }
    
    void FixedUpdate()
    {
        // Move player
        rb.linearVelocity = new Vector2(moveInput.x * moveSpeed, rb.linearVelocity.y);

        // Flip the sprite based on input direction
        if (moveInput.x > 0.01f) // Moving Right
        {
            // Set local scale X to 1 (facing right, assuming this is the default)
            transform.localScale = new Vector3(1f, 1f, 1f);
        }
        else if (moveInput.x < -0.01f) // Moving Left
        {
            // Set local scale X to -1 (facing left)
            transform.localScale = new Vector3(-1f, 1f, 1f);
        }
        // If input is near zero (idle), the sprite maintains its last direction.
    }

    // Called by Unity's Input System
    public void OnMove(InputValue value)
    {
        // Simply assign the new value. The Input System is responsible for sending (0,0)
        // when the keys are released.
        moveInput = value.Get<Vector2>();

        // **ADD THIS DEBUG LINE:**
        // This will print the exact value of moveInput to the console every time it changes.
        Debug.Log($"New moveInput: {moveInput}");
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

            // Calculate the offset position for the Gizmo drawing
            Vector2 checkPosition = (Vector2)groundCheck.position + groundCheckOffset;

            // Draw the cube at the offset position
            Gizmos.DrawWireCube(checkPosition, groundCheckSize);
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