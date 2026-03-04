using UnityEngine;
using System.Collections.Generic;

public class RealisticWindManager : MonoBehaviour
{
    public static RealisticWindManager Instance { get; private set; }
    
    [Header("Base Wind Settings")]
    [SerializeField] private bool enableWind = true;
    [SerializeField] private Vector2 baseWindDirection = Vector2.right;
    [SerializeField] private float baseWindStrength = 3f;
    
    [Header("Wind Gusts (Realistic!)")]
    [Tooltip("Enable periodic wind gusts")]
    [SerializeField] private bool enableGusts = true;
    [Tooltip("How often gusts occur (seconds)")]
    [SerializeField] private float gustFrequency = 8f;
    [Tooltip("Duration of a gust")]
    [SerializeField] private float gustDuration = 2f;
    [Tooltip("How much stronger gusts are (multiplier)")]
    [Range(1f, 3f)]
    [SerializeField] private float gustStrengthMultiplier = 2f;
    [Tooltip("Randomness in gust timing")]
    [SerializeField] private float gustRandomness = 0.3f;
    
    [Header("Wind Lulls (Calm Periods)")]
    [Tooltip("Enable calm periods between gusts")]
    [SerializeField] private bool enableLulls = true;
    [Tooltip("Minimum wind strength during lulls")]
    [Range(0f, 1f)]
    [SerializeField] private float lullStrengthMultiplier = 0.4f;
    
    [Header("Layered Turbulence (Multi-Scale Noise)")]
    [Tooltip("Use multiple noise frequencies for realistic variation")]
    [SerializeField] private bool useLayeredTurbulence = true;
    
    [Tooltip("Large-scale wind shifts (slow)")]
    [SerializeField] private float macroTurbulenceFrequency = 0.5f;
    [SerializeField] private float macroTurbulenceAmount = 0.4f;
    
    [Tooltip("Medium-scale variations")]
    [SerializeField] private float mesoTurbulenceFrequency = 2f;
    [SerializeField] private float mesoTurbulenceAmount = 0.25f;
    
    [Tooltip("Small-scale fluctuations (fast)")]
    [SerializeField] private float microTurbulenceFrequency = 5f;
    [SerializeField] private float microTurbulenceAmount = 0.15f;
    
    [Header("Direction Changes")]
    [Tooltip("Wind direction gradually shifts over time")]
    [SerializeField] private bool enableDirectionShift = true;
    [Tooltip("How much direction can shift (degrees)")]
    [Range(0f, 180f)]
    [SerializeField] private float maxDirectionShift = 30f;
    [Tooltip("How fast direction changes")]
    [SerializeField] private float directionShiftSpeed = 0.5f;
    
    [Header("Height-Based Wind")]
    [Tooltip("Wind gets stronger at higher altitudes")]
    [SerializeField] private bool enableHeightWind = true;
    [Tooltip("Reference Y position (ground level)")]
    [SerializeField] private float groundLevel = 0f;
    [Tooltip("Height at which wind reaches maximum")]
    [SerializeField] private float maxWindHeight = 10f;
    [Tooltip("Wind strength multiplier at max height")]
    [Range(1f, 3f)]
    [SerializeField] private float heightMultiplier = 1.5f;
    
    [Header("Ground Turbulence & Updrafts")]
    [SerializeField] private bool enableGroundUpdrafts = true;
    [Tooltip("Max height where ground updrafts can occur")]
    [SerializeField] private float groundUpdraftHeight = 1.2f;
    [Tooltip("Strength of upward gust lift")]
    [SerializeField] private float groundUpdraftStrength = 1.5f;
    [Tooltip("How often updrafts appear (noise scale)")]
    [SerializeField] private float groundUpdraftFrequency = 2.5f;
    
[SerializeField] private float smoothedUpdraft = 0f;
    
    [Header("Wind Reflection/Deflection")]
    [Tooltip("Enable wind reflection off obstacles")]
    [SerializeField] private bool enableWindReflection = true;
    [Tooltip("How much wind bounces off obstacles (0=none, 1=full reflection)")]
    [Range(0f, 1f)]
    [SerializeField] private float reflectionStrength = 0.7f;
    [Tooltip("Distance around obstacle where reflected wind is felt")]
    [SerializeField] private float reflectionRadius = 1.5f;
    [Tooltip("Number of sample points per unit length of surface")]
    [SerializeField] private float surfaceSampleDensity = 2f;
    [Tooltip("Layers that block/reflect wind")]
    [SerializeField] private LayerMask windBlockingLayers = ~0;
    [Tooltip("Cache reflection data for performance")]
    [SerializeField] private bool cacheReflections = true;
    [Tooltip("How often to update reflection cache (seconds)")]
    [SerializeField] private float reflectionCacheUpdateInterval = 0.1f;
    
    [Header("Audio")]
    [Tooltip("Play wind sound that matches intensity")]
    [SerializeField] private bool enableWindAudio = true;
    [SerializeField] private AudioClip windLoopSound;
    [Range(0f, 1f)]
    [SerializeField] private float audioVolume = 0.5f;

    
    [Header("Safety & Performance")]
    [SerializeField] private float maxWindForce = 10f;
    [SerializeField] private float warningForceThreshold = 5f;
    
    [Header("Visual")]
    [Range(0.1f, 5f)]
    [SerializeField] private float visualSpeedMultiplier = 1f;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool showDebugLogs = false;
    [SerializeField] private bool showReflectionGizmos = true;
    
    private Vector2 currentWindVelocity;
    private float currentWindStrength;
    
    private float macroTimer = 0f;
    private float mesoTimer = 0f;
    private float microTimer = 0f;
    private float directionTimer = 0f;
    
    private float gustTimer = 0f;
    private float nextGustTime = 0f;
    private bool isGusting = false;
    private float gustStartTime = 0f;
    private float gustTargetStrength = 1f;
    
    private struct ReflectionData
    {
        public Vector2 position;
        public Vector2 reflectedWind;
        public Vector2 normal;
        public float timestamp;
    }
    
    private struct ObstacleData
    {
        public Collider2D collider;
        public List<ReflectionData> surfacePoints;
    }
    
    private List<ObstacleData> obstacleCache = new List<ObstacleData>();
    private float reflectionCacheTimer = 0f;
    
    private CellularLiquidSimulation liquidSim;
    private List<Rigidbody2D> cachedRigidbodies = new List<Rigidbody2D>();
    private float rebuildCacheTimer = 0f;
    private const float CACHE_REBUILD_INTERVAL = 1f;
    
    private AudioSource windAudioSource;
    
    public Vector2 CurrentWind => currentWindVelocity;
    public float CurrentStrength => currentWindStrength;
    public float VisualWindSpeed => currentWindVelocity.magnitude * visualSpeedMultiplier;
    public Vector2 VisualWindVelocity => currentWindVelocity * visualSpeedMultiplier;
    public float GroundLevel => groundLevel;
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    private bool loggingYPositive = false;
    private float debugLogStartTime = 0f;
    private float debugLogDuration = 20f;
    
    void Start()
    {
        liquidSim = FindObjectOfType<CellularLiquidSimulation>();
        currentWindVelocity = baseWindDirection.normalized * baseWindStrength;
        currentWindStrength = baseWindStrength;
        RebuildRigidbodyCache();
        
        if (enableWindAudio)
        {
            SetupWindAudio();
        }
        
        nextGustTime = Random.Range(gustFrequency * 0.5f, gustFrequency * 1.5f);
        
        if (showDebugLogs)
        {
            Debug.Log($"Realistic Wind System initialized: Base Strength={baseWindStrength}, Reflection={enableWindReflection}");
        }

        loggingYPositive = true;
        debugLogStartTime = Time.time;
    }
    
    void Update()
    {
        if (!enableWind) return;

        UpdateRealisticWindPattern();
        
        if (enableWindReflection && cacheReflections)
        {
            reflectionCacheTimer += Time.deltaTime;
            if (reflectionCacheTimer >= reflectionCacheUpdateInterval)
            {
                reflectionCacheTimer = 0f;
                UpdateReflectionCache();
            }
        }
        
        if (loggingYPositive)
        {
            if (Time.time - debugLogStartTime <= debugLogDuration)
            {
                if (currentWindVelocity.y > 0f)
                {
                    Debug.Log($"[Y-Positive] Wind Y={currentWindVelocity.y:F2} at time {Time.time - debugLogStartTime:F2}s");
                }
            }
            else
            {
                loggingYPositive = false;
                Debug.Log("Stopped Y-positive wind logging after 20 seconds.");
            }
        }
        
        if (enableWindAudio && windAudioSource != null)
        {
            UpdateWindAudio();
        }
        
        rebuildCacheTimer += Time.deltaTime;
        if (rebuildCacheTimer >= CACHE_REBUILD_INTERVAL)
        {
            rebuildCacheTimer = 0f;
            RebuildRigidbodyCache();
        }
        
    }
    
    void FixedUpdate()
    {
        if (enableWind)
        {
            ApplyWindToRigidbodies();
        }
    }
    
    void UpdateRealisticWindPattern()
    {
        Vector2 turbulenceOffset = CalculateLayeredTurbulence();
        float directionShift = CalculateDirectionShift();
        float gustMultiplier = CalculateGustMultiplier();
        
        Vector2 direction = baseWindDirection.normalized;
        
        if (enableDirectionShift)
        {
            float angle = Mathf.Atan2(direction.y, direction.x);
            angle += directionShift * Mathf.Deg2Rad;
            direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
        
        direction += turbulenceOffset;
        direction.Normalize();
        
        currentWindStrength = baseWindStrength * gustMultiplier;
        currentWindVelocity = direction * currentWindStrength;
        
        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            Debug.Log($"Wind: Dir={direction}, Strength={currentWindStrength:F2}, Gust={isGusting}");
        }
    }
    
    Vector2 CalculateLayeredTurbulence()
    {
        if (!useLayeredTurbulence)
        {
            return Vector2.zero;
        }
        
        Vector2 totalTurbulence = Vector2.zero;
        
        macroTimer += Time.deltaTime * macroTurbulenceFrequency;
        float macroX = Mathf.PerlinNoise(macroTimer, 0f) - 0.5f;
        float macroY = Mathf.PerlinNoise(0f, macroTimer) - 0.5f;
        totalTurbulence += new Vector2(macroX, macroY) * macroTurbulenceAmount;
        
        mesoTimer += Time.deltaTime * mesoTurbulenceFrequency;
        float mesoX = Mathf.PerlinNoise(mesoTimer * 2f, 5f) - 0.5f;
        float mesoY = Mathf.PerlinNoise(5f, mesoTimer * 2f) - 0.5f;
        totalTurbulence += new Vector2(mesoX, mesoY) * mesoTurbulenceAmount;
        
        microTimer += Time.deltaTime * microTurbulenceFrequency;
        float microX = Mathf.PerlinNoise(microTimer * 5f, 10f) - 0.5f;
        float microY = Mathf.PerlinNoise(10f, microTimer * 5f) - 0.5f;
        totalTurbulence += new Vector2(microX, microY) * microTurbulenceAmount;
        
        return totalTurbulence;
    }


Vector2 CalculateGroundUpdraft(Vector2 position)
{
    if (!enableGroundUpdrafts)
        return Vector2.zero;

    float height = position.y - groundLevel;
    if (height < 0f || height > groundUpdraftHeight)
    {
        smoothedUpdraft = Mathf.Lerp(smoothedUpdraft, 0f, Time.deltaTime * 3f);
        return Vector2.up * smoothedUpdraft;
    }

    float heightFactor = 1f - (height / groundUpdraftHeight);
    float noise = Mathf.PerlinNoise(Time.time * groundUpdraftFrequency, position.x * 0.5f);

    float targetLift = 0f;
    if (noise > 0.5f && isGusting)
    {
        float ramp = (noise - 0.5f) * 2f;
        targetLift = ramp * groundUpdraftStrength * heightFactor;
    }
    
    smoothedUpdraft = Mathf.Lerp(smoothedUpdraft, targetLift, Time.deltaTime * 2f);
    
    return Vector2.up * smoothedUpdraft;
}
    
    float CalculateDirectionShift()
    {
        if (!enableDirectionShift)
        {
            return 0f;
        }
        
        directionTimer += Time.deltaTime * directionShiftSpeed;
        float noise = Mathf.PerlinNoise(directionTimer, 100f) - 0.5f;
        return noise * maxDirectionShift * 2f;
    }
    
    float CalculateGustMultiplier()
    {
        if (!enableGusts)
        {
            return 1f;
        }
        
        gustTimer += Time.deltaTime;
        
        if (!isGusting && gustTimer >= nextGustTime)
        {
            StartGust();
        }
        
        if (isGusting)
        {
            float gustProgress = (Time.time - gustStartTime) / gustDuration;
            
            if (gustProgress >= 1f)
            {
                EndGust();
                return enableLulls ? lullStrengthMultiplier : 1f;
            }
            else
            {
                float gustCurve = Mathf.Sin(gustProgress * Mathf.PI);
                float lullStrength = enableLulls ? lullStrengthMultiplier : 1f;
                return Mathf.Lerp(lullStrength, gustTargetStrength, gustCurve);
            }
        }
        else
        {
            if (enableLulls)
            {
                float timeSinceGust = gustTimer - nextGustTime;
                if (timeSinceGust < 1f)
                {
                    return Mathf.Lerp(gustTargetStrength, lullStrengthMultiplier, timeSinceGust);
                }
                return lullStrengthMultiplier;
            }
            return 1f;
        }
    }
    
    void StartGust()
    {
        isGusting = true;
        gustStartTime = Time.time;
        gustTargetStrength = gustStrengthMultiplier * Random.Range(0.8f, 1.2f);
        
        if (showDebugLogs)
        {
            Debug.Log($"Wind gust starting! Strength: {gustTargetStrength:F2}x");
        }
    }
    
    void EndGust()
    {
        isGusting = false;
        gustTimer = 0f;
        
        float randomFactor = Random.Range(1f - gustRandomness, 1f + gustRandomness);
        nextGustTime = gustFrequency * randomFactor;
        
        if (showDebugLogs)
        {
            Debug.Log($"Wind gust ended. Next gust in {nextGustTime:F1}s");
        }
    }
    
    Vector2 CalculateWindReflection(Vector2 position, Vector2 incomingWind)
    {
        if (!enableWindReflection || incomingWind.magnitude < 0.1f)
        {
            return Vector2.zero;
        }
        
        Vector2 totalReflection = Vector2.zero;
        
        Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(position, reflectionRadius * 2f, windBlockingLayers);
        
        foreach (Collider2D collider in nearbyColliders)
        {
            Vector2 closestPoint = collider.ClosestPoint(position);
            float distanceToSurface = Vector2.Distance(position, closestPoint);
            
            if (distanceToSurface > reflectionRadius) continue;
            
            Vector2 surfaceNormal = GetSurfaceNormal(collider, closestPoint, position);
            
            float windDotNormal = Vector2.Dot(incomingWind.normalized, -surfaceNormal);
            if (windDotNormal <= 0) continue;
            
            Vector2 reflectedDir = Vector2.Reflect(incomingWind.normalized, surfaceNormal);
            
            float falloff = 1f - (distanceToSurface / reflectionRadius);
            falloff = falloff * falloff;
            
            float angleFactor = windDotNormal;
            
            Vector2 reflectedWind = reflectedDir * incomingWind.magnitude * reflectionStrength * falloff * angleFactor;
            totalReflection += reflectedWind;
        }
        
        return totalReflection;
    }
    
    Vector2 GetSurfaceNormal(Collider2D collider, Vector2 surfacePoint, Vector2 fromPosition)
    {
        Vector2 toSurface = (surfacePoint - fromPosition).normalized;
        RaycastHit2D hit = Physics2D.Raycast(fromPosition, toSurface, Vector2.Distance(fromPosition, surfacePoint) + 0.1f, windBlockingLayers);
        
        if (hit.collider == collider)
        {
            return hit.normal;
        }
        
        return (fromPosition - surfacePoint).normalized;
    }
    
    void UpdateReflectionCache()
    {
        obstacleCache.Clear();
        
        Collider2D[] obstacles = Physics2D.OverlapCircleAll(transform.position, 50f, windBlockingLayers);
        
        foreach (Collider2D obstacle in obstacles)
        {
            ObstacleData obstacleData = new ObstacleData
            {
                collider = obstacle,
                surfacePoints = new List<ReflectionData>()
            };
            
            Bounds bounds = obstacle.bounds;
            float perimeter = CalculateApproximatePerimeter(bounds);
            int sampleCount = Mathf.Max(4, Mathf.CeilToInt(perimeter * surfaceSampleDensity));
            
            if (obstacle is BoxCollider2D)
            {
                SampleBoxColliderSurface(obstacle as BoxCollider2D, obstacleData.surfacePoints, sampleCount);
            }
            else if (obstacle is CircleCollider2D)
            {
                SampleCircleColliderSurface(obstacle as CircleCollider2D, obstacleData.surfacePoints, sampleCount);
            }
            else if (obstacle is PolygonCollider2D)
            {
                SamplePolygonColliderSurface(obstacle as PolygonCollider2D, obstacleData.surfacePoints, sampleCount);
            }
            else
            {
                SampleGenericColliderSurface(obstacle, obstacleData.surfacePoints, sampleCount);
            }
            
            if (obstacleData.surfacePoints.Count > 0)
            {
                obstacleCache.Add(obstacleData);
            }
        }
        
        if (showDebugLogs)
        {
            int totalPoints = 0;
            foreach (var obs in obstacleCache)
            {
                totalPoints += obs.surfacePoints.Count;
            }
            Debug.Log($"Wind reflection cache updated: {obstacleCache.Count} obstacles, {totalPoints} surface points");
        }
    }
    
    void SampleBoxColliderSurface(BoxCollider2D boxCollider, List<ReflectionData> surfacePoints, int sampleCount)
    {
        Vector2 size = boxCollider.size;
        Vector2 offset = boxCollider.offset;
        Transform transform = boxCollider.transform;
        
        int samplesPerEdge = sampleCount / 4;
        
        for (int i = 0; i <= samplesPerEdge; i++)
        {
            float t = (float)i / samplesPerEdge;
            Vector2 localPos = new Vector2(Mathf.Lerp(-size.x / 2, size.x / 2, t), size.y / 2) + offset;
            AddSurfacePoint(transform, localPos, Vector2.up, surfacePoints);
        }
        
        for (int i = 0; i <= samplesPerEdge; i++)
        {
            float t = (float)i / samplesPerEdge;
            Vector2 localPos = new Vector2(Mathf.Lerp(-size.x / 2, size.x / 2, t), -size.y / 2) + offset;
            AddSurfacePoint(transform, localPos, Vector2.down, surfacePoints);
        }
        
        for (int i = 0; i <= samplesPerEdge; i++)
        {
            float t = (float)i / samplesPerEdge;
            Vector2 localPos = new Vector2(-size.x / 2, Mathf.Lerp(-size.y / 2, size.y / 2, t)) + offset;
            AddSurfacePoint(transform, localPos, Vector2.left, surfacePoints);
        }
        
        for (int i = 0; i <= samplesPerEdge; i++)
        {
            float t = (float)i / samplesPerEdge;
            Vector2 localPos = new Vector2(size.x / 2, Mathf.Lerp(-size.y / 2, size.y / 2, t)) + offset;
            AddSurfacePoint(transform, localPos, Vector2.right, surfacePoints);
        }
    }
    
    void SampleCircleColliderSurface(CircleCollider2D circleCollider, List<ReflectionData> surfacePoints, int sampleCount)
    {
        float radius = circleCollider.radius;
        Vector2 offset = circleCollider.offset;
        Transform transform = circleCollider.transform;
        
        for (int i = 0; i < sampleCount; i++)
        {
            float angle = (float)i / sampleCount * 2f * Mathf.PI;
            Vector2 localPos = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius + offset;
            Vector2 localNormal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            AddSurfacePoint(transform, localPos, localNormal, surfacePoints);
        }
    }
    
    void SamplePolygonColliderSurface(PolygonCollider2D polygonCollider, List<ReflectionData> surfacePoints, int sampleCount)
    {
        Transform transform = polygonCollider.transform;
        Vector2[] points = polygonCollider.points;
        
        if (points.Length < 2) return;
        
        float totalLength = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            int nextI = (i + 1) % points.Length;
            totalLength += Vector2.Distance(points[i], points[nextI]);
        }
        
        int samplesPerUnit = Mathf.Max(1, sampleCount / Mathf.Max(1, (int)totalLength));
        
        for (int i = 0; i < points.Length; i++)
        {
            int nextI = (i + 1) % points.Length;
            Vector2 start = points[i];
            Vector2 end = points[nextI];
            float edgeLength = Vector2.Distance(start, end);
            int edgeSamples = Mathf.Max(1, Mathf.CeilToInt(edgeLength * samplesPerUnit));
            
            Vector2 edgeDir = (end - start).normalized;
            Vector2 edgeNormal = new Vector2(-edgeDir.y, edgeDir.x);
            
            for (int j = 0; j <= edgeSamples; j++)
            {
                float t = (float)j / edgeSamples;
                Vector2 localPos = Vector2.Lerp(start, end, t);
                AddSurfacePoint(transform, localPos, edgeNormal, surfacePoints);
            }
        }
    }
    
    void SampleGenericColliderSurface(Collider2D collider, List<ReflectionData> surfacePoints, int sampleCount)
    {
        Bounds bounds = collider.bounds;
        float perimeter = (bounds.size.x + bounds.size.y) * 2f;
        
        for (int i = 0; i < sampleCount; i++)
        {
            float angle = (float)i / sampleCount * 2f * Mathf.PI;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 testPoint = (Vector2)bounds.center + direction * Mathf.Max(bounds.size.x, bounds.size.y);
            
            Vector2 closestPoint = collider.ClosestPoint(testPoint);
            Vector2 normal = (testPoint - closestPoint).normalized;
            
            ReflectionData data = new ReflectionData
            {
                position = closestPoint,
                normal = normal,
                reflectedWind = Vector2.Reflect(currentWindVelocity.normalized, normal) * currentWindStrength * reflectionStrength,
                timestamp = Time.time
            };
            
            surfacePoints.Add(data);
        }
    }
    
    void AddSurfacePoint(Transform transform, Vector2 localPos, Vector2 localNormal, List<ReflectionData> surfacePoints)
    {
        Vector2 worldPos = transform.TransformPoint(localPos);
        Vector2 worldNormal = transform.TransformDirection(localNormal).normalized;
        
        float windDotNormal = Vector2.Dot(currentWindVelocity.normalized, -worldNormal);
        if (windDotNormal <= 0.1f) return;
        
        Vector2 reflectedDir = Vector2.Reflect(currentWindVelocity.normalized, worldNormal);
        Vector2 reflectedWind = reflectedDir * currentWindStrength * reflectionStrength;
        
        ReflectionData data = new ReflectionData
        {
            position = worldPos,
            normal = worldNormal,
            reflectedWind = reflectedWind,
            timestamp = Time.time
        };
        
        surfacePoints.Add(data);
    }
    
    float CalculateApproximatePerimeter(Bounds bounds)
    {
        return (bounds.size.x + bounds.size.y) * 2f;
    }
    
    Vector2 GetCachedReflectedWind(Vector2 position)
    {
        Vector2 totalReflection = Vector2.zero;
        
        foreach (ObstacleData obstacle in obstacleCache)
        {
            foreach (ReflectionData data in obstacle.surfacePoints)
            {
                float distance = Vector2.Distance(position, data.position);
                
                if (distance < reflectionRadius)
                {
                    float falloff = 1f - (distance / reflectionRadius);
                    falloff = falloff * falloff;
                    
                    totalReflection += data.reflectedWind * falloff;
                }
            }
        }
        
        return totalReflection;
    }
    
    public Vector2 GetWindAtPosition(Vector2 position)
    {
        WindZone2D[] windZones = FindObjectsOfType<WindZone2D>();
        foreach (WindZone2D zone in windZones)
        {
            if (zone.IsPositionInZone(position))
            {
                return zone.GetWindVelocity();
            }
        }

        Vector2 wind = currentWindVelocity;

        if (enableHeightWind)
        {
            float height = position.y - groundLevel;
            float heightRatio = Mathf.Clamp01(height / maxWindHeight);
            float heightBoost = Mathf.Lerp(1f, heightMultiplier, heightRatio);
            wind *= heightBoost;
        }

        wind += CalculateGroundUpdraft(position);
        
        if (enableWindReflection)
        {
            Vector2 reflectedWind;
            
            if (cacheReflections)
            {
                reflectedWind = GetCachedReflectedWind(position);
            }
            else
            {
                reflectedWind = CalculateWindReflection(position, wind);
            }
            
            wind += reflectedWind;
        }

        return wind;
    }
    
    void ApplyWindToRigidbodies()
    {
        cachedRigidbodies.RemoveAll(rb => rb == null);
        
        foreach (Rigidbody2D rb in cachedRigidbodies)
        {
            if (rb == null || !rb.gameObject.activeInHierarchy) continue;
            
            WindAffected windAffected = rb.GetComponent<WindAffected>();
            
            if (windAffected != null && !windAffected.IsAffectedByWind())
            {
                continue;
            }
            
            Vector2 windForce = GetWindForceForObject(rb, windAffected);
            rb.AddForce(windForce, ForceMode2D.Force);
        }
    }
    
    Vector2 GetWindForceForObject(Rigidbody2D rb, WindAffected windAffected)
    {
        float dragCoefficient = windAffected != null ? windAffected.GetDragCoefficient() : 0.5f;
        float surfaceArea = windAffected != null ? windAffected.GetSurfaceArea() : 1f;
        
        Vector2 windAtPosition = GetWindAtPosition(rb.position);
        Vector2 relativeWind = windAtPosition - rb.linearVelocity;
        float windSpeed = relativeWind.magnitude;
        
        if (windSpeed < 0.01f) return Vector2.zero;
        
        Vector2 windDirection = relativeWind.normalized;
        float forceMagnitude = 0.1f * dragCoefficient * surfaceArea * windSpeed * windSpeed;
        
        if (windAffected != null)
        {
            forceMagnitude *= windAffected.GetWindMultiplier();
        }
        
        Vector2 finalForce = windDirection * forceMagnitude;
        
        if (float.IsNaN(finalForce.x) || float.IsNaN(finalForce.y))
        {
            return Vector2.zero;
        }
        
        if (finalForce.magnitude > maxWindForce)
        {
            finalForce = finalForce.normalized * maxWindForce;
        }
        
        return finalForce;
    }
    

    
    void RebuildRigidbodyCache()
    {
        cachedRigidbodies.Clear();
        Rigidbody2D[] allRbs = FindObjectsOfType<Rigidbody2D>();
        
        foreach (Rigidbody2D rb in allRbs)
        {
            if (rb != null && rb.bodyType != RigidbodyType2D.Static)
            {
                int layer = rb.gameObject.layer;
                if (((1 << layer)) != 0)
                {
                    cachedRigidbodies.Add(rb);
                }
            }
        }
    }
    
    void SetupWindAudio()
    {
        if (windLoopSound == null) return;
        
        windAudioSource = gameObject.AddComponent<AudioSource>();
        windAudioSource.clip = windLoopSound;
        windAudioSource.loop = true;
        windAudioSource.volume = 0f;
        windAudioSource.spatialBlend = 0f;
        windAudioSource.Play();
    }
    
    void UpdateWindAudio()
    {
        if (windAudioSource == null) return;
        
        float targetVolume = (currentWindStrength / baseWindStrength) * audioVolume;
        windAudioSource.volume = Mathf.Lerp(windAudioSource.volume, targetVolume, Time.deltaTime * 2f);
        
        float targetPitch = 0.9f + (currentWindStrength / (baseWindStrength * 3f)) * 0.4f;
        windAudioSource.pitch = Mathf.Lerp(windAudioSource.pitch, targetPitch, Time.deltaTime);
    }
    
    public void SetWind(Vector2 direction, float strength)
    {
        baseWindDirection = direction.normalized;
        baseWindStrength = strength;
    }
    
    public void SetWindEnabled(bool enabled)
    {
        enableWind = enabled;
    }
    
    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;
        
        Vector3 center = transform.position;
        Vector3 windEnd = center + (Vector3)currentWindVelocity.normalized * (2f + currentWindStrength * 0.5f);
        
        Gizmos.color = isGusting ? Color.red : Color.cyan;
        Gizmos.DrawLine(center, windEnd);
        
        Vector3 arrowDir = (windEnd - center).normalized;
        Vector3 arrowLeft = Quaternion.Euler(0, 0, 30) * arrowDir;
        Vector3 arrowRight = Quaternion.Euler(0, 0, -30) * arrowDir;
        
        Gizmos.DrawLine(windEnd, windEnd - arrowLeft * 0.5f);
        Gizmos.DrawLine(windEnd, windEnd - arrowRight * 0.5f);
        
        float radius = currentWindStrength * 0.2f;
        Gizmos.DrawWireSphere(center, radius);
        
        if (isGusting)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(center, radius * 1.5f);
        }
        
        if (showReflectionGizmos && enableWindReflection && obstacleCache.Count > 0)
        {
            foreach (ObstacleData obstacle in obstacleCache)
            {
                foreach (ReflectionData data in obstacle.surfacePoints)
                {
                    Vector3 pos = new Vector3(data.position.x, data.position.y, 0);
                    
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawSphere(pos, 0.05f);
                    
                    Gizmos.color = Color.blue;
                    Gizmos.DrawLine(pos, pos + (Vector3)data.normal * 0.3f);
                    
                    Vector3 reflectedEnd = pos + (Vector3)data.reflectedWind.normalized * 0.5f;
                    Gizmos.color = new Color(1f, 0.5f, 0f);
                    Gizmos.DrawLine(pos, reflectedEnd);
                    
                    Gizmos.color = new Color(1f, 1f, 0f, 0.1f);
                    Gizmos.DrawWireSphere(pos, reflectionRadius);
                }
            }
        }
    }
}