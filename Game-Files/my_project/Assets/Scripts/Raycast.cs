using UnityEngine;
using UnityEngine.InputSystem;

public class Raycast : MonoBehaviour
{
    [Header("Raycast Settings")]
    [SerializeField] private float raycastMaxDistance = 100f;
    [SerializeField] private float dotSize = 0.2f;

    [Header("Explosive Settings")]
    [SerializeField] private GameObject explosiveBallPrefab;
    [SerializeField] private float throwForce = 15f;
    [SerializeField] private float ballSpawnOffset = 1.0f;

    private LineRenderer lineRenderer;
    private GameObject entryDot;
    private GameObject exitDot;
    private RaycastReceiver currentlyHighlighted;
    private Transform playerTransform;

    // Tool State Management
    private PlayerController.ToolType currentTool;
    private Vector2 currentEntryPoint;
    private Vector2 currentExitPoint;
    private bool hasValidCut;

    public void Initialize(Transform player)
    {
        playerTransform = player;

        // Set up LineRenderer for visualization
        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;

        // Use Unlit shader for consistent colors
        lineRenderer.material = new Material(Shader.Find("Unlit/Color"));
        lineRenderer.sortingOrder = 10;
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;

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
        exitDot.GetComponent<Renderer>().material.color = (color == Color.red) ? new Color(1f, 0.5f, 0f) : color;
    }

    public void SetCurrentTool(PlayerController.ToolType tool)
    {
        currentTool = tool;

        // Update visual feedback based on tool type
        if (currentTool == PlayerController.ToolType.CuttingTool)
            SetLaserColor(Color.red);
        else
            SetLaserColor(Color.white);
    }

    public void DrawLineAndCheckHits()
    {
        if (playerTransform == null) return;

        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mousePosition.z = 0;

        Vector2 direction = (mousePosition - playerTransform.position).normalized;
        float distance = Vector2.Distance(playerTransform.position, mousePosition);

        lineRenderer.SetPosition(0, playerTransform.position);
        lineRenderer.SetPosition(1, mousePosition);

        // --- Handle Input First ---
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (currentTool == PlayerController.ToolType.ExplosiveBall)
            {
                ThrowExplosiveBall(direction);
                return;
            }
        }

        // --- Handle Cutting Logic ---
        if (currentTool != PlayerController.ToolType.CuttingTool)
        {
            ClearAllHighlights();
            return;
        }

        RaycastHit2D[] hits = Physics2D.RaycastAll(playerTransform.position, direction, distance);
        RaycastHit2D firstHit = default;
        bool foundValidHit = false;

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider.gameObject == playerTransform.gameObject) continue;
            if (hit.collider.gameObject.name.Contains("Debris") || hit.collider.gameObject.name.Contains("Fragment")) continue;

            firstHit = hit;
            foundValidHit = true;
            break;
        }

        if (foundValidHit)
        {
            Vector2 entryPoint = firstHit.point;
            entryDot.SetActive(true);
            entryDot.transform.position = new Vector3(entryPoint.x, entryPoint.y, 0);

            Collider2D hitCollider = firstHit.collider;
            Bounds bounds = hitCollider.bounds;
            Vector2 farPoint = (Vector2)playerTransform.position + direction * (distance + bounds.size.magnitude);

            RaycastHit2D[] reverseHits = Physics2D.RaycastAll(farPoint, -direction, distance + bounds.size.magnitude);
            Vector2 exitPoint = Vector2.zero;
            bool foundExit = false;

            foreach (RaycastHit2D hit in reverseHits)
            {
                if (hit.collider == hitCollider)
                {
                    exitPoint = hit.point;
                    foundExit = true;
                    break;
                }
            }

            if (foundExit)
            {
                exitDot.SetActive(true);
                exitDot.transform.position = new Vector3(exitPoint.x, exitPoint.y, 0);
                currentEntryPoint = entryPoint;
                currentExitPoint = exitPoint;
                hasValidCut = true;

                RaycastReceiver receiver = hitCollider.GetComponent<RaycastReceiver>();
                if (receiver != null)
                {
                    if (currentlyHighlighted != null && currentlyHighlighted != receiver)
                        currentlyHighlighted.ClearHighlight();

                    receiver.HighlightCutEdges(entryPoint, exitPoint);
                    currentlyHighlighted = receiver;
                }
            }
            else { HideDots(); }
        }
        else { ClearAllHighlights(); }

        // Execute Cut
        if (Mouse.current.leftButton.wasPressedThisFrame && hasValidCut && currentlyHighlighted != null)
        {
            currentlyHighlighted.ExecuteCut(currentEntryPoint, currentExitPoint);
            ClearAllHighlights();
        }
    }

    private void ThrowExplosiveBall(Vector2 direction)
    {
        if (explosiveBallPrefab == null) return;

        Vector3 spawnPosition = playerTransform.position + (Vector3)(direction * ballSpawnOffset);
        GameObject ball = Instantiate(explosiveBallPrefab, spawnPosition, Quaternion.identity);

        Rigidbody2D rb = ball.GetComponent<Rigidbody2D>();
        if (rb == null) rb = ball.AddComponent<Rigidbody2D>();

        rb.gravityScale = 1f;
        rb.linearVelocity = direction * throwForce;
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
        if (entryDot != null) Object.Destroy(entryDot);
        if (exitDot != null) Object.Destroy(exitDot);
        ClearAllHighlights();
    }

    private void OnDisable()
    {
        if (lineRenderer != null) lineRenderer.enabled = false;
        ClearAllHighlights();
    }

    private void OnEnable()
    {
        if (lineRenderer != null) lineRenderer.enabled = true;
    }
}