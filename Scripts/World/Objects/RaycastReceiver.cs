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
    
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }
    
    public void HighlightCutEdges(Vector2 entryPoint, Vector2 exitPoint)
    {
        // Clear previous highlights
        ClearHighlight();
        
        // Get the 4 corners of the rectangle in world space
        Vector2[] corners = GetWorldCorners();
        
        // Find which edges are intersected by the cut line
        List<Vector2> newShape1 = new List<Vector2>();
        List<Vector2> newShape2 = new List<Vector2>();
        
        // Add entry and exit points
        newShape1.Add(entryPoint);
        newShape1.Add(exitPoint);
        newShape2.Add(entryPoint);
        newShape2.Add(exitPoint);
        
        // Check each edge of the rectangle
        for (int i = 0; i < 4; i++)
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
        List<Vector2> shapeToHighlight = ChooseShapeToHighlight(newShape1, newShape2);
        
        // Draw the outline for the chosen shape
        DrawShapeOutline(shapeToHighlight);
        
        Debug.Log($"Cut {gameObject.name} - Highlighting piece with {shapeToHighlight.Count} vertices (Mode: {highlightMode})");
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
        Bounds bounds = spriteRenderer.bounds;
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
    }
    
    void OnDestroy()
    {
        ClearHighlight();
    }
}