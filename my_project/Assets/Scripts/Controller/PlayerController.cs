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

    [SerializeField] private Camera gameCamera;

    private Animator anim;
    
    private Rigidbody2D rb;
    private bool isGrounded;
    private bool isRunning;
    private bool isAiming;
    private Vector2 moveInput = Vector2.zero;
    private Raycast raycast;

    private float runningBufferTime = 0.1f; // Time (in seconds) to keep running animation active
    private float currentRunningBuffer = 0f;

    private float velocityX;
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
        velocityX = Mathf.Abs(rb.linearVelocity.x);

        if (velocityX > 0.1f) // Use 0.1f as a small threshold
        {
            isRunning = true;
            currentRunningBuffer = runningBufferTime; // Retain buffer for direction changes
        }
        else
        {
            currentRunningBuffer -= Time.deltaTime;
            // isRunning remains true until buffer runs out IF the velocity is near zero.
            isRunning = currentRunningBuffer > 0f;
        }

        // Check if grounded
        Vector2 checkPosition = (Vector2)groundCheck.position + groundCheckOffset;
        isGrounded = Physics2D.OverlapBox(checkPosition, groundCheckSize, groundCheckAngle, groundLayer);
        //velocityY = rb.linearVelocity.y;

        if (!isRunning)
        {
            // State 1: IDLE or AIMING (Mouse-based flip)
            if (gameCamera != null)
            {
                Vector3 mousePosition = gameCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());

                if (mousePosition.x > transform.position.x)
                {
                    transform.localScale = new Vector3(1f, 1f, 1f); // Face Right
                }
                else
                {
                    transform.localScale = new Vector3(-1f, 1f, 1f); // Face Left
                }
            }
        }
        else // isRunning is true
        {
            // State 2: RUNNING (Movement-based flip)
            if (moveInput.x > 0.01f) // Moving Right
            {
                transform.localScale = new Vector3(1f, 1f, 1f); // Face Right
            }
            else if (moveInput.x < -0.01f) // Moving Left
            {
                transform.localScale = new Vector3(-1f, 1f, 1f); // Face Left
            }
            // If moveInput.x is near zero (buffer active), it maintains the last direction.
        }

        // Call raycast system
        if (raycast != null)
        {
            // Set the raycast component to enabled ONLY when isAiming is true
            raycast.enabled = isAiming;
        }
        if (anim != null)
        {
            anim.SetBool("isRunning", isRunning);
            anim.SetBool("isGrounded", isGrounded);
            anim.SetBool("isAiming", isAiming);
        }
    }
    
    void FixedUpdate()
    {
        // Move player
        rb.linearVelocity = new Vector2(moveInput.x * moveSpeed, rb.linearVelocity.y);


        // PREVENT MOVEMENT WHILE AIMING
        if (isAiming && isGrounded)
        {
            // Stop all horizontal movement immediately
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
        else
        {
            // Normal move player logic
            rb.linearVelocity = new Vector2(moveInput.x * moveSpeed, rb.linearVelocity.y);
        }
    }

    public void OnAim(InputValue value)
    {
        // Check if the input value is a button press/release
        if (value.isPressed)
        {
            // Toggle the aiming state on press
            isAiming = !isAiming;
        }
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
        // PREVENT JUMPING WHILE AIMING
        if (isAiming)
        {
            return; // Exit the function immediately
        }

        if (isGrounded && value.isPressed)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            isGrounded = false;
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