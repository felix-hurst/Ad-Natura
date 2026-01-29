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
                for (int j = 0; j < pixelatedEdge.Count - 1; j++)
                {
                    pixelatedVertices.Add(pixelatedEdge[j]);
                }
            }
            else
            {
                pixelatedVertices.Add(start);
            }
        }
        
        return pixelatedVertices;
    }

    List<Vector2> PixelateCutLine(Vector2 start, Vector2 end)
    {
        List<Vector2> pixels = new List<Vector2>();

        Vector2 gridStart = SnapToPixelGrid(start);
        Vector2 gridEnd = SnapToPixelGrid(end);

        pixels.Add(gridStart);
        float deltaX = gridEnd.x - gridStart.x;
        float deltaY = gridEnd.y - gridStart.y;
        float xDirection = deltaX >= 0 ? pixelSize : -pixelSize;
        float yDirection = deltaY >= 0 ? pixelSize : -pixelSize;

        bool xDominant = Mathf.Abs(deltaX) >= Mathf.Abs(deltaY);

        if (xDominant)
        {
            int steps = Mathf.FloorToInt(Mathf.Abs(deltaX) / pixelSize);
            float x = gridStart.x;
            float y = gridStart.y;
            float param = 2 * Mathf.Abs(deltaY) - Mathf.Abs(deltaX);

            for (int i = 0; i < steps; i++)
            {
                if (param < 0)
                {
                    x += xDirection;
                    pixels.Add(new Vector2(x, y));
                    param += 2 * Mathf.Abs(deltaY);
                }
                else
                {
                    x += xDirection;
                    pixels.Add(new Vector2(x, y));
                    y += yDirection;
                    pixels.Add(new Vector2(x, y));
                    param += 2 * Mathf.Abs(deltaY) - 2 * Mathf.Abs(deltaX);
                }
            }
        }
        else
        {
            int steps = Mathf.FloorToInt(Mathf.Abs(deltaY) / pixelSize);
            float x = gridStart.x;
            float y = gridStart.y;
            float param = 2 * Mathf.Abs(deltaX) - Mathf.Abs(deltaY);

            for (int i = 0; i < steps; i++)
            {
                if (param < 0)
                {
                    y += yDirection;
                    pixels.Add(new Vector2(x, y));
                    param += 2 * Mathf.Abs(deltaX);
                }
                else
                {
                    y += yDirection;
                    pixels.Add(new Vector2(x, y));
                    x += xDirection;
                    pixels.Add(new Vector2(x, y));
                    param += 2 * Mathf.Abs(deltaX) - 2 * Mathf.Abs(deltaY);
                }
            }
        }
        if (pixels[pixels.Count - 1] != gridEnd)
        {
            pixels.Add(gridEnd);
        }

        if (showDebugPixels)
            DebugDrawPixels(pixels);

        return pixels;
    }

    Vector2 SnapToPixelGrid(Vector2 point)
    {
        return new Vector2(
            Mathf.Floor(point.x / pixelSize) * pixelSize,
            Mathf.Floor(point.y / pixelSize) * pixelSize
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