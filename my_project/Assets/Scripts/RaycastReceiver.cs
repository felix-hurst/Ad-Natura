using UnityEngine;
using System.Collections.Generic;

public class RaycastReceiver : MonoBehaviour
{
    [Header("Highlight Settings")]
    [Tooltip("Choose which piece to highlight after the cut")]
    public HighlightMode highlightMode = HighlightMode.Default;
    
    public enum HighlightMode
    {
        Default,              // Always highlights the top piece
        ClosestToGround,      // Highlights the lower piece
        FarthestFromGround    // Highlights the upper piece
    }
    
    private LineRenderer edgeLineRenderer;
    private SpriteRenderer spriteRenderer;
    private List<Vector2> currentHighlightedShape;
    
    // References to the other components
    private ObjectReshape objectReshape;
    private DebrisSpawner debrisSpawner;
    
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        
        // Get or add ObjectReshape component
        objectReshape = GetComponent<ObjectReshape>();
        if (objectReshape == null)
        {
            objectReshape = gameObject.AddComponent<ObjectReshape>();
        }
        
        // Get or add DebrisSpawner component
        debrisSpawner = GetComponent<DebrisSpawner>();
        if (debrisSpawner == null)
        {
            debrisSpawner = gameObject.AddComponent<DebrisSpawner>();
        }
    }
    
    public void HighlightCutEdges(Vector2 entryPoint, Vector2 exitPoint)
    {
        // Clear previous highlights
        ClearHighlight();
        
        // Get the current shape vertices (could be polygon from previous cuts)
        Vector2[] corners = GetCurrentShapeVertices();
        
        // Find which edges are intersected by the cut line
        List<Vector2> newShape1 = new List<Vector2>();
        List<Vector2> newShape2 = new List<Vector2>();
        
        // Add entry and exit points
        newShape1.Add(entryPoint);
        newShape1.Add(exitPoint);
        newShape2.Add(entryPoint);
        newShape2.Add(exitPoint);
        
        // Check each vertex of the current shape
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 corner = corners[i];
            
            // Determine which side of the cut line this corner is on
            float side = GetSideOfLine(entryPoint, exitPoint, corner);
            
            if (side > 0)
            {
                newShape1.Add(corner);
            }
            else
            {
                newShape2.Add(corner);
            }
        }
        
        // Sort vertices to form proper polygons
        newShape1 = SortVerticesClockwise(newShape1);
        newShape2 = SortVerticesClockwise(newShape2);
        
        // Determine which shape to highlight based on mode
        currentHighlightedShape = ChooseShapeToHighlight(newShape1, newShape2);
        
        // Draw the outline for the chosen shape
        DrawShapeOutline(currentHighlightedShape);
        
        Debug.Log($"Cut {gameObject.name} - Highlighting piece with {currentHighlightedShape.Count} vertices (Mode: {highlightMode})");
    }
    
    /// <summary>
    /// Gets the current shape vertices - either from polygon collider or sprite bounds
    /// </summary>
    Vector2[] GetCurrentShapeVertices()
    {
        // First, try to get vertices from the polygon collider (if object was already cut)
        PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
        if (polyCol != null && polyCol.points.Length > 0)
        {
            // Convert local points to world space
            Vector2[] worldVertices = new Vector2[polyCol.points.Length];
            for (int i = 0; i < polyCol.points.Length; i++)
            {
                worldVertices[i] = transform.TransformPoint(polyCol.points[i]);
            }
            return worldVertices;
        }
        
        // Otherwise, fall back to the rectangular bounds
        return GetWorldCorners();
    }
    
    public void ExecuteCut(Vector2 entryPoint, Vector2 exitPoint)
    {
        if (currentHighlightedShape == null || currentHighlightedShape.Count < 3)
        {
            Debug.LogWarning("No valid highlighted shape to cut!");
            return;
        }
        
        // Calculate the area of the highlighted shape
        float cutOffArea = ObjectReshape.CalculatePolygonArea(currentHighlightedShape);
        
        Debug.Log($"Executing cut - Cut-off area: {cutOffArea:F2}");
        
        // Use ObjectReshape to cut off the portion and get the cut shape
        List<Vector2> cutOffShape = objectReshape.CutOffPortion(entryPoint, exitPoint, currentHighlightedShape);
        
        if (cutOffShape != null && cutOffShape.Count >= 3)
        {
            // Use DebrisSpawner to spawn debris fragments
            debrisSpawner.SpawnDebris(cutOffShape, cutOffArea);
        }
        else
        {
            Debug.LogWarning("Failed to get valid cut-off shape from ObjectReshape");
        }
    }
    
    List<Vector2> ChooseShapeToHighlight(List<Vector2> shape1, List<Vector2> shape2)
    {
        // Calculate average Y position (height) of each shape
        float avgY1 = 0;
        foreach (Vector2 v in shape1)
        {
            avgY1 += v.y;
        }
        avgY1 /= shape1.Count;
        
        float avgY2 = 0;
        foreach (Vector2 v in shape2)
        {
            avgY2 += v.y;
        }
        avgY2 /= shape2.Count;
        
        // Choose based on highlight mode
        switch (highlightMode)
        {
            case HighlightMode.Default:
            case HighlightMode.FarthestFromGround:
                // Return the shape with higher average Y (top piece)
                return avgY1 > avgY2 ? shape1 : shape2;
                
            case HighlightMode.ClosestToGround:
                // Return the shape with lower average Y (bottom piece)
                return avgY1 < avgY2 ? shape1 : shape2;
                
            default:
                return avgY1 > avgY2 ? shape1 : shape2;
        }
    }
    
    Vector2[] GetWorldCorners()
    {
        Bounds bounds;
        
        // First check if we have an ObjectReshape component with a mesh renderer
        ObjectReshape objectReshape = GetComponent<ObjectReshape>();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        
        // Check for mesh renderer on child (from previous cuts)
        if (meshRenderer == null)
        {
            meshRenderer = GetComponentInChildren<MeshRenderer>();
        }
        
        // Check if we have a mesh from a previous cut
        if (meshRenderer != null && meshRenderer.enabled)
        {
            bounds = meshRenderer.bounds;
        }
        else if (spriteRenderer != null)
        {
            bounds = spriteRenderer.bounds;
        }
        else
        {
            // If we have a polygon collider, use that
            PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
            if (polyCol != null && polyCol.points.Length > 0)
            {
                // Get bounds from the polygon collider
                Vector2 minPoint = transform.TransformPoint(polyCol.points[0]);
                Vector2 maxPoint = minPoint;
                
                foreach (Vector2 localPoint in polyCol.points)
                {
                    Vector2 worldPoint = transform.TransformPoint(localPoint);
                    minPoint.x = Mathf.Min(minPoint.x, worldPoint.x);
                    minPoint.y = Mathf.Min(minPoint.y, worldPoint.y);
                    maxPoint.x = Mathf.Max(maxPoint.x, worldPoint.x);
                    maxPoint.y = Mathf.Max(maxPoint.y, worldPoint.y);
                }
                
                Vector2 boundsCenter = (minPoint + maxPoint) / 2f;
                Vector2 boundsSize = maxPoint - minPoint;
                bounds = new Bounds(boundsCenter, boundsSize);
            }
            else
            {
                // Fallback to transform-based bounds
                bounds = new Bounds(transform.position, Vector3.one);
            }
        }
        
        Vector2 center = bounds.center;
        Vector2 extents = bounds.extents;
        
        return new Vector2[]
        {
            new Vector2(center.x - extents.x, center.y - extents.y), // Bottom-left
            new Vector2(center.x + extents.x, center.y - extents.y), // Bottom-right
            new Vector2(center.x + extents.x, center.y + extents.y), // Top-right
            new Vector2(center.x - extents.x, center.y + extents.y)  // Top-left
        };
    }
    
    float GetSideOfLine(Vector2 lineStart, Vector2 lineEnd, Vector2 point)
    {
        // Cross product to determine which side of the line the point is on
        return (lineEnd.x - lineStart.x) * (point.y - lineStart.y) - 
               (lineEnd.y - lineStart.y) * (point.x - lineStart.x);
    }
    
    List<Vector2> SortVerticesClockwise(List<Vector2> vertices)
    {
        // Find centroid
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 v in vertices)
        {
            centroid += v;
        }
        centroid /= vertices.Count;
        
        // Sort by angle from centroid
        vertices.Sort((a, b) =>
        {
            float angleA = Mathf.Atan2(a.y - centroid.y, a.x - centroid.x);
            float angleB = Mathf.Atan2(b.y - centroid.y, b.x - centroid.x);
            return angleA.CompareTo(angleB);
        });
        
        return vertices;
    }
    
    void DrawShapeOutline(List<Vector2> vertices)
    {
        if (vertices.Count < 2) return;
        
        // Create a new LineRenderer if needed
        if (edgeLineRenderer == null)
        {
            GameObject lineObj = new GameObject($"{gameObject.name}_Outline");
            lineObj.transform.SetParent(transform);
            
            edgeLineRenderer = lineObj.AddComponent<LineRenderer>();
            edgeLineRenderer.startWidth = 0.08f;
            edgeLineRenderer.endWidth = 0.08f;
            edgeLineRenderer.material = new Material(Shader.Find("Unlit/Color"));
            edgeLineRenderer.material.color = Color.red;
            edgeLineRenderer.startColor = Color.red;
            edgeLineRenderer.endColor = Color.red;
            edgeLineRenderer.sortingOrder = 15;
            edgeLineRenderer.useWorldSpace = true;
            edgeLineRenderer.loop = true; // Make it a closed loop
        }
        
        // Set positions
        edgeLineRenderer.positionCount = vertices.Count;
        for (int i = 0; i < vertices.Count; i++)
        {
            edgeLineRenderer.SetPosition(i, new Vector3(vertices[i].x, vertices[i].y, 0));
        }
    }
    
    public void ClearHighlight()
    {
        if (edgeLineRenderer != null)
        {
            Destroy(edgeLineRenderer.gameObject);
            edgeLineRenderer = null;
        }
        
        currentHighlightedShape = null;
    }
    
    void OnDestroy()
    {
        ClearHighlight();
    }
}