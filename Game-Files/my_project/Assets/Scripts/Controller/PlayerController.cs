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

    [Header("Explosive Ball Settings")]
    [SerializeField] private GameObject explosiveBallPrefab;
    [SerializeField] private float explosiveBallThrowForce = 10f;
    [SerializeField] private float explosiveBallSpawnOffset = 1.0f;

    [Header("Water Ball Settings")]
    [SerializeField] private GameObject waterBallPrefab;
    [SerializeField] private float waterBallThrowForce = 10f;
    [SerializeField] private float waterBallSpawnOffset = 1.0f;
    [SerializeField] private Texture2D dashTexture;

    [Header("Cutting Tool Settings")]
    [SerializeField] private float maxCuttingRange = 10f;

    [Header("Rifle Settings")]
    [SerializeField] private int rifleExplosionRounds = 3;
    [SerializeField] private float rifleDelayBetweenRounds = 0.8f;


    [Header("References")]
    [SerializeField] private Camera gameCamera;
    [SerializeField] private GameObject classicModel; //classic model uses the animators, and is all non-aiming behaviour
    [SerializeField] private GameObject aimingModel; //aiming model is the model that the aiming section manipulates
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

    // Slime surface state
    private bool isOnSlimeSurface = false;
    private bool isClimbingSlime = false;
    private SlimeMovementProperties currentSlimeProps;
    private float originalGravityScale;

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
        originalGravityScale = rb.gravityScale;

        // Create and initialize raycast system
        raycast = gameObject.AddComponent<Raycast>();
        raycast.Initialize(transform, dashTexture);
        raycast.SetCurrentTool(currentTool); // Set initial tool
        raycast.enabled = false; // Start with it off

        if (classicModel != null)
        {
            anim = classicModel.GetComponent<Animator>();
        }
    }

    void Update()
    {
        // Physics-based Running Logic. The player must stop running for a very short time to be idle again
        // This is to prevent going left -> right -> left -> right and being idle between them
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
        //isGrounded = Physics2D.OverlapBox(checkPosition, groundCheckSize, groundCheckAngle, groundLayer);

        // Check for slime surface interaction
        CheckSlimeSurface();

        //If the player is not grounded, force them to stop aiming
        if (isAiming && !isGrounded)
        {
            isAiming = false;
        }

        HandleSpriteFlipping();
        HandleArmRotation();
        HandleVisualSwitch();

        // Sync Raycast state with Aiming
        if (raycast != null)
        {
            raycast.enabled = isAiming;

            if (isAiming)
            {
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
        else
        {
            //Rotate both arms, and scale the far one so its always holding the gun
            nearArmGun.localRotation = Quaternion.identity;
            farArm.localRotation = Quaternion.identity;
            farArm.localScale = Vector3.one;
        }
    }

        // PREVENT MOVEMENT WHILE AIMING
    //Switch between classic model and aiming model based on if player is aiming
    private void HandleVisualSwitch()
    {
        if (classicModel == null || aimingModel == null) return;
        classicModel.SetActive(!isAiming);
        aimingModel.SetActive(isAiming);
    }

    void FixedUpdate()
    {
        // Calculate effective move speed (modified by slime if applicable)
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
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        isGrounded = false;
    }

}
