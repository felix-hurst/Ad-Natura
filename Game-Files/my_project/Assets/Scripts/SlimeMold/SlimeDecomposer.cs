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
    public float minTrailDensity = 0.001f;
    [Tooltip("Damage multiplier based on trail density")]
    public AnimationCurve damageMultiplier = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Attraction")]
    [Tooltip("Enable attraction toward decomposable objects")]
    public bool enableAttraction = true;
    [Tooltip("Attraction strength for decomposable objects")]
    [Range(0f, 5f)]
    public float attractionStrength = 2f;
    [Tooltip("Radius of attraction field around objects inside bounds (in world units)")]
    [Range(0.05f, 10f)]
    public float internalAttractionRadius = 3f;

    [Header("Debug")]
    public bool showDebugGizmos = true;
    public bool logDecomposition = false;
    [Tooltip("Master switch for step-by-step decomposition debugging")]
    public bool debugDecomposition = false;

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
            Debug.LogWarning("[Decompose] 'Decompose' layer not found. Create it in Project Settings > Tags and Layers.");
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

        if (debugDecomposition)
            Debug.Log($"[Decompose] SCAN START: found {allOrganic.Length} OrganicMatter objects in scene. Slime worldBounds={worldBounds}");

        foreach (OrganicMatter organic in allOrganic)
        {
            if (organic == null) continue;
            // Check if object's collider bounds overlap with slime bounds (not just center point)
            Rect objectBounds = GetObjectBounds(organic.gameObject);
            bool overlaps = worldBounds.Overlaps(objectBounds);
            bool onDecomposeLayer = decomposeLayerIndex != -1 && organic.gameObject.layer == decomposeLayerIndex;

            if (!overlaps) continue;

            if (requireDecomposeLayer && !onDecomposeLayer) continue;

            trackedObjects.Add(organic);
        }

        if (debugDecomposition)
            Debug.Log($"[Decompose] SCAN COMPLETE: {trackedObjects.Count}/{allOrganic.Length} objects tracked");
    }

    Rect GetObjectBounds(GameObject obj)
    {
        // Try collider first
        var col = obj.GetComponent<Collider2D>();
        if (col != null)
            return new Rect(col.bounds.min.x, col.bounds.min.y, col.bounds.size.x, col.bounds.size.y);
        // Try sprite renderer
        var sr = obj.GetComponent<SpriteRenderer>();
        if (sr != null)
            return new Rect(sr.bounds.min.x, sr.bounds.min.y, sr.bounds.size.x, sr.bounds.size.y);
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
                insidePoints.Add(worldPoint);
        }

        if (insidePoints.Count == 0) return null;

        // Return centroid of inside points
        Vector2 centroid = Vector2.zero;
        foreach (var p in insidePoints)
            centroid += p;

        return centroid / insidePoints.Count;
    }

    void ProcessDecomposition()
    {
        worldBounds = slimeSimulation.GetWorldBounds();

        Texture2D trailTexture = slimeSimulation.GetTrailTexture();
        if (trailTexture == null) return;

        foreach (OrganicMatter organic in trackedObjects)
        {
            if (organic == null) continue;
            List<Vector2> samplePoints = GetSamplePointsInBounds(organic.gameObject, worldBounds);

            if (samplePoints.Count == 0) continue;

            float maxDensity = 0f;

            foreach (Vector2 point in samplePoints)
            {
                float normX = Mathf.Clamp01((point.x - worldBounds.x) / worldBounds.width);
                float normY = Mathf.Clamp01((point.y - worldBounds.y) / worldBounds.height);
                int texX = Mathf.FloorToInt(normX * (resolution.x - 1));
                int texY = Mathf.FloorToInt(normY * (resolution.y - 1));
                Color pixel = trailTexture.GetPixel(texX, texY);
                float density = (pixel.r + pixel.g + pixel.b) / 3f;

                if (density > maxDensity)
                    maxDensity = density;
            }

            if (maxDensity >= minTrailDensity)
            {
                float normalizedDensity = Mathf.InverseLerp(minTrailDensity, 1f, maxDensity);
                float multiplier = damageMultiplier.Evaluate(normalizedDensity);
                float damage = baseDamagePerSecond * multiplier * Time.deltaTime;

                var linked = organic.GetComponent<LinkedOrganicMatter>();
                if (linked != null)
                {
                    linked.TakeLinkedDamage(damage);
                }
                else
                {
                    organic.TakeDecompositionDamage(damage);
                }

                if (logDecomposition)
                    Debug.Log($"[Decompose] {organic.gameObject.name}: density={maxDensity:F2}, damage={damage:F2}, health={organic.GetHealthPercent() * 100:F1}%");
            }
        }
    }

    List<Vector2> GetSamplePointsInBounds(GameObject obj, Rect bounds)
    {
        if (obj == null) return new List<Vector2>();
        List<Vector2> points = new List<Vector2>();

        Rect objectBounds = GetObjectBounds(obj);
        float overlapMinX = Mathf.Max(objectBounds.xMin, bounds.xMin);
        float overlapMaxX = Mathf.Min(objectBounds.xMax, bounds.xMax);
        float overlapMinY = Mathf.Max(objectBounds.yMin, bounds.yMin);
        float overlapMaxY = Mathf.Min(objectBounds.yMax, bounds.yMax);

        if (overlapMinX >= overlapMaxX || overlapMinY >= overlapMaxY)
            return points;

        // 3x3 grid across the full overlap region — robust regardless of
        // approach direction or which side the slime enters from
        for (int ix = 0; ix < 3; ix++)
        {
            for (int iy = 0; iy < 3; iy++)
            {
                float tx = Mathf.Lerp(overlapMinX, overlapMaxX, (ix + 0.5f) / 3f);
                float ty = Mathf.Lerp(overlapMinY, overlapMaxY, (iy + 0.5f) / 3f);
                points.Add(new Vector2(tx, ty));
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
        float density = (pixel.r + pixel.g + pixel.b) / 3f;

        if (debugDecomposition)
            Debug.Log($"[Decompose] Sample worldPos={worldPos}, norm=({normX:F2},{normY:F2}), texel=({texX},{texY}), pixel={pixel}, density={density:F3}");

        return density;
    }

    /// <summary>
    /// Get attraction values for decomposable objects (called by SlimeMoldManager).
    /// Creates radial attraction fields around OrganicMatter objects inside bounds.
    /// </summary>
    public void GetDecomposableAttractions(Color[] pixelBuffer, int w, int h, Rect bounds)
    {
        if (!enableAttraction)
        {
            if (debugDecomposition)
                Debug.Log("[Decompose] ATTRACTION skipped: enableAttraction is false");
            return;
        }

        if (debugDecomposition)
            Debug.Log($"[Decompose] ATTRACTION writing for {trackedObjects.Count} tracked objects");

        foreach (OrganicMatter organic in trackedObjects)
        {
            if (organic == null) continue;

            Rect objectBounds = GetObjectBounds(organic.gameObject);
            float overlapMinX = Mathf.Max(objectBounds.xMin, bounds.xMin);
            float overlapMaxX = Mathf.Min(objectBounds.xMax, bounds.xMax);
            float overlapMinY = Mathf.Max(objectBounds.yMin, bounds.yMin);
            float overlapMaxY = Mathf.Min(objectBounds.yMax, bounds.yMax);

            if (overlapMinX >= overlapMaxX || overlapMinY >= overlapMaxY)
            {
                if (debugDecomposition)
                    Debug.Log($"[Decompose] ATTRACTION SKIP '{organic.name}': no overlap region");
                continue;
            }

            // Always use the AABB overlap center as the attraction target.
            // The old polygon centroid approach only worked when multiple vertices
            // were inside the slime bounds — for beams that enter from the side,
            // only one vertex is inside, pulling the target to the wrong edge.
            Vector2 attractionPoint = new Vector2(
                (overlapMinX + overlapMaxX) * 0.5f,
                (overlapMinY + overlapMaxY) * 0.5f
            );

            if (debugDecomposition)
                Debug.Log($"[Decompose] ATTRACTION '{organic.name}': target={attractionPoint} (overlap center), overlapSize=({overlapMaxX - overlapMinX:F2} x {overlapMaxY - overlapMinY:F2})");

            attractionPoint.x = Mathf.Clamp(attractionPoint.x, bounds.xMin, bounds.xMax);
            attractionPoint.y = Mathf.Clamp(attractionPoint.y, bounds.yMin, bounds.yMax);

            int texMinX = Mathf.Clamp(Mathf.FloorToInt((overlapMinX - bounds.x) / bounds.width * w), 0, w - 1);
            int texMaxX = Mathf.Clamp(Mathf.CeilToInt((overlapMaxX - bounds.x) / bounds.width * w), 0, w - 1);
            int texMinY = Mathf.Clamp(Mathf.FloorToInt((overlapMinY - bounds.y) / bounds.height * h), 0, h - 1);
            int texMaxY = Mathf.Clamp(Mathf.CeilToInt((overlapMaxY - bounds.y) / bounds.height * h), 0, h - 1);

            // Expand by the attraction radius so slime gets pulled toward the object from a distance
            float pixelsPerWorldUnitX = w / bounds.width;
            float pixelsPerWorldUnitY = h / bounds.height;
            int expandX = Mathf.RoundToInt(internalAttractionRadius * pixelsPerWorldUnitX);
            int expandY = Mathf.RoundToInt(internalAttractionRadius * pixelsPerWorldUnitY);

            int drawMinX = Mathf.Max(0, texMinX - expandX);
            int drawMaxX = Mathf.Min(w - 1, texMaxX + expandX);
            int drawMinY = Mathf.Max(0, texMinY - expandY);
            int drawMaxY = Mathf.Min(h - 1, texMaxY + expandY);

            int pixelsWritten = 0;
            for (int ty = drawMinY; ty <= drawMaxY; ty++)
            {
                for (int tx = drawMinX; tx <= drawMaxX; tx++)
                {
                    // Inside the overlap region: full strength
                    if (tx >= texMinX && tx <= texMaxX && ty >= texMinY && ty <= texMaxY)
                    {
                        int idx = ty * w + tx;
                        pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + attractionStrength);
                        pixelsWritten++;
                    }
                    else
                    {
                        // Outside the overlap but within attraction radius: falloff gradient
                        float distX = tx < texMinX ? texMinX - tx : tx - texMaxX;
                        float distY = ty < texMinY ? texMinY - ty : ty - texMaxY;
                        distX = Mathf.Max(0, distX);
                        distY = Mathf.Max(0, distY);

                        float normDist = Mathf.Sqrt(
                            (distX / expandX) * (distX / expandX) +
                            (distY / expandY) * (distY / expandY));

                        if (normDist <= 1f)
                        {
                            float falloff = 1f - normDist;
                            int idx = ty * w + tx;
                            pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + attractionStrength * falloff);
                            pixelsWritten++;
                        }
                    }
                }
            }

            if (debugDecomposition)
                Debug.Log($"[Decompose] ATTRACTION '{organic.name}': filled overlap texels ({texMinX},{texMinY})-({texMaxX},{texMaxY}) + radius {expandX}x{expandY}px, {pixelsWritten} pixels written, strength={attractionStrength}");
        }
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        if (!Application.isPlaying) return;
        if (slimeSimulation == null) return;

        Rect bounds = slimeSimulation.GetWorldBounds();

        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireCube(bounds.center, new Vector3(bounds.width, bounds.height, 0));

        foreach (OrganicMatter organic in trackedObjects)
        {
            if (organic == null) continue;

            Rect objectBounds = GetObjectBounds(organic.gameObject);
            float overlapMinX = Mathf.Max(objectBounds.xMin, bounds.xMin);
            float overlapMaxX = Mathf.Min(objectBounds.xMax, bounds.xMax);
            float overlapMinY = Mathf.Max(objectBounds.yMin, bounds.yMin);
            float overlapMaxY = Mathf.Min(objectBounds.yMax, bounds.yMax);

            if (overlapMinX >= overlapMaxX || overlapMinY >= overlapMaxY) continue;

            Vector2 attractionPoint = new Vector2(
                (overlapMinX + overlapMaxX) * 0.5f,
                (overlapMinY + overlapMaxY) * 0.5f
            );

            // Draw the overlap region so you can see what portion of the object is inside bounds
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Vector3 overlapCenter = new Vector3(attractionPoint.x, attractionPoint.y, 0);
            Vector3 overlapSize = new Vector3(overlapMaxX - overlapMinX, overlapMaxY - overlapMinY, 0);
            Gizmos.DrawWireCube(overlapCenter, overlapSize);

            // Draw the actual attraction target point
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(attractionPoint, 0.3f);

            // Draw the attraction radius
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.5f);
            Gizmos.DrawWireSphere(attractionPoint, internalAttractionRadius);
        }
    }
}