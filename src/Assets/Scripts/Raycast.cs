using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class Raycast : MonoBehaviour
{
    [Header("Raycast Settings")]
    [SerializeField] private float dotSize = 0.2f;
    [SerializeField] private float dashWorldSize = 0.2f;

    [Header("Arc Settings")]
    [SerializeField] private int arcResolution = 60;

    private LineRenderer lineRenderer;

    // Entry and exit dots are created but currently unused visually —
    // kept as placeholders for future cut-point preview functionality.
    private GameObject entryDot;
    private GameObject exitDot;
    private Transform playerTransform;
    private Transform muzzleTransform;
    private PlayerController.ToolType currentTool;
    private GameObject projectilePrefab;
    private float throwForce = 15f;
    private float ballSpawnOffset = 1.0f;

    // Cached at init time so arc raycasting doesn't rebuild the mask every frame.
    private int arcMask;

    [Header("Muzzle Blast Settings")]
    [SerializeField] private BurstLeafSystem.MuzzleBlastSettings muzzleBlastSettings = new BurstLeafSystem.MuzzleBlastSettings();

    public void Initialize(Transform player, Transform muzzle, Texture2D dashTexture)
    {
        playerTransform = player;
        muzzleTransform = muzzle;

        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        // Tiled texture on the line renderer produces the dashed-line aiming style
        // rather than a plain solid beam.
        lineRenderer.material.mainTexture = dashTexture;
        lineRenderer.sortingOrder = 5;
        lineRenderer.useWorldSpace = true;
        lineRenderer.textureMode = LineTextureMode.Tile;

        // Dot colliders are removed so the aiming indicators don't interfere
        // with physics queries or trigger enter/exit events.
        entryDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        entryDot.transform.localScale = Vector3.one * dotSize;
        Destroy(entryDot.GetComponent<Collider>());
        entryDot.SetActive(false);

        exitDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        exitDot.transform.localScale = Vector3.one * dotSize;
        Destroy(exitDot.GetComponent<Collider>());
        exitDot.SetActive(false);

        // The arc mask excludes layers the projectile should pass through or
        // that would produce misleading arc termination points — the player's own
        // collider, UI elements, water surfaces, and already-cut debris.
        arcMask = ~((1 << LayerMask.NameToLayer("Player")) |
            (1 << LayerMask.NameToLayer("Ignore Raycast")) |
            (1 << LayerMask.NameToLayer("WaterCollider")) |
            (1 << LayerMask.NameToLayer("SlimeBoundary")) |
            (1 << LayerMask.NameToLayer("CutPiece")) |
            (1 << LayerMask.NameToLayer("SlimeObstacle")) |
            (1 << LayerMask.NameToLayer("Background")) |
            (1 << LayerMask.NameToLayer("UI")));

        SetLaserColor(Color.white);
    }

    private void SetLaserColor(Color color)
    {
        if (lineRenderer == null) return;
        lineRenderer.material.color = color;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
    }

    public void SetCurrentTool(PlayerController.ToolType tool, GameObject ballPrefab, float force, float spawnOffset, float range)
    {
        currentTool = tool;
        projectilePrefab = ballPrefab;
        throwForce = force;
        ballSpawnOffset = spawnOffset;
    }

    void Update()
    {
        if (muzzleTransform == null) return;

        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mousePos.z = 0;
        Vector2 direction = ((Vector2)mousePos - (Vector2)muzzleTransform.position).normalized;

        // Projectiles affected by gravity get an arc preview so the player can
        // judge the parabolic drop. Wind ball travels straight so a simple line
        // is accurate enough and less visually noisy.
        if (currentTool == PlayerController.ToolType.WaterBall || currentTool == PlayerController.ToolType.IncendiaryBall)
        {
            DrawArc(direction);
        }
        else
        {
            DrawStraightLine(mousePos);
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Ammo check is delegated to PlayerController so this class doesn't
            // need to know anything about inventory or ammo management.
            PlayerController pc = playerTransform.GetComponent<PlayerController>();
            if (pc != null && pc.RequestAmmoUse(currentTool))
            {
                ThrowProjectile(direction);
            }
        }
    }

    private void DrawStraightLine(Vector3 target)
    {
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, muzzleTransform.position);
        lineRenderer.SetPosition(1, target);

        // Scale the texture tiling by world-space length so the dash pattern
        // stays a consistent physical size regardless of how far the cursor is.
        float dist = Vector3.Distance(muzzleTransform.position, target);
        lineRenderer.material.mainTextureScale = new Vector2(dist / dashWorldSize, 1f);
    }

    private void DrawArc(Vector2 direction)
    {
        // Simulate projectile motion using the same initial velocity and gravity
        // the actual Rigidbody2D will experience, so the preview arc matches
        // where the ball will actually land.
        Vector2 velocity = direction * throwForce;
        Vector2 startPos = (Vector2)muzzleTransform.position;
        List<Vector3> points = new List<Vector3>() { startPos };
        Vector2 lastPos = startPos;
        float totalLength = 0;

        for (int i = 1; i < arcResolution; i++)
        {
            float t = i * 0.05f;
            Vector2 pos = startPos + (velocity * t) + 0.5f * Physics2D.gravity * t * t;

            // Cast between each arc segment so the preview line stops exactly at
            // the first surface the projectile would hit, rather than clipping
            // through geometry.
            RaycastHit2D hit = Physics2D.Linecast(lastPos, pos, arcMask);

            Vector3 currentPoint = hit.collider != null ? (Vector3)hit.point : (Vector3)pos;
            totalLength += Vector3.Distance(lastPos, currentPoint);
            points.Add(currentPoint);

            // Early out once a collision is found — there's no need to simulate
            // the trajectory past the impact point.
            if (hit.collider != null) break;
            lastPos = currentPoint;
        }

        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());

        // Scale tiling by total arc length for consistent dash size along a
        // curved path, just as DrawStraightLine does for a straight one.
        lineRenderer.material.mainTextureScale = new Vector2(totalLength / dashWorldSize, 1f);
    }

    void ThrowProjectile(Vector2 direction)
    {
        if (projectilePrefab == null) return;

        GameObject projectile = Instantiate(projectilePrefab, muzzleTransform.position, Quaternion.identity);
        Rigidbody2D rb = projectile.GetComponent<Rigidbody2D>();

        // Wind ball travels in a straight line so gravity is disabled to match
        // its straight-line aiming preview.
        rb.gravityScale = (currentTool == PlayerController.ToolType.WindBall) ? 0f : 1f;
        rb.linearVelocity = direction * throwForce;

        // Muzzle blast is only triggered for the incendiary ball — it simulates
        // the pressure wave from the firing event disturbing nearby leaves.
        if (currentTool == PlayerController.ToolType.IncendiaryBall)
            BurstLeafSystem.MuzzleBlastAll(muzzleTransform.position, direction, muzzleBlastSettings);
    }

    public void Cleanup()
    {
        if (entryDot) Destroy(entryDot);
        if (exitDot) Destroy(exitDot);
    }

    private void OnEnable()
    {
        // Show the aiming line whenever this tool becomes the active one.
        if (lineRenderer != null)
        {
            lineRenderer.enabled = true;
        }
    }

    private void OnDisable()
    {
        // Hide the aiming line when switching to a different tool so the
        // previous tool's preview doesn't linger on screen.
        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
        }
    }
}