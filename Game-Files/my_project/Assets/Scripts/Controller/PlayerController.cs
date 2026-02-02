using UnityEngine;
using UnityEngine.InputSystem;

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

[Header("Cutting Tool Settings")]
[SerializeField] private float maxCuttingRange = 10f; 

[Header("Explosive Ball Settings")]
[SerializeField] private GameObject explosiveBallPrefab;
[SerializeField] private float throwForce = 10f;
[SerializeField] private float ballSpawnOffset = 1.0f;
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
        isGrounded = Physics2D.OverlapBox(checkPosition, groundCheckSize, groundCheckAngle, groundLayer);
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
                raycast.SetCurrentTool(currentTool, explosiveBallPrefab, throwForce, ballSpawnOffset, maxCuttingRange);
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