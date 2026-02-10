

using UnityEngine;
using System.Collections.Generic;

public class DebrisSpawner : MonoBehaviour
{
    [Header("Debris Settings")]
    [Tooltip("Cellular debris system")]
    [SerializeField] private bool useCellularDebris = true;
    
    private SpriteRenderer spriteRenderer;
    private string parentMaterialTag;
    private CellularDebrisSimulation cellularDebris;
    
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        parentMaterialTag = gameObject.tag;

        if (useCellularDebris)
        {
            cellularDebris = FindObjectOfType<CellularDebrisSimulation>();
        }
    }
    
    public void SpawnDebris(List<Vector2> cutOffShape, float totalArea, string materialTag = null)
    {

        if (cutOffShape == null || cutOffShape.Count < 3)
        {
            Debug.LogWarning("❌ [DebrisSpawner] Invalid cut-off shape!");
            return;
        }

        string debrisMaterialTag = string.IsNullOrEmpty(materialTag) ? parentMaterialTag : materialTag;

        if (useCellularDebris)
        {

            if (cellularDebris != null)
            {

                cellularDebris.SpawnDebrisInRegion(cutOffShape, totalArea, debrisMaterialTag, gameObject);
                return;
            }
        }
    }

    /// <summary>
    /// Spawn debris for the entire object (used by decomposition).
    /// Uses the polygon collider shape or sprite bounds.
    /// </summary>
    public void SpawnDecompositionDebris()
    {
        List<Vector2> shape = GetObjectShape();
        if (shape == null || shape.Count < 3) return;

        float area = CalculatePolygonArea(shape);
        SpawnDebris(shape, area, parentMaterialTag);
    }

    List<Vector2> GetObjectShape()
    {
        // Try polygon collider first
        var poly = GetComponent<PolygonCollider2D>();
        if (poly != null && poly.points.Length >= 3)
        {
            List<Vector2> worldPoints = new List<Vector2>();
            foreach (Vector2 localPoint in poly.points)
            {
                worldPoints.Add(transform.TransformPoint(localPoint));
            }
            return worldPoints;
        }

        // Fallback to sprite bounds
        if (spriteRenderer != null)
        {
            Bounds b = spriteRenderer.bounds;
            return new List<Vector2>
            {
                new Vector2(b.min.x, b.min.y),
                new Vector2(b.max.x, b.min.y),
                new Vector2(b.max.x, b.max.y),
                new Vector2(b.min.x, b.max.y)
            };
        }

        return null;
    }

    float CalculatePolygonArea(List<Vector2> vertices)
    {
        float area = 0f;
        int j = vertices.Count - 1;
        for (int i = 0; i < vertices.Count; i++)
        {
            area += (vertices[j].x + vertices[i].x) * (vertices[j].y - vertices[i].y);
            j = i;
        }
        return Mathf.Abs(area / 2f);
    }
}