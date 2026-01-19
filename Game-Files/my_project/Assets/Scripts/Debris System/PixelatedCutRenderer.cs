using UnityEngine;
using System.Collections.Generic;

public class PixelatedCutRenderer : MonoBehaviour
{
    [Header("Pixel Settings")]
    [SerializeField] private float pixelSize = 0.05f;
    [SerializeField] private bool enablePixelation = true;
    [SerializeField] private bool showDebugPixels = true;

    public List<Vector2> PixelatePolygonWithCutLine(List<Vector2> vertices, Vector2 entryPoint, Vector2 exitPoint)
    {
        if (vertices == null || vertices.Count < 3 || !enablePixelation)
        {
            return vertices;
        }
        
        List<Vector2> pixelatedVertices = new List<Vector2>();
        int entryIndex = FindClosestVertexIndex(vertices, entryPoint);
        int exitIndex = FindClosestVertexIndex(vertices, exitPoint);

        for (int i = 0; i < vertices.Count; i++)
        {
            Vector2 start = vertices[i];
            Vector2 next = vertices[(i + 1) % vertices.Count];
            bool isStartEntry = Vector2.Distance(start, entryPoint) < 0.01f;
            bool isNextExit = Vector2.Distance(next, exitPoint) < 0.01f;
            bool isStartExit = Vector2.Distance(start, exitPoint) < 0.01f;
            bool isNextEntry = Vector2.Distance(next, entryPoint) < 0.01f;
            
            bool isCutEdge = (isStartEntry && isNextExit) || (isStartExit && isNextEntry);
            
            if (isCutEdge)
            {
                List<Vector2> pixelatedEdge = PixelateCutLine(start, next);
                pixelatedVertices.AddRange(pixelatedEdge);
            }
            else
            {
                pixelatedVertices.Add(start);
            }
        }
        
        return pixelatedVertices;
    }

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

    List<Vector2> PixelateCutLine(Vector2 start, Vector2 end)
    {
        List<Vector2> pixels = new List<Vector2>();

        Vector2 current = SnapToPixelGrid(start);
        Vector2 target = SnapToPixelGrid(end);

        pixels.Add(current);

        int stepsX = Mathf.RoundToInt(Mathf.Abs(target.x - current.x) / pixelSize);
        int stepsY = Mathf.RoundToInt(Mathf.Abs(target.y - current.y) / pixelSize);

        if (stepsX == 0 && stepsY == 0)
        {
            return pixels;
        }

        int dirX = target.x > current.x ? 1 : -1;
        int dirY = target.y > current.y ? 1 : -1;

        float x = current.x;
        float y = current.y;

        float dx = Mathf.Abs(target.x - current.x);
        float dy = Mathf.Abs(target.y - current.y);

        bool xDominant = dx >= dy;
        float error = (xDominant ? dx : dy) / 2f;

        int steps = Mathf.Max(stepsX, stepsY);

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
                    Vector2 cornerPoint = new Vector2(x, y);
                    pixels.Add(cornerPoint);
                    previousPoint = cornerPoint;
                    
                    y += dirY * pixelSize;
                    error += dx;
                }
            }
            else
            {
                y += dirY * pixelSize;
                error -= dx;
                if (error < 0)
                {
                    Vector2 cornerPoint = new Vector2(x, y);
                    pixels.Add(cornerPoint);
                    previousPoint = cornerPoint;
                    
                    x += dirX * pixelSize;
                    error += dy;
                }
            }

            Vector2 newPoint = new Vector2(x, y);
            pixels.Add(newPoint);

            // Check if this creates a diagonal line
            Vector2 diff = newPoint - previousPoint;
            bool isDiagonal = Mathf.Abs(diff.x) > 0.001f && Mathf.Abs(diff.y) > 0.001f;
            previousPoint = newPoint;
        }
        if (showDebugPixels)
            DebugDrawPixels(pixels);

        return pixels;
    }

    Vector2 SnapToPixelGrid(Vector2 point)
    {
        return new Vector2(
            Mathf.Round(point.x / pixelSize) * pixelSize,
            Mathf.Round(point.y / pixelSize) * pixelSize
        );
    }

    void DebugDrawPixels(List<Vector2> pixels)
    {
        for (int i = 0; i < pixels.Count - 1; i++)
        {
            Debug.DrawLine(pixels[i], pixels[i + 1], Color.cyan, 2f);
        }
    }
}