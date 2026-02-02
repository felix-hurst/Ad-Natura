using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Properties for player movement when on slime surfaces.
/// </summary>
public struct SlimeMovementProperties
{
    public bool isOnSlime;
    public float density;
    public float speedMultiplier;
    public float gravityMultiplier;
    public bool canClimb;
    public bool canWallStick;
}

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 3f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.5f, 0.1f);
    [SerializeField] private float groundCheckAngle = 0f;
    [SerializeField] private Vector2 groundCheckOffset = new Vector2(0f, 0f);
    [SerializeField] private LayerMask groundLayer;

    [Header("Slime Surface Interaction")]
    [SerializeField] private bool enableSlimeClimbing = true;
    [SerializeField] private float slimeClimbSpeed = 3f;
    [SerializeField] private float slimeWallCheckDistance = 0.3f;
    [SerializeField] private LayerMask slimeSurfaceLayer;

    [SerializeField] private Camera gameCamera;

    public enum ToolType
    {
        CuttingTool,
        ExplosiveBall
    }

    [Header("Tool Selection")]
    [SerializeField] private ToolType currentTool = ToolType.CuttingTool;

    [Header("Explosive Ball Settings")]
    [SerializeField] private GameObject explosiveBallPrefab;
    [SerializeField] private float throwForce = 10f;
    [SerializeField] private float ballSpawnOffset = 1.0f;

    [Header("Cutting Tool Settings")]
    [SerializeField] private float maxCuttingRange = 10f;

    private Animator anim;
    private Rigidbody2D rb;
    private bool isGrounded;
    private bool isRunning;
    private bool isAiming;
    private Vector2 moveInput = Vector2.zero;
    private Raycast raycast;

    // Slime surface state
    private bool isOnSlimeSurface = false;
    private bool isClimbingSlime = false;
    private SlimeMovementProperties currentSlimeProps;
    private float originalGravityScale;

    private float runningBufferTime = 0.1f;
    private float currentRunningBuffer = 0f;

    private float velocityX;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        originalGravityScale = rb.gravityScale;

        // Create and initialize raycast system
        raycast = gameObject.AddComponent<Raycast>();
        raycast.Initialize(transform);
        anim = GetComponent<Animator>();
    }

    void Update()
    {
        velocityX = Mathf.Abs(rb.linearVelocity.x);

        if (velocityX > 0.1f)
        {
            isRunning = true;
            currentRunningBuffer = runningBufferTime;
        }
        else
        {
            currentRunningBuffer -= Time.deltaTime;
            isRunning = currentRunningBuffer > 0f;
        }

        Vector2 checkPosition = (Vector2)groundCheck.position + groundCheckOffset;
        isGrounded = Physics2D.OverlapBox(checkPosition, groundCheckSize, groundCheckAngle, groundLayer);

        // Check for slime surface interaction
        CheckSlimeSurface();

        if (!isRunning)
        {
            if (gameCamera != null)
            {
                Vector3 mousePosition = gameCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());

                if (mousePosition.x > transform.position.x)
                {
                    transform.localScale = new Vector3(1f, 1f, 1f);
                }
                else
                {
                    transform.localScale = new Vector3(-1f, 1f, 1f);
                }
            }
        }
        else
        {
            if (moveInput.x > 0.01f)
            {
                transform.localScale = new Vector3(1f, 1f, 1f);
            }
            else if (moveInput.x < -0.01f)
            {
                transform.localScale = new Vector3(-1f, 1f, 1f);
            }
        }

        if (raycast != null)
        {
            raycast.enabled = isAiming;

            if (isAiming)
            {
                raycast.SetCurrentTool(currentTool, explosiveBallPrefab, throwForce, ballSpawnOffset, maxCuttingRange);
            }
        }
    }

    void FixedUpdate()
    {
        // Calculate effective move speed (modified by slime if applicable)
        float effectiveMoveSpeed = moveSpeed;
        if (isOnSlimeSurface && currentSlimeProps.isOnSlime)
        {
            effectiveMoveSpeed *= currentSlimeProps.speedMultiplier;
        }

        // PREVENT MOVEMENT WHILE AIMING
        if (isAiming && isGrounded)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
        else if (isClimbingSlime && enableSlimeClimbing)
        {
            // Climbing on slime wall - use vertical input for climbing
            rb.linearVelocity = new Vector2(moveInput.x * effectiveMoveSpeed * 0.5f, moveInput.y * slimeClimbSpeed);
        }
        else
        {
            // Normal move player logic
            rb.linearVelocity = new Vector2(moveInput.x * effectiveMoveSpeed, rb.linearVelocity.y);
        }

        // Apply gravity modification when on slime
        if (isOnSlimeSurface && currentSlimeProps.isOnSlime)
        {
            rb.gravityScale = originalGravityScale * currentSlimeProps.gravityMultiplier;
        }
        else
        {
            rb.gravityScale = originalGravityScale;
        }
    }

    /// <summary>
    /// Checks if the player is on or near a slime surface.
    /// </summary>
    void CheckSlimeSurface()
    {
        if (!enableSlimeClimbing)
        {
            isOnSlimeSurface = false;
            isClimbingSlime = false;
            return;
        }

        // Check for slime surface below (ground)
        Vector2 checkPosition = (Vector2)groundCheck.position + groundCheckOffset;
        Collider2D slimeGround = Physics2D.OverlapBox(checkPosition, groundCheckSize, groundCheckAngle, slimeSurfaceLayer);

        // Check for slime surface on walls (left and right)
        Vector2 playerCenter = transform.position;
        RaycastHit2D leftWall = Physics2D.Raycast(playerCenter, Vector2.left, slimeWallCheckDistance, slimeSurfaceLayer);
        RaycastHit2D rightWall = Physics2D.Raycast(playerCenter, Vector2.right, slimeWallCheckDistance, slimeSurfaceLayer);

        isOnSlimeSurface = (slimeGround != null) || (leftWall.collider != null) || (rightWall.collider != null);

        // Check if climbing (on wall slime and pressing into the wall)
        bool pressingIntoLeftWall = leftWall.collider != null && moveInput.x < -0.1f;
        bool pressingIntoRightWall = rightWall.collider != null && moveInput.x > 0.1f;
        isClimbingSlime = (pressingIntoLeftWall || pressingIntoRightWall) && !isGrounded;

        // Default slime surface movement properties
        currentSlimeProps = new SlimeMovementProperties
        {
            isOnSlime = isOnSlimeSurface,
            density = 0.5f,
            speedMultiplier = 0.7f,
            gravityMultiplier = 0.3f,
            canClimb = true,
            canWallStick = true
        };
    }

    public void OnAim(InputValue value)
    {
        if (value.isPressed)
        {
            isAiming = !isAiming;
        }
    }

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        if (isAiming)
        {
            return;
        }

        if (isGrounded && value.isPressed)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            isGrounded = false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Vector2 checkPosition = (Vector2)groundCheck.position + groundCheckOffset;

            Gizmos.DrawWireCube(checkPosition, groundCheckSize);
        }
    }

    void OnDestroy()
    {
        if (raycast != null)
        {
            raycast.Cleanup();
        }
    }
}
