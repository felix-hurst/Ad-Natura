using UnityEngine;
using UnityEngine.InputSystem;

public class Raycast : MonoBehaviour
{
    [Header("Raycast Settings")]
    [SerializeField] private float raycastMaxDistance = 100f;
    [SerializeField] private float dotSize = 0.2f;
    [SerializeField] private float dashWorldSize = 0.2f;

    [Header("Incendiary Settings")]
    [SerializeField] private GameObject incendiaryBallPrefab;
    [SerializeField] private float throwForce = 15f;
    [SerializeField] private float ballSpawnOffset = 1.0f;

    [Header("Arc Settings")]
    [SerializeField] private int arcResolution = 60; // How many points in the arc should be simulated

    private LineRenderer lineRenderer;
    private GameObject entryDot;
    private GameObject exitDot;
    private RaycastReceiver currentlyHighlighted;
    private Transform playerTransform;
    private PlayerController.ToolType currentTool;
    private GameObject projectilePrefab;
    private float throwForce;
    private float ballSpawnOffset;
    private float maxCuttingRange = 10f;

    [Header("Muzzle Blast Settings (Incendiary Ball)")]
    [SerializeField] private BurstLeafSystem.MuzzleBlastSettings muzzleBlastSettings = new BurstLeafSystem.MuzzleBlastSettings();

    private Vector2 currentEntryPoint;
    private Vector2 currentExitPoint;
    private bool hasValidCut;

    public void Initialize(Transform player)
    {
        playerTransform = player;

        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;

        lineRenderer.material = new Material(Shader.Find("Unlit/Color"));
        lineRenderer.material.color = Color.white;

        lineRenderer.startColor = Color.white;
        lineRenderer.endColor = Color.white;
        lineRenderer.sortingOrder = 10;
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;

        entryDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        entryDot.transform.localScale = Vector3.one * dotSize;
        entryDot.GetComponent<Renderer>().material.color = Color.white;
        Object.Destroy(entryDot.GetComponent<Collider>());
        entryDot.SetActive(false);

        exitDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        exitDot.transform.localScale = Vector3.one * dotSize;
        exitDot.GetComponent<Renderer>().material.color = Color.white;
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
        projectilePrefab = null;
        SetLaserColor(Color.white);
    }

    public void DrawLineAndCheckHits()
    {
        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mousePosition.z = 0;

        Vector2 direction = (mousePosition - muzzleTransform.position).normalized;
        float distance = Vector2.Distance(new Vector2(muzzleTransform.position.x, muzzleTransform.position.y), new Vector2(mousePosition.x, mousePosition.y));

        if (currentTool == PlayerController.ToolType.WaterBall ||
            currentTool == PlayerController.ToolType.IncendiaryBall)
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
            if (currentTool == PlayerController.ToolType.IncendiaryBall ||
                currentTool == PlayerController.ToolType.WaterBall)
            {
                // Get reference to player to check ammo
                PlayerController pc = playerTransform.GetComponent<PlayerController>();

                // Only throw if RequestAmmoUse returns true
                if (pc != null && pc.RequestAmmoUse(currentTool))
                {
                    ThrowProjectile(direction);
                }
                else
                {
                    // Optional: Play a "click" empty sound here
                    Debug.Log("Click! Out of ammo.");
                }
                return;
            }
        }

        //Hit Detection Logic (For Cutting/Rifle)
        if (currentTool != PlayerController.ToolType.Rifle)
        {
            entryDot.SetActive(false);
            exitDot.SetActive(false);
            hasValidCut = false;

            if (currentlyHighlighted != null)
            {
                currentlyHighlighted.ClearHighlight();
                currentlyHighlighted = null;
            }
            return;
        }

        RaycastHit2D[] hits = Physics2D.RaycastAll(playerTransform.position, direction, distance);

        RaycastHit2D targetHit = default;
        Collider2D targetCollider = null;
        bool foundValidTarget = false;
        float closestDistance = float.MaxValue;

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider.gameObject == playerTransform.gameObject)
                continue;

            if (hit.collider.gameObject.name.Contains("Debris") ||
                hit.collider.gameObject.name.Contains("Fragment"))
                continue;

            float hitDistance = Vector2.Distance(muzzleTransform.position, hit.point);

            if (currentTool == PlayerController.ToolType.Rifle && hitDistance > maxCuttingRange)
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
            Vector2 entryPoint = Vector2.zero;
            Vector2 exitPoint = Vector2.zero;
            bool foundEntry = false;
            bool foundExit = false;

            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider == targetCollider)
                {
                    entryPoint = hit.point;
                    foundEntry = true;
                    break;
                }
            }

            if (foundEntry)
            {
                Bounds bounds = targetCollider.bounds;
                Vector2 farPoint = (Vector2)playerTransform.position + direction * (distance + bounds.size.magnitude * 2f);
                
                RaycastHit2D[] reverseHits = Physics2D.RaycastAll(farPoint, -direction, distance + bounds.size.magnitude * 2f);

                foreach (RaycastHit2D hit in reverseHits)
                {
                    if (hit.collider == targetCollider)
                    {
                        if (Vector2.Distance(hit.point, entryPoint) > 0.1f)
                        {
                            exitPoint = hit.point;
                            foundExit = true;
                            break;
                        }
                    }
                }

                if (foundExit)
                {
                    entryDot.SetActive(true);
                    entryDot.transform.position = new Vector3(entryPoint.x, entryPoint.y, 0);

                    exitDot.SetActive(true);
                    exitDot.transform.position = new Vector3(exitPoint.x, exitPoint.y, 0);

                    currentEntryPoint = entryPoint;
                    currentExitPoint = exitPoint;
                    hasValidCut = true;

                    RaycastReceiver receiver = targetCollider.GetComponent<RaycastReceiver>();
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
            else
            {
                entryDot.SetActive(false);
                exitDot.SetActive(false);
                hasValidCut = false;

                if (currentlyHighlighted != null)
                {
                    currentlyHighlighted.ClearHighlight();
                    currentlyHighlighted = null;
                }
            }
        }
        else
        {
            entryDot.SetActive(false);
            exitDot.SetActive(false);
            hasValidCut = false;

            if (currentlyHighlighted != null)
            {
                currentlyHighlighted.ClearHighlight();
                currentlyHighlighted = null;
            }
        }

        if (Mouse.current.leftButton.wasPressedThisFrame && hasValidCut && currentlyHighlighted != null)
        {
 
            currentlyHighlighted.ExecuteCut(currentEntryPoint, currentExitPoint);

            currentlyHighlighted.ClearHighlight();
            currentlyHighlighted = null;
            hasValidCut = false;
        }
    }

    public void SetCurrentTool(PlayerController.ToolType tool, GameObject ballPrefab, float force, float spawnOffset = 1.0f, float cuttingRange = 10f)
    {
        currentTool = tool;
        projectilePrefab = ballPrefab; 
        throwForce = force;
        ballSpawnOffset = spawnOffset;
        maxCuttingRange = cuttingRange;
    }

    void ThrowProjectile(Vector2 direction)
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning($"Raycast: No projectile prefab assigned for {currentTool}!");
            return;
        }

        Vector3 spawnPosition = playerTransform.position + (Vector3)(direction * ballSpawnOffset);

        GameObject projectile = Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);

        Rigidbody2D rb = projectile.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = projectile.AddComponent<Rigidbody2D>();
        }

        rb.gravityScale = 1f;
        rb.linearVelocity = direction * throwForce;
        
        if (currentTool == PlayerController.ToolType.IncendiaryBall)
        {
            BurstLeafSystem.MuzzleBlastAll(spawnPosition, direction, muzzleBlastSettings);
        }
        
        Debug.Log($"Raycast: Threw {currentTool} with force {throwForce} in direction {direction}");
    }

    public void Cleanup()
    {
        if (entryDot != null) Object.Destroy(entryDot);
        if (exitDot != null) Object.Destroy(exitDot);

        if (currentlyHighlighted != null)
        {
            currentlyHighlighted.ClearHighlight();
        }
    }

    void Update()
    {
        DrawLineAndCheckHits();
    }

    private void OnDisable()
    {
        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
        }

        if (entryDot != null)
        {
            entryDot.SetActive(false);
        }
        if (exitDot != null)
        {
            exitDot.SetActive(false);
        }

        if (currentlyHighlighted != null)
        {
            currentlyHighlighted.ClearHighlight();
            currentlyHighlighted = null;
        }

        hasValidCut = false;
    }

    private void OnEnable()
    {
        if (lineRenderer != null)
        {
            lineRenderer.enabled = true;
        }
    }
}