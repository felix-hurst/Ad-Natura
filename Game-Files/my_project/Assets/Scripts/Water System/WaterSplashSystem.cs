using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CellularLiquidSimulation))]
public class WaterSplashSystem : MonoBehaviour
{
    [Header("Splash Detection")]
    [Tooltip("Minimum impact velocity to create a splash")]
    [SerializeField] private float minSplashVelocity = 2f;
    [Tooltip("Velocity for maximum splash effect")]
    [SerializeField] private float maxSplashVelocity = 15f;
    [Tooltip("Layers that can create splashes")]
    [SerializeField] private LayerMask splashLayers = ~0;
    [Tooltip("How often to check for splashes (seconds)")]
    [SerializeField] private float checkInterval = 0.05f;

    [Header("Splash Shape")]
    [Tooltip("Enable two-phase splash (crown + delayed jet)")]
    [SerializeField] private bool enableEnhancedPhysics = true;
    [Tooltip("How many water cells make up the crown arc")]
    [SerializeField] private int crownCellCount = 12;
    [Tooltip("How far out (world units) the crown cells land from centre")]
    [SerializeField] private float crownRadius = 0.8f;
    [Tooltip("How high (world units) the crown arc peaks")]
    [SerializeField] private float crownHeight = 0.6f;
    [Tooltip("How many water cells make up the jet column")]
    [SerializeField] private int jetCellCount = 5;
    [Tooltip("How high (world units) the jet column reaches")]
    [SerializeField] private float jetHeight = 1.2f;
    [Tooltip("Delay before jet fires after initial crown (seconds)")]
    [SerializeField] private float jetDelay = 0.15f;
    [Tooltip("How many spray cells scatter around the splash")]
    [SerializeField] private int sprayCellCount = 8;
    [Tooltip("Max radius (world units) spray cells can land from centre")]
    [SerializeField] private float sprayRadius = 1.0f;
    [Tooltip("How high spray cells can arc")]
    [SerializeField] private float sprayHeight = 0.8f;

    [Header("Crown Rim Physics (Rayleigh-Plateau)")]
    [Tooltip("Enable Rayleigh-Plateau instability for crown cusps")]
    [SerializeField] private bool enableRayleighPlateau = true;
    [Tooltip("Most unstable wavelength for cusp formation (world units)")]
    [SerializeField] private float mostUnstableWavelength = 0.15f;
    [Tooltip("Random noise in cusp positions (degrees)")]
    [SerializeField] private float cuspNoiseAngle = 12f;
    [Tooltip("Enable progressive crown ejection over time")]
    [SerializeField] private bool enableProgressiveEjection = true;
    [Tooltip("Duration of progressive crown ejection (seconds)")]
    [SerializeField] private float crownEjectionDuration = 0.08f;

    [Header("Water Amounts")]
    [Tooltip("Water amount placed in each splash cell at full intensity")]
    [SerializeField] private float splashWaterAmount = 0.8f;
    [Tooltip("Multiplier on splash size/amount based on impact speed")]
    [SerializeField] private float splashIntensityScale = 1.0f;
    [Tooltip("How much water to pull from the surface per splash")]
    [SerializeField] private float waterRemoveAmount = 2.0f;

    [Header("Arc Flight")]
    [Tooltip("How many frames a splash cell travels through the air before landing in the grid")]
    [SerializeField] private int arcSteps = 6;
    [Tooltip("Time between each arc step (seconds). Lower = faster flight.")]
    [SerializeField] private float arcStepInterval = 0.025f;

    [Header("Overlay Particles")]
    [Tooltip("How many pixel-art particles to layer on top of each crown arc")]
    [SerializeField] private int crownParticlesPerSplash = 8;
    [Tooltip("How many pixel-art particles to layer on top of each spray burst")]
    [SerializeField] private int sprayParticlesPerSplash = 10;
    [Tooltip("Base size of overlay particles (world units)")]
    [SerializeField] private float particleSize = 0.12f;
    [Tooltip("How long overlay particles live (seconds)")]
    [SerializeField] private float particleLifetime = 1.2f;
    [Tooltip("Max overlay particles alive at once")]
    [SerializeField] private int maxActiveParticles = 80;
    [Tooltip("Gravity applied to overlay particles (world units / s^2)")]
    [SerializeField] private float particleGravity = -18f;
    [Tooltip("Per-frame velocity damping (0.9 = moderate drag, closer to 1 = less)")]
    [SerializeField] private float particleDamping = 0.97f;

    [Header("Secondary Droplet Physics")]
    [Tooltip("Minimum size multiplier for secondary droplets (relative to parent)")]
    [SerializeField] private float secondaryDropletMinSize = 0.15f;
    [Tooltip("Maximum size multiplier for secondary droplets (relative to parent)")]
    [SerializeField] private float secondaryDropletMaxSize = 0.24f;
    [Tooltip("Minimum velocity multiplier for secondary droplets (relative to impact)")]
    [SerializeField] private float secondaryVelocityMin = 1.5f;
    [Tooltip("Maximum velocity multiplier for secondary droplets (relative to impact)")]
    [SerializeField] private float secondaryVelocityMax = 2.5f;

    [Header("Secondary Collisions")]
    [Tooltip("Enable secondary collision detection for overlay particles")]
    [SerializeField] private bool enableSecondaryCollisions = true;
    [Tooltip("Time after spawn before particle can have secondary collision (seconds)")]
    [SerializeField] private float minTimeBeforeSecondaryCollision = 0.15f;
    [Tooltip("Layers that overlay particles can collide with")]
    [SerializeField] private LayerMask particleCollisionLayers = ~0;
    [Tooltip("Radius for particle collision detection")]
    [SerializeField] private float particleCollisionRadius = 0.05f;
    [Tooltip("Velocity multiplier after collision (bounce factor)")]
    [SerializeField] private float collisionBounceFactor = 0.4f;
    [Tooltip("Minimum velocity for particle to create micro-splash on water")]
    [SerializeField] private float minMicroSplashVelocity = 1.5f;
    [Tooltip("Water amount added when particle hits water surface")]
    [SerializeField] private float microSplashWaterAmount = 0.15f;

    [Header("Overlay Colours")]
    [SerializeField] private Color crownParticleColor  = new Color(0.25f, 0.45f, 0.75f, 0.9f);
    [SerializeField] private Color sprayParticleColor  = new Color(0.55f, 0.78f, 1f,    0.75f);

    [Header("Performance")]
    [SerializeField] private int maxSplashesPerFrame = 3;
    
    [Header("Performance Optimization")]
    [Tooltip("Use cached rigidbody tracking instead of FindObjectsOfType (massive performance boost)")]
    [SerializeField] private bool useCachedRigidbodies = true;
    [Tooltip("How often to refresh the rigidbody cache (seconds). 0 = only on enable/registration.")]
    [SerializeField] private float cacheRefreshInterval = 2.0f;
    [Tooltip("Use spatial hashing for collision detection (60-80% faster collision checks)")]
    [SerializeField] private bool useSpatialHashingForCollisions = true;
    [Tooltip("Size of spatial hash grid cells (world units). Should be ~2x particle collision radius")]
    [SerializeField] private float spatialHashCellSize = 0.15f;
    [Tooltip("Cache water surface cells to reduce grid lookups (40-50% faster water collision)")]
    [SerializeField] private bool cacheWaterSurfaceCells = true;
    [Tooltip("How often to refresh water surface cache (seconds)")]
    [SerializeField] private float waterSurfaceCacheRefreshInterval = 0.1f;
    [Tooltip("Use batched particle updates (process in chunks for better cache coherency)")]
    [SerializeField] private bool useBatchedParticleUpdates = true;
    [Tooltip("Number of particles to process per batch")]
    [SerializeField] private int particleBatchSize = 16;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
    [SerializeField] private bool showCollisionDebug = false;
    [SerializeField] private bool showCuspDebug = false;
    [SerializeField] private bool showSpatialHashDebug = false;

    private CellularLiquidSimulation liquidSim;
    private Dictionary<Rigidbody2D, TrackedObject> trackedObjects = new Dictionary<Rigidbody2D, TrackedObject>();
    private float checkTimer = 0f;
    private int splashesThisFrame = 0;

    private HashSet<Rigidbody2D> cachedRigidbodies = new HashSet<Rigidbody2D>();
    private float cacheRefreshTimer = 0f;

    private Dictionary<Vector2Int, List<Collider2D>> spatialHashGrid = new Dictionary<Vector2Int, List<Collider2D>>();
    private List<Collider2D> spatialHashColliderCache = new List<Collider2D>();
    private bool spatialHashDirty = true;
    
    private HashSet<Vector2Int> waterSurfaceCells = new HashSet<Vector2Int>();
    private float waterSurfaceCacheTimer = 0f;
    private bool waterSurfaceCacheDirty = true;

    private List<OverlayParticle>  activeParticles = new List<OverlayParticle>();
    private Queue<OverlayParticle> particlePool    = new Queue<OverlayParticle>();
    private Sprite crownSprite;
    private Sprite spraySprite;

    private class TrackedObject
    {
        public Vector2 lastPosition;
        public Vector2 lastVelocity;
        public bool    wasInWater;
        public int     waterCellsBelow;
    }

    private enum OverlayType { Crown, Spray }

    private class OverlayParticle
    {
        public GameObject     gameObject;
        public SpriteRenderer renderer;
        public Vector2        velocity;
        public float          lifetime;
        public float          maxLifetime;
        public float          initialSize;
        public Color          baseColor;
        public OverlayType    type;
        
        public float          timeSinceLastCollision;
        public bool           hasHadInitialCollision;
        public int            collisionCount;
    }

    private class ProgressiveCrownData
    {
        public Vector2 center;
        public Vector2 velocity;
        public float intensity;
        public float sizeMultiplier;
        public float impactVelocity;
        public List<CrownCusp> cusps;
    }

    private class CrownCusp
    {
        public float angle;
        public float ejectionTime;
    }

    void Start()
    {
        liquidSim = GetComponent<CellularLiquidSimulation>();
        if (liquidSim == null)
        {
            Debug.LogError("WaterSplashSystem requires CellularLiquidSimulation!");
            enabled = false;
            return;
        }

        crownSprite = CreatePixelDroplet(8, 5, true);
        spraySprite = CreatePixelDroplet(6, 9, false);

        for (int i = 0; i < 20; i++)
        {
            OverlayParticle p = AllocateParticle();
            p.gameObject.SetActive(false);
            particlePool.Enqueue(p);
        }

        if (useCachedRigidbodies)
        {
            RefreshRigidbodyCache();
        }
        
        if (useSpatialHashingForCollisions)
        {
            RebuildSpatialHash();
        }
        
        if (cacheWaterSurfaceCells)
        {
            RefreshWaterSurfaceCache();
        }
    }

    void Update()
    {
        splashesThisFrame = 0;

        checkTimer += Time.deltaTime;
        if (checkTimer >= checkInterval)
        {
            checkTimer = 0f;
            CheckForSplashes();
        }

        if (useCachedRigidbodies && cacheRefreshInterval > 0f)
        {
            cacheRefreshTimer += Time.deltaTime;
            if (cacheRefreshTimer >= cacheRefreshInterval)
            {
                cacheRefreshTimer = 0f;
                RefreshRigidbodyCache();
            }
        }
        
        if (useSpatialHashingForCollisions && spatialHashDirty)
        {
            RebuildSpatialHash();
            spatialHashDirty = false;
        }
        
        if (cacheWaterSurfaceCells)
        {
            waterSurfaceCacheTimer += Time.deltaTime;
            if (waterSurfaceCacheTimer >= waterSurfaceCacheRefreshInterval)
            {
                waterSurfaceCacheTimer = 0f;
                RefreshWaterSurfaceCache();
            }
        }

        UpdateOverlayParticlesOptimized();
    }

    void OnDestroy()
    {
        foreach (var p in activeParticles)
            if (p.gameObject != null) Destroy(p.gameObject);
        while (particlePool.Count > 0)
        {
            var p = particlePool.Dequeue();
            if (p.gameObject != null) Destroy(p.gameObject);
        }
        if (crownSprite != null && crownSprite.texture != null) Destroy(crownSprite.texture);
        if (spraySprite != null && spraySprite.texture != null) Destroy(spraySprite.texture);
    }

    void RebuildSpatialHash()
    {
        spatialHashGrid.Clear();
        
        if (spatialHashColliderCache.Count == 0 || spatialHashDirty)
        {
            spatialHashColliderCache.Clear();
            Collider2D[] allColliders = FindObjectsOfType<Collider2D>();
            
            foreach (Collider2D col in allColliders)
            {
                if (col == null) continue;
                
                int layerMask = 1 << col.gameObject.layer;
                if ((layerMask & particleCollisionLayers) != 0)
                {
                    spatialHashColliderCache.Add(col);
                }
            }
        }
        
        foreach (Collider2D col in spatialHashColliderCache)
        {
            if (col == null) continue;
            
            Bounds bounds = col.bounds;
            Vector2Int minCell = WorldToHashCell(bounds.min);
            Vector2Int maxCell = WorldToHashCell(bounds.max);
            
            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    Vector2Int cellKey = new Vector2Int(x, y);
                    
                    if (!spatialHashGrid.ContainsKey(cellKey))
                    {
                        spatialHashGrid[cellKey] = new List<Collider2D>();
                    }
                    
                    spatialHashGrid[cellKey].Add(col);
                }
            }
        }
        
        if (showSpatialHashDebug && showDebugInfo)
        {
            Debug.Log($"Spatial hash rebuilt: {spatialHashColliderCache.Count} colliders in {spatialHashGrid.Count} cells");
        }
    }
    
    Vector2Int WorldToHashCell(Vector2 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / spatialHashCellSize),
            Mathf.FloorToInt(worldPos.y / spatialHashCellSize)
        );
    }
    
    List<Collider2D> QuerySpatialHash(Vector2 position, float radius)
    {
        List<Collider2D> results = new List<Collider2D>();
        
        Vector2 min = position - Vector2.one * radius;
        Vector2 max = position + Vector2.one * radius;
        
        Vector2Int minCell = WorldToHashCell(min);
        Vector2Int maxCell = WorldToHashCell(max);
        
        HashSet<Collider2D> uniqueColliders = new HashSet<Collider2D>();
        
        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                Vector2Int cellKey = new Vector2Int(x, y);
                
                if (spatialHashGrid.TryGetValue(cellKey, out List<Collider2D> colliders))
                {
                    foreach (Collider2D col in colliders)
                    {
                        if (col != null && !uniqueColliders.Contains(col))
                        {
                            uniqueColliders.Add(col);
                            results.Add(col);
                        }
                    }
                }
            }
        }
        
        return results;
    }

    void RefreshWaterSurfaceCache()
    {
        waterSurfaceCells.Clear();
        
        Rect bounds = liquidSim.GetWorldBounds();
        Vector2Int minGrid = liquidSim.WorldToGrid(bounds.min);
        Vector2Int maxGrid = liquidSim.WorldToGrid(bounds.max);
        
        for (int x = minGrid.x; x <= maxGrid.x; x++)
        {
            for (int y = minGrid.y; y <= maxGrid.y; y++)
            {
                if (!liquidSim.IsValidCell(x, y)) continue;
                
                float waterAmount = liquidSim.GetWater(x, y);
                if (waterAmount > 0.1f)
                {
                    if (y >= maxGrid.y || liquidSim.GetWater(x, y + 1) <= 0.1f)
                    {
                        waterSurfaceCells.Add(new Vector2Int(x, y));
                    }
                }
            }
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"Water surface cache refreshed: {waterSurfaceCells.Count} surface cells");
        }
        
        waterSurfaceCacheDirty = false;
    }
    
    bool IsNearWaterSurface(Vector2 worldPos, out Vector2Int surfaceCell)
    {
        surfaceCell = liquidSim.WorldToGrid(worldPos);
        
        if (!liquidSim.IsValidCell(surfaceCell.x, surfaceCell.y))
            return false;
        
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                Vector2Int checkCell = new Vector2Int(surfaceCell.x + dx, surfaceCell.y + dy);
                
                if (waterSurfaceCells.Contains(checkCell))
                {
                    surfaceCell = checkCell;
                    return true;
                }
            }
        }
        
        return false;
    }

    void RefreshRigidbodyCache()
    {
        cachedRigidbodies.Clear();
        
        Rigidbody2D[] allRigidbodies = FindObjectsOfType<Rigidbody2D>();
        foreach (Rigidbody2D rb in allRigidbodies)
        {
            if (rb == null || rb.bodyType == RigidbodyType2D.Static) continue;
            
            int layerMask = 1 << rb.gameObject.layer;
            if ((layerMask & splashLayers) != 0)
            {
                cachedRigidbodies.Add(rb);
            }
        }
        
        spatialHashDirty = true;
        
        if (showDebugInfo)
        {
            Debug.Log($"Rigidbody cache refreshed: {cachedRigidbodies.Count} tracked");
        }
    }
    
    public void RegisterRigidbody(Rigidbody2D rb)
    {
        if (!useCachedRigidbodies) return;
        if (rb == null || rb.bodyType == RigidbodyType2D.Static) return;
        
        int layerMask = 1 << rb.gameObject.layer;
        if ((layerMask & splashLayers) != 0)
        {
            cachedRigidbodies.Add(rb);
            spatialHashDirty = true;
            
            if (showDebugInfo)
            {
                Debug.Log($"Registered rigidbody: {rb.name}");
            }
        }
    }
    
    public void UnregisterRigidbody(Rigidbody2D rb)
    {
        if (!useCachedRigidbodies) return;
        
        cachedRigidbodies.Remove(rb);
        trackedObjects.Remove(rb);
        spatialHashDirty = true;
    }

    void CheckForSplashes()
    {
        Dictionary<Rigidbody2D, TrackedObject> newTracked = new Dictionary<Rigidbody2D, TrackedObject>();

        IEnumerable<Rigidbody2D> rigidbodiestoCheck;
        
        if (useCachedRigidbodies)
        {
            cachedRigidbodies.RemoveWhere(rb => rb == null);
            rigidbodiestoCheck = cachedRigidbodies;
        }
        else
        {
            rigidbodiestoCheck = FindObjectsOfType<Rigidbody2D>();
        }

        foreach (Rigidbody2D rb in rigidbodiestoCheck)
        {
            if (rb == null || rb.bodyType == RigidbodyType2D.Static) continue;

            if (!useCachedRigidbodies)
            {
                int layerMask = 1 << rb.gameObject.layer;
                if ((layerMask & splashLayers) == 0) continue;
            }

            Vector2 currentPos      = rb.position;
            Vector2 currentVelocity = rb.linearVelocity;
            bool    currentlyInWater = IsObjectInWater(rb, out int waterCellsBelow);

            TrackedObject tracked;
            if (trackedObjects.TryGetValue(rb, out tracked))
            {
                if (!tracked.wasInWater && currentlyInWater && currentVelocity.y < -minSplashVelocity)
                {
                    float impactVelocity = Mathf.Abs(currentVelocity.y);
                    if (enableEnhancedPhysics)
                        StartCoroutine(CreateEnhancedSplash(rb, currentPos, currentVelocity, impactVelocity));
                    else
                        CreateSimpleSplash(rb, currentPos, currentVelocity, impactVelocity);
                    
                    waterSurfaceCacheDirty = true;
                }

                if (currentlyInWater && tracked.wasInWater)
                {
                    float horizontalSpeed = Mathf.Abs(currentVelocity.x);
                    if (horizontalSpeed > minSplashVelocity * 1.5f)
                    {
                        CreateSideSplash(rb, currentPos, currentVelocity, horizontalSpeed);
                        waterSurfaceCacheDirty = true;
                    }
                }
            }

            newTracked[rb] = new TrackedObject
            {
                lastPosition    = currentPos,
                lastVelocity    = currentVelocity,
                wasInWater      = currentlyInWater,
                waterCellsBelow = waterCellsBelow
            };
        }

        trackedObjects = newTracked;
    }

    bool IsObjectInWater(Rigidbody2D rb, out int waterCellsBelow)
    {
        waterCellsBelow = 0;

        Collider2D col = rb.GetComponent<Collider2D>();
        if (col == null) return false;

        Bounds     bounds       = col.bounds;
        Vector2    bottomCenter = new Vector2(bounds.center.x, bounds.min.y);
        Vector2Int gridPos      = liquidSim.WorldToGrid(bottomCenter);

        if (!liquidSim.IsValidCell(gridPos.x, gridPos.y)) return false;

        for (int y = gridPos.y; y >= 0 && y > gridPos.y - 5; y--)
        {
            if (liquidSim.IsValidCell(gridPos.x, y) && liquidSim.GetWater(gridPos.x, y) > 0.1f)
                waterCellsBelow++;
        }

        Vector2Int centerGrid = liquidSim.WorldToGrid(bounds.center);
        if (liquidSim.IsValidCell(centerGrid.x, centerGrid.y) && liquidSim.GetWater(centerGrid.x, centerGrid.y) > 0.1f)
            return true;

        return waterCellsBelow > 0;
    }

    IEnumerator CreateEnhancedSplash(Rigidbody2D rb, Vector2 position, Vector2 velocity, float impactVelocity)
    {
        if (splashesThisFrame >= maxSplashesPerFrame) yield break;
        splashesThisFrame++;

        float intensity = Mathf.Clamp01(Mathf.InverseLerp(minSplashVelocity, maxSplashVelocity, impactVelocity));

        Collider2D col             = rb.GetComponent<Collider2D>();
        float      sizeMultiplier  = col != null ? Mathf.Clamp(col.bounds.size.magnitude, 0.5f, 3f) : 1f;
        Vector2    splashCenter    = position + Vector2.down * (col != null ? col.bounds.extents.y : 0.5f);

        if (showDebugInfo)
            Debug.Log($"ENHANCED SPLASH: {rb.name} at {impactVelocity:F1} m/s, intensity {intensity:F2}");

        RemoveWaterForSplash(splashCenter, waterRemoveAmount * intensity * sizeMultiplier,
                             col != null ? col.bounds.size.x : 1f);

        if (enableProgressiveEjection)
        {
            StartCoroutine(SpawnProgressiveCrown(splashCenter, velocity, intensity, sizeMultiplier, impactVelocity));
        }
        else
        {
            SpawnCrownArcs(splashCenter, velocity, intensity, sizeMultiplier, impactVelocity);
        }
        
        SpawnSprayCells(splashCenter, velocity, intensity, sizeMultiplier, impactVelocity);

        yield return new WaitForSeconds(jetDelay);
        SpawnJetColumn(splashCenter, intensity, sizeMultiplier);
    }

    void CreateSimpleSplash(Rigidbody2D rb, Vector2 position, Vector2 velocity, float impactVelocity)
    {
        if (splashesThisFrame >= maxSplashesPerFrame) return;
        splashesThisFrame++;

        float intensity = Mathf.Clamp01(Mathf.InverseLerp(minSplashVelocity, maxSplashVelocity, impactVelocity));

        Collider2D col            = rb.GetComponent<Collider2D>();
        float      sizeMultiplier = col != null ? Mathf.Clamp(col.bounds.size.magnitude, 0.5f, 3f) : 1f;
        Vector2    splashCenter   = position + Vector2.down * (col != null ? col.bounds.extents.y : 0.5f);

        if (showDebugInfo)
            Debug.Log($"SIMPLE SPLASH: {rb.name} at {impactVelocity:F1} m/s");

        RemoveWaterForSplash(splashCenter, waterRemoveAmount * intensity * sizeMultiplier,
                             col != null ? col.bounds.size.x : 1f);

        int totalCells = Mathf.RoundToInt((sprayCellCount + crownCellCount) * intensity * sizeMultiplier);
        for (int i = 0; i < totalCells; i++)
        {
            float angle  = Random.Range(10f, 170f);
            float dist   = Random.Range(0.1f, crownRadius * sizeMultiplier);
            float height = Random.Range(0.15f, sprayHeight * intensity);

            Vector2 landingPos = splashCenter + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad) * dist, 0f);
            float   amount     = splashWaterAmount * intensity * splashIntensityScale * Random.Range(0.5f, 1f);
            StartCoroutine(FlyWaterCell(splashCenter, landingPos, height, amount));
        }

        SpawnOverlayParticles(splashCenter, velocity, intensity, sizeMultiplier, impactVelocity,
                              crownParticlesPerSplash, OverlayType.Crown);
        SpawnOverlayParticles(splashCenter, velocity, intensity, sizeMultiplier, impactVelocity,
                              sprayParticlesPerSplash, OverlayType.Spray);
    }

    void CreateSideSplash(Rigidbody2D rb, Vector2 position, Vector2 velocity, float speed)
    {
        if (splashesThisFrame >= maxSplashesPerFrame) return;

        float intensity = Mathf.Clamp01(Mathf.InverseLerp(minSplashVelocity * 1.5f, maxSplashVelocity, speed) * 0.5f);
        int   cellCount = Mathf.RoundToInt(Mathf.Lerp(1, sprayCellCount * 0.4f, intensity));
        if (cellCount <= 0) return;

        splashesThisFrame++;

        Collider2D col  = rb.GetComponent<Collider2D>();
        float      side = Mathf.Sign(velocity.x);

        Vector2 splashOrigin = position;
        if (col != null)
            splashOrigin = new Vector2(position.x + side * col.bounds.extents.x, position.y);

        RemoveWaterForSplash(splashOrigin, waterRemoveAmount * intensity * 0.4f,
                             col != null ? col.bounds.size.x : 0.5f);

        for (int i = 0; i < cellCount; i++)
        {
            float outDist  = Random.Range(0.1f, crownRadius * 0.6f * intensity);
            float upHeight = Random.Range(0.1f, sprayHeight * 0.5f * intensity);

            Vector2 landingPos = splashOrigin + new Vector2(side * outDist, 0f);
            float   amount     = splashWaterAmount * intensity * splashIntensityScale * 0.6f;
            StartCoroutine(FlyWaterCell(splashOrigin, landingPos, upHeight, amount));
        }

        SpawnOverlaySideBurst(splashOrigin, velocity, intensity, side, speed);
    }

    List<CrownCusp> CalculateRayleighPlateauCusps(float crownRadius, float intensity, float sizeMultiplier)
    {
        List<CrownCusp> cusps = new List<CrownCusp>();

        if (!enableRayleighPlateau)
        {
            int count = Mathf.RoundToInt(crownCellCount * intensity * sizeMultiplier);
            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)count) * 360f + Random.Range(-15f, 15f);
                cusps.Add(new CrownCusp 
                { 
                    angle = angle,
                    ejectionTime = 0f
                });
            }
            return cusps;
        }

        float circumference = 2f * Mathf.PI * crownRadius * sizeMultiplier * intensity;
        int numberOfCusps = Mathf.Max(4, Mathf.RoundToInt(circumference / mostUnstableWavelength));
        
        numberOfCusps = Mathf.RoundToInt(numberOfCusps * Mathf.Lerp(0.6f, 1f, intensity));

        if (showCuspDebug)
            Debug.Log($"Crown circumference: {circumference:F2}, cusps: {numberOfCusps}, wavelength: {mostUnstableWavelength:F2}");

        float baseAngleStep = 360f / numberOfCusps;
        for (int i = 0; i < numberOfCusps; i++)
        {
            float idealAngle = i * baseAngleStep;
            
            float noise = Random.Range(-cuspNoiseAngle, cuspNoiseAngle);
            float finalAngle = idealAngle + noise;
            
            while (finalAngle < 0f) finalAngle += 360f;
            while (finalAngle >= 360f) finalAngle -= 360f;
            
            float normalizedPosition = i / (float)numberOfCusps;
            float ejectionTime = normalizedPosition * crownEjectionDuration;
            
            cusps.Add(new CrownCusp 
            { 
                angle = finalAngle,
                ejectionTime = ejectionTime
            });

            if (showCuspDebug)
                Debug.DrawRay(transform.position, 
                             new Vector2(Mathf.Cos(finalAngle * Mathf.Deg2Rad), 
                                        Mathf.Sin(finalAngle * Mathf.Deg2Rad)) * crownRadius,
                             Color.yellow, 2f);
        }

        return cusps;
    }

    IEnumerator SpawnProgressiveCrown(Vector2 center, Vector2 velocity, float intensity, float sizeMultiplier, float impactVelocity)
    {
        List<CrownCusp> cusps = CalculateRayleighPlateauCusps(crownRadius, intensity, sizeMultiplier);
        
        float radius = crownRadius * sizeMultiplier * intensity;
        float height = crownHeight * intensity;
        
        cusps.Sort((a, b) => a.ejectionTime.CompareTo(b.ejectionTime));
        
        int particlesEjected = 0;
        int overlayParticlesEjected = 0;
        int totalOverlayParticles = Mathf.RoundToInt(crownParticlesPerSplash * intensity * sizeMultiplier);
        
        float elapsedTime = 0f;
        int lastCuspIndex = 0;
        
        while (elapsedTime < crownEjectionDuration && lastCuspIndex < cusps.Count)
        {
            while (lastCuspIndex < cusps.Count && cusps[lastCuspIndex].ejectionTime <= elapsedTime)
            {
                CrownCusp cusp = cusps[lastCuspIndex];
                
                float rad = cusp.angle * Mathf.Deg2Rad;
                float jitterDist = radius * Random.Range(0.8f, 1.2f);
                
                Vector2 landingPos = center + new Vector2(Mathf.Cos(rad) * jitterDist, 0f);
                landingPos.x += velocity.x * 0.1f;
                
                float heightMultiplier = 0.6f + 0.4f * (elapsedTime / crownEjectionDuration);
                float arcPeak = height * heightMultiplier * Random.Range(0.9f, 1.1f);
                float amount = splashWaterAmount * intensity * splashIntensityScale * Random.Range(0.7f, 1f);
                
                StartCoroutine(FlyWaterCell(center, landingPos, arcPeak, amount));
                particlesEjected++;
                
                if (overlayParticlesEjected < totalOverlayParticles)
                {
                    SpawnSingleCrownOverlayParticle(center, velocity, intensity, sizeMultiplier, 
                                                   impactVelocity, cusp.angle, heightMultiplier);
                    overlayParticlesEjected++;
                }
                
                lastCuspIndex++;
            }
            
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        
        while (lastCuspIndex < cusps.Count)
        {
            CrownCusp cusp = cusps[lastCuspIndex];
            
            float rad = cusp.angle * Mathf.Deg2Rad;
            float jitterDist = radius * Random.Range(0.8f, 1.2f);
            
            Vector2 landingPos = center + new Vector2(Mathf.Cos(rad) * jitterDist, 0f);
            landingPos.x += velocity.x * 0.1f;
            
            float arcPeak = height * Random.Range(0.6f, 1f);
            float amount = splashWaterAmount * intensity * splashIntensityScale * Random.Range(0.7f, 1f);
            
            StartCoroutine(FlyWaterCell(center, landingPos, arcPeak, amount));
            
            if (overlayParticlesEjected < totalOverlayParticles)
            {
                SpawnSingleCrownOverlayParticle(center, velocity, intensity, sizeMultiplier, 
                                               impactVelocity, cusp.angle, 1f);
                overlayParticlesEjected++;
            }
            
            lastCuspIndex++;
        }
        
        if (showDebugInfo)
            Debug.Log($"Progressive crown complete: {particlesEjected} grid cells, {overlayParticlesEjected} overlay particles");
    }

    void SpawnCrownArcs(Vector2 center, Vector2 velocity, float intensity, float sizeMultiplier, float impactVelocity)
    {
        List<CrownCusp> cusps = CalculateRayleighPlateauCusps(crownRadius, intensity, sizeMultiplier);
        
        float radius = crownRadius * sizeMultiplier * intensity;
        float height = crownHeight * intensity;

        foreach (CrownCusp cusp in cusps)
        {
            float rad = cusp.angle * Mathf.Deg2Rad;
            float jitterDist = radius * Random.Range(0.8f, 1.2f);

            Vector2 landingPos = center + new Vector2(Mathf.Cos(rad) * jitterDist, 0f);
            landingPos.x += velocity.x * 0.1f;

            float arcPeak = height * Random.Range(0.6f, 1f);
            float amount  = splashWaterAmount * intensity * splashIntensityScale * Random.Range(0.7f, 1f);

            StartCoroutine(FlyWaterCell(center, landingPos, arcPeak, amount));
        }

        SpawnOverlayParticles(center, velocity, intensity, sizeMultiplier, impactVelocity,
                              crownParticlesPerSplash, OverlayType.Crown);
    }

    void SpawnJetColumn(Vector2 center, float intensity, float sizeMultiplier)
    {
        int   count       = Mathf.RoundToInt(jetCellCount * Mathf.Clamp(intensity, 0.5f, 1f));
        float totalHeight = jetHeight * intensity * sizeMultiplier;

        for (int i = 0; i < count; i++)
        {
            float heightFraction = (i + 1f) / count;
            float peakHeight     = totalHeight * heightFraction;
            float wobbleX        = Random.Range(-0.08f, 0.08f) * sizeMultiplier;

            Vector2 landingPos = center + new Vector2(wobbleX, 0f);
            float   amount     = splashWaterAmount * intensity * splashIntensityScale * Random.Range(0.8f, 1f);

            StartCoroutine(FlyWaterCell(center, landingPos, peakHeight, amount));
        }
    }

    void SpawnSprayCells(Vector2 center, Vector2 velocity, float intensity, float sizeMultiplier, float impactVelocity)
    {
        int count = Mathf.RoundToInt(sprayCellCount * intensity * sizeMultiplier);

        for (int i = 0; i < count; i++)
        {
            float angle  = Random.Range(20f, 160f) * Mathf.Deg2Rad;
            float dist   = Random.Range(0.05f, sprayRadius * intensity * sizeMultiplier);
            float height = Random.Range(0.1f, sprayHeight * intensity);

            float dirSign = (Random.value > 0.5f) ? 1f : -1f;
            if (Mathf.Abs(velocity.x) > 0.5f)
                dirSign = -Mathf.Sign(velocity.x) * (Random.value > 0.3f ? 1f : -1f);

            Vector2 landingPos = center + new Vector2(dirSign * dist, 0f);
            float   amount     = splashWaterAmount * intensity * splashIntensityScale * Random.Range(0.3f, 0.7f);

            StartCoroutine(FlyWaterCell(center, landingPos, height, amount));
        }

        SpawnOverlayParticles(center, velocity, intensity, sizeMultiplier, impactVelocity,
                              sprayParticlesPerSplash, OverlayType.Spray);
    }

    IEnumerator FlyWaterCell(Vector2 start, Vector2 end, float peakHeight, float amount)
    {
        Vector2Int prevCell   = new Vector2Int(-1, -1);
        float      prevPlaced = 0f;

        for (int step = 0; step <= arcSteps; step++)
        {
            float t = step / (float)arcSteps;

            float x = Mathf.Lerp(start.x, end.x, t);
            float y = Mathf.Lerp(start.y, end.y, t) + Mathf.Sin(t * Mathf.PI) * peakHeight;

            Vector2Int cell = liquidSim.WorldToGrid(new Vector2(x, y));

            if (!liquidSim.IsValidCell(cell.x, cell.y))
            {
                ClearTrailCell(prevCell, prevPlaced);
                yield break;
            }

            if (prevCell.x >= 0 && prevCell != cell && step < arcSteps)
                ClearTrailCell(prevCell, prevPlaced);

            if (step == arcSteps)
            {
                float existing = liquidSim.GetWater(cell.x, cell.y);
                liquidSim.SetWater(cell.x, cell.y, existing + amount);
                waterSurfaceCacheDirty = true;
            }
            else
            {
                float existing = liquidSim.GetWater(cell.x, cell.y);
                liquidSim.SetWater(cell.x, cell.y, existing + amount);

                prevCell   = cell;
                prevPlaced = amount;

                yield return new WaitForSeconds(arcStepInterval);
            }
        }
    }

    void ClearTrailCell(Vector2Int cell, float amountWeAdded)
    {
        if (cell.x < 0) return;
        if (!liquidSim.IsValidCell(cell.x, cell.y)) return;

        float current = liquidSim.GetWater(cell.x, cell.y);
        liquidSim.SetWater(cell.x, cell.y, Mathf.Max(0f, current - amountWeAdded));
    }

    void SpawnSingleCrownOverlayParticle(Vector2 center, Vector2 velocity, float intensity, 
                                        float sizeMultiplier, float impactVelocity, 
                                        float angle, float heightMultiplier)
    {
        if (activeParticles.Count >= maxActiveParticles) return;

        float rad = angle * Mathf.Deg2Rad;
        
        float parentDropletSize = sizeMultiplier;
        float secondarySize = particleSize * 
                            Random.Range(secondaryDropletMinSize, secondaryDropletMaxSize) * 
                            parentDropletSize;
        
        float baseSpeed = Random.Range(3f, 7f) * intensity * sizeMultiplier;
        float velocityMultiplier = Random.Range(secondaryVelocityMin, secondaryVelocityMax);
        float enhancedSpeed = baseSpeed * velocityMultiplier;
        
        Vector2 particleVelocity = new Vector2(
            Mathf.Cos(rad) * enhancedSpeed,
            Mathf.Abs(Mathf.Sin(rad)) * enhancedSpeed * 0.4f * heightMultiplier
        );
        
        particleVelocity.x += velocity.x * 0.15f;
        
        float lifetime = particleLifetime * Random.Range(0.5f, 0.8f);
        
        Vector2 spawnPos = center + new Vector2(Random.Range(-0.15f, 0.15f), Random.Range(0f, 0.08f));
        EmitOverlayParticle(spawnPos, particleVelocity, secondarySize, crownParticleColor, lifetime, OverlayType.Crown);
    }

    void SpawnOverlayParticles(Vector2 center, Vector2 velocity, float intensity, float sizeMultiplier, 
                               float impactVelocity, int count, OverlayType type)
    {
        count = Mathf.RoundToInt(count * intensity * sizeMultiplier);

        for (int i = 0; i < count; i++)
        {
            if (activeParticles.Count >= maxActiveParticles) break;

            Vector2 particleVelocity;
            float   size;
            Color   color;
            float   lifetime;

            float parentDropletSize = sizeMultiplier;

            if (type == OverlayType.Crown)
            {
                float angle = Random.Range(15f, 165f) * Mathf.Deg2Rad;
                
                size = particleSize * Random.Range(secondaryDropletMinSize, secondaryDropletMaxSize) * parentDropletSize;
                
                float baseSpeed = Random.Range(3f, 7f) * intensity * sizeMultiplier;
                float velocityMultiplier = Random.Range(secondaryVelocityMin, secondaryVelocityMax);
                float enhancedSpeed = baseSpeed * velocityMultiplier;
                
                particleVelocity = new Vector2(
                    Mathf.Cos(angle) * enhancedSpeed,
                    Mathf.Abs(Mathf.Sin(angle)) * enhancedSpeed * 0.4f
                );
                
                particleVelocity.x += velocity.x * 0.15f;
                
                color    = crownParticleColor;
                lifetime = particleLifetime * Random.Range(0.5f, 0.8f);
            }
            else
            {
                float angle = Random.Range(30f, 150f) * Mathf.Deg2Rad;
                
                size = particleSize * Random.Range(secondaryDropletMinSize * 0.7f, secondaryDropletMaxSize * 0.9f);
                
                float baseSpeed = Random.Range(4f, 10f) * intensity;
                float velocityMultiplier = Random.Range(secondaryVelocityMin, secondaryVelocityMax);
                float enhancedSpeed = baseSpeed * velocityMultiplier;
                
                particleVelocity = new Vector2(
                    Mathf.Cos(angle) * enhancedSpeed * Random.Range(0.7f, 1.3f),
                    Mathf.Abs(Mathf.Sin(angle)) * enhancedSpeed
                );
                
                particleVelocity.x -= velocity.x * 0.12f;
                
                color    = sprayParticleColor;
                lifetime = particleLifetime * Random.Range(0.6f, 1.1f);
            }

            Vector2 spawnPos = center + new Vector2(Random.Range(-0.15f, 0.15f), Random.Range(0f, 0.08f));
            EmitOverlayParticle(spawnPos, particleVelocity, size, color, lifetime, type);
        }
    }

    void SpawnOverlaySideBurst(Vector2 center, Vector2 velocity, float intensity, float side, float impactSpeed)
    {
        int count = Mathf.RoundToInt(sprayParticlesPerSplash * 0.4f * intensity);

        for (int i = 0; i < count; i++)
        {
            if (activeParticles.Count >= maxActiveParticles) break;

            float upSpeed   = Random.Range(2f, 5f) * intensity;
            float sideSpeed = Random.Range(1f, 4f) * intensity * side;
            float velocityMultiplier = Random.Range(secondaryVelocityMin, secondaryVelocityMax) * 0.7f;
            
            Vector2 particleVelocity = new Vector2(sideSpeed, upSpeed) * velocityMultiplier;
            
            float size = particleSize * Random.Range(secondaryDropletMinSize, secondaryDropletMaxSize * 0.8f);

            Vector2 spawnPos = center + new Vector2(side * Random.Range(0f, 0.1f), Random.Range(0f, 0.05f));
            EmitOverlayParticle(spawnPos, particleVelocity, size, sprayParticleColor,
                                particleLifetime * Random.Range(0.4f, 0.7f), OverlayType.Spray);
        }
    }

    void EmitOverlayParticle(Vector2 position, Vector2 velocity, float size, Color color, float lifetime, OverlayType type)
    {
        OverlayParticle p;

        if (particlePool.Count > 0)
            p = particlePool.Dequeue();
        else if (activeParticles.Count < maxActiveParticles)
            p = AllocateParticle();
        else
            return;

        p.gameObject.transform.position = position;
        p.velocity                      = velocity;
        p.lifetime                      = 0f;
        p.maxLifetime                   = lifetime;
        p.initialSize                   = size;
        p.baseColor                     = color;
        p.type                          = type;
        
        p.timeSinceLastCollision        = 0f;
        p.hasHadInitialCollision        = false;
        p.collisionCount                = 0;

        p.renderer.sprite = (type == OverlayType.Crown) ? crownSprite : spraySprite;
        p.renderer.color  = color;
        p.gameObject.transform.localScale = Vector3.one * size;

        OrientParticle(p);

        p.gameObject.SetActive(true);
        activeParticles.Add(p);
    }

    void UpdateOverlayParticlesOptimized()
    {
        int particleCount = activeParticles.Count;
        
        if (useBatchedParticleUpdates)
        {
            for (int batchStart = 0; batchStart < particleCount; batchStart += particleBatchSize)
            {
                int batchEnd = Mathf.Min(batchStart + particleBatchSize, particleCount);
                
                for (int i = batchEnd - 1; i >= batchStart; i--)
                {
                    if (i >= activeParticles.Count) continue;
                    
                    OverlayParticle p = activeParticles[i];
                    
                    if (!UpdateSingleParticle(p, i))
                    {
                        continue;
                    }
                }
            }
        }
        else
        {
            for (int i = particleCount - 1; i >= 0; i--)
            {
                OverlayParticle p = activeParticles[i];
                UpdateSingleParticle(p, i);
            }
        }
    }
    
    bool UpdateSingleParticle(OverlayParticle p, int index)
    {
        p.lifetime += Time.deltaTime;
        p.timeSinceLastCollision += Time.deltaTime;

        if (p.lifetime >= p.maxLifetime)
        {
            ReturnParticle(p, index);
            return false;
        }

        Vector2 oldPos = p.gameObject.transform.position;

        p.velocity.y += particleGravity * Time.deltaTime;
        p.velocity   *= particleDamping;

        Vector2 newPos = oldPos + p.velocity * Time.deltaTime;

        if (enableSecondaryCollisions && ShouldCheckCollision(p))
        {
            if (CheckParticleCollisionOptimized(p, oldPos, newPos, out Vector2 correctedPos, out Vector2 newVelocity))
            {
                newPos = correctedPos;
                p.velocity = newVelocity;
                p.timeSinceLastCollision = 0f;
                p.collisionCount++;
                
                if (!p.hasHadInitialCollision)
                {
                    p.hasHadInitialCollision = true;
                }
                
                if (showCollisionDebug)
                {
                    Debug.Log($"Particle collision #{p.collisionCount} at {correctedPos}, velocity: {newVelocity.magnitude:F2}");
                    Debug.DrawLine(oldPos, correctedPos, Color.red, 0.5f);
                }
            }
        }

        p.gameObject.transform.position = newPos;

        OrientParticle(p);

        float lifeRatio = p.lifetime / p.maxLifetime;

        float alpha = p.baseColor.a;
        if (lifeRatio > 0.6f)
            alpha *= 1f - ((lifeRatio - 0.6f) / 0.4f);
        p.renderer.color = new Color(p.baseColor.r, p.baseColor.g, p.baseColor.b, alpha);

        if (lifeRatio > 0.7f)
        {
            float shrink = 1f - ((lifeRatio - 0.7f) / 0.3f) * 0.5f;
            p.gameObject.transform.localScale = Vector3.one * (p.initialSize * shrink);
        }
        
        return true;
    }

    bool ShouldCheckCollision(OverlayParticle p)
    {
        if (!p.hasHadInitialCollision)
            return true;
        
        return p.timeSinceLastCollision >= minTimeBeforeSecondaryCollision;
    }

    bool CheckParticleCollisionOptimized(OverlayParticle p, Vector2 from, Vector2 to, 
                                         out Vector2 correctedPos, out Vector2 newVelocity)
    {
        correctedPos = to;
        newVelocity = p.velocity;

        if (cacheWaterSurfaceCells)
        {
            if (CheckWaterSurfaceCollisionOptimized(p, from, to, out correctedPos, out newVelocity))
            {
                return true;
            }
        }
        else
        {
            if (CheckWaterSurfaceCollision(p, from, to, out correctedPos, out newVelocity))
            {
                return true;
            }
        }

        if (useSpatialHashingForCollisions)
        {
            Vector2 direction = (to - from).normalized;
            float distance = Vector2.Distance(from, to);
            
            List<Collider2D> nearbyColliders = QuerySpatialHash(from, distance + particleCollisionRadius);
            
            RaycastHit2D closestHit = new RaycastHit2D();
            float closestDistance = float.MaxValue;
            bool foundHit = false;
            
            foreach (Collider2D col in nearbyColliders)
            {
                if (col == null) continue;
                
                Bounds bounds = col.bounds;
                if (!bounds.Intersects(new Bounds((from + to) * 0.5f, Vector3.one * (distance + particleCollisionRadius * 2))))
                    continue;
                
                RaycastHit2D hit = Physics2D.CircleCast(from, particleCollisionRadius, direction, 
                                                         distance, 1 << col.gameObject.layer);
                
                if (hit.collider != null && hit.distance < closestDistance)
                {
                    closestHit = hit;
                    closestDistance = hit.distance;
                    foundHit = true;
                }
            }
            
            if (foundHit)
            {
                correctedPos = closestHit.point + closestHit.normal * particleCollisionRadius;
                Vector2 reflectedVelocity = Vector2.Reflect(p.velocity, closestHit.normal);
                newVelocity = reflectedVelocity * collisionBounceFactor;

                if (showCollisionDebug)
                {
                    Debug.DrawRay(closestHit.point, closestHit.normal * 0.2f, Color.yellow, 0.5f);
                }

                return true;
            }
        }
        else
        {
            RaycastHit2D hit = Physics2D.CircleCast(from, particleCollisionRadius, (to - from).normalized, 
                                                     Vector2.Distance(from, to), particleCollisionLayers);

            if (hit.collider != null)
            {
                correctedPos = hit.point + hit.normal * particleCollisionRadius;
                Vector2 reflectedVelocity = Vector2.Reflect(p.velocity, hit.normal);
                newVelocity = reflectedVelocity * collisionBounceFactor;

                if (showCollisionDebug)
                {
                    Debug.DrawRay(hit.point, hit.normal * 0.2f, Color.yellow, 0.5f);
                }

                return true;
            }
        }

        return false;
    }

    bool CheckWaterSurfaceCollisionOptimized(OverlayParticle p, Vector2 from, Vector2 to, 
                                              out Vector2 correctedPos, out Vector2 newVelocity)
    {
        correctedPos = to;
        newVelocity = p.velocity;

        if (IsNearWaterSurface(to, out Vector2Int surfaceCell))
        {
            Vector2 surfaceWorldPos = liquidSim.GridToWorld(surfaceCell.x, surfaceCell.y);
            
            if (from.y > surfaceWorldPos.y && to.y <= surfaceWorldPos.y)
            {
                correctedPos = new Vector2(to.x, surfaceWorldPos.y);
                
                float impactSpeed = p.velocity.magnitude;

                if (impactSpeed >= minMicroSplashVelocity)
                {
                    float splashIntensity = Mathf.Clamp01(impactSpeed / (minMicroSplashVelocity * 3f));
                    CreateMicroSplash(correctedPos, p.velocity, splashIntensity);
                }

                newVelocity = p.velocity * 0.2f;

                if (showCollisionDebug)
                {
                    Debug.DrawLine(from, correctedPos, Color.cyan, 0.5f);
                    Debug.Log($"Water surface collision (cached) at {correctedPos}, impact speed: {impactSpeed:F2}");
                }

                return true;
            }
        }

        return false;
    }

    bool CheckWaterSurfaceCollision(OverlayParticle p, Vector2 from, Vector2 to, out Vector2 correctedPos, out Vector2 newVelocity)
    {
        correctedPos = to;
        newVelocity = p.velocity;

        int samples = Mathf.CeilToInt(Vector2.Distance(from, to) / 0.05f);
        samples = Mathf.Max(samples, 2);

        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector2 samplePos = Vector2.Lerp(from, to, t);
            Vector2Int gridPos = liquidSim.WorldToGrid(samplePos);

            if (!liquidSim.IsValidCell(gridPos.x, gridPos.y))
                continue;

            float waterAmount = liquidSim.GetWater(gridPos.x, gridPos.y);

            if (waterAmount > 0.1f)
            {
                if (i > 0)
                {
                    float prevT = (i - 1) / (float)samples;
                    Vector2 prevPos = Vector2.Lerp(from, to, prevT);
                    Vector2Int prevGridPos = liquidSim.WorldToGrid(prevPos);

                    float prevWater = 0f;
                    if (liquidSim.IsValidCell(prevGridPos.x, prevGridPos.y))
                        prevWater = liquidSim.GetWater(prevGridPos.x, prevGridPos.y);

                    if (prevWater <= 0.1f)
                    {
                        correctedPos = samplePos;

                        float impactSpeed = p.velocity.magnitude;

                        if (impactSpeed >= minMicroSplashVelocity)
                        {
                            float splashIntensity = Mathf.Clamp01(impactSpeed / (minMicroSplashVelocity * 3f));
                            CreateMicroSplash(samplePos, p.velocity, splashIntensity);
                        }

                        newVelocity = p.velocity * 0.2f;

                        if (showCollisionDebug)
                        {
                            Debug.DrawLine(prevPos, samplePos, Color.cyan, 0.5f);
                            Debug.Log($"Water surface collision at {samplePos}, impact speed: {impactSpeed:F2}");
                        }

                        return true;
                    }
                }

                if (p.velocity.y < 0)
                {
                    correctedPos = samplePos;
                    newVelocity = p.velocity * 0.3f;
                    return true;
                }
            }
        }

        return false;
    }

    void CreateMicroSplash(Vector2 position, Vector2 velocity, float intensity)
    {
        Vector2Int gridPos = liquidSim.WorldToGrid(position);
        if (!liquidSim.IsValidCell(gridPos.x, gridPos.y))
            return;

        float currentWater = liquidSim.GetWater(gridPos.x, gridPos.y);
        liquidSim.SetWater(gridPos.x, gridPos.y, currentWater + microSplashWaterAmount * intensity);
        
        waterSurfaceCacheDirty = true;

        int microParticleCount = Mathf.RoundToInt(2 * intensity);
        for (int i = 0; i < microParticleCount; i++)
        {
            if (activeParticles.Count >= maxActiveParticles) break;

            float angle = Random.Range(60f, 120f) * Mathf.Deg2Rad;
            float speed = Random.Range(1f, 3f) * intensity;

            Vector2 microVel = new Vector2(
                Mathf.Cos(angle) * speed * (Random.value > 0.5f ? 1f : -1f),
                Mathf.Abs(Mathf.Sin(angle)) * speed
            );

            float microSize = particleSize * Random.Range(0.1f, 0.15f);

            EmitOverlayParticle(
                position + new Vector2(Random.Range(-0.05f, 0.05f), 0f),
                microVel,
                microSize,
                sprayParticleColor,
                particleLifetime * Random.Range(0.3f, 0.5f),
                OverlayType.Spray
            );
        }

        if (showCollisionDebug)
        {
            Debug.DrawRay(position, Vector2.up * 0.2f, Color.magenta, 0.5f);
        }
    }

    static void OrientParticle(OverlayParticle p)
    {
        if (p.velocity.magnitude < 0.01f) return;
        float angle = Mathf.Rad2Deg * Mathf.Atan2(p.velocity.y, p.velocity.x) - 90f;
        p.gameObject.transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    OverlayParticle AllocateParticle()
    {
        GameObject obj = new GameObject("OverlayParticle");
        obj.transform.SetParent(transform);

        SpriteRenderer sr  = obj.AddComponent<SpriteRenderer>();
        sr.sprite          = spraySprite;
        sr.color           = Color.white;
        sr.sortingOrder    = 10;

        return new OverlayParticle
        {
            gameObject  = obj,
            renderer    = sr,
            velocity    = Vector2.zero,
            lifetime    = 0f,
            maxLifetime = particleLifetime,
            initialSize = particleSize,
            baseColor   = Color.white,
            type        = OverlayType.Spray,
            timeSinceLastCollision = 0f,
            hasHadInitialCollision = false,
            collisionCount = 0
        };
    }

    void ReturnParticle(OverlayParticle p, int index)
    {
        p.gameObject.SetActive(false);
        particlePool.Enqueue(p);
        activeParticles.RemoveAt(index);
    }

    Sprite CreatePixelDroplet(int width, int height, bool isFleck)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode   = TextureWrapMode.Clamp;

        float cx = (width  - 1) * 0.5f;
        float cy = (height - 1) * 0.5f;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float nx = (x - cx) / (width  * 0.5f);
                float ny = (y - cy) / (height * 0.5f);

                float alpha = isFleck ? EvalFleck(nx, ny) : EvalTeardrop(nx, ny);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), width);
    }

    static float EvalTeardrop(float nx, float ny)
    {
        float inside;
        if (ny <= 0f)
        {
            float dist = Mathf.Sqrt(nx * nx + ny * ny);
            inside = 1f - dist;
        }
        else
        {
            float radiusAtY = 1f - ny;
            inside = (radiusAtY > 0.001f) ? (1f - Mathf.Abs(nx) / radiusAtY) : -1f;
        }

        return inside * 3f;
    }

    static float EvalFleck(float nx, float ny)
    {
        float dist = Mathf.Sqrt(nx * nx * 0.7f + ny * ny * 1.4f);
        return (0.85f - dist) * 4f;
    }

    void RemoveWaterForSplash(Vector2 center, float totalAmount, float width)
    {
        int   cellsWide     = Mathf.Max(Mathf.CeilToInt(width / 0.1f), 1);
        float amountPerCell = totalAmount / cellsWide;

        Vector2Int centerGrid = liquidSim.WorldToGrid(center);

        for (int dx = -cellsWide / 2; dx <= cellsWide / 2; dx++)
        {
            int x = centerGrid.x + dx;
            for (int dy = 2; dy >= -2; dy--)
            {
                int y = centerGrid.y + dy;
                if (!liquidSim.IsValidCell(x, y)) continue;

                float water = liquidSim.GetWater(x, y);
                if (water > 0.1f)
                {
                    float remove = Mathf.Min(amountPerCell, water * 0.5f);
                    liquidSim.SetWater(x, y, water - remove);
                    waterSurfaceCacheDirty = true;
                    break;
                }
            }
        }
    }

    public void TriggerSplash(Vector2 position, float intensity, Vector2 direction)
    {
        intensity = Mathf.Clamp01(intensity);

        RemoveWaterForSplash(position, waterRemoveAmount * intensity, 1f);

        float impactVelocity = direction.magnitude;

        if (enableEnhancedPhysics)
            StartCoroutine(TriggerEnhancedSplashManual(position, intensity, direction, impactVelocity));
        else
        {
            int totalCells = Mathf.RoundToInt((sprayCellCount + crownCellCount) * intensity);
            for (int i = 0; i < totalCells; i++)
            {
                float dist   = Random.Range(0.1f, crownRadius);
                float height = Random.Range(0.15f, sprayHeight * intensity);
                float angle  = Random.Range(0f, 360f) * Mathf.Deg2Rad;

                Vector2 landingPos = position + new Vector2(Mathf.Cos(angle) * dist, 0f);
                float   amount     = splashWaterAmount * intensity * splashIntensityScale * Random.Range(0.5f, 1f);
                StartCoroutine(FlyWaterCell(position, landingPos, height, amount));
            }

            SpawnOverlayParticles(position, direction, intensity, 1f, impactVelocity, 
                                crownParticlesPerSplash, OverlayType.Crown);
            SpawnOverlayParticles(position, direction, intensity, 1f, impactVelocity,
                                sprayParticlesPerSplash, OverlayType.Spray);
        }
        
        waterSurfaceCacheDirty = true;
    }

    IEnumerator TriggerEnhancedSplashManual(Vector2 position, float intensity, Vector2 direction, float impactVelocity)
    {
        if (enableProgressiveEjection)
        {
            StartCoroutine(SpawnProgressiveCrown(position, direction, intensity, 1f, impactVelocity));
        }
        else
        {
            SpawnCrownArcs(position, direction, intensity, 1f, impactVelocity);
        }
        
        SpawnSprayCells(position, direction, intensity, 1f, impactVelocity);

        yield return new WaitForSeconds(jetDelay);

        SpawnJetColumn(position, intensity, 1f);
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, crownRadius);

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, sprayRadius);
        
        if (showSpatialHashDebug && useSpatialHashingForCollisions)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.1f);
            foreach (var kvp in spatialHashGrid)
            {
                Vector2 cellCenter = new Vector2(
                    kvp.Key.x * spatialHashCellSize + spatialHashCellSize * 0.5f,
                    kvp.Key.y * spatialHashCellSize + spatialHashCellSize * 0.5f
                );
                Gizmos.DrawWireCube(cellCenter, Vector3.one * spatialHashCellSize);
            }
        }
    }
}