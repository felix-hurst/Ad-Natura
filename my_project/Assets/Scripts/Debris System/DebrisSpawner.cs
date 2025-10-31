using UnityEngine;
using System.Collections.Generic;

public class DebrisSpawner : MonoBehaviour
{
    [Header("Debris Settings")]
    [SerializeField] private int numberOfFragments = 10;
    [SerializeField] private GameObject debrisPrefab; // Optional: Custom prefab for debris
    [SerializeField] private float explosionForceMin = 2f;
    [SerializeField] private float explosionForceMax = 5f;
    [SerializeField] private float angularVelocityMin = -180f;
    [SerializeField] private float angularVelocityMax = 180f;
    [SerializeField] private float debrisLifetime = 5f;
    [SerializeField] private float massMultiplier = 0.1f;
    
    private SpriteRenderer spriteRenderer;
    
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }
    
    /// <summary>
    /// Spawns debris fragments that fill the cut-off area
    /// </summary>
    public void SpawnDebris(List<Vector2> cutOffShape, float totalArea)
    {
        if (cutOffShape == null || cutOffShape.Count < 3)
        {
            Debug.LogWarning("Invalid cut-off shape for debris spawning!");
            return;
        }
        
        Debug.Log($"Spawning {numberOfFragments} debris pieces with total area {totalArea:F2}");
        
        // Calculate the centroid of the cut shape
        Vector2 centroid = CalculateCentroid(cutOffShape);
        
        // Calculate area per fragment
        float areaPerFragment = totalArea / numberOfFragments;
        
        // Calculate approximate size for each fragment (assuming square fragments)
        float fragmentSize = Mathf.Sqrt(areaPerFragment);
        
        // Get the bounds of the cut shape
        Bounds shapeBounds = GetShapeBounds(cutOffShape);
        
        for (int i = 0; i < numberOfFragments; i++)
        {
            GameObject fragment = CreateFragment(fragmentSize);
            
            // Position fragment within the cut shape
            Vector2 fragmentPosition = GeneratePositionInShape(cutOffShape, shapeBounds, centroid, fragmentSize);
            fragment.transform.position = new Vector3(fragmentPosition.x, fragmentPosition.y, transform.position.z);
            
            // Setup physics
            SetupFragmentPhysics(fragment, areaPerFragment, centroid, fragmentPosition);
            
            fragment.name = $"{gameObject.name}_Debris_{i}";
            
            // Auto-destroy after lifetime
            //Destroy(fragment, debrisLifetime);
        }
        
        Debug.Log($"Successfully spawned {numberOfFragments} debris fragments");
    }
    
    /// <summary>
    /// Creates a single fragment GameObject
    /// </summary>
    GameObject CreateFragment(float size)
    {
        GameObject fragment;
        
        if (debrisPrefab != null)
        {
            fragment = Instantiate(debrisPrefab);
            fragment.transform.localScale = Vector3.one * size;
        }
        else
        {
            // Create a simple sprite-based fragment for 2D
            fragment = new GameObject("Fragment");
            
            // Add a sprite renderer
            SpriteRenderer fragmentSpriteRenderer = fragment.AddComponent<SpriteRenderer>();
            
            // Create a simple white square sprite
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            fragmentSpriteRenderer.sprite = sprite;
            
            // Copy the material/color from the original object
            if (spriteRenderer != null)
            {
                fragmentSpriteRenderer.color = spriteRenderer.color;
                fragmentSpriteRenderer.sortingLayerName = spriteRenderer.sortingLayerName;
                fragmentSpriteRenderer.sortingOrder = spriteRenderer.sortingOrder;
            }
            else
            {
                fragmentSpriteRenderer.color = Color.white;
            }
            
            // Set scale
            fragment.transform.localScale = new Vector3(size, size, 1f);
        }
        
        return fragment;
    }
    
    /// <summary>
    /// Generates a random position within the cut shape
    /// </summary>
    Vector2 GeneratePositionInShape(List<Vector2> shape, Bounds bounds, Vector2 centroid, float fragmentSize)
    {
        Vector2 position;
        int maxAttempts = 10;
        int attempts = 0;
        
        // Try to find a position inside the polygon
        do
        {
            position = new Vector2(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y)
            );
            attempts++;
            
            if (IsPointInPolygon(position, shape))
            {
                return position;
            }
        }
        while (attempts < maxAttempts);
        
        // If we couldn't find a point inside, use centroid with random offset
        return centroid + Random.insideUnitCircle * (fragmentSize * 2);
    }
    
    /// <summary>
    /// Sets up physics components for a fragment
    /// </summary>
    void SetupFragmentPhysics(GameObject fragment, float area, Vector2 centroid, Vector2 position)
    {
        // Ensure we have a 2D collider
        Collider2D collider2D = fragment.GetComponent<Collider2D>();
        if (collider2D == null)
        {
            // Remove any 3D colliders first
            Collider[] colliders3D = fragment.GetComponents<Collider>();
            foreach (Collider col in colliders3D)
            {
                Destroy(col);
            }
            
            // Add a 2D box collider
            BoxCollider2D boxCollider = fragment.AddComponent<BoxCollider2D>();
            boxCollider.size = Vector2.one;
        }
        
        // Add or get Rigidbody2D
        Rigidbody2D rb = fragment.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = fragment.AddComponent<Rigidbody2D>();
        }
        
        rb.gravityScale = 1f;
        rb.mass = area * massMultiplier;
        
        // Add explosion force
        Vector2 explosionDir = (position - centroid).normalized;
        if (explosionDir.magnitude < 0.1f)
        {
            explosionDir = Random.insideUnitCircle.normalized;
        }
        
        float explosionForce = Random.Range(explosionForceMin, explosionForceMax);
        rb.linearVelocity = explosionDir * explosionForce;
        rb.angularVelocity = Random.Range(angularVelocityMin, angularVelocityMax);
    }
    
    /// <summary>
    /// Calculates the centroid of a polygon
    /// </summary>
    Vector2 CalculateCentroid(List<Vector2> vertices)
    {
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 v in vertices)
        {
            centroid += v;
        }
        return centroid / vertices.Count;
    }
    
    /// <summary>
    /// Gets the bounding box of a shape
    /// </summary>
    Bounds GetShapeBounds(List<Vector2> vertices)
    {
        if (vertices.Count == 0) return new Bounds();
        
        Vector2 min = vertices[0];
        Vector2 max = vertices[0];
        
        foreach (Vector2 v in vertices)
        {
            min.x = Mathf.Min(min.x, v.x);
            min.y = Mathf.Min(min.y, v.y);
            max.x = Mathf.Max(max.x, v.x);
            max.y = Mathf.Max(max.y, v.y);
        }
        
        Vector2 center = (min + max) / 2f;
        Vector2 size = max - min;
        
        return new Bounds(center, size);
    }
    
    /// <summary>
    /// Checks if a point is inside a polygon using ray casting algorithm
    /// </summary>
    bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        int n = polygon.Count;
        
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / 
                (polygon[j].y - polygon[i].y) + polygon[i].x))
            {
                inside = !inside;
            }
        }
        
        return inside;
    }
}