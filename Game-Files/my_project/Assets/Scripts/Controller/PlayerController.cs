using NUnit.Framework;
using UnityEditor.ShaderGraph;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

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

    [Header("Wall Climb")]
    [SerializeField] private float slowFall = 1f;


    private Animator anim;
    
    private Rigidbody2D rb;
    private bool isGrounded;
    private bool isRunning;
    private bool isAiming;
    private Vector2 moveInput = Vector2.zero;
    private Raycast raycast;

    private float runningBufferTime = 0.1f;
    private float currentRunningBuffer = 0f;

    private float velocityX;

    private bool canClimb = false;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
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
        //isGrounded = Physics2D.OverlapBox(checkPosition, groundCheckSize, groundCheckAngle, groundLayer);
        //velocityY = rb.linearVelocity.y;

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
                raycast.SetCurrentTool(currentTool, explosiveBallPrefab, throwForce, ballSpawnOffset);
            }
        }
    }
    
    void FixedUpdate()
    {
        rb.linearVelocity = new Vector2(moveInput.x * moveSpeed, rb.linearVelocity.y);

        if (isAiming && isGrounded)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
        else
        {
            rb.linearVelocity = new Vector2(moveInput.x * moveSpeed, rb.linearVelocity.y);
        }
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

        if ((isGrounded || canClimb) && value.isPressed)
        {
            if (canClimb)
                Debug.Log("Wall Jump");
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

    private void OnCollisionStay2D(Collision2D collision)
    {
        // Check if player is grounded based on collision normals
        // If at least one collision normal is within a certain range of
        // upward angles, the player must be standing on top of something
        isGrounded = false;
        ContactPoint2D[] contacts = new ContactPoint2D[collision.contactCount];
        collision.GetContacts(contacts);
        foreach (ContactPoint2D contact in contacts)
        {
            Vector3 norm = contact.normal;
            if (norm.y > 0)
            {
                float angle = Mathf.Atan(norm.y / Mathf.Abs(norm.x)) * Mathf.Rad2Deg;
                if (angle >= 80 && angle <= 90)
                {
                    isGrounded = true;
                }
            }
        }

        // Colliding with ground, walls, etc.
        // Check if overlapping with slime mold
        // If so, enable wall climbing
        if (collision.gameObject.name == "SlimeDisplay")
        {
            // Bool to allow jumping while on wall
            // Slow fall
            canClimb = true;
            if (rb.linearVelocityY < slowFall)
                rb.linearVelocityY = slowFall;
        }
        else
        {
            canClimb = false;
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        isGrounded = false;
        canClimb = false;
    }
}