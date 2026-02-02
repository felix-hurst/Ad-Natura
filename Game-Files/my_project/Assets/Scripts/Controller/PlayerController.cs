using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 10f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.5f, 0.1f);
    [SerializeField] private float groundCheckAngle = 0f;
    [SerializeField] private Vector2 groundCheckOffset = new Vector2(0f, 0f);
    [SerializeField] private LayerMask groundLayer;

    [Header("References")]
    [SerializeField] private Camera gameCamera;
    [SerializeField] private GameObject classicModel;
    [SerializeField] private GameObject aimingModel;
    [SerializeField] private Transform nearArmGun;
    [SerializeField] private Transform farArm;

    [Header("Stretching Settings")]
    [SerializeField] private Transform farHandGrip;
    [SerializeField] private float armLength = 0.5f;

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

    public enum ToolType { CuttingTool, ExplosiveBall }
    private ToolType currentTool = ToolType.CuttingTool;
    private int currentToolIndex = -1;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        // Setup Raycast component
        raycast = gameObject.AddComponent<Raycast>();
        raycast.Initialize(transform);
        raycast.SetCurrentTool(currentTool); // Set initial tool
        raycast.enabled = false; // Start with it off

        if (classicModel != null)
        {
            anim = classicModel.GetComponent<Animator>();
        }
    }

    void Update()
    {
        // Physics-based Running Logic
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

        // Ground Check
        Vector2 checkPosition = (Vector2)groundCheck.position + groundCheckOffset;
        isGrounded = Physics2D.OverlapBox(checkPosition, groundCheckSize, groundCheckAngle, groundLayer);

        // --- NEW: Force Aiming Off if not Grounded ---
        if (isAiming && !isGrounded)
        {
            isAiming = false;
        }

        HandleSpriteFlipping();
        HandleArmRotation();
        HandleVisualSwitch();

        // Sync Raycast state with Aiming
        if (raycast != null) raycast.enabled = isAiming;

        if (anim != null)
        {
            anim.SetBool("isRunning", isRunning);
            anim.SetBool("isGrounded", isGrounded);
            anim.SetBool("isAiming", isAiming);
            anim.SetInteger("ToolIndex", currentToolIndex);
        }
    }

    public void SwitchTool(int toolIndex)
    {
        Debug.Log("SwitchTool called with: " + toolIndex);

        currentToolIndex = toolIndex; 

        if (anim != null)
        {
            anim.SetInteger("ToolIndex", toolIndex);
        }

        if (toolIndex == -1)
        {
            isAiming = false;
            if (raycast != null) raycast.enabled = false;
            Debug.Log("Player unselected all tools.");
            return;
        }

        currentTool = (ToolType)toolIndex;

        if (raycast != null)
        {
            raycast.SetCurrentTool(currentTool);
            raycast.enabled = isAiming;
        }

        Debug.Log("Player switched to: " + currentTool.ToString());
    }

    private void HandleSpriteFlipping()
    {
        if (!isRunning)
        {
            if (gameCamera != null)
            {
                Vector3 mousePos = gameCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
                transform.localScale = new Vector3(mousePos.x > transform.position.x ? 1f : -1f, 1f, 1f);
            }
        }
        else
        {
            if (Mathf.Abs(moveInput.x) > 0.01f)
            {
                transform.localScale = new Vector3(moveInput.x > 0 ? 1f : -1f, 1f, 1f);
            }
        }
    }

    private void HandleArmRotation()
    {
        if (nearArmGun == null || farArm == null || gameCamera == null) return;

        if (isAiming)
        {
            Vector3 mousePos = gameCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            Vector2 lookDir = (Vector2)mousePos - (Vector2)nearArmGun.position;
            float angle = Mathf.Atan2(lookDir.y, lookDir.x) * Mathf.Rad2Deg;
            float finalAngle = (transform.localScale.x < 0) ? angle + 180f : angle;
            nearArmGun.rotation = Quaternion.Euler(0, 0, finalAngle);

            if (farHandGrip != null)
            {
                Vector2 shoulderToGrip = (Vector2)farHandGrip.position - (Vector2)farArm.position;
                float distance = shoulderToGrip.magnitude;
                float farAngle = Mathf.Atan2(shoulderToGrip.y, shoulderToGrip.x) * Mathf.Rad2Deg;
                float finalFarAngle = (transform.localScale.x < 0) ? farAngle + 180f : farAngle;
                farArm.rotation = Quaternion.Euler(0, 0, finalFarAngle);
                float stretchFactor = distance / armLength;
                farArm.localScale = new Vector3(stretchFactor, 1f, 1f);
            }
        }
        else
        {
            nearArmGun.localRotation = Quaternion.identity;
            farArm.localRotation = Quaternion.identity;
            farArm.localScale = Vector3.one;
        }
    }

    private void HandleVisualSwitch()
    {
        if (classicModel == null || aimingModel == null) return;
        classicModel.SetActive(!isAiming);
        aimingModel.SetActive(isAiming);
    }

    void FixedUpdate()
    {
        if (isAiming && isGrounded)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        else
            rb.linearVelocity = new Vector2(moveInput.x * moveSpeed, rb.linearVelocity.y);
    }

    public void OnAim(InputValue value)
    {
        if (value.isPressed && isGrounded)
        {
            isAiming = !isAiming;
        }
    }

    public void OnMove(InputValue value) { moveInput = value.Get<Vector2>(); }

    public void OnJump(InputValue value)
    {
        if (!isAiming && isGrounded && value.isPressed)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            isGrounded = false;
        }
    }

    void OnDestroy() { if (raycast != null) raycast.Cleanup(); }
}