using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Handles slime mold decomposition of organic matter.
/// Detects objects on Decompose layer or with OrganicMatter component,
/// attracts slime toward them, and deals decomposition damage when slime covers them.
/// </summary>
[RequireComponent(typeof(Slime))]
[RequireComponent(typeof(SlimeMoldManager))]
public class SlimeDecomposer : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Only decompose objects on the Decompose layer (requires water to soak them first)")]
    public bool requireDecomposeLayer = true;
    [Tooltip("How often to scan for decomposable objects (seconds)")]
    [Range(0.1f, 1f)]
    public float detectionInterval = 0.2f;

    [Header("Decomposition")]
    [Tooltip("Base damage per second when fully covered by slime")]
    public float baseDamagePerSecond = 25f;
    [Tooltip("Minimum slime trail density to start dealing damage (0-1)")]
    [Range(0f, 1f)]
    public float minTrailDensity = 0.2f;
    [Tooltip("Damage multiplier based on trail density")]
    public AnimationCurve damageMultiplier = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Attraction")]
    [Tooltip("Enable attraction toward decomposable objects")]
    public bool enableAttraction = true;
    [Tooltip("Attraction strength for decomposable objects")]
    [Range(0f, 5f)]
    public float attractionStrength = 2f;
    [Tooltip("Radius of attraction field around objects inside bounds (in world units)")]
    [Range(0.5f, 10f)]
    public float internalAttractionRadius = 3f;

    [Header("Debug")]
    public bool showDebugGizmos = true;
    public bool logDecomposition = false;

    private Slime slimeSimulation;
    private SlimeMoldManager slimeManager;
    private Rect worldBounds;
    private Vector2Int resolution;

    private List<OrganicMatter> trackedObjects = new List<OrganicMatter>();
    private float detectionTimer;
    private int decomposeLayerIndex = -1;

    void Start()
    {
        slimeSimulation = GetComponent<Slime>();
        slimeManager = GetComponent<SlimeMoldManager>();

        // Don't cache bounds - get fresh each frame since they can change (auto-sync with liquid sim)
        resolution = slimeSimulation.GetSimulationResolution();

        // Find the Decompose layer
        decomposeLayerIndex = LayerMask.NameToLayer("Decompose");
        if (decomposeLayerIndex == -1)
        {
            Debug.LogWarning("[SlimeDecomposer] 'Decompose' layer not found. Create it in Project Settings > Tags and Layers.");
        }

        // Initial scan
        ScanForDecomposableObjects();
    }

    void Update()
    {
        // Periodic detection scan
        detectionTimer += Time.deltaTime;
        if (detectionTimer >= detectionInterval)
        {
            detectionTimer = 0f;
            ScanForDecomposableObjects();
        }

        // Process decomposition damage
        ProcessDecomposition();
    }

    void ScanForDecomposableObjects()
    {
        trackedObjects.Clear();

        // Get fresh bounds each scan (may change due to auto-sync with liquid sim)
        worldBounds = slimeSimulation.GetWorldBounds();

        // Find all OrganicMatter components
        OrganicMatter[] allOrganic = FindObjectsByType<OrganicMatter>(FindObjectsSortMode.None);

        if (logDecomposition)
        {
            Debug.Log($"[SlimeDecomposer] SlimeBounds: X({worldBounds.xMin:F1} to {worldBounds.xMax:F1}) Y({worldBounds.yMin:F1} to {worldBounds.yMax:F1}), Found {allOrganic.Length} OrganicMatter");
        }

        foreach (OrganicMatter organic in allOrganic)
        {
            if (organic == null) continue;

            // Check if object's collider bounds overlap with slime bounds (not just center point)
            Rect objectBounds = GetObjectBounds(organic.gameObject);
            bool overlaps = worldBounds.Overlaps(objectBounds);

            if (logDecomposition)
            {
                Debug.Log($"[SlimeDecomposer] {organic.name}: ObjectBounds X({objectBounds.xMin:F1} to {objectBounds.xMax:F1}) Y({objectBounds.yMin:F1} to {objectBounds.yMax:F1}) - overlaps={overlaps}");
            }

            if (!overlaps)
            {
                continue;
            }

            // Check if on Decompose layer
            bool onDecomposeLayer = decomposeLayerIndex != -1 && organic.gameObject.layer == decomposeLayerIndex;

            if (requireDecomposeLayer && !onDecomposeLayer)
            {
                if (logDecomposition)
                {
                    Debug.Log($"[SlimeDecomposer] REJECTED {organic.name}: layer={organic.gameObject.layer} not Decompose({decomposeLayerIndex})");
                }
                continue;
            }

            trackedObjects.Add(organic);
            if (logDecomposition)
            {
                // Calculate overlap region for logging
                float overlapMinX = Mathf.Max(objectBounds.xMin, worldBounds.xMin);
                float overlapMaxX = Mathf.Min(objectBounds.xMax, worldBounds.xMax);
                float overlapMinY = Mathf.Max(objectBounds.yMin, worldBounds.yMin);
                float overlapMaxY = Mathf.Min(objectBounds.yMax, worldBounds.yMax);
                Vector2 attractionPoint = new Vector2((overlapMinX + overlapMaxX) * 0.5f, (overlapMinY + overlapMaxY) * 0.5f);
                Debug.Log($"[SlimeDecomposer] TRACKING {organic.name} - overlap X({overlapMinX:F1} to {overlapMaxX:F1}) Y({overlapMinY:F1} to {overlapMaxY:F1}) -> attraction at ({attractionPoint.x:F1},{attractionPoint.y:F1})");
            }
        }
    }

    Rect GetObjectBounds(GameObject obj)
    {
        // Try collider first
        var col = obj.GetComponent<Collider2D>();
        if (col != null)
        {
            return new Rect(col.bounds.min.x, col.bounds.min.y, col.bounds.size.x, col.bounds.size.y);
        }

        // Try sprite renderer
        var sr = obj.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            return new Rect(sr.bounds.min.x, sr.bounds.min.y, sr.bounds.size.x, sr.bounds.size.y);
        }

        // Fallback to point at transform position
        Vector2 pos = obj.transform.position;
        return new Rect(pos.x, pos.y, 0.1f, 0.1f);
    }

    /// <summary>
    /// For polygon colliders, find the centroid of vertices inside the bounds.
    /// This handles rotated objects better than AABB overlap.
    /// </summary>
    Vector2? GetPolygonOverlapCenter(GameObject obj, Rect bounds)
    {
        var poly = obj.GetComponent<PolygonCollider2D>();
        if (poly == null) return null;

        // Get polygon points in world space
        List<Vector2> insidePoints = new List<Vector2>();
        foreach (Vector2 localPoint in poly.points)
        {
            Vector2 worldPoint = obj.transform.TransformPoint(localPoint);
            if (bounds.Contains(worldPoint))
            {
                insidePoints.Add(worldPoint);
            }
        }

        if (insidePoints.Count == 0) return null;

        // Return centroid of inside points
        Vector2 centroid = Vector2.zero;
        foreach (var p in insidePoints)
        {
            centroid += p;
        }
        return centroid / insidePoints.Count;
    }

    void ProcessDecomposition()
    {
        if (trackedObjects.Count == 0) return;

        Texture2D trailTexture = slimeSimulation.GetTrailTexture();
        if (trailTexture == null) return;

        foreach (OrganicMatter organic in trackedObjects)
        {
            if (organic == null) continue;

            // Get sample points - use polygon vertices inside bounds, or fallback to AABB overlap
            List<Vector2> samplePoints = GetSamplePointsInBounds(organic.gameObject, worldBounds);

            if (samplePoints.Count == 0) continue;

            // Sample trail density at all points and use the maximum
            float maxDensity = 0f;
            foreach (Vector2 point in samplePoints)
            {
                float density = SampleTrailDensity(point, trailTexture);
                maxDensity = Mathf.Max(maxDensity, density);
            }

            if (maxDensity >= minTrailDensity)
            {
                // Calculate damage
                float normalizedDensity = Mathf.InverseLerp(minTrailDensity, 1f, maxDensity);
                float multiplier = damageMultiplier.Evaluate(normalizedDensity);
                float damage = baseDamagePerSecond * multiplier * Time.deltaTime;

                // Apply damage
                organic.TakeDecompositionDamage(damage);

                if (logDecomposition)
                {
                    Debug.Log($"[SlimeDecomposer] {organic.gameObject.name}: density={maxDensity:F2}, damage={damage:F2}");
                }
            }
        }
    }

    List<Vector2> GetSamplePointsInBounds(GameObject obj, Rect bounds)
    {
        List<Vector2> points = new List<Vector2>();

        // Try polygon collider first
        var poly = obj.GetComponent<PolygonCollider2D>();
        if (poly != null)
        {
            foreach (Vector2 localPoint in poly.points)
            {
                Vector2 worldPoint = obj.transform.TransformPoint(localPoint);
                if (bounds.Contains(worldPoint))
                {
                    points.Add(worldPoint);
                }
            }
        }

        // If no polygon points, use center if inside bounds
        if (points.Count == 0)
        {
            Vector2 center = obj.transform.position;
            if (bounds.Contains(center))
            {
                points.Add(center);
            }
        }

        return points;
    }

    float SampleTrailDensity(Vector2 worldPos, Texture2D trailTexture)
    {
        // Convert world position to texture coordinates
        float normX = (worldPos.x - worldBounds.x) / worldBounds.width;
        float normY = (worldPos.y - worldBounds.y) / worldBounds.height;

        // Clamp to valid range
        normX = Mathf.Clamp01(normX);
        normY = Mathf.Clamp01(normY);

        int texX = Mathf.FloorToInt(normX * (resolution.x - 1));
        int texY = Mathf.FloorToInt(normY * (resolution.y - 1));

        // Sample the pixel
        Color pixel = trailTexture.GetPixel(texX, texY);

        // Return brightness as density (trail color intensity)
        return (pixel.r + pixel.g + pixel.b) / 3f;
    }

    /// <summary>
    /// Get attraction values for decomposable objects (called by SlimeMoldManager)
    /// Creates radial attraction fields around OrganicMatter objects inside bounds.
    /// </summary>
    public void GetDecomposableAttractions(Color[] pixelBuffer, int w, int h, Rect bounds)
    {
        if (!enableAttraction) return;

        foreach (OrganicMatter organic in trackedObjects)
        {
            if (organic == null) continue;

            // Try polygon-based detection first (more accurate for rotated objects)
            Vector2? polyCenter = GetPolygonOverlapCenter(organic.gameObject, bounds);
            Vector2 attractionPoint;

            if (polyCenter.HasValue)
            {
                attractionPoint = polyCenter.Value;
            }
            else
            {
                // Fallback to AABB overlap
                Rect objectBounds = GetObjectBounds(organic.gameObject);
                float overlapMinX = Mathf.Max(objectBounds.xMin, bounds.xMin);
                float overlapMaxX = Mathf.Min(objectBounds.xMax, bounds.xMax);
                float overlapMinY = Mathf.Max(objectBounds.yMin, bounds.yMin);
                float overlapMaxY = Mathf.Min(objectBounds.yMax, bounds.yMax);

                if (overlapMinX >= overlapMaxX || overlapMinY >= overlapMaxY) continue;

                attractionPoint = new Vector2(
                    (overlapMinX + overlapMaxX) * 0.5f,
                    (overlapMinY + overlapMaxY) * 0.5f
                );
            }

            // Clamp to bounds
            attractionPoint.x = Mathf.Clamp(attractionPoint.x, bounds.xMin, bounds.xMax);
            attractionPoint.y = Mathf.Clamp(attractionPoint.y, bounds.yMin, bounds.yMax);

            // Convert to texture coordinates
            float normX = (attractionPoint.x - bounds.x) / bounds.width;
            float normY = (attractionPoint.y - bounds.y) / bounds.height;
            int centerX = Mathf.Clamp(Mathf.RoundToInt(normX * w), 0, w - 1);
            int centerY = Mathf.Clamp(Mathf.RoundToInt(normY * h), 0, h - 1);

            // Convert world-unit radius to pixel radius
            float pixelsPerWorldUnitX = w / bounds.width;
            float pixelsPerWorldUnitY = h / bounds.height;
            int radiusX = Mathf.RoundToInt(internalAttractionRadius * pixelsPerWorldUnitX);
            int radiusY = Mathf.RoundToInt(internalAttractionRadius * pixelsPerWorldUnitY);

            if (logDecomposition)
            {
                Debug.Log($"[SlimeDecomposer] Attraction at tex({centerX},{centerY}) radius({radiusX}x{radiusY}) for {organic.name}");
            }

            // Create radial attraction field
            for (int dy = -radiusY; dy <= radiusY; dy++)
            {
                int texY = centerY + dy;
                if (texY < 0 || texY >= h) continue;

                for (int dx = -radiusX; dx <= radiusX; dx++)
                {
                    int texX = centerX + dx;
                    if (texX < 0 || texX >= w) continue;

                    // Normalized distance (0 at center, 1 at edge)
                    float normDistX = (float)dx / radiusX;
                    float normDistY = (float)dy / radiusY;
                    float normDist = Mathf.Sqrt(normDistX * normDistX + normDistY * normDistY);
                    if (normDist > 1f) continue;

                    // Attraction falls off with distance
                    float falloff = 1f - normDist;
                    float attraction = attractionStrength * falloff;

                    int idx = texY * w + texX;
                    pixelBuffer[idx].r = Mathf.Min(1f, pixelBuffer[idx].r + attraction);
                }
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        if (!Application.isPlaying) return;
        if (slimeSimulation == null) return;

        // Get fresh bounds for gizmo drawing
        Rect bounds = slimeSimulation.GetWorldBounds();

        // Draw slime bounds for reference
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireCube(bounds.center, new Vector3(bounds.width, bounds.height, 0));

        // Draw tracked objects with attraction radius
        foreach (OrganicMatter organic in trackedObjects)
        {
            if (organic == null) continue;

            // Use polygon-based center if available
            Vector2? polyCenter = GetPolygonOverlapCenter(organic.gameObject, bounds);
            Vector2 attractionPoint;

            if (polyCenter.HasValue)
            {
                attractionPoint = polyCenter.Value;

                // Draw the polygon vertices that are inside bounds
                var poly = organic.GetComponent<PolygonCollider2D>();
                if (poly != null)
                {
                    Gizmos.color = Color.cyan;
                    foreach (Vector2 localPoint in poly.points)
                    {
                        Vector2 worldPoint = organic.transform.TransformPoint(localPoint);
                        if (bounds.Contains(worldPoint))
                        {
                            Gizmos.DrawWireSphere(worldPoint, 0.15f);
                        }
                    }
                }
            }
            else
            {
                // Fallback AABB
                Rect objectBounds = GetObjectBounds(organic.gameObject);
                float overlapMinX = Mathf.Max(objectBounds.xMin, bounds.xMin);
                float overlapMaxX = Mathf.Min(objectBounds.xMax, bounds.xMax);
                float overlapMinY = Mathf.Max(objectBounds.yMin, bounds.yMin);
                float overlapMaxY = Mathf.Min(objectBounds.yMax, bounds.yMax);

                if (overlapMinX >= overlapMaxX || overlapMinY >= overlapMaxY) continue;

                attractionPoint = new Vector2(
                    (overlapMinX + overlapMaxX) * 0.5f,
                    (overlapMinY + overlapMaxY) * 0.5f
                );

                // Draw overlap region for AABB fallback
                Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
                Vector3 overlapCenter = new Vector3((overlapMinX + overlapMaxX) * 0.5f, (overlapMinY + overlapMaxY) * 0.5f, 0);
                Vector3 overlapSize = new Vector3(overlapMaxX - overlapMinX, overlapMaxY - overlapMinY, 0);
                Gizmos.DrawWireCube(overlapCenter, overlapSize);
            }

            // Draw attraction point
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(attractionPoint, 0.3f);

            // Draw attraction radius
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.5f);
            Gizmos.DrawWireSphere(attractionPoint, internalAttractionRadius);
        }
    }
}
