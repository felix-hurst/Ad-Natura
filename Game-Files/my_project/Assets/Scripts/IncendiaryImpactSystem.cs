using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Incendiary Impact System W/ Backblast and burning particles
/// </summary>
public class IncendiaryImpactSystem : MonoBehaviour
{

    //  Thermite Config
    
    [Header("Thermite Configuration")]
    [Tooltip("Thermite burns at 2000-2500°C creating molten metal spray")]
    [SerializeField] private float thermiteTemperature = 2200f;

    //  Impact Detection

    [Header("Detection Settings")]
    [Tooltip("Minimum impact velocity to create incendiary effect (m/s)")]
    [SerializeField] private float minImpactVelocity = 5f;
    [Tooltip("Velocity for maximum effect intensity")]
    [SerializeField] private float maxImpactVelocity = 20f;
    [Tooltip("Layers that trigger incendiary effects on collision")]
    [SerializeField] private LayerMask impactLayers = ~0;
    [Tooltip("Only trigger effect once, then destroy this component")]
    [SerializeField] private bool triggerOnce = true;
    
    private bool hasTriggered = false;

    //  Impact Ejecta Plume
    
    [Header("Impact Ejecta Plume (Backwards Blast)")]
    [Tooltip("Enable realistic impact ejecta plume that blasts backwards")]
    [SerializeField] private bool enableImpactPlume = true;
    [Tooltip("Number of ejecta particles in backwards plume")]
    [SerializeField] private int ejectaParticleCount = 60;
    [Tooltip("Ejecta cone angle (degrees) - concentrated around 45-50° from impact axis")]
    [SerializeField] private float ejectaConeAngle = 50f;
    [Tooltip("Cone angle variation (degrees) - creates spread")]
    [SerializeField] private float ejectaConeSpread = 20f;
    [Tooltip("Ejecta velocity as fraction of impact velocity (0-1)")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float ejectaVelocityFraction = 0.5f;
    [Tooltip("Random velocity variation for ejecta")]
    [Range(0f, 1f)]
    [SerializeField] private float ejectaVelocityRandomness = 0.4f;
    [Tooltip("Ejecta particle size (dust/debris)")]
    [SerializeField] private float ejectaParticleSize = 0.1f;
    [Tooltip("Size variation for ejecta particles")]
    [Range(0f, 1f)]
    [SerializeField] private float ejectaSizeVariation = 0.7f;
    [Tooltip("Ejecta particle lifetime (seconds)")]
    [SerializeField] private float ejectaLifetime = 2.0f;
    [Tooltip("Color of ejecta cloud (dust/debris color)")]
    [SerializeField] private Color ejectaColor = new Color(0.55f, 0.45f, 0.35f, 0.85f);
    [Tooltip("Enable ejecta particles to fade and expand")]
    [SerializeField] private bool ejectaFadeAndExpand = true;
    [Tooltip("Gravity applied to ejecta (lighter dust falls slower)")]
    [SerializeField] private float ejectaGravity = -5f;
    [Tooltip("Air resistance for ejecta (higher = more drag)")]
    [Range(0.85f, 0.99f)]
    [SerializeField] private float ejectaAirResistance = 0.94f;
    [Tooltip("Spawn offset from impact point along surface normal (closer to wall)")]
    [Range(0.01f, 0.3f)]
    [SerializeField] private float ejectaSpawnOffset = 0.08f;
    [Tooltip("Random scatter around spawn point")]
    [Range(0f, 0.2f)]
    [SerializeField] private float ejectaSpawnScatter = 0.12f;

    
    [Header("Initial Burst (Impact Scatter)")]
    [Tooltip("Number of molten particles ejected on impact")]
    [SerializeField] private int burstParticleCount = 25;
    [Tooltip("Horizontal spray distance (meters) - thermite can spray 2-5m")]
    [SerializeField] private float horizontalSprayDistance = 3f;
    [Tooltip("Maximum vertical spray height (meters)")]
    [SerializeField] private float verticalSprayHeight = 1.5f;
    [Tooltip("Base particle size (world units)")]
    [SerializeField] private float particleSize = 0.08f;
    [Tooltip("Random size variation (0-1)")]
    [Range(0f, 1f)]
    [SerializeField] private float sizeVariation = 0.4f;
    

    
    [Header("Burning Behavior")]
    [Tooltip("Short-lived spark particles (seconds) - most particles")]
    [SerializeField] private float sparkLifetime = 0.5f;
    [Tooltip("Long-lived smoke-emitting particles (seconds) - fewer particles")]
    [SerializeField] private float smokeEmitterLifetime = 2f;
    [Tooltip("Percentage of particles that emit smoke (0-1)")]
    [Range(0f, 1f)]
    [SerializeField] private float smokeEmitterRatio = 0.2f;
    [Tooltip("Enable particles to ignite flammable objects (requires FireSpreadSystem in scene)")]
    [SerializeField] private bool enableIgnition = true;
    [Tooltip("Reference to FireSpreadSystem for realistic fire spreading")]
    [SerializeField] private FireSpreadSystem fireSpreadSystem;
    [Tooltip("Enable particle stick/embed behavior (molten metal sticks to surfaces)")]
    [SerializeField] private bool enableStickyParticles = true;

    [Header("Visual Appearance")]
    [Tooltip("Core color of burning particles")]
    [SerializeField] private Color coreColor = new Color(1f, 0.9f, 0.7f, 1f); // Bright yellow-white
    
    [Tooltip("Smoke color")]
    [SerializeField] private Color smokeColor = new Color(0.9f, 0.9f, 0.9f, 0.7f);
    [Tooltip("Enable intense brightness flicker (thermite sparkle effect)")]
    [SerializeField] private bool enableFlicker = true;
    [Tooltip("Flicker intensity")]
    [Range(0f, 1f)]
    [SerializeField] private float flickerIntensity = 0.3f;
    [Tooltip("Flicker speed")]
    [SerializeField] private float flickerSpeed = 15f;

    [Header("Glow & Lighting")]
    [Tooltip("Enable dynamic 2D lighting (requires Universal Render Pipeline)")]
    [SerializeField] private bool enableDynamicLighting = true;
    [Tooltip("Light intensity for each particle")]
    [SerializeField] private float lightIntensity = 1.5f;
    [Tooltip("Light radius (world units)")]
    [SerializeField] private float lightRadius = 2.0f;
    [Tooltip("Enable glow trails behind particles")]
    [SerializeField] private bool enableGlowTrails = true;
    [Tooltip("Trail lifetime (seconds)")]
    [SerializeField] private float trailLifetime = 0.3f;
    [Tooltip("Trail width at start")]
    [SerializeField] private float trailWidthStart = 0.15f;
    [Tooltip("Trail width at end")]
    [SerializeField] private float trailWidthEnd = 0.02f;
    [Tooltip("Use additive blending for extra glow")]
    [SerializeField] private bool useAdditiveBlending = true;

    [Header("Impact Light Burst")]
[Tooltip("Enable bright light flash at impact point")]
[SerializeField] private bool enableImpactLightBurst = true;
[Tooltip("Peak intensity of impact light burst")]
[SerializeField] private float impactLightIntensity = 5.0f;
[Tooltip("Radius of impact light burst")]
[SerializeField] private float impactLightRadius = 4.0f;
[Tooltip("Duration of impact light burst fade (seconds)")]
[SerializeField] private float impactLightDuration = 0.3f;
[Tooltip("Color of impact light burst")]
[SerializeField] private Color impactLightColor = new Color(1f, 0.9f, 0.6f);


[Header("Impact Smoke - Backwards Blast")]
[Tooltip("Enable backwards smoke blast at impact")]
[SerializeField] private bool enableImpactSmoke = true;
[Tooltip("Number of smoke puffs in impact blast")]
[SerializeField] private int impactSmokeCount = 8;
[Tooltip("Impact smoke velocity as fraction of impact velocity")]
[Range(0.3f, 1.0f)]
[SerializeField] private float impactSmokeVelocityFraction = 0.6f;
[Tooltip("Impact smoke lifetime (seconds)")]
[SerializeField] private float impactSmokeLifetime = 4f;
[Tooltip("Impact smoke base size")]
[SerializeField] private float impactSmokeSize = 0.35f;
[Tooltip("Impact smoke rise speed (m/s)")]
[SerializeField] private float impactSmokeRiseSpeed = 0.5f;
[Tooltip("Impact smoke expansion rate multiplier")]
[Range(0.8f, 2.5f)]
[SerializeField] private float impactSmokeBillowing = 1.4f;
[Tooltip("Impact smoke color")]
[SerializeField] private Color impactSmokeColor = new Color(0.9f, 0.9f, 0.9f, 0.7f);

[Header("Particle Smoke - From Burning Particles")]
[Tooltip("Enable smoke emission from burning particles")]
[SerializeField] private bool enableParticleSmoke = true;
[Tooltip("Smoke puffs per burning particle (fewer, chunkier)")]
[SerializeField] private int smokePuffsPerBurner = 2;
[Tooltip("How often to emit smoke puffs (seconds)")]
[SerializeField] private float smokeEmissionInterval = 0.3f;
[Tooltip("Particle smoke lifetime (seconds)")]
[SerializeField] private float particleSmokeLifetime = 3.5f;
[Tooltip("Particle smoke base size")]
[SerializeField] private float particleSmokeSize = 0.3f;
[Tooltip("Particle smoke rise speed (m/s)")]
[SerializeField] private float particleSmokeRiseSpeed = 0.5f;
[Tooltip("Particle smoke expansion rate multiplier")]
[Range(0.8f, 2.5f)]
[SerializeField] private float particleSmokeBillowing = 1.4f;
[Tooltip("Particle smoke color")]
[SerializeField] private Color particleSmokeColor = new Color(0.9f, 0.9f, 0.9f, 0.7f);

[Tooltip("Minimum smoke size multiplier when particle is dying (0-1)")]
[Range(0.1f, 1f)]
[SerializeField] private float minSmokeScaleWhenDying = 0.3f;
[Tooltip("Reduce puff count when particle life is below this threshold")]
[Range(0f, 0.5f)]
[SerializeField] private float reducePuffsThreshold = 0.3f;


[Header("Anime Smoke Style - Shared")]
[Tooltip("Number of spheres per puff (3-5 typical)")]
[Range(2, 8)]
[SerializeField] private int spheresPerPuff = 5;
[Tooltip("Enable frame-stepping for traditional anime feel (animate on 2s/3s)")]
[SerializeField] private bool enableFrameStepping = false;
[Tooltip("Frame step interval (2 = animate on 2s, 3 = on 3s)")]
[Range(1, 4)]
[SerializeField] private int frameStepInterval = 2;
[Tooltip("Rotation speed multiplier (anime smoke tumbles/rolls)")]
[Range(0.5f, 3f)]
[SerializeField] private float animeRotationSpeed = 1.2f;
[Tooltip("Enable bold outlines on smoke puffs")]
[SerializeField] private bool enableSmokeOutlines = false;
[Tooltip("Outline thickness")]
[Range(0.02f, 0.15f)]
[SerializeField] private float outlineThickness = 0.05f;
[Tooltip("Outline color (typically darker than smoke body)")]
[SerializeField] private Color outlineColor = new Color(0.4f, 0.4f, 0.4f, 0.9f);
[Tooltip("Enable internal shadow for depth")]
[SerializeField] private bool enableInternalShadow = false;
[Tooltip("Shadow density (0-1)")]
[Range(0f, 1f)]
[SerializeField] private float shadowDensity = 0.3f;
[Tooltip("Puff spawning pattern - cluster spawns multiple at once")]
[SerializeField] private bool spawnInClusters = true;

    //  Secondary Ingition - fire spread
    
    [Header("Fire Spread")]
    [Tooltip("Ignition temperature threshold (°C)")]
    [SerializeField] private float ignitionThreshold = 400f;
    [Tooltip("Ignition check radius (meters)")]
    [SerializeField] private float ignitionRadius = 0.3f;
    [Tooltip("Tags of objects that can be ignited")]
    [SerializeField] private List<string> flammableTags = new List<string> { "Wood", "Cloth", "Fuel" };
    [Tooltip("Check interval for ignition (seconds)")]
    [SerializeField] private float ignitionCheckInterval = 0.5f;
    
    
    [Header("Surface Interaction")]
    [Tooltip("Enable collision detection for particles")]
    [SerializeField] private bool enableCollision = true;
    [Tooltip("Layers particles can collide with")]
    [SerializeField] private LayerMask collisionLayers = ~0;
    [Tooltip("Collision detection radius")]
    [SerializeField] private float collisionRadius = 0.04f;
    [Tooltip("Bounce factor (molten metal splatters, doesn't bounce much)")]
    [Range(0f, 0.5f)]
    [SerializeField] private float bounceFactor = 0.1f;
    [Tooltip("Probability particle sticks on collision (0-1)")]
    [Range(0f, 1f)]
    [SerializeField] private float stickProbability = 0.7f;
    

    [Header("Particle Physics")]
    [Tooltip("Gravity applied to particles")]
    [SerializeField] private float particleGravity = -9.8f;
    [Tooltip("Air resistance (molten metal is dense, low drag)")]
    [Range(0.9f, 1f)]
    [SerializeField] private float airResistance = 0.98f;
    [Tooltip("Initial ejection speed multiplier")]
    [SerializeField] private float ejectionForceMultiplier = 1.5f;
    [Tooltip("Random velocity variation")]
    [Range(0f, 1f)]
    [SerializeField] private float velocityRandomness = 0.3f;

    [Header("Performance")]
    [Tooltip("Maximum active burning particles")]
    [SerializeField] private int maxActiveParticles = 250;
    [Tooltip("Maximum active smoke puffs")]
    [SerializeField] private int maxSmokeParticles = 150;
    [Tooltip("Maximum active ejecta particles")]
    [SerializeField] private int maxEjectaParticles = 100;
    

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
    [SerializeField] private bool showCollisionDebug = false;
    [SerializeField] private bool visualizeIgnitionRadius = false;
    [SerializeField] private bool visualizeEjectaCone = false;

    
private class BurningParticle
{
    public GameObject gameObject;
    public SpriteRenderer renderer;
    public UnityEngine.Rendering.Universal.Light2D light2D;
    public TrailRenderer trailRenderer;
    public Vector2 velocity;
    public float lifetime;
    public float maxLifetime;
    public float initialSize;
    public Color baseColor;
    public float temperature;
    public bool isStuck;
    public Vector2 stuckPosition;
    public float smokeEmissionTimer;
    public float ignitionCheckTimer;
    public Collider2D attachedTo;
    public bool shouldEmitSmoke;
    public bool hasTrail;
}
    
    private class AnimeSmokePuff
    {
        public GameObject gameObject;
        public SpriteRenderer renderer;
        public SpriteRenderer outlineRenderer;
        public Vector2 velocity;
        public Vector2 initialVelocity;
        public float lifetime;
        public float maxLifetime;
        public float initialSize;
        public Color baseColor;
        public float rotationAngle;
        public float rotationSpeed;
        public float expansionRate;

        public Vector2 lastSteppedPosition;
        public float stepAccumulator;

        public List<Vector2> sphereOffsets;
        public List<float> sphereSizes; 

            public float customRiseSpeed; 
    public bool isImpactSmoke;  
    }
    
    private class EjectaParticle
    {
        public GameObject gameObject;
        public SpriteRenderer renderer;
        public Vector2 velocity;
        public float lifetime;
        public float maxLifetime;
        public float initialSize;
        public Color baseColor;
    }

    private class ImpactLightBurst
{
    public GameObject gameObject;
    public UnityEngine.Rendering.Universal.Light2D light2D;
    public float lifetime;
    public float maxLifetime;
    public float peakIntensity;
}

private List<ImpactLightBurst> activeImpactLights = new List<ImpactLightBurst>();
    
    private List<BurningParticle> activeParticles = new List<BurningParticle>();
    private List<AnimeSmokePuff> activeSmoke = new List<AnimeSmokePuff>();
    private List<EjectaParticle> activeEjecta = new List<EjectaParticle>();
    private Queue<BurningParticle> particlePool = new Queue<BurningParticle>();
    private Queue<AnimeSmokePuff> smokePool = new Queue<AnimeSmokePuff>();
    private Queue<EjectaParticle> ejectaPool = new Queue<EjectaParticle>();
    
    private Sprite particleSprite;
    private Sprite ejectaSprite;
    private Rigidbody2D rb;

private int collisionCheckIndex = 0;

private Dictionary<Vector2Int, List<Collider2D>> flammableObjectsGrid = new Dictionary<Vector2Int, List<Collider2D>>();
private float gridCellSize = 2f;
private float gridRebuildTimer = 0f;
private float gridRebuildInterval = 1f;
private int ignitionCheckIndex = 0;


private List<TrailRenderer> trailPool = new List<TrailRenderer>();
private Queue<TrailRenderer> availableTrails = new Queue<TrailRenderer>();
private int maxActiveTrails = 30; 
    
    private List<Sprite> smokeTextureVariants = new List<Sprite>();
private List<Sprite> smokeOutlineVariants = new List<Sprite>();
private const int smokeTextureVariantCount = 8; 
  
void Start()
{
    if (fireSpreadSystem == null && enableIgnition)
    {
        fireSpreadSystem = FindObjectOfType<FireSpreadSystem>();
        if (fireSpreadSystem == null && showDebugInfo)
        {
            Debug.LogWarning("IncendiaryImpactSystem: No FireSpreadSystem found in scene. Ignition will not work.");
        }
    }

    rb = GetComponent<Rigidbody2D>();

    particleSprite = CreateIncendiaryParticleSprite(8);
    ejectaSprite = CreateEjectaSprite(10);

    PreGenerateSmokeTextures();

    if (enableGlowTrails)
    {
        CreateTrailPool();
    }

    for (int i = 0; i < 20; i++)
    {
        BurningParticle p = AllocateBurningParticle();
        p.gameObject.SetActive(false);
        particlePool.Enqueue(p);
        
        AnimeSmokePuff s = AllocateAnimeSmokePuff();
        s.gameObject.SetActive(false);
        smokePool.Enqueue(s);
        
        EjectaParticle e = AllocateEjectaParticle();
        e.gameObject.SetActive(false);
        ejectaPool.Enqueue(e);
    }
}

void PreGenerateSmokeTextures()
{
    for (int variant = 0; variant < smokeTextureVariantCount; variant++)
    {
        List<Vector2> sphereOffsets = new List<Vector2>();
        List<float> sphereSizes = new List<float>();
        
        int numSpheres = spheresPerPuff;

        Vector2 centerOffset = Random.insideUnitCircle * 0.08f;
        sphereOffsets.Add(centerOffset);
        sphereSizes.Add(Random.Range(0.9f, 1.1f));

        for (int i = 1; i < numSpheres; i++)
        {
            float baseAngle = (360f / (numSpheres - 1)) * (i - 1);
            float angleVariation = Random.Range(-40f, 40f);
            float angle = baseAngle + angleVariation;
            float distance = Random.Range(0.25f, 0.65f);
            
            Vector2 offset = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad) * distance,
                Mathf.Sin(angle * Mathf.Deg2Rad) * distance
            );
            
            float size = Random.Range(0.5f, 0.95f);
            
            sphereOffsets.Add(offset);
            sphereSizes.Add(size);
        }

        int wispyCount = Random.Range(1, 3);
        for (int i = 0; i < wispyCount; i++)
        {
            Vector2 wispyOffset = Random.insideUnitCircle * Random.Range(0.6f, 0.9f);
            float wispySize = Random.Range(0.3f, 0.5f);
            
            sphereOffsets.Add(wispyOffset);
            sphereSizes.Add(wispySize);
        }

        Texture2D smokeTex = CreateAnimeSmokePuffTexture(1f, sphereOffsets, sphereSizes);
        Sprite smokeSprite = Sprite.Create(
            smokeTex,
            new Rect(0, 0, smokeTex.width, smokeTex.height),
            new Vector2(0.5f, 0.5f),
            100f
        );
        smokeTextureVariants.Add(smokeSprite);

        if (enableSmokeOutlines)
        {
            Texture2D outlineTex = CreateAnimeSmokeOutlineTexture(1f, sphereOffsets, sphereSizes);
            Sprite outlineSprite = Sprite.Create(
                outlineTex,
                new Rect(0, 0, outlineTex.width, outlineTex.height),
                new Vector2(0.5f, 0.5f),
                100f
            );
            smokeOutlineVariants.Add(outlineSprite);
        }
    }
}

void CreateTrailPool()
{
    for (int i = 0; i < maxActiveTrails; i++)
    {
        GameObject trailObj = new GameObject($"PooledTrail_{i}");
        trailObj.transform.SetParent(transform);
        
        TrailRenderer trail = trailObj.AddComponent<TrailRenderer>();
        trail.time = trailLifetime;
        trail.startWidth = trailWidthStart;
        trail.endWidth = trailWidthEnd;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { 
                new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f),
                new GradientColorKey(new Color(1f, 0.5f, 0.2f), 1f)
            },
            new GradientAlphaKey[] { 
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        trail.colorGradient = gradient;
        
        if (useAdditiveBlending)
        {
            trail.material.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
            trail.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            trail.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        }
        
        trail.sortingOrder = 9;
        trail.emitting = false;
        trail.gameObject.SetActive(false);
        
        trailPool.Add(trail);
        availableTrails.Enqueue(trail);
    }
}

TrailRenderer GetTrailFromPool()
{
    if (!enableGlowTrails) return null;
    
    if (availableTrails.Count > 0)
    {
        TrailRenderer trail = availableTrails.Dequeue();
        trail.gameObject.SetActive(true);
        trail.emitting = true;
        return trail;
    }
    
    return null; 
}

void ReturnTrailToPool(TrailRenderer trail)
{
    if (trail == null) return;
    
    trail.emitting = false;
    trail.Clear();
    trail.gameObject.SetActive(false);
    
    if (!availableTrails.Contains(trail))
        availableTrails.Enqueue(trail);
}

void AssignTrailsToParticles()
{
    int trailsInUse = 0;
    foreach (BurningParticle p in activeParticles)
    {
        if (p.hasTrail && p.trailRenderer != null)
            trailsInUse++;
    }

    if (trailsInUse < maxActiveTrails)
    {
        List<BurningParticle> particlesNeedingTrails = new List<BurningParticle>();
        
        foreach (BurningParticle p in activeParticles)
        {
            if (p.hasTrail || p.isStuck) continue;
            
            float remainingLife = 1f - (p.lifetime / p.maxLifetime);
            if (remainingLife < 0.3f) continue; 
            
            particlesNeedingTrails.Add(p);
        }

        particlesNeedingTrails.Sort((a, b) => {
            float priorityA = a.velocity.magnitude * (1f - (a.lifetime / a.maxLifetime));
            float priorityB = b.velocity.magnitude * (1f - (b.lifetime / b.maxLifetime));
            return priorityB.CompareTo(priorityA);
        });

        int toAssign = Mathf.Min(maxActiveTrails - trailsInUse, particlesNeedingTrails.Count);
        for (int i = 0; i < toAssign; i++)
        {
            BurningParticle p = particlesNeedingTrails[i];
            TrailRenderer trail = GetTrailFromPool();
            if (trail == null) break;
            
            p.trailRenderer = trail;
            p.hasTrail = true;
            trail.transform.position = p.gameObject.transform.position;
            trail.Clear(); 
        }
    }
}
    
void Update()
{
    UpdateBurningParticles();
    UpdateAnimeSmokePuffs();
    UpdateEjectaParticles();
    UpdateImpactLightBursts();

    if (enableGlowTrails)
        AssignTrailsToParticles();

    gridRebuildTimer += Time.deltaTime;
    if (gridRebuildTimer >= gridRebuildInterval)
    {
        gridRebuildTimer = 0f;
        RebuildFlammableObjectsGrid();
    }

    if (enableIgnition)
        CheckIgnitionStaggered();
}
void RebuildFlammableObjectsGrid()
{
    flammableObjectsGrid.Clear();

    foreach (string tag in flammableTags)
    {
        GameObject[] flammableObjects = GameObject.FindGameObjectsWithTag(tag);
        
        foreach (GameObject obj in flammableObjects)
        {
            Collider2D col = obj.GetComponent<Collider2D>();
            if (col == null) continue;

            Vector2Int gridPos = WorldToGrid(obj.transform.position);
            
            if (!flammableObjectsGrid.ContainsKey(gridPos))
                flammableObjectsGrid[gridPos] = new List<Collider2D>();
            
            flammableObjectsGrid[gridPos].Add(col);
        }
    }
}
   
Vector2Int WorldToGrid(Vector2 worldPos)
{
    return new Vector2Int(
        Mathf.FloorToInt(worldPos.x / gridCellSize),
        Mathf.FloorToInt(worldPos.y / gridCellSize)
    );
} 
void CheckIgnitionStaggered()
{
    if (activeParticles.Count == 0) return;

    int checksPerFrame = Mathf.Min(5, activeParticles.Count);
    
    for (int i = 0; i < checksPerFrame; i++)
    {
        if (ignitionCheckIndex >= activeParticles.Count)
            ignitionCheckIndex = 0;
        
        BurningParticle p = activeParticles[ignitionCheckIndex];
        
        if (p.ignitionCheckTimer >= ignitionCheckInterval)
        {
            p.ignitionCheckTimer = 0f;
            CheckForIgnitionOptimized(p);
        }
        
        ignitionCheckIndex++;
    }
}

void CheckForIgnitionOptimized(BurningParticle p)
{
    if (p.temperature < ignitionThreshold) return;
    
    Vector2 particlePos = p.gameObject.transform.position;
    Vector2Int gridPos = WorldToGrid(particlePos);

    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            Vector2Int checkPos = gridPos + new Vector2Int(x, y);
            
            if (!flammableObjectsGrid.ContainsKey(checkPos))
                continue;
            
            foreach (Collider2D col in flammableObjectsGrid[checkPos])
            {
                if (col == null) continue;
                
                float distance = Vector2.Distance(particlePos, col.transform.position);
                
                if (distance <= ignitionRadius)
                {
                    IgniteObject(col.gameObject, p);
                    return;
                }
            }
        }
    }
}
void OnDestroy()
{
    foreach (var burst in activeImpactLights)
        if (burst.gameObject != null) Destroy(burst.gameObject);

    foreach (var trail in trailPool)
        if (trail != null && trail.gameObject != null) Destroy(trail.gameObject);

    foreach (var sprite in smokeTextureVariants)
    {
        if (sprite != null && sprite.texture != null)
            Destroy(sprite.texture);
    }
    foreach (var sprite in smokeOutlineVariants)
    {
        if (sprite != null && sprite.texture != null)
            Destroy(sprite.texture);
    }

    foreach (var p in activeParticles)
        if (p.gameObject != null) Destroy(p.gameObject);
    foreach (var s in activeSmoke)
        if (s.gameObject != null) Destroy(s.gameObject);
    foreach (var e in activeEjecta)
        if (e.gameObject != null) Destroy(e.gameObject);
        
    while (particlePool.Count > 0)
    {
        var p = particlePool.Dequeue();
        if (p.gameObject != null) Destroy(p.gameObject);
    }
    while (smokePool.Count > 0)
    {
        var s = smokePool.Dequeue();
        if (s.gameObject != null) Destroy(s.gameObject);
    }
    while (ejectaPool.Count > 0)
    {
        var e = ejectaPool.Dequeue();
        if (e.gameObject != null) Destroy(e.gameObject);
    }
    
    if (particleSprite != null && particleSprite.texture != null)
        Destroy(particleSprite.texture);
    if (ejectaSprite != null && ejectaSprite.texture != null)
        Destroy(ejectaSprite.texture);
}
    
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (triggerOnce && hasTriggered)
            return;
        
        int collisionLayer = 1 << collision.gameObject.layer;
        if ((collisionLayer & impactLayers) == 0)
            return;
        
        Vector2 impactVelocity = rb != null ? rb.linearVelocity : collision.relativeVelocity;
        float impactSpeed = impactVelocity.magnitude;
        
        if (impactSpeed < minImpactVelocity)
            return;
        
        Vector2 impactPoint = collision.GetContact(0).point;
        Vector2 impactNormal = collision.GetContact(0).normal;
        
        CreateIncendiaryImpact(impactPoint, impactVelocity, impactNormal, impactSpeed);
        
        hasTriggered = true;
        
        if (triggerOnce)
            enabled = false;
    }

void CreateIncendiaryImpact(Vector2 position, Vector2 velocity, Vector2 surfaceNormal, float impactSpeed)
{
    float intensity = Mathf.Clamp01(Mathf.InverseLerp(minImpactVelocity, maxImpactVelocity, impactSpeed));

    CreateImpactLightBurst(position, intensity);

    if (enableImpactPlume)
    {
        CreateEjectaPlume(position, velocity, surfaceNormal, impactSpeed, intensity);
    }

    if (enableImpactSmoke)
    {
        CreateImpactAnimeSmoke(position, velocity, surfaceNormal, impactSpeed, intensity);
    }
    
    int particleCount = burstParticleCount;
    float temperature = GetTemperatureForType();
    
    int spawnAttempts = 0;
    int successfulSpawns = 0;
    int maxAttempts = particleCount * 3;
    
    while (successfulSpawns < particleCount && spawnAttempts < maxAttempts)
    {
        spawnAttempts++;
        
        if (activeParticles.Count >= maxActiveParticles)
            break;

        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        float dotProduct = Vector2.Dot(direction, surfaceNormal);
        
        if (dotProduct < 0f)
        {
            continue;
        }

        float distance = Random.Range(0.2f, horizontalSprayDistance) * intensity;
        float height = Random.Range(0.1f, verticalSprayHeight) * intensity * 0.6f;
        
        float baseSpeed = impactSpeed * ejectionForceMultiplier * 200f;
        Vector2 ejectionVelocity = new Vector2(
            direction.x * baseSpeed * Random.Range(1f - velocityRandomness, 1f + velocityRandomness),
            height * baseSpeed * Random.Range(0.8f, 1.2f)
        );
        
        ejectionVelocity += velocity * 0.3f;
        
        bool shouldEmitSmoke = Random.value < smokeEmitterRatio;
        float particleLifetime = shouldEmitSmoke ? smokeEmitterLifetime : sparkLifetime;
        float size = particleSize * Random.Range(1f - sizeVariation, 1f + sizeVariation);
        
        EmitBurningParticle(position, ejectionVelocity, size, temperature, particleLifetime, shouldEmitSmoke);
        
        successfulSpawns++;
    }
    
    if (showDebugInfo && successfulSpawns < particleCount)
    {
        Debug.LogWarning($"IncendiaryImpact: Only spawned {successfulSpawns}/{particleCount} particles after {spawnAttempts} attempts");
    }
}

    void CreateImpactLightBurst(Vector2 position, float intensity)
{
    if (!enableImpactLightBurst || !enableDynamicLighting)
        return;
    
    GameObject lightObj = new GameObject("ImpactLightBurst");
    lightObj.transform.SetParent(transform);
    lightObj.transform.position = position;
    
    var light2D = lightObj.AddComponent<UnityEngine.Rendering.Universal.Light2D>();
    light2D.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Point;
    light2D.intensity = impactLightIntensity * intensity;
    light2D.pointLightOuterRadius = impactLightRadius * Mathf.Lerp(0.7f, 1.3f, intensity);
    light2D.color = impactLightColor;
    light2D.falloffIntensity = 0.7f;
    
    activeImpactLights.Add(new ImpactLightBurst
    {
        gameObject = lightObj,
        light2D = light2D,
        lifetime = 0f,
        maxLifetime = impactLightDuration,
        peakIntensity = impactLightIntensity * intensity
    });
}

    void CreateEjectaPlume(Vector2 position, Vector2 projectileVelocity, Vector2 surfaceNormal, float impactSpeed, float intensity)
    {
        Vector2 incomingDir = projectileVelocity.normalized;
        Vector2 reflectedDir = Vector2.Reflect(incomingDir, surfaceNormal);
        Vector2 upwardBias = Vector2.up * 0.2f;
        Vector2 primaryEjectaDir = (reflectedDir + upwardBias).normalized;
        
        float ejectaSpeed = impactSpeed * ejectaVelocityFraction;
        float primaryAngle = Mathf.Atan2(primaryEjectaDir.y, primaryEjectaDir.x);
        
        for (int i = 0; i < ejectaParticleCount; i++)
        {
            if (activeEjecta.Count >= maxEjectaParticles)
                break;
            
            float angleFromPrimary = Random.Range(-ejectaConeSpread, ejectaConeSpread) * Mathf.Deg2Rad;
            float coneDeviation = Random.Range(0f, ejectaConeAngle) * Mathf.Deg2Rad;
            float azimuthAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float angleOffset = angleFromPrimary + (Mathf.Cos(azimuthAngle) * coneDeviation);
            float ejectaAngle = primaryAngle + angleOffset;
            
            Vector2 ejectaDirection = new Vector2(Mathf.Cos(ejectaAngle), Mathf.Sin(ejectaAngle));
            
            if (Vector2.Dot(ejectaDirection, surfaceNormal) < 0f)
            {
                ejectaDirection = Vector2.Reflect(ejectaDirection, surfaceNormal);
            }
            
            float velocityMultiplier = Random.Range(1f - ejectaVelocityRandomness, 1f + ejectaVelocityRandomness);
            Vector2 ejectaVelocity = ejectaDirection * ejectaSpeed * velocityMultiplier;
            
            float spawnOffset = Random.Range(ejectaSpawnOffset * 0.5f, ejectaSpawnOffset);
            Vector2 spawnPosition = position + (surfaceNormal * spawnOffset);
            Vector2 randomScatter = Random.insideUnitCircle * ejectaSpawnScatter;
            spawnPosition += randomScatter;
            
            float size = ejectaParticleSize * Random.Range(1f - ejectaSizeVariation, 1f + ejectaSizeVariation);
            
            EmitEjectaParticle(spawnPosition, ejectaVelocity, size, ejectaLifetime);
        }
    }

void CreateImpactAnimeSmoke(Vector2 position, Vector2 projectileVelocity, Vector2 surfaceNormal, float impactSpeed, float intensity)
{
    if (!enableImpactSmoke) return;
        
        Vector2 incomingDir = projectileVelocity.normalized;
        Vector2 reflectedDir = Vector2.Reflect(incomingDir, surfaceNormal);
        Vector2 upwardBias = Vector2.up * 0.3f;
        Vector2 primarySmokeDir = (reflectedDir + upwardBias).normalized;
        
        float smokeSpeed = impactSpeed * impactSmokeVelocityFraction;
        float primaryAngle = Mathf.Atan2(primarySmokeDir.y, primarySmokeDir.x);

        int clustersToSpawn = spawnInClusters ? Mathf.CeilToInt(impactSmokeCount / 3f) : impactSmokeCount;
        int puffsPerCluster = spawnInClusters ? 3 : 1;
        
        for (int cluster = 0; cluster < clustersToSpawn; cluster++)
        {
            Vector2 clusterDirection = Vector2.zero;
            float clusterAngleOffset = Random.Range(-40f, 40f) * Mathf.Deg2Rad;
            float clusterAngle = primaryAngle + clusterAngleOffset;
            clusterDirection = new Vector2(Mathf.Cos(clusterAngle), Mathf.Sin(clusterAngle));
            
            if (Vector2.Dot(clusterDirection, surfaceNormal) < 0f)
            {
                clusterDirection = Vector2.Reflect(clusterDirection, surfaceNormal);
            }
            
            for (int puff = 0; puff < puffsPerCluster; puff++)
            {
                if (activeSmoke.Count >= maxSmokeParticles)
                    break;
                
                float velocityVariation = Random.Range(0.7f, 1.3f);
                Vector2 puffVelocity = clusterDirection * smokeSpeed * velocityVariation;
                
                float spawnOffset = Random.Range(0.05f, 0.15f);
                Vector2 spawnPosition = position + (surfaceNormal * spawnOffset);
                Vector2 randomScatter = Random.insideUnitCircle * 0.2f;
                spawnPosition += randomScatter;
                
float size = impactSmokeSize * Random.Range(0.9f, 1.2f);
float lifetime = impactSmokeLifetime * Random.Range(0.9f, 1.1f);

EmitAnimeSmokePuff(spawnPosition, puffVelocity, size, lifetime, impactSmokeColor, impactSmokeRiseSpeed, impactSmokeBillowing, true);
            }
        }
    }
 
void EmitBurningParticle(Vector2 position, Vector2 velocity, float size, float temperature, float lifetime, bool shouldEmitSmoke)
{
    BurningParticle p;
    
    if (particlePool.Count > 0)
    {
        p = particlePool.Dequeue();
    }
    else if (activeParticles.Count < maxActiveParticles)
    {
        p = AllocateBurningParticle();
    }
    else
    {
        return;
    }
    
    p.gameObject.transform.position = position;
    p.velocity = velocity;
    p.lifetime = 0f;
    p.maxLifetime = lifetime;
    p.initialSize = size;
    p.temperature = temperature;
    p.isStuck = false;
    p.stuckPosition = Vector2.zero;
    p.smokeEmissionTimer = 0f;
    p.ignitionCheckTimer = 0f;
    p.attachedTo = null;
    p.shouldEmitSmoke = shouldEmitSmoke;
    p.hasTrail = false; 
    p.trailRenderer = null; 
    
    p.baseColor = GetColorForType(temperature);
    p.renderer.sprite = particleSprite;
    p.renderer.color = p.baseColor;
    p.gameObject.transform.localScale = Vector3.one * size;
    
    if (p.light2D != null)
    {
        p.light2D.color = p.baseColor;
        p.light2D.intensity = lightIntensity;
        p.light2D.enabled = true;
    }

    OrientParticle(p);
    
    p.gameObject.SetActive(true);
    activeParticles.Add(p);
}
    
void EmitAnimeSmokePuff(Vector2 position, Vector2 velocity, float size, float lifetime, Color color, float riseSpeed, float billowing, bool isImpactSmoke)
{
    AnimeSmokePuff s;
    
    if (smokePool.Count > 0)
    {
        s = smokePool.Dequeue();
    }
    else if (activeSmoke.Count < maxSmokeParticles)
    {
        s = AllocateAnimeSmokePuff();
    }
    else
    {
        return;
    }
    
    s.gameObject.transform.position = position;
    s.velocity = velocity;
    s.initialVelocity = velocity;
    s.lifetime = 0f;
    s.maxLifetime = lifetime;
    s.initialSize = size;
    s.baseColor = color;
    s.rotationAngle = Random.Range(0f, 360f);
    s.rotationSpeed = Random.Range(-50f, 50f) * animeRotationSpeed;
    s.expansionRate = billowing;
    s.customRiseSpeed = riseSpeed;
    s.isImpactSmoke = isImpactSmoke;

    s.lastSteppedPosition = position;
    s.stepAccumulator = 0f;

    int variantIndex = Random.Range(0, smokeTextureVariants.Count);
    s.renderer.sprite = smokeTextureVariants[variantIndex];
    s.renderer.color = color;
    s.gameObject.transform.localScale = Vector3.one;

    if (enableSmokeOutlines && s.outlineRenderer != null && smokeOutlineVariants.Count > 0)
    {
        s.outlineRenderer.sprite = smokeOutlineVariants[variantIndex];
        s.outlineRenderer.color = outlineColor;
        s.outlineRenderer.gameObject.SetActive(true);
    }
    else if (s.outlineRenderer != null)
    {
        s.outlineRenderer.gameObject.SetActive(false);
    }
    
    s.gameObject.transform.rotation = Quaternion.Euler(0, 0, s.rotationAngle);
    
    s.gameObject.SetActive(true);
    activeSmoke.Add(s);
}
    
    void EmitEjectaParticle(Vector2 position, Vector2 velocity, float size, float lifetime)
    {
        EjectaParticle e;
        
        if (ejectaPool.Count > 0)
        {
            e = ejectaPool.Dequeue();
        }
        else if (activeEjecta.Count < maxEjectaParticles)
        {
            e = AllocateEjectaParticle();
        }
        else
        {
            return;
        }
        
        e.gameObject.transform.position = position;
        e.velocity = velocity;
        e.lifetime = 0f;
        e.maxLifetime = lifetime;
        e.initialSize = size;
        e.baseColor = ejectaColor;
        
        e.renderer.sprite = ejectaSprite;
        e.renderer.color = ejectaColor;
        e.gameObject.transform.localScale = Vector3.one * size;
        
        e.gameObject.SetActive(true);
        activeEjecta.Add(e);
    }
 

void UpdateBurningParticles()
{
    int particlesPerFrame = Mathf.Max(5, activeParticles.Count / 4);
    int collisionsCheckedThisFrame = 0;
    
    for (int i = activeParticles.Count - 1; i >= 0; i--)
    {
        BurningParticle p = activeParticles[i];

        if (p.isStuck && p.attachedTo == null)
        {
            ReturnBurningParticle(p, i);
            continue;
        }
        p.lifetime += Time.deltaTime;
        
        if (p.lifetime >= p.maxLifetime)
        {
            ReturnBurningParticle(p, i);
            continue;
        }
        
        p.smokeEmissionTimer += Time.deltaTime;
        p.ignitionCheckTimer += Time.deltaTime;

        if (p.hasTrail && p.trailRenderer != null)
        {
            float remainingLife = 1f - (p.lifetime / p.maxLifetime);
            if (p.isStuck || remainingLife < 0.3f)
            {
                ReturnTrailToPool(p.trailRenderer);
                p.trailRenderer = null;
                p.hasTrail = false;
            }
        }
        
        if (p.isStuck)
        {
            p.gameObject.transform.position = p.stuckPosition;
        }
        else
        {
            Vector2 oldPos = p.gameObject.transform.position;
            
            p.velocity.y += particleGravity * Time.deltaTime;
            p.velocity *= airResistance;
            
            Vector2 newPos = oldPos + p.velocity * Time.deltaTime;
            
            if (enableCollision && collisionsCheckedThisFrame < particlesPerFrame)
            {
                if (CheckParticleCollision(p, oldPos, newPos, out Vector2 correctedPos, out Vector2 newVelocity))
                {
                    newPos = correctedPos;
                    p.velocity = newVelocity;
                    
                    if (enableStickyParticles && Random.value < stickProbability)
                    {
                        p.isStuck = true;
                        p.stuckPosition = correctedPos;
                        p.velocity = Vector2.zero;
                    }
                }
                collisionsCheckedThisFrame++;
            }
            
            p.gameObject.transform.position = newPos;
            OrientParticle(p);
        }

        if (p.hasTrail && p.trailRenderer != null)
        {
            p.trailRenderer.transform.position = p.gameObject.transform.position;
        }
        
        if (enableParticleSmoke && p.shouldEmitSmoke && p.smokeEmissionTimer >= smokeEmissionInterval)
        {
            p.smokeEmissionTimer = 0f;
            EmitAnimeSmokeFromParticle(p);
        }
        
        UpdateParticleAppearance(p);
    }
}
    
    void UpdateAnimeSmokePuffs()
    {
        float frameStepTime = enableFrameStepping ? (1f / 60f) * frameStepInterval : 0f;
        
        for (int i = activeSmoke.Count - 1; i >= 0; i--)
        {
            AnimeSmokePuff s = activeSmoke[i];
            
            s.lifetime += Time.deltaTime;
            
            if (s.lifetime >= s.maxLifetime)
            {
                ReturnAnimeSmokePuff(s, i);
                continue;
            }
            
            float lifeRatio = s.lifetime / s.maxLifetime;
            bool hasBackwardsMomentum = s.initialVelocity.magnitude > 2f;
            
            if (hasBackwardsMomentum)
            {
                if (lifeRatio < 0.3f)
                {
                    float blastPhase = lifeRatio / 0.3f;
                    s.velocity = Vector2.Lerp(s.initialVelocity, s.initialVelocity * 0.15f, blastPhase);
                    s.velocity *= 0.93f;
                    s.velocity.y += s.customRiseSpeed * 0.4f * blastPhase;
                }
                else if (lifeRatio < 0.6f)
                {
                    float transitionPhase = (lifeRatio - 0.3f) / 0.3f;
                    s.velocity.x *= Mathf.Lerp(0.93f, 0.85f, transitionPhase);
                    s.velocity.y = Mathf.Lerp(s.velocity.y, s.customRiseSpeed * 0.9f, transitionPhase * 0.5f);
                }
                else
                {
                    s.velocity.x *= 0.92f;
                    s.velocity.y = s.customRiseSpeed * 0.9f;
                }
            }
            else
            {
                s.velocity.x *= 0.94f;
                s.velocity.y = s.customRiseSpeed * Mathf.Lerp(0.85f, 1.0f, lifeRatio * 0.4f);
            }
            if (enableFrameStepping)
            {
                s.stepAccumulator += Time.deltaTime;
                
                if (s.stepAccumulator >= frameStepTime)
                {
                    s.stepAccumulator -= frameStepTime;
                    Vector2 currentPos = s.gameObject.transform.position;
                    Vector2 nextPos = currentPos + s.velocity * frameStepTime;
                    s.lastSteppedPosition = nextPos;
                }
                
                s.gameObject.transform.position = s.lastSteppedPosition;
            }
            else
            {
                Vector2 pos = s.gameObject.transform.position;
                pos += s.velocity * Time.deltaTime;
                s.gameObject.transform.position = pos;
            }

            s.rotationAngle += s.rotationSpeed * Time.deltaTime;
            s.gameObject.transform.rotation = Quaternion.Euler(0, 0, s.rotationAngle);

            float expansionCurve;
            if (lifeRatio < 0.4f)
            {
                expansionCurve = Mathf.Lerp(1.0f, 1.6f, lifeRatio / 0.4f);
            }
            else
            {
                expansionCurve = Mathf.Lerp(1.6f, 2.2f, (lifeRatio - 0.4f) / 0.6f);
            }
            
            float scale = s.initialSize * expansionCurve * s.expansionRate;
            s.gameObject.transform.localScale = Vector3.one * scale;

            float fadeStart = 0.7f;
            float alpha = s.baseColor.a;
            if (lifeRatio > fadeStart)
            {
                alpha *= 1f - ((lifeRatio - fadeStart) / (1f - fadeStart));
            }
            
            s.renderer.color = new Color(s.baseColor.r, s.baseColor.g, s.baseColor.b, alpha);
            
            if (enableSmokeOutlines && s.outlineRenderer != null && s.outlineRenderer.gameObject.activeSelf)
            {
                s.outlineRenderer.color = new Color(outlineColor.r, outlineColor.g, outlineColor.b, alpha * outlineColor.a);
            }
        }
    }
    
    void UpdateEjectaParticles()
    {
        for (int i = activeEjecta.Count - 1; i >= 0; i--)
        {
            EjectaParticle e = activeEjecta[i];
            
            e.lifetime += Time.deltaTime;
            
            if (e.lifetime >= e.maxLifetime)
            {
                ReturnEjectaParticle(e, i);
                continue;
            }
            
            e.velocity.y += ejectaGravity * Time.deltaTime;
            e.velocity *= ejectaAirResistance;
            
            Vector2 pos = e.gameObject.transform.position;
            pos += e.velocity * Time.deltaTime;
            e.gameObject.transform.position = pos;
            
            float rotationSpeed = e.velocity.magnitude * 50f;
            e.gameObject.transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);
            
            if (ejectaFadeAndExpand)
            {
                float lifeRatio = e.lifetime / e.maxLifetime;
                float fadeAmount = 1f - Mathf.Pow(lifeRatio, 1.5f);
                float alpha = e.baseColor.a * fadeAmount;
                
                e.renderer.color = new Color(
                    e.baseColor.r,
                    e.baseColor.g,
                    e.baseColor.b,
                    alpha
                );
                
                float scaleCurve;
                if (lifeRatio < 0.3f)
                {
                    scaleCurve = Mathf.Lerp(1.0f, 1.3f, lifeRatio / 0.3f);
                }
                else
                {
                    scaleCurve = Mathf.Lerp(1.3f, 0.7f, (lifeRatio - 0.3f) / 0.7f);
                }
                
                float scale = e.initialSize * scaleCurve;
                e.gameObject.transform.localScale = Vector3.one * scale;
            }
        }
    }

    void UpdateImpactLightBursts()
{
    for (int i = activeImpactLights.Count - 1; i >= 0; i--)
    {
        ImpactLightBurst burst = activeImpactLights[i];
        
        burst.lifetime += Time.deltaTime;
        
        if (burst.lifetime >= burst.maxLifetime)
        {
            Destroy(burst.gameObject);
            activeImpactLights.RemoveAt(i);
            continue;
        }

        float lifeRatio = burst.lifetime / burst.maxLifetime;
        float intensityCurve;
        
        if (lifeRatio < 0.1f)
        {
            intensityCurve = lifeRatio / 0.1f;
        }
        else
        {
            intensityCurve = Mathf.Pow(1f - ((lifeRatio - 0.1f) / 0.9f), 2.5f);
        }
        
        burst.light2D.intensity = burst.peakIntensity * intensityCurve;

        float radiusPulse = 1f + Mathf.Sin(burst.lifetime * 30f) * 0.1f;
        burst.light2D.pointLightOuterRadius = impactLightRadius * intensityCurve * radiusPulse;
    }
}
    
    void UpdateParticleAppearance(BurningParticle p)
    {
        float lifeRatio = p.lifetime / p.maxLifetime;
        float currentTemp = p.temperature * (1f - lifeRatio * 0.3f);
        Color currentColor = GetColorForTemperature(currentTemp);
        
        float flickerValue = 1f;
        if (enableFlicker)
        {
            float flicker = Mathf.PerlinNoise(Time.time * flickerSpeed, p.lifetime * 10f);
            flickerValue = Mathf.Lerp(1f - flickerIntensity, 1f, flicker);
            currentColor *= flickerValue;
        }
        
        float fadeAmount = 1f;
        if (lifeRatio > 0.8f)
        {
            fadeAmount = (lifeRatio - 0.8f) / 0.2f;
            currentColor.a *= (1f - fadeAmount);
        }
        
        p.renderer.color = currentColor;
        
        if (p.light2D != null)
        {
            p.light2D.color = currentColor;
            p.light2D.intensity = lightIntensity * (1f - lifeRatio * 0.5f) * flickerValue * (1f - fadeAmount);
        }
        
        float sizeScale = Mathf.Lerp(1f, 0.7f, lifeRatio);
        p.gameObject.transform.localScale = Vector3.one * (p.initialSize * sizeScale);
    }

    //  Particle Smoke emission

    
void EmitAnimeSmokeFromParticle(BurningParticle p)
{
    float lifeRatio = p.lifetime / p.maxLifetime;
    float remainingLife = 1f - lifeRatio;

float sizeMultiplier = Mathf.Lerp(minSmokeScaleWhenDying, 1.0f, remainingLife);

    int puffsToEmit = smokePuffsPerBurner;
if (remainingLife < reducePuffsThreshold)
{
    puffsToEmit = Mathf.Max(1, smokePuffsPerBurner / 2);
}

    if (spawnInClusters)
    {
        Vector2 clusterCenter = (Vector2)p.gameObject.transform.position + Random.insideUnitCircle * 0.08f;
        
        for (int i = 0; i < puffsToEmit; i++)
        {
            Vector2 puffPos = clusterCenter + Random.insideUnitCircle * 0.05f;

            Vector2 puffVel = new Vector2(
                Random.Range(-0.15f, 0.15f),
                particleSmokeRiseSpeed * Random.Range(0.85f, 1.15f)
            );

            float size = particleSmokeSize * Random.Range(0.85f, 1.15f) * sizeMultiplier;
            float lifetime = particleSmokeLifetime * Random.Range(0.9f, 1.1f);

            EmitAnimeSmokePuff(puffPos, puffVel, size, lifetime, particleSmokeColor, particleSmokeRiseSpeed, particleSmokeBillowing, false);
        }
    }
    else
    {
        for (int i = 0; i < puffsToEmit; i++)
        {
            Vector2 smokePos = (Vector2)p.gameObject.transform.position + Random.insideUnitCircle * 0.05f;
            
            Vector2 smokeVel = new Vector2(
                Random.Range(-0.2f, 0.2f),
                particleSmokeRiseSpeed * Random.Range(0.8f, 1.2f)
            );
            
            // Apply size reduction based on particle life
            float size = particleSmokeSize * Random.Range(0.7f, 1.3f) * sizeMultiplier;
            float lifetime = particleSmokeLifetime * Random.Range(0.8f, 1.2f);

            EmitAnimeSmokePuff(smokePos, smokeVel, size, lifetime, particleSmokeColor, particleSmokeRiseSpeed, particleSmokeBillowing, false);
        }
    }
}

    //  Collision detection

    bool CheckParticleCollision(BurningParticle p, Vector2 from, Vector2 to, out Vector2 correctedPos, out Vector2 newVelocity)
    {
        correctedPos = to;
        newVelocity = p.velocity;
        
        RaycastHit2D hit = Physics2D.CircleCast(
            from,
            collisionRadius,
            (to - from).normalized,
            Vector2.Distance(from, to),
            collisionLayers
        );
        
        if (hit.collider != null)
        {
            correctedPos = hit.point + hit.normal * collisionRadius;
            Vector2 reflectedVelocity = Vector2.Reflect(p.velocity, hit.normal);
            newVelocity = reflectedVelocity * bounceFactor;
            p.attachedTo = hit.collider;
            
            return true;
        }
        
        return false;
    }

    //  Ignition system

    void IgniteObject(GameObject target, BurningParticle source)
    {
        if (fireSpreadSystem != null)
        {
            fireSpreadSystem.TryIgniteObject(target, source.temperature);
        }
    }

    
    float GetTemperatureForType()
    {
        return thermiteTemperature;
    }
    
    Color GetColorForType(float temperature)
    {
        return new Color(1f, 0.95f, 0.8f, 1f);
    }
    
    Color GetColorForTemperature(float temperature)
    {
        Color baseColor = GetColorForType(temperature);
        
        if (temperature > 2000f)
        {
            return Color.Lerp(baseColor, Color.white, (temperature - 2000f) / 500f);
        }
        else if (temperature > 1200f)
        {
            return Color.Lerp(new Color(1f, 0.7f, 0.3f, 1f), baseColor, (temperature - 1200f) / 800f);
        }
        else if (temperature > 600f)
        {
            return Color.Lerp(new Color(1f, 0.5f, 0.1f, 1f), new Color(1f, 0.7f, 0.3f, 1f), (temperature - 600f) / 600f);
        }
        else
        {
            return Color.Lerp(new Color(0.8f, 0.2f, 0.1f, 1f), new Color(1f, 0.5f, 0.1f, 1f), temperature / 600f);
        }
    }
    
    static void OrientParticle(BurningParticle p)
    {
        if (p.isStuck) return;
        if (p.velocity.magnitude < 0.1f) return;
        
        float angle = Mathf.Rad2Deg * Mathf.Atan2(p.velocity.y, p.velocity.x) - 90f;
        p.gameObject.transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    //  Anime Style Smoke Generation
    
    Texture2D CreateAnimeSmokePuffTexture(float worldSize, List<Vector2> sphereOffsets, List<float> sphereSizes)
    {
        int texSize = 64;
        Texture2D tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        
        float cx = (texSize - 1) * 0.5f;
        float cy = (texSize - 1) * 0.5f;
        
        for (int x = 0; x < texSize; x++)
        {
            for (int y = 0; y < texSize; y++)
            {
                float nx = (x - cx) / (texSize * 0.5f);
                float ny = (y - cy) / (texSize * 0.5f);
                
                float totalDensity = 0f;

                for (int i = 0; i < sphereOffsets.Count; i++)
                {
                    Vector2 offset = sphereOffsets[i];
                    float size = sphereSizes[i];
                    
                    float dx = nx - offset.x;
                    float dy = ny - offset.y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    float sphereRadius = size * 1.1f;
                    float density = Mathf.Max(0f, 1f - (dist / sphereRadius));

                    density = Mathf.Pow(density, 0.8f);

                    if (density > 0f && density < 0.3f)
                    {
                        density = Mathf.SmoothStep(0f, 0.3f, density);
                    }

                    if (enableInternalShadow && density > 0.2f)
                    {
                        float shadowAngle = Mathf.Atan2(dy - offset.y, dx - offset.x);
                        float lightAngle = 45f * Mathf.Deg2Rad;
                        float angleDiff = Mathf.Abs(Mathf.DeltaAngle(shadowAngle * Mathf.Rad2Deg, lightAngle * Mathf.Rad2Deg));
                        
                        if (angleDiff > 110f)
                        {
                            float shadowFactor = Mathf.Clamp01((angleDiff - 110f) / 70f);
                            density *= (1f - shadowDensity * shadowFactor * 0.5f); // More subtle
                        }
                    }
                    
                    totalDensity += density;
                }

                totalDensity = Mathf.Clamp01(totalDensity);

                totalDensity = Mathf.SmoothStep(0f, 1f, totalDensity);
                
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, totalDensity));
            }
        }
        
        tex.Apply();
        return tex;
    }
    
    Texture2D CreateAnimeSmokeOutlineTexture(float worldSize, List<Vector2> sphereOffsets, List<float> sphereSizes)
    {
        int texSize = 64;
        Texture2D tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        
        float cx = (texSize - 1) * 0.5f;
        float cy = (texSize - 1) * 0.5f;
        
        for (int x = 0; x < texSize; x++)
        {
            for (int y = 0; y < texSize; y++)
            {
                float nx = (x - cx) / (texSize * 0.5f);
                float ny = (y - cy) / (texSize * 0.5f);
                
                float minDistToEdge = float.MaxValue;

                for (int i = 0; i < sphereOffsets.Count; i++)
                {
                    Vector2 offset = sphereOffsets[i];
                    float size = sphereSizes[i];
                    
                    float dx = nx - offset.x;
                    float dy = ny - offset.y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    
                    float sphereRadius = size * 0.95f;
                    float distToEdge = Mathf.Abs(dist - sphereRadius);
                    minDistToEdge = Mathf.Min(minDistToEdge, distToEdge);
                }

                float outlineWidth = outlineThickness;
                float outlineAlpha = 0f;
                
                if (minDistToEdge < outlineWidth)
                {
                    outlineAlpha = 1f - (minDistToEdge / outlineWidth);
                    outlineAlpha = Mathf.Pow(outlineAlpha, 0.8f);
                }
                
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, outlineAlpha));
            }
        }
        
        tex.Apply();
        return tex;
    }

    // Allocation + Pooling

BurningParticle AllocateBurningParticle()
{
    GameObject obj = new GameObject("ThermiteParticle");
    obj.transform.SetParent(transform);
    
    SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
    sr.sprite = particleSprite;
    sr.color = Color.white;
    sr.sortingOrder = 10;
    
    if (useAdditiveBlending)
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        sr.material = mat;
    }
    
    UnityEngine.Rendering.Universal.Light2D light = null;
if (enableDynamicLighting)
{
    light = obj.AddComponent<UnityEngine.Rendering.Universal.Light2D>();

    light.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Point;

    light.intensity = lightIntensity;
    light.pointLightOuterRadius = lightRadius;
    light.pointLightInnerRadius = 0f;
    light.pointLightInnerAngle = 360f;  
    light.pointLightOuterAngle = 360f;
    light.color = new Color(1f, 0.8f, 0.4f);
    light.falloffIntensity = 0.5f;
    light.blendStyleIndex = 1;
}
 
    return new BurningParticle
    {
        gameObject = obj,
        renderer = sr,
        light2D = light,
        trailRenderer = null,
        velocity = Vector2.zero,
        lifetime = 0f,
        maxLifetime = sparkLifetime,
        initialSize = particleSize,
        baseColor = coreColor,
        temperature = thermiteTemperature,
        isStuck = false,
        smokeEmissionTimer = 0f,
        ignitionCheckTimer = 0f,
        shouldEmitSmoke = false,
        hasTrail = false 
    };
}
    
    AnimeSmokePuff AllocateAnimeSmokePuff()
    {
        GameObject obj = new GameObject("AnimeSmokePuff");
        obj.transform.SetParent(transform);
        
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.color = smokeColor;
        sr.sortingOrder = 9;

        SpriteRenderer outlineRenderer = null;
        if (enableSmokeOutlines)
        {
            GameObject outlineObj = new GameObject("Outline");
            outlineObj.transform.SetParent(obj.transform);
            outlineObj.transform.localPosition = Vector3.zero;
            outlineObj.transform.localScale = Vector3.one * 1.05f;
            
            outlineRenderer = outlineObj.AddComponent<SpriteRenderer>();
            outlineRenderer.color = outlineColor;
            outlineRenderer.sortingOrder = 8;
        }
        
return new AnimeSmokePuff
{
    gameObject = obj,
    renderer = sr,
    outlineRenderer = outlineRenderer,
    velocity = Vector2.zero,
    initialVelocity = Vector2.zero,
    lifetime = 0f,
    maxLifetime = 0f,
    initialSize = 0f,
    baseColor = Color.white,
    rotationAngle = 0f,
    rotationSpeed = 0f,
    expansionRate = 1f,
    lastSteppedPosition = Vector2.zero,
    stepAccumulator = 0f,
    sphereOffsets = new List<Vector2>(),
    sphereSizes = new List<float>(),
    customRiseSpeed = 0f, 
    isImpactSmoke = false  
};
    }
    
    EjectaParticle AllocateEjectaParticle()
    {
        GameObject obj = new GameObject("EjectaParticle");
        obj.transform.SetParent(transform);
        
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = ejectaSprite;
        sr.color = ejectaColor;
        sr.sortingOrder = 8;
        
        return new EjectaParticle
        {
            gameObject = obj,
            renderer = sr,
            velocity = Vector2.zero,
            lifetime = 0f,
            maxLifetime = ejectaLifetime,
            initialSize = ejectaParticleSize,
            baseColor = ejectaColor
        };
    }
    
void ReturnBurningParticle(BurningParticle p, int index)
{
    if (p.light2D != null)
        p.light2D.enabled = false;

    if (p.hasTrail && p.trailRenderer != null)
    {
        ReturnTrailToPool(p.trailRenderer);
        p.trailRenderer = null;
        p.hasTrail = false;
    }
    
    p.gameObject.SetActive(false);
    particlePool.Enqueue(p);
    activeParticles.RemoveAt(index);
}
    
    void ReturnAnimeSmokePuff(AnimeSmokePuff s, int index)
    {
        s.gameObject.SetActive(false);
        smokePool.Enqueue(s);
        activeSmoke.RemoveAt(index);
    }
    
    void ReturnEjectaParticle(EjectaParticle e, int index)
    {
        e.gameObject.SetActive(false);
        ejectaPool.Enqueue(e);
        activeEjecta.RemoveAt(index);
    }

    Sprite CreateIncendiaryParticleSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float nx = (x - cx) / (size * 0.5f);
                float ny = (y - cy) / (size * 0.5f);
                
                float alpha = EvalMoltenDroplet(nx, ny);
                
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
            }
        }
        
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
    
    Sprite CreateEjectaSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float nx = (x - cx) / (size * 0.5f);
                float ny = (y - cy) / (size * 0.5f);
                
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = Mathf.Max(0f, 1.2f - dist * 1.1f);
                
                float noise1 = Mathf.PerlinNoise(x * 0.3f, y * 0.3f);
                float noise2 = Mathf.PerlinNoise(x * 0.6f + 100f, y * 0.6f + 100f);
                float noise3 = Mathf.PerlinNoise(x * 1.2f + 200f, y * 1.2f + 200f);
                
                float combinedNoise = (noise1 * 0.5f) + (noise2 * 0.3f) + (noise3 * 0.2f);
                alpha *= Mathf.Lerp(0.5f, 1.2f, combinedNoise);
                
                if (combinedNoise < 0.35f && dist > 0.5f)
                {
                    alpha *= 0.6f;
                }
                
                alpha = Mathf.Clamp01(alpha) * 0.8f;
                
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
    
    static float EvalMoltenDroplet(float nx, float ny)
    {
        float dist = Mathf.Sqrt(nx * nx + ny * ny * 1.2f);
        float edge = 0.9f - dist;
        
        float angle = Mathf.Atan2(ny, nx);
        float noise = Mathf.Sin(angle * 3f) * 0.15f;
        edge += noise;
        
        return edge * 4f;
    }

    public void TriggerIncendiaryImpact(Vector2 position, Vector2 velocity, float intensity = 1f)
    {
        float impactSpeed = Mathf.Lerp(minImpactVelocity, maxImpactVelocity, Mathf.Clamp01(intensity));
        
        if (velocity.magnitude > impactSpeed)
        {
            impactSpeed = velocity.magnitude;
        }
        
        Vector2 surfaceNormal = new Vector2(-velocity.y, velocity.x).normalized;
        
        CreateIncendiaryImpact(position, velocity, surfaceNormal, impactSpeed);
    }
    
    public void TriggerIncendiaryImpactWithNormal(Vector2 position, Vector2 velocity, Vector2 surfaceNormal, float intensity = 1f)
    {
        float impactSpeed = Mathf.Lerp(minImpactVelocity, maxImpactVelocity, Mathf.Clamp01(intensity));
        
        if (velocity.magnitude > impactSpeed)
        {
            impactSpeed = velocity.magnitude;
        }
        
        CreateIncendiaryImpact(position, velocity, surfaceNormal, impactSpeed);
    }
    

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        
        if (visualizeIgnitionRadius)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            
            foreach (BurningParticle p in activeParticles)
            {
                if (p.gameObject.activeSelf)
                {
                    Gizmos.DrawWireSphere(p.gameObject.transform.position, ignitionRadius);
                }
            }
        }
        
        if (visualizeEjectaCone && activeEjecta.Count > 0)
        {
            Gizmos.color = new Color(0.6f, 0.5f, 0.4f, 0.5f);
            
            foreach (EjectaParticle e in activeEjecta)
            {
                if (e.gameObject.activeSelf)
                {
                    Gizmos.DrawWireSphere(e.gameObject.transform.position, 0.05f);
                    Gizmos.DrawRay(e.gameObject.transform.position, e.velocity.normalized * 0.3f);
                }
            }
        }
    }
}