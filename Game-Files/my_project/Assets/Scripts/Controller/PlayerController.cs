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
    ExplosiveBall,
    Rifle,
    WaterBall,
    IncendiaryBall
}

[Header("Tool Selection")]
[SerializeField] private ToolType currentTool = ToolType.CuttingTool;

[Header("Cutting Tool Settings")]
[SerializeField] private float maxCuttingRange = 10f;

[Header("Explosive Ball Settings")]
[SerializeField] private GameObject explosiveBallPrefab;
[SerializeField] private float explosiveBallThrowForce = 10f;
[SerializeField] private float explosiveBallSpawnOffset = 1.0f;

[Header("Water Ball Settings")]
[SerializeField] private GameObject waterBallPrefab;
[SerializeField] private float waterBallThrowForce = 10f;
[SerializeField] private float waterBallSpawnOffset = 1.0f;

[Header("Incendiary Ball Settings")]
[SerializeField] private GameObject incendiaryBallPrefab;
[SerializeField] private float incendiaryBallThrowForce = 10f;
[SerializeField] private float incendiaryBallSpawnOffset = 1.0f;

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

    public enum ToolType
    {
        CuttingTool,
        WaterBall,
        Rifle,
        ExplosiveBall
    }
    private ToolType currentTool = ToolType.CuttingTool;
    private int currentToolIndex = -1; //-1 = unequipped, 0 = shovel, 1 = shooter water ammo, 2 shooter explosive ammo, 4 unassigned yet

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
                // Pass appropriate settings based on current tool
                GameObject ballPrefab = null;
                float throwForce = 0f;
                float spawnOffset = 0f;
                
                if (currentTool == ToolType.ExplosiveBall)
                {
                    ballPrefab = explosiveBallPrefab;
                    throwForce = explosiveBallThrowForce;
                    spawnOffset = explosiveBallSpawnOffset;
                }
                else if (currentTool == ToolType.WaterBall)
                {
                    ballPrefab = waterBallPrefab;
                    throwForce = waterBallThrowForce;
                    spawnOffset = waterBallSpawnOffset;
                }

                raycast.SetCurrentTool(currentTool, ballPrefab, throwForce, spawnOffset, maxCuttingRange, rifleExplosionRounds, rifleDelayBetweenRounds);
            }
        }

        if (anim != null)
        {
            anim.SetBool("isRunning", isRunning);
            anim.SetBool("isGrounded", isGrounded);
            anim.SetBool("isAiming", isAiming);

            int animValue = currentToolIndex;
            if (currentToolIndex == 1 || currentToolIndex == 2)
            {
                animValue = 1; 
            }
            else if (currentToolIndex == 3)
            {
                animValue = 2; 
            }

            anim.SetInteger("ToolIndex", animValue);
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

    //When player looks left, flip the entire sprite
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
            //Takes the mouse position and calculates the angle towards it for the near Arm (and Gun)
            Vector3 mousePos = gameCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            Vector2 lookDir = (Vector2)mousePos - (Vector2)nearArmGun.position;
            float angle = Mathf.Atan2(lookDir.y, lookDir.x) * Mathf.Rad2Deg;
            float finalAngle = (transform.localScale.x < 0) ? angle + 180f : angle;
            nearArmGun.rotation = Quaternion.Euler(0, 0, finalAngle);

            //Does the same as above but for the far arm
            if (farHandGrip != null)
            {
                // TODO: Pass waterball attributes when appropriate
                raycast.SetCurrentTool(currentTool, explosiveBallPrefab, explosiveBallThrowForce, explosiveBallSpawnOffset, maxCuttingRange);
                Vector2 shoulderToGrip = (Vector2)farHandGrip.position - (Vector2)farArm.position;
                float distance = shoulderToGrip.magnitude;
                float farAngle = Mathf.Atan2(shoulderToGrip.y, shoulderToGrip.x) * Mathf.Rad2Deg;
                float finalFarAngle = (transform.localScale.x < 0) ? farAngle + 180f : farAngle;
                farArm.rotation = Quaternion.Euler(0, 0, finalFarAngle);
                //When not aiming perfectly left or right, gun will be closer/shorter to far arm, need to scale up/down
                float stretchFactor = distance / armLength;
                farArm.localScale = new Vector3(stretchFactor, 1f, 1f);
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