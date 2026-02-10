using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class FallDamageHandler : MonoBehaviour
{
    [Header("Fall Damage Settings")]
    [Tooltip("Minimum velocity to trigger fall damage (units/second)")]
    [SerializeField] private float minDamageVelocity = 5f;
    
    [Tooltip("Velocity at which maximum damage occurs")]
    [SerializeField] private float maxDamageVelocity = 20f;
    
    [Tooltip("Minimum debris ratio (at minimum velocity)")]
    [Range(0f, 0.3f)]
    [SerializeField] private float minDebrisRatio = 0.05f;
    
    [Tooltip("Maximum debris ratio (at maximum velocity)")]
    [Range(0.1f, 0.5f)]
    [SerializeField] private float maxDebrisRatio = 0.3f;
    
    [Header("Mass Influence")]
    [Tooltip("How much mass affects damage (higher = more damage from heavier objects)")]
    [Range(0f, 2f)]
    [SerializeField] private float massInfluence = 0.5f;
    
    [Tooltip("Reference mass for calculations (objects heavier than this take more damage)")]
    [SerializeField] private float referenceMass = 1f;
    
    [Header("Impact Detection")]
    [Tooltip("Layer mask for ground/surfaces that cause damage")]
    [SerializeField] private LayerMask groundLayer = ~0;
    
    [Tooltip("Minimum time between damage events (prevents multiple impacts)")]
    [SerializeField] private float damageCooldown = 0.5f;
    
    [Header("Debris Settings")]
    [Tooltip("How far from impact point to break off debris")]
    [SerializeField] private float impactRadius = 0.5f;
    
    [Tooltip("Number of debris fragments to spawn")]
    [SerializeField] private int debrisFragmentCount = 5;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool showDebugLogs = true;
    
    private Rigidbody2D rb;
    private PolygonCollider2D polyCollider;
    private ObjectReshape objectReshape;
    private DebrisSpawner debrisSpawner;
    private float lastDamageTime;
    private Vector2 lastImpactPoint;
    private Vector2 lastImpactNormal;
    
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        polyCollider = GetComponent<PolygonCollider2D>();
        
        // Get or add ObjectReshape
        objectReshape = GetComponent<ObjectReshape>();
        if (objectReshape == null)
        {
            objectReshape = gameObject.AddComponent<ObjectReshape>();
        }
        
        // Get or add DebrisSpawner
        debrisSpawner = GetComponent<DebrisSpawner>();
        if (debrisSpawner == null)
        {
            debrisSpawner = gameObject.AddComponent<DebrisSpawner>();
        }
    }
    
    void OnCollisionEnter2D(Collision2D collision)
    {
        // Check if we hit the ground layer
        if (((1 << collision.gameObject.layer) & groundLayer) == 0)
        {
            return;
        }
        
        // Check cooldown
        if (Time.time - lastDamageTime < damageCooldown)
        {
            return;
        }
        
        // Get impact velocity (magnitude of velocity at collision)
        float impactVelocity = collision.relativeVelocity.magnitude;
        
        // Check if velocity is high enough to cause damage
        if (impactVelocity < minDamageVelocity)
        {
            return;
        }
        
        // Get the first contact point (main impact point)
        if (collision.contactCount == 0)
        {
            return;
        }
        
        ContactPoint2D contact = collision.GetContact(0);
        Vector2 impactPoint = contact.point;
        Vector2 impactNormal = contact.normal;
        
        lastImpactPoint = impactPoint;
        lastImpactNormal = impactNormal;
        
        // Calculate damage based on velocity and mass
        float damage = CalculateDamage(impactVelocity, rb.mass);
        
        if (showDebugLogs)
        {
            Debug.Log($"{gameObject.name} impact: velocity={impactVelocity:F2}, mass={rb.mass:F2}, damage={damage:F3}");
        }
        
        // Apply fall damage
        ApplyFallDamage(impactPoint, impactNormal, damage);
        
        lastDamageTime = Time.time;
    }
    
    /// <summary>
    /// Calculates damage ratio based on impact velocity and mass
    /// </summary>
    float CalculateDamage(float velocity, float mass)
    {
        // Normalize velocity to 0-1 range
        float velocityFactor = Mathf.InverseLerp(minDamageVelocity, maxDamageVelocity, velocity);
        velocityFactor = Mathf.Clamp01(velocityFactor);
        
        // Calculate mass factor (heavier objects take more damage)
        float massFactor = 1f + ((mass / referenceMass) - 1f) * massInfluence;
        massFactor = Mathf.Max(0.5f, massFactor); // Minimum 0.5x, no upper limit
        
        // Calculate debris ratio
        float debrisRatio = Mathf.Lerp(minDebrisRatio, maxDebrisRatio, velocityFactor);
        debrisRatio *= massFactor;
        
        // Cap at maximum
        debrisRatio = Mathf.Min(debrisRatio, maxDebrisRatio * 1.5f); // Allow some overflow for very heavy objects
        
        return debrisRatio;
    }
    
    /// <summary>
    /// Applies fall damage by breaking off a chunk at the impact point
    /// </summary>
    void ApplyFallDamage(Vector2 impactPoint, Vector2 impactNormal, float debrisRatio)
    {
        // Get material tag
        string materialTag = gameObject.tag;
        if (string.IsNullOrEmpty(materialTag) || materialTag == "Untagged")
        {
            materialTag = gameObject.name;
        }
        
        // Get current shape vertices
        Vector2[] currentVertices = GetCurrentShapeVertices();
        if (currentVertices == null || currentVertices.Length < 3)
        {
            if (showDebugLogs)
            {
                Debug.LogWarning($"{gameObject.name}: Cannot apply fall damage - invalid shape");
            }
            return;
        }
        
        // Calculate total object area
        List<Vector2> currentShape = new List<Vector2>(currentVertices);
        float totalArea = ObjectReshape.CalculatePolygonArea(currentShape);
        
        // Calculate debris area
        float debrisArea = totalArea * debrisRatio;
        
        if (showDebugLogs)
        {
            Debug.Log($"{gameObject.name}: Breaking off {debrisRatio:P1} ({debrisArea:F2} area) at impact point");
        }
        
        // Find the impact region (vertices near impact point)
        List<Vector2> impactRegion = FindImpactRegion(currentVertices, impactPoint, impactNormal);
        
        if (impactRegion.Count < 3)
        {
            if (showDebugLogs)
            {
                Debug.LogWarning($"{gameObject.name}: Impact region too small, spawning debris only");
            }
            // Just spawn some debris at impact point
            SpawnImpactDebris(impactPoint, debrisArea, materialTag);
            return;
        }
        
        // Calculate impact region area
        float impactRegionArea = ObjectReshape.CalculatePolygonArea(impactRegion);
        
        // If impact region is smaller than debris area, just use impact region
        if (impactRegionArea < debrisArea)
        {
            debrisArea = impactRegionArea;
        }
        
        // Create a cut line that separates the impact region
        Vector2 cutStart, cutEnd;
        if (CreateImpactCutLine(currentVertices, impactPoint, impactNormal, out cutStart, out cutEnd))
        {
            // Use ObjectReshape to cut off the damaged portion
            List<Vector2> damagedShape = GetShapeOnSideOfLine(currentVertices, cutStart, cutEnd, impactPoint);
            
            if (damagedShape != null && damagedShape.Count >= 3)
            {
                // Apply the cut using ObjectReshape
                List<Vector2> cutOffShape = objectReshape.CutOffPortion(cutStart, cutEnd, damagedShape);
                
                if (cutOffShape != null && cutOffShape.Count >= 3)
                {
                    // ===== FIX: Calculate the ACTUAL area of the cut-off shape =====
                    float actualCutOffArea = ObjectReshape.CalculatePolygonArea(cutOffShape);
                    
                    // Use the smaller of: intended debris area OR actual cut-off area
                    // This prevents spawning more debris than what was actually cut off
                    float debrisToSpawn = Mathf.Min(debrisArea, actualCutOffArea);
                    
                    // Spawn debris from the cut-off portion
                    debrisSpawner.SpawnDebris(cutOffShape, debrisToSpawn, materialTag);
                    
                    if (showDebugLogs)
                    {
                        Debug.Log($"{gameObject.name}: Successfully broke off impact damage (intended: {debrisArea:F2}, actual: {actualCutOffArea:F2}, spawning: {debrisToSpawn:F2})");
                    }
                    // ===== END FIX =====
                }
                else
                {
                    // Fallback: spawn debris at impact point
                    SpawnImpactDebris(impactPoint, debrisArea, materialTag);
                }
            }
            else
            {
                // Fallback: spawn debris at impact point
                SpawnImpactDebris(impactPoint, debrisArea, materialTag);
            }
        }
        else
        {
            // Fallback: spawn debris at impact point
            SpawnImpactDebris(impactPoint, debrisArea, materialTag);
        }
    }
    
    /// <summary>
    /// Spawns debris at impact point without cutting
    /// </summary>
    void SpawnImpactDebris(Vector2 impactPoint, float debrisArea, string materialTag)
    {
        // Create a small region around impact point
        List<Vector2> debrisRegion = new List<Vector2>();
        
        float radius = Mathf.Sqrt(debrisArea) * 0.5f;
        int segments = 6;
        
        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector2 point = impactPoint + new Vector2(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius
            );
            debrisRegion.Add(point);
        }
        
        // Spawn debris
        debrisSpawner.SpawnDebris(debrisRegion, debrisArea, materialTag);
        
        if (showDebugLogs)
        {
            Debug.Log($"{gameObject.name}: Spawned impact debris (fallback mode)");
        }
    }
    
    /// <summary>
    /// Finds vertices near the impact point
    /// </summary>
    List<Vector2> FindImpactRegion(Vector2[] vertices, Vector2 impactPoint, Vector2 impactNormal)
    {
        List<Vector2> region = new List<Vector2>();
        
        foreach (Vector2 vertex in vertices)
        {
            float distance = Vector2.Distance(vertex, impactPoint);
            
            // Check if vertex is within impact radius
            if (distance <= impactRadius)
            {
                // Also check if vertex is on the impact side (not opposite side)
                Vector2 toVertex = (vertex - impactPoint).normalized;
                float alignment = Vector2.Dot(toVertex, -impactNormal);
                
                if (alignment > -0.5f) // Within 120 degrees of impact direction
                {
                    region.Add(vertex);
                }
            }
        }
        
        return region;
    }
    
    /// <summary>
    /// Creates a cut line that separates the impact region from the rest
    /// </summary>
    bool CreateImpactCutLine(Vector2[] vertices, Vector2 impactPoint, Vector2 impactNormal, out Vector2 cutStart, out Vector2 cutEnd)
    {
        // Create a line perpendicular to impact normal, passing through impact point
        Vector2 perpendicular = new Vector2(-impactNormal.y, impactNormal.x);
        
        // Offset the line slightly into the object
        Vector2 lineCenter = impactPoint - impactNormal * (impactRadius * 0.3f);
        
        // Create cut line endpoints
        cutStart = lineCenter - perpendicular * impactRadius * 2f;
        cutEnd = lineCenter + perpendicular * impactRadius * 2f;
        
        // Validate that the cut line intersects the object
        int intersections = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 v1 = vertices[i];
            Vector2 v2 = vertices[(i + 1) % vertices.Length];
            
            if (LineSegmentsIntersect(cutStart, cutEnd, v1, v2))
            {
                intersections++;
            }
        }
        
        return intersections >= 2; // Need at least 2 intersections to create a valid cut
    }
    
    /// <summary>
    /// Gets all vertices on one side of a cut line (the side containing the test point)
    /// </summary>
    List<Vector2> GetShapeOnSideOfLine(Vector2[] vertices, Vector2 lineStart, Vector2 lineEnd, Vector2 testPoint)
    {
        List<Vector2> shape = new List<Vector2>();
        
        // Determine which side of the line the test point is on
        float testSide = GetSideOfLine(lineStart, lineEnd, testPoint);
        
        // Add the cut line points
        shape.Add(lineStart);
        shape.Add(lineEnd);
        
        // Add vertices on the same side as test point
        foreach (Vector2 vertex in vertices)
        {
            float side = GetSideOfLine(lineStart, lineEnd, vertex);
            
            if (Mathf.Sign(side) == Mathf.Sign(testSide))
            {
                shape.Add(vertex);
            }
        }
        
        // Sort vertices clockwise
        return SortVerticesClockwise(shape);
    }
    
    /// <summary>
    /// Gets current shape vertices from collider or bounds
    /// </summary>
    Vector2[] GetCurrentShapeVertices()
    {
        // Try to get from polygon collider first
        if (polyCollider != null && polyCollider.points.Length > 0)
        {
            Vector2[] worldVertices = new Vector2[polyCollider.points.Length];
            for (int i = 0; i < polyCollider.points.Length; i++)
            {
                worldVertices[i] = transform.TransformPoint(polyCollider.points[i]);
            }
            return worldVertices;
        }
        
        // Fallback to bounds
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            Bounds bounds = spriteRenderer.bounds;
            return GetCornersFromBounds(bounds);
        }
        
        // Check for mesh renderer (from previous cuts)
        MeshRenderer meshRenderer = GetComponentInChildren<MeshRenderer>();
        if (meshRenderer != null)
        {
            Bounds bounds = meshRenderer.bounds;
            return GetCornersFromBounds(bounds);
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets 4 corners from bounds
    /// </summary>
    Vector2[] GetCornersFromBounds(Bounds bounds)
    {
        Vector2 center = bounds.center;
        Vector2 extents = bounds.extents;
        
        return new Vector2[]
        {
            new Vector2(center.x - extents.x, center.y - extents.y),
            new Vector2(center.x + extents.x, center.y - extents.y),
            new Vector2(center.x + extents.x, center.y + extents.y),
            new Vector2(center.x - extents.x, center.y + extents.y)
        };
    }
    
    /// <summary>
    /// Determines which side of a line a point is on
    /// </summary>
    float GetSideOfLine(Vector2 lineStart, Vector2 lineEnd, Vector2 point)
    {
        return (lineEnd.x - lineStart.x) * (point.y - lineStart.y) - 
               (lineEnd.y - lineStart.y) * (point.x - lineStart.x);
    }
    
    /// <summary>
    /// Sorts vertices in clockwise order around centroid
    /// </summary>
    List<Vector2> SortVerticesClockwise(List<Vector2> vertices)
    {
        if (vertices.Count < 3) return vertices;
        
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 v in vertices)
        {
            centroid += v;
        }
        centroid /= vertices.Count;
        
        vertices.Sort((a, b) =>
        {
            float angleA = Mathf.Atan2(a.y - centroid.y, a.x - centroid.x);
            float angleB = Mathf.Atan2(b.y - centroid.y, b.x - centroid.x);
            return angleA.CompareTo(angleB);
        });
        
        return vertices;
    }
    
    /// <summary>
    /// Checks if two line segments intersect
    /// </summary>
    bool LineSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        float d = (p2.x - p1.x) * (p4.y - p3.y) - (p2.y - p1.y) * (p4.x - p3.x);
        
        if (Mathf.Abs(d) < 0.0001f)
        {
            return false; // Lines are parallel
        }
        
        float t = ((p3.x - p1.x) * (p4.y - p3.y) - (p3.y - p1.y) * (p4.x - p3.x)) / d;
        float u = ((p3.x - p1.x) * (p2.y - p1.y) - (p3.y - p1.y) * (p2.x - p1.x)) / d;
        
        return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
    }
    
    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        
        // Draw impact point and normal
        if (Time.time - lastDamageTime < 2f) // Show for 2 seconds after impact
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(lastImpactPoint, 0.1f);
            
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(lastImpactPoint, lastImpactPoint + lastImpactNormal * 0.5f);
            
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(lastImpactPoint, impactRadius);
        }
    }
}