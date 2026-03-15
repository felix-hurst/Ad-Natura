using UnityEngine;
using UnityEngine.InputSystem;

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
    [SerializeField] private float minGroundAngleToJump = 60f;

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

    [Header("Explosive Ball Settings")]
    [SerializeField] private GameObject explosiveBallPrefab;
    [SerializeField] private float explosiveBallThrowForce = 10f;
    [SerializeField] private float explosiveBallSpawnOffset = 1.0f;

    [Header("Incendiary Ball Settings")]
    [SerializeField] private GameObject incendiaryBallPrefab;
    [SerializeField] private float incendiaryBallThrowForce = 10f;
    [SerializeField] private float incendiaryBallSpawnOffset = 1.0f;

    [Header("Water Ball Settings")]
    [SerializeField] private GameObject waterBallPrefab;
    [SerializeField] private float waterBallThrowForce = 10f;
    [SerializeField] private float waterBallSpawnOffset = 1.0f;

    [Header("Wind Ball Settings")]
    [SerializeField] private GameObject windBallPrefab;
    [SerializeField] private float windBallThrowForce = 10f;
    [SerializeField] private float windBallSpawnOffset = 1.0f;

    [Header("Cutting Tool Settings")]
    [SerializeField] private float maxCuttingRange = 10f;

    [Header("Rifle Settings")]
    [SerializeField] private int rifleExplosionRounds = 3;
    [SerializeField] private float rifleDelayBetweenRounds = 0.8f;

    [Header("Ammo Settings")]
    [SerializeField] private int maxWaterAmmo = 5;
    [SerializeField] private int maxIncendiaryAmmo = 5;
    [SerializeField] private int maxWindAmmo = 5;


    [Header("References")]
    [SerializeField] private Camera gameCamera;
    [SerializeField] private GameObject classicModel;
    [SerializeField] private GameObject aimingModel;
    [SerializeField] private Transform nearArmGun;
    [SerializeField] private Transform farArm;
    [SerializeField] private Transform muzzle;

    [Header("Stretching Settings")]
    [SerializeField] private Transform farHandGrip;
    [SerializeField] private float armLength = 0.5f;
    [SerializeField] private Texture2D dashTexture;

    [Header("Wall Climb")]
    [SerializeField] private float slowFall = -0.5f;
    private bool isWallClimbing = false;

    private Animator anim;
    private Rigidbody2D rb;
    private bool isGrounded;
    private bool isRunning;
    private bool isAiming;
    private Vector2 moveInput = Vector2.zero;
    private Raycast raycast;

    private bool isOnSlimeSurface = false;
    private bool isClimbingSlime = false;
    private SlimeMovementProperties currentSlimeProps;
    private float originalGravityScale;

    private float runningBufferTime = 0.1f;
    private float currentRunningBuffer = 0f;
    private float velocityX;

    private int waterAmmo;
    private int incendiaryAmmo;
    private int windAmmo;

    public enum ToolType
    {
        WaterBall,
        IncendiaryBall,
        WindBall
    }
    private ToolType currentTool = ToolType.WaterBall;
    private int currentToolIndex = -1;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        originalGravityScale = rb.gravityScale;

        raycast = gameObject.AddComponent<Raycast>();
        raycast.Initialize(transform, muzzle, dashTexture);
        raycast.SetCurrentTool(currentTool, waterBallPrefab, waterBallThrowForce, waterBallSpawnOffset, maxCuttingRange);
        raycast.enabled = false;

        waterAmmo = maxWaterAmmo;
        incendiaryAmmo = maxIncendiaryAmmo;
        windAmmo = maxWindAmmo;

        if (classicModel != null)
        {
            anim = classicModel.GetComponent<Animator>();
        }
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

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            ReloadAmmo();
        }

        Vector2 checkPosition = (Vector2)groundCheck.position + groundCheckOffset;

        CheckSlimeSurface();

        if (isAiming && !isGrounded)
        {
            isAiming = false;
        }

        HandleSpriteFlipping();
        HandleArmRotation();
        HandleVisualSwitch();

        if (raycast != null)
        {
            bool hasAmmo = true;
            if (currentTool == ToolType.WaterBall) hasAmmo = waterAmmo > 0;
            else if (currentTool == ToolType.IncendiaryBall) hasAmmo = incendiaryAmmo > 0;
            else if (currentTool == ToolType.WindBall) hasAmmo = windAmmo > 0;

            raycast.enabled = isAiming && hasAmmo;

            if (isAiming)
            {
                GameObject ballPrefab = null;
                float throwForce = 0f;
                float spawnOffset = 0f;

                if (currentTool == ToolType.WaterBall)
                {
                    ballPrefab = waterBallPrefab;
                    throwForce = waterBallThrowForce;
                    spawnOffset = waterBallSpawnOffset;
                }
                else if (currentTool == ToolType.IncendiaryBall)
                {
                    ballPrefab = incendiaryBallPrefab;
                    throwForce = incendiaryBallThrowForce;
                    spawnOffset = incendiaryBallSpawnOffset;
                }
                else if (currentTool == ToolType.WindBall)
                {
                    ballPrefab = windBallPrefab;
                    throwForce = windBallThrowForce;
                    spawnOffset = windBallSpawnOffset;
                }

                raycast.SetCurrentTool(currentTool, ballPrefab, throwForce, spawnOffset, maxCuttingRange);
            }
        }

        if (anim != null)
        {
            anim.SetBool("isRunning", isRunning);
            anim.SetBool("isGrounded", isGrounded);
            anim.SetBool("isAiming", isAiming);

            anim.SetInteger("ToolIndex", currentToolIndex);
        }
    }

    public int GetCurrentTool()
    {
        return currentToolIndex;
    }

    public int GetWaterAmmo()
    {
        return waterAmmo;
    }

    public int GetIncendiaryAmmo()
    {
        return incendiaryAmmo;
    }

    public int GetWindAmmo()
    {
        return windAmmo;
    }

    public bool IsRunning()
    {
        return isRunning;
    }

    public void SwitchTool(int toolIndex)
    {
        Debug.Log("SwitchTool called with: " + toolIndex);

        if (toolIndex == -1)
        {
            isAiming = false;
            if (raycast != null) raycast.enabled = false;
            Debug.Log("Player unselected all tools.");
            return;
        }

        currentToolIndex = toolIndex;
        currentTool = (ToolType)toolIndex;

        if (anim != null)
        {
            anim.SetInteger("ToolIndex", toolIndex);
        }

        if (raycast != null)
        {
            GameObject ballPrefab = null;
            float throwForce = 0f;
            float spawnOffset = 0f;

            // Map data based on the new 3-tool setup
            if (currentTool == ToolType.WaterBall)
            {
                ballPrefab = waterBallPrefab;
                throwForce = waterBallThrowForce;
                spawnOffset = waterBallSpawnOffset;
            }
            else if (currentTool == ToolType.IncendiaryBall)
            {
                ballPrefab = incendiaryBallPrefab;
                throwForce = incendiaryBallThrowForce;
                spawnOffset = incendiaryBallSpawnOffset;
            }
            else if (currentTool == ToolType.WindBall)
            {
                ballPrefab = windBallPrefab;
                throwForce = windBallThrowForce;
                spawnOffset = windBallSpawnOffset;
            }

            // Push data to Raycast
            raycast.SetCurrentTool(currentTool, ballPrefab, throwForce, spawnOffset, maxCuttingRange);

            // Update visibility based on ammo
            raycast.enabled = isAiming && HasAmmo(currentTool);

            Debug.Log("Player switched to: " + currentTool.ToString());
        }
    }

    public bool RequestAmmoUse(ToolType tool)
    {
        if (!HasAmmo(tool)) return false;

        if (tool == ToolType.WaterBall) waterAmmo--;
        else if (tool == ToolType.IncendiaryBall) incendiaryAmmo--;
        else if (tool == ToolType.WindBall) windAmmo--;

        return true;
    }

    private bool HasAmmo(ToolType tool)
    {
        if (tool == ToolType.WaterBall) return waterAmmo > 0;
        if (tool == ToolType.IncendiaryBall) return incendiaryAmmo > 0;
        if (tool == ToolType.WindBall) return windAmmo > 0;
        return false;
    }

    public void ReloadAmmo()
    {
        waterAmmo = maxWaterAmmo;
        incendiaryAmmo = maxIncendiaryAmmo;
        windAmmo = maxWindAmmo;
        Debug.Log("All Ammo Refilled!");
    }

    private void HandleSpriteFlipping()
    {
        if (!isRunning && !isWallClimbing)
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
        float effectiveMoveSpeed = moveSpeed;
        if (isOnSlimeSurface && currentSlimeProps.isOnSlime)
        {
            effectiveMoveSpeed *= currentSlimeProps.speedMultiplier;
        }

        if (isAiming && isGrounded)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
        else if (isClimbingSlime && enableSlimeClimbing)
        {
            rb.linearVelocity = new Vector2(moveInput.x * effectiveMoveSpeed * 0.5f, moveInput.y * slimeClimbSpeed);
        }
        else
        {
            rb.linearVelocity = new Vector2(moveInput.x * effectiveMoveSpeed, rb.linearVelocity.y);
        }

        if (isOnSlimeSurface && currentSlimeProps.isOnSlime)
        {
            rb.gravityScale = originalGravityScale * currentSlimeProps.gravityMultiplier;
        }
        else
        {
            rb.gravityScale = originalGravityScale;
        }
    }

    void CheckSlimeSurface()
    {
        if (!enableSlimeClimbing)
        {
            isOnSlimeSurface = false;
            isClimbingSlime = false;
            return;
        }

        Vector2 checkPosition = (Vector2)groundCheck.position + groundCheckOffset;
        Collider2D slimeGround = Physics2D.OverlapBox(checkPosition, groundCheckSize, groundCheckAngle, slimeSurfaceLayer);

        Vector2 playerCenter = transform.position;
        RaycastHit2D leftWall = Physics2D.Raycast(playerCenter, Vector2.left, slimeWallCheckDistance, slimeSurfaceLayer);
        RaycastHit2D rightWall = Physics2D.Raycast(playerCenter, Vector2.right, slimeWallCheckDistance, slimeSurfaceLayer);

        isOnSlimeSurface = (slimeGround != null) || (leftWall.collider != null) || (rightWall.collider != null);

        bool pressingIntoLeftWall = leftWall.collider != null && moveInput.x < -0.1f;
        bool pressingIntoRightWall = rightWall.collider != null && moveInput.x > 0.1f;
        isClimbingSlime = (pressingIntoLeftWall || pressingIntoRightWall) && !isGrounded;

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
        if (value.isPressed && isGrounded)
        {
            isAiming = !isAiming;
        }
    }

    public void OnMove(InputValue value) { moveInput = value.Get<Vector2>(); }

    public void OnJump(InputValue value)
    {
        if (!isAiming && (isGrounded || isWallClimbing) && value.isPressed)
        {
            if (isWallClimbing)               // Debug
                Debug.Log("Wall Jump"); // Debug
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

        if (collision.gameObject.layer == 7)
        {
            // Bool to allow jumping while on wall
            // Slow fall
            isWallClimbing = true;
            anim.SetBool("isWallClimbing", isWallClimbing);
            if (rb.linearVelocityY < slowFall)
                rb.linearVelocityY = slowFall;
        }
        else
        {
            isWallClimbing = false;
            anim.SetBool("isWallClimbing", isWallClimbing);
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        isGrounded = false;
        isWallClimbing = false;
        anim.SetBool("isWallClimbing", isWallClimbing);
    }

}