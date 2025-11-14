using UnityEngine;
using System.Collections.Generic;

public class PixelatedCutRenderer : MonoBehaviour
{
    [Header("Pixel Settings")]
    [SerializeField] private float pixelSize = 0.05f; // Size of each square pixel
    [SerializeField] private bool enablePixelation = true; // Toggle pixelation on/off
    [SerializeField] private bool showDebugPixels = true; // Visualize individual pixels
    
    /// <summary>
    /// Pixelates ONLY the cut line (entry to exit points), preserves all other edges
    /// </summary>
    public List<Vector2> PixelatePolygonWithCutLine(List<Vector2> vertices, Vector2 entryPoint, Vector2 exitPoint)
    {
        if (vertices == null || vertices.Count < 3 || !enablePixelation)
        {
            return vertices;
        }
        
        List<Vector2> pixelatedVertices = new List<Vector2>();
        
        // Find the indices where entry and exit points are in the vertex list
        int entryIndex = FindClosestVertexIndex(vertices, entryPoint);
        int exitIndex = FindClosestVertexIndex(vertices, exitPoint);
        
        Debug.Log($"Entry point at index {entryIndex}, Exit point at index {exitIndex}");
        
        // Process each edge of the polygon
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector2 start = vertices[i];
            Vector2 next = vertices[(i + 1) % vertices.Count];
            
            // Check if this edge is the cut line (between entry and exit points)
            bool isStartEntry = Vector2.Distance(start, entryPoint) < 0.01f;
            bool isNextExit = Vector2.Distance(next, exitPoint) < 0.01f;
            bool isStartExit = Vector2.Distance(start, exitPoint) < 0.01f;
            bool isNextEntry = Vector2.Distance(next, entryPoint) < 0.01f;
            
            bool isCutEdge = (isStartEntry && isNextExit) || (isStartExit && isNextEntry);
            
            if (isCutEdge)
            {
                // This is the cut line - pixelate it!
                Debug.Log($"Pixelating cut edge from ({start.x:F2}, {start.y:F2}) to ({next.x:F2}, {next.y:F2})");
                List<Vector2> pixelatedEdge = PixelateCutLine(start, next);
                pixelatedVertices.AddRange(pixelatedEdge);
            }
            else
            {
                // This is an original edge - preserve it exactly
                pixelatedVertices.Add(start);
            }
        }
        
        Debug.Log($"Pixelated polygon: {vertices.Count} vertices → {pixelatedVertices.Count} vertices");
        
        return pixelatedVertices;
    }
    
    /// <summary>
    /// Finds the index of the vertex closest to the given point
    /// </summary>
    int FindClosestVertexIndex(List<Vector2> vertices, Vector2 point)
    {
        int closestIndex = 0;
        float closestDistance = Vector2.Distance(vertices[0], point);
        
        for (int i = 1; i < vertices.Count; i++)
        {
            float distance = Vector2.Distance(vertices[i], point);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }
        
        return closestIndex;
    }
    
    /// <summary>
    /// Converts the cut line into a staircase pattern - ONLY horizontal and vertical moves
    /// </summary>
    List<Vector2> PixelateCutLine(Vector2 start, Vector2 end)
    {
        List<Vector2> pixels = new List<Vector2>();

        Vector2 current = SnapToPixelGrid(start);
        Vector2 target = SnapToPixelGrid(end);

        Debug.Log($"=== PIXELATE CUT LINE ===");
        Debug.Log($"Start: ({start.x:F3}, {start.y:F3}) -> Snapped: ({current.x:F3}, {current.y:F3})");
        Debug.Log($"End: ({end.x:F3}, {end.y:F3}) -> Snapped: ({target.x:F3}, {target.y:F3})");

        pixels.Add(current);

        int stepsX = Mathf.RoundToInt(Mathf.Abs(target.x - current.x) / pixelSize);
        int stepsY = Mathf.RoundToInt(Mathf.Abs(target.y - current.y) / pixelSize);

        Debug.Log($"Steps needed - X: {stepsX}, Y: {stepsY}, PixelSize: {pixelSize}");

        if (stepsX == 0 && stepsY == 0)
        {
            Debug.Log("No steps needed - start and end are same pixel");
            return pixels;
        }

        int dirX = target.x > current.x ? 1 : -1;
        int dirY = target.y > current.y ? 1 : -1;

        Debug.Log($"Direction - X: {dirX}, Y: {dirY}");

        float x = current.x;
        float y = current.y;

        float dx = Mathf.Abs(target.x - current.x);
        float dy = Mathf.Abs(target.y - current.y);

        bool xDominant = dx >= dy;
        float error = (xDominant ? dx : dy) / 2f;

        Debug.Log($"X Dominant: {xDominant}, Initial Error: {error:F3}");

        int steps = Mathf.Max(stepsX, stepsY);
        Debug.Log($"Total steps: {steps}");

        Vector2 previousPoint = current;

        for (int i = 0; i < steps; i++)
        {
            Vector2 beforeMove = new Vector2(x, y);
            
            if (xDominant)
            {
                x += dirX * pixelSize;
                error -= dy;
                if (error < 0)
                {
                    // Add the position after X move, BEFORE Y move (corner point)
                    Vector2 cornerPoint = new Vector2(x, y);
                    pixels.Add(cornerPoint);
                    previousPoint = cornerPoint; // Update previousPoint!
                    
                    y += dirY * pixelSize;
                    error += dx;
                    Debug.Log($"Step {i}: Moved X then Y - ({beforeMove.x:F3}, {beforeMove.y:F3}) -> Corner ({cornerPoint.x:F3}, {cornerPoint.y:F3}) -> ({x:F3}, {y:F3})");
                }
                else
                {
                    Debug.Log($"Step {i}: Moved X only - ({beforeMove.x:F3}, {beforeMove.y:F3}) -> ({x:F3}, {y:F3})");
                }
            }
            else
            {
                y += dirY * pixelSize;
                error -= dx;
                if (error < 0)
                {
                    // Add the position after Y move, BEFORE X move (corner point)
                    Vector2 cornerPoint = new Vector2(x, y);
                    pixels.Add(cornerPoint);
                    previousPoint = cornerPoint; // Update previousPoint!
                    
                    x += dirX * pixelSize;
                    error += dy;
                    Debug.Log($"Step {i}: Moved Y then X - ({beforeMove.x:F3}, {beforeMove.y:F3}) -> Corner ({cornerPoint.x:F3}, {cornerPoint.y:F3}) -> ({x:F3}, {y:F3})");
                }
                else
                {
                    Debug.Log($"Step {i}: Moved Y only - ({beforeMove.x:F3}, {beforeMove.y:F3}) -> ({x:F3}, {y:F3})");
                }
            }

            Vector2 newPoint = new Vector2(x, y);
            pixels.Add(newPoint);

            // Check if this creates a diagonal line
            Vector2 diff = newPoint - previousPoint;
            bool isDiagonal = Mathf.Abs(diff.x) > 0.001f && Mathf.Abs(diff.y) > 0.001f;
            
            if (isDiagonal)
            {
                Debug.LogWarning($"!!! DIAGONAL DETECTED !!! Step {i}: ({previousPoint.x:F3}, {previousPoint.y:F3}) -> ({newPoint.x:F3}, {newPoint.y:F3})");
                Debug.LogWarning($"    Diff X: {diff.x:F3}, Diff Y: {diff.y:F3}");
            }
            
            previousPoint = newPoint;
        }

        Debug.Log($"Final pixel count: {pixels.Count}");
        Debug.Log($"=========================");

        if (showDebugPixels)
            DebugDrawPixels(pixels);

        return pixels;
    }
    
    /// <summary>
    /// Snaps a point to the nearest pixel grid position
    /// </summary>
    Vector2 SnapToPixelGrid(Vector2 point)
    {
        return new Vector2(
            Mathf.Round(point.x / pixelSize) * pixelSize,
            Mathf.Round(point.y / pixelSize) * pixelSize
        );
    }
    
    /// <summary>
    /// Debug visualization of individual pixels
    /// </summary>
    void DebugDrawPixels(List<Vector2> pixels)
    {
        for (int i = 0; i < pixels.Count - 1; i++)
        {
            Debug.DrawLine(pixels[i], pixels[i + 1], Color.cyan, 2f);
        }
    }
}