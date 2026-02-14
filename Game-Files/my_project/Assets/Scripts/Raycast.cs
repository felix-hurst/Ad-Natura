using UnityEngine;
using UnityEngine.InputSystem;

public class Raycast : MonoBehaviour
{
    [Header("Raycast Settings")]
    [SerializeField] private float raycastMaxDistance = 100f;
    [SerializeField] private float dotSize = 0.2f;
    [SerializeField] private float dashWorldSize = 0.2f;

    [Header("Explosive Settings")]
    [SerializeField] private GameObject explosiveBallPrefab;
    [SerializeField] private float throwForce = 15f;
    [SerializeField] private float ballSpawnOffset = 1.0f;

    [Header("Arc Settings")]
    [SerializeField] private int arcResolution = 60; // How many points in the arc should be simulated

    private LineRenderer lineRenderer;
    private GameObject entryDot;
    private GameObject exitDot;
    private RaycastReceiver currentlyHighlighted;
    private Transform playerTransform;
    private GameObject projectilePrefab; 
    private float maxCuttingRange = 10f;
    private int rifleExplosionRounds = 3;
    private float rifleDelayBetweenRounds = 0.8f;
    private int arcMask;
    private Transform muzzleTransform;

    // Tool State Management
    private PlayerController.ToolType currentTool;
    private Vector2 currentEntryPoint;
    private Vector2 currentExitPoint;
    private bool hasValidCut;

    public void Initialize(Transform player, Transform muzzle, Texture2D dashTexture)
    {
        playerTransform = player;
        muzzleTransform = muzzle;

        // Set up LineRenderer for visualization
        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;

        // Use Unlit shader for consistent colors
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material.mainTexture = dashTexture;
        lineRenderer.sortingOrder = 5;
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.textureMode = LineTextureMode.Tile;

        // Create entry dot
        entryDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        entryDot.transform.localScale = Vector3.one * dotSize;
        Object.Destroy(entryDot.GetComponent<Collider>());
        entryDot.SetActive(false);

        // Create exit dot
        exitDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        exitDot.transform.localScale = Vector3.one * dotSize;
        Object.Destroy(exitDot.GetComponent<Collider>());
        exitDot.SetActive(false);

        //Mask Calculation
        int playerLayer = LayerMask.NameToLayer("Player");
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        arcMask = ~((1 << playerLayer) | (1 << ignoreRaycastLayer));

        // Default color
        SetLaserColor(Color.white);
    }

    private void SetLaserColor(Color color)
    {
        if (lineRenderer == null) return;
        lineRenderer.material.color = color;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        entryDot.GetComponent<Renderer>().material.color = color;
        exitDot.GetComponent<Renderer>().material.color = color;
    }

    public void SetCurrentTool(PlayerController.ToolType tool)
    {
        currentTool = tool;
        SetLaserColor(Color.white);
    }

    public void DrawLineAndCheckHits()
    {
        Vector3 mouseScreenPos = Mouse.current.position.ReadValue();

        mouseScreenPos.z = Mathf.Abs(Camera.main.transform.position.z);
        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(mouseScreenPos);
        mousePosition.z = 0;

        Vector2 direction = (mousePosition - muzzleTransform.position).normalized;
        float distance = Vector2.Distance(new Vector2(muzzleTransform.position.x, muzzleTransform.position.y), new Vector2(mousePosition.x, mousePosition.y));

        if (currentTool == PlayerController.ToolType.WaterBall ||
            currentTool == PlayerController.ToolType.ExplosiveBall)
        {
            DrawArc(direction);
            HideDots();
        }
        else
        {
            DrawStraightLine(mousePosition);
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (currentTool == PlayerController.ToolType.ExplosiveBall ||
                currentTool == PlayerController.ToolType.WaterBall)
            {
                ThrowProjectile(direction);
                return;
            }
        }

        //Hit Detection Logic (For Cutting/Rifle)
        if (currentTool != PlayerController.ToolType.CuttingTool && currentTool != PlayerController.ToolType.Rifle)
        {
            //If its not a cutting tool, return
            ClearAllHighlights();
            return;
        }
        RaycastHit2D[] hits = Physics2D.RaycastAll(muzzleTransform.position, direction, distance);

        RaycastHit2D targetHit = default;
        Collider2D targetCollider = null;
        bool foundValidTarget = false;
        float closestDistance = float.MaxValue;

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider.gameObject == playerTransform.gameObject) continue;
            if (hit.collider.gameObject.name.Contains("Debris") || hit.collider.gameObject.name.Contains("Fragment")) continue;

            float hitDistance = Vector2.Distance(muzzleTransform.position, hit.point);

            if (currentTool == PlayerController.ToolType.CuttingTool && hitDistance > maxCuttingRange)
                continue;

            float distToMouse = Vector2.Distance(hit.point, mousePosition);
            if (distToMouse < closestDistance)
            {
                targetHit = hit;
                targetCollider = hit.collider;
                closestDistance = distToMouse;
                foundValidTarget = true;
            }
        }

        if (foundValidTarget && targetCollider != null)
        {
            HandleCuttingDots(targetHit, direction, distance);
        }
        else
        {
            ClearAllHighlights();
        }

        //Perform the cut
        if (Mouse.current.leftButton.wasPressedThisFrame && hasValidCut && currentlyHighlighted != null)
        {
            if (currentTool == PlayerController.ToolType.Rifle)
            {
                currentlyHighlighted.ExecuteCutWithExplosion(currentEntryPoint, currentExitPoint, rifleExplosionRounds, rifleDelayBetweenRounds);
            }
            else
            {
                currentlyHighlighted.ExecuteCut(currentEntryPoint, currentExitPoint);
            }

            currentlyHighlighted.ClearHighlight();
            currentlyHighlighted = null;
            hasValidCut = false;
        }
    }

    private void DrawStraightLine(Vector3 target)
    {
        Vector3 start = muzzleTransform.position;
        start.z = 0; 
        target.z = 0;
        float distance = Vector3.Distance(start, target);

        // 'Tile' maps the texture based on world units
        lineRenderer.textureMode = LineTextureMode.Tile;
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, target);

        //Total distance divided by the size of one dash cycle
        lineRenderer.material.mainTextureScale = new Vector2(distance / dashWorldSize, 1f);
    }
  

    private void DrawArc(Vector2 direction)
    {
        Vector2 velocity = direction * throwForce;
        Vector2 startPosition = (Vector2)muzzleTransform.position;

        System.Collections.Generic.List<Vector3> points = new System.Collections.Generic.List<Vector3>();
        points.Add(new Vector3(startPosition.x, startPosition.y, 0));

        float totalLength = 0f;
        Vector2 lastPos = startPosition;

        for (int i = 1; i < arcResolution; i++)
        {
            float t = i * 0.05f;
            Vector2 pos = startPosition + (velocity * t) + 0.5f * Physics2D.gravity * t * t;

            RaycastHit2D hit = Physics2D.Linecast(lastPos, pos, arcMask);
            Vector3 currentPoint = hit.collider != null ? (Vector3)hit.point : (Vector3)pos;
            currentPoint.z = 0;

            totalLength += Vector3.Distance(new Vector3(lastPos.x, lastPos.y, 0), currentPoint);
            points.Add(currentPoint);

            if (hit.collider != null) break;
            lastPos = pos;
        }

        if (points.Count < 2) return;

        lineRenderer.textureMode = LineTextureMode.Tile;
        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());

        lineRenderer.material.mainTextureScale = new Vector2(totalLength / dashWorldSize, 1f);
    }

    private void HandleCuttingDots(RaycastHit2D targetHit, Vector2 direction, float distance)
    {
        Vector2 entryPoint = targetHit.point;
        entryDot.SetActive(true);
        entryDot.transform.position = new Vector3(entryPoint.x, entryPoint.y, 0);

        Collider2D hitCollider = targetHit.collider;
        Bounds bounds = hitCollider.bounds;

        // Calculate a point far enough on the other side of the object to raycast back
        Vector2 farPoint = (Vector2)muzzleTransform.position + direction * (distance + bounds.size.magnitude);

        RaycastHit2D[] reverseHits = Physics2D.RaycastAll(farPoint, -direction, distance + bounds.size.magnitude);
        Vector2 exitPoint = Vector2.zero;
        bool foundExit = false;

        RaycastReceiver receiver = hitCollider.GetComponent<RaycastReceiver>();

        // Find the exit point by checking reverse hits on the same collider
        foreach (RaycastHit2D hit in reverseHits)
        {
            if (hit.collider == hitCollider)
            {
                // Ensure the exit point isn't basically the same as the entry point
                if (Vector2.Distance(hit.point, entryPoint) > 0.05f)
                {
                    exitPoint = hit.point;
                    foundExit = true;
                    break;
                }
            }
        }

        if (foundExit)
        {
            exitDot.SetActive(true);
            exitDot.transform.position = new Vector3(exitPoint.x, exitPoint.y, 0);
            currentEntryPoint = entryPoint;
            currentExitPoint = exitPoint;
            hasValidCut = true;

            if (receiver != null)
            {
                if (currentlyHighlighted != null && currentlyHighlighted != receiver)
                {
                    currentlyHighlighted.ClearHighlight();
                }

                receiver.HighlightCutEdges(entryPoint, exitPoint);
                currentlyHighlighted = receiver;
            }
        }
        else
        {
            exitDot.SetActive(false);
            hasValidCut = false;
            if (currentlyHighlighted != null)
            {
                currentlyHighlighted.ClearHighlight();
                currentlyHighlighted = null;
            }
        }
    }

    public void SetCurrentTool(PlayerController.ToolType tool, GameObject ballPrefab, float force, float spawnOffset = 1.0f, float cuttingRange = 10f, int explosionRounds = 3, float delayBetweenRounds = 0.8f)
    {
        currentTool = tool;
        projectilePrefab = ballPrefab; 
        throwForce = force;
        ballSpawnOffset = spawnOffset;
        maxCuttingRange = cuttingRange;
        rifleExplosionRounds = explosionRounds;
        rifleDelayBetweenRounds = delayBetweenRounds;
    }

    void ThrowProjectile(Vector2 direction)
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning($"Raycast: No projectile prefab assigned for {currentTool}!");
            return;
        }

        Vector3 spawnPosition = muzzleTransform.position;

        GameObject projectile = Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);

        Rigidbody2D rb = projectile.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = projectile.AddComponent<Rigidbody2D>();
        }

        rb.gravityScale = 1f;
        rb.linearVelocity = direction * throwForce;
        
        Debug.Log($"Raycast: Threw {currentTool} with force {throwForce} in direction {direction}");
    }

    private void HideDots()
    {
        entryDot.SetActive(false);
        exitDot.SetActive(false);
        hasValidCut = false;
    }

    private void ClearAllHighlights()
    {
        HideDots();
        if (currentlyHighlighted != null)
        {
            currentlyHighlighted.ClearHighlight();
            currentlyHighlighted = null;
        }
    }

    void Update() => DrawLineAndCheckHits();

    public void Cleanup()
    {
        ClearAllHighlights();

        if (entryDot != null) Object.Destroy(entryDot);
        if (exitDot != null) Object.Destroy(exitDot);
    }

    private void OnDisable()
    {
        if (lineRenderer != null) lineRenderer.enabled = false;
        ClearAllHighlights();
        lineRenderer.positionCount = 0;
    }

    private void OnEnable()
    {
        if (lineRenderer != null) lineRenderer.enabled = true;
    }
}