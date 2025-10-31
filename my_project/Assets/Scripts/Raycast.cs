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
    
    // Store the current cut data for when we click
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
        
        // Use Unlit shader for better visibility
        lineRenderer.material = new Material(Shader.Find("Unlit/Color"));
        lineRenderer.material.color = Color.red;
        
        lineRenderer.startColor = Color.red;
        lineRenderer.endColor = Color.red;
        lineRenderer.sortingOrder = 10;
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        
        // Create entry dot (green)
        entryDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        entryDot.transform.localScale = Vector3.one * dotSize;
        entryDot.GetComponent<Renderer>().material.color = Color.green;
        Object.Destroy(entryDot.GetComponent<Collider>()); // Remove collider so it doesn't interfere
        entryDot.SetActive(false);
        
        // Create exit dot (orange)
        exitDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        exitDot.transform.localScale = Vector3.one * dotSize;
        exitDot.GetComponent<Renderer>().material.color = new Color(1f, 0.5f, 0f); // Orange
        Object.Destroy(exitDot.GetComponent<Collider>()); // Remove collider so it doesn't interfere
        exitDot.SetActive(false);
    }
    
    public void DrawLineAndCheckHits()
    {
        // Get mouse position in world space
        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mousePosition.z = 0;
        
        // Calculate direction from player to mouse
        Vector2 direction = (mousePosition - playerTransform.position).normalized;
        float distance = Vector2.Distance(playerTransform.position, mousePosition);
        
        // Set line renderer to draw to mouse
        lineRenderer.SetPosition(0, playerTransform.position);
        lineRenderer.SetPosition(1, mousePosition);
        
        // Perform raycast to check for hits
        RaycastHit2D[] hits = Physics2D.RaycastAll(playerTransform.position, direction, distance);
        
        // Filter out hits on the player itself and debris
        RaycastHit2D firstHit = default;
        bool foundValidHit = false;
        
        foreach (RaycastHit2D hit in hits)
        {
            // Ignore the player
            if (hit.collider.gameObject == playerTransform.gameObject)
                continue;
            
            // Ignore debris fragments (check if name contains "Debris" or "Fragment")
            if (hit.collider.gameObject.name.Contains("Debris") || 
                hit.collider.gameObject.name.Contains("Fragment"))
                continue;
            
            // This is a valid hit
            firstHit = hit;
            foundValidHit = true;
            break;
        }
        
        if (foundValidHit)
        {
            // Get the first hit (entry point)
            Vector2 entryPoint = firstHit.point;
            
            // Show entry dot
            entryDot.SetActive(true);
            entryDot.transform.position = new Vector3(entryPoint.x, entryPoint.y, 0);
            
            // To find exit point, we need to cast from inside the object outward
            Collider2D hitCollider = firstHit.collider;
            
            // Get the bounds of the collider
            Bounds bounds = hitCollider.bounds;
            Vector2 farPoint = (Vector2)playerTransform.position + direction * (distance + bounds.size.magnitude);
            
            // Cast from far away back towards the entry to find exit point
            RaycastHit2D[] reverseHits = Physics2D.RaycastAll(farPoint, -direction, distance + bounds.size.magnitude);
            
            Vector2 exitPoint = Vector2.zero;
            bool foundExit = false;
            
            foreach (RaycastHit2D hit in reverseHits)
            {
                // Skip the player
                if (hit.collider.gameObject == playerTransform.gameObject)
                    continue;
                
                // Skip debris
                if (hit.collider.gameObject.name.Contains("Debris") || 
                    hit.collider.gameObject.name.Contains("Fragment"))
                    continue;
                    
                if (hit.collider == hitCollider)
                {
                    exitPoint = hit.point;
                    foundExit = true;
                    break;
                }
            }
            
            if (foundExit)
            {
                // Show exit dot
                exitDot.SetActive(true);
                exitDot.transform.position = new Vector3(exitPoint.x, exitPoint.y, 0);
                
                // Store the current cut data
                currentEntryPoint = entryPoint;
                currentExitPoint = exitPoint;
                hasValidCut = true;
                
                // Log entry and exit coordinates
                Debug.Log($"Entry Point: ({entryPoint.x:F2}, {entryPoint.y:F2}) | Exit Point: ({exitPoint.x:F2}, {exitPoint.y:F2}) | Object: {hitCollider.gameObject.name}");
                
                // Highlight cut edges on the hit object
                RaycastReceiver receiver = hitCollider.GetComponent<RaycastReceiver>();
                if (receiver != null)
                {
                    // Clear previous highlight if it's a different object
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
                
                // Clear highlight if no exit found
                if (currentlyHighlighted != null)
                {
                    currentlyHighlighted.ClearHighlight();
                    currentlyHighlighted = null;
                }
            }
        }
        else
        {
            // No valid hits, hide dots and clear highlights
            entryDot.SetActive(false);
            exitDot.SetActive(false);
            hasValidCut = false;
            
            // Clear any existing highlights
            if (currentlyHighlighted != null)
            {
                currentlyHighlighted.ClearHighlight();
                currentlyHighlighted = null;
            }
        }
        
        // Check for left mouse click
        if (Mouse.current.leftButton.wasPressedThisFrame && hasValidCut && currentlyHighlighted != null)
        {
            // Execute the cut
            currentlyHighlighted.ExecuteCut(currentEntryPoint, currentExitPoint);
            
            // Clear the highlight after cutting
            currentlyHighlighted.ClearHighlight();
            currentlyHighlighted = null;
            hasValidCut = false;
        }
    }
    
    public void Cleanup()
    {
        // Clean up the dots
        if (entryDot != null) Object.Destroy(entryDot);
        if (exitDot != null) Object.Destroy(exitDot);
        
        // Clear any highlights
        if (currentlyHighlighted != null)
        {
            currentlyHighlighted.ClearHighlight();
        }
    }
}