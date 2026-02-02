using UnityEngine;
using UnityEngine.InputSystem;

public class Raycast : MonoBehaviour
{
    [Header("Raycast Settings")]
    [SerializeField] private float raycastMaxDistance = 100f;
    [SerializeField] private float dotSize = 0.2f;

    private LineRenderer lineRenderer;
    private GameObject entryDot;
    private GameObject exitDot;
    private RaycastReceiver currentlyHighlighted;
    private Transform playerTransform;
    private PlayerController.ToolType currentTool;
    private GameObject explosiveBallPrefab;
    private float throwForce;
    private float ballSpawnOffset;
    private float maxCuttingRange = 10f; 

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
    }

    public void DrawLineAndCheckHits()
    {
        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mousePosition.z = 0;

        Vector2 direction = (mousePosition - playerTransform.position).normalized;
        float distance = Vector2.Distance(playerTransform.position, mousePosition);

        lineRenderer.SetPosition(0, playerTransform.position);
        lineRenderer.SetPosition(1, mousePosition);

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (currentTool == PlayerController.ToolType.ExplosiveBall)
            {
                ThrowExplosiveBall(direction);
                return;
            }
        }

        if (currentTool != PlayerController.ToolType.CuttingTool)
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

        // CRITICAL FIX: Find the CLOSEST valid target under the mouse
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

            float hitDistance = Vector2.Distance(playerTransform.position, hit.point);
            if (hitDistance > maxCuttingRange)
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
        explosiveBallPrefab = ballPrefab;
        throwForce = force;
        ballSpawnOffset = spawnOffset;
        maxCuttingRange = cuttingRange;
    }

    void ThrowExplosiveBall(Vector2 direction)
    {
        if (explosiveBallPrefab == null)
        {
            return;
        }

        Vector3 spawnPosition = playerTransform.position + (Vector3)(direction * ballSpawnOffset);

        GameObject ball = Instantiate(explosiveBallPrefab, spawnPosition, Quaternion.identity);

        Rigidbody2D rb = ball.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = ball.AddComponent<Rigidbody2D>();
        }

        rb.gravityScale = 1f;
        rb.linearVelocity = direction * throwForce;
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