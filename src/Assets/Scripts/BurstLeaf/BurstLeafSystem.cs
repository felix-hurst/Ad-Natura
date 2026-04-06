using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public class BurstLeafSystem : MonoBehaviour
{
    // Static list allows external systems (e.g. weapons, wind events) to broadcast
    // force effects to every active leaf system without needing direct references.
    private static List<BurstLeafSystem> allInstances = new List<BurstLeafSystem>();

    public static BurstLeafSystem Instance => allInstances.Count > 0 ? allInstances[0] : null;

    public static IReadOnlyList<BurstLeafSystem> AllInstances => allInstances;

    public enum SpawnPattern
    {
        Rectangle,
        Circle,
        Cluster
    }

    [System.Serializable]
    public class LeafParticleType
    {
        [Tooltip("Name for identification (e.g. 'Heavy Oak Leaf', 'Light Petal', 'Ash Flake')")]
        public string name = "Default Leaf";

        [Header("Weight / Mass")]
        [Tooltip("Mass of this particle type. Lower = blown more easily by wind and blasts. Default leaf is 0.0001")]
        [Range(0.00001f, 0.01f)]
        public float mass = 0.0001f;

        [Header("Size")]
        [Tooltip("Min and max size for this particle type")]
        public Vector2 sizeRange = new Vector2(0.03f, 0.08f);

        [Header("Color")]
        [Tooltip("Color gradient to sample from when spawning this type")]
        public Gradient colorGradient;

        [Header("Spawning")]
        [Tooltip("Relative spawn probability. Higher = more of this type will spawn. E.g. weight 3 spawns 3x more often than weight 1")]
        [Range(0.1f, 10f)]
        public float spawnWeight = 1f;

        [Header("Drag")]
        [Tooltip("General air resistance. Higher = slows down faster in all directions")]
        [Range(0.5f, 5f)]
        public float drag = 1.5f;

        [Header("Air Resistance (Falling Behavior)")]
        [Tooltip("Extra vertical drag when falling. Simulates a flat surface catching air underneath. 0 = drops like a rock, 3+ = floats down gently like a flat leaf")]
        [Range(0f, 8f)]
        public float airResistance = 2f;

        [Tooltip("How much the particle sways/flutters side to side while falling. 0 = falls straight, 2+ = drifts and weaves")]
        [Range(0f, 5f)]
        public float flutter = 1.5f;

        [Tooltip("Speed of the flutter oscillation. Higher = faster sway")]
        [Range(0.5f, 8f)]
        public float flutterSpeed = 3f;

        // Guarantees every particle type has a valid gradient at runtime, since
        // Unity does not serialize null Gradient fields reliably across all workflows.
        public void EnsureGradient()
        {
            if (colorGradient == null)
            {
                colorGradient = new Gradient();
                colorGradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(new Color(0.85f, 0.15f, 0.05f), 0f),
                        new GradientColorKey(new Color(0.95f, 0.5f, 0.1f), 0.33f),
                        new GradientColorKey(new Color(0.95f, 0.75f, 0.2f), 0.66f),
                        new GradientColorKey(new Color(0.6f, 0.35f, 0.1f), 1f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                );
            }
        }
    }

    // Kills leaves that have been inside a SlimeMold's light zone long enough.
    // This is intentionally decoupled from the physics job so environmental kill
    // logic can reference managed types (Texture2D) that Burst cannot access.
    private void ProcessEnvironmentalDeath()
    {
        foreach (var manager in SlimeMoldManager.AllInstances)
        {
            Texture2D lightTex = manager.GetLightTexture();
            Rect bounds = manager.GetWorldBounds();

            for (int i = 0; i < activeLeafCount; i++)
            {
                float3 pos = positions[i];
                float u = (pos.x - bounds.x) / bounds.width;
                float v = (pos.y - bounds.y) / bounds.height;

                // Leaves outside this manager's bounds can't be affected by it.
                if (u < 0f || u > 1f || v < 0f || v > 1f) continue;

                float lightLevel = lightTex.GetPixelBilinear(u, v).r;

                /*
                if (lightLevel > 0.5f && Time.frameCount % 60 == 0)
                    Debug.Log($"[Spore] Inside Killzone! Intensity: {lightLevel}");
                */

                // Force-expire the leaf by setting its age to its lifetime cap.
                // This lets the normal fade-out path handle the visual transition
                // rather than popping the leaf out of existence instantly.
                if (lightLevel > 0.8f)
                {
                    leafAge[i] = leafMaxLifetime[i];
                }
            }
        }
    }

    [Header("Particle Types")]
    [Tooltip("Define different types of particles with unique weight, size, and color. Leave empty to use the global Appearance/Physics settings as a single type.")]
    [SerializeField] private LeafParticleType[] particleTypes = new LeafParticleType[0];

    [Header("Spawning")]
    [SerializeField] private bool autoSpawn = true;
    [Tooltip("If true, spawns all leaves instantly on Start instead of continuously over time")]
    [SerializeField] private bool spawnAllAtOnce = false;
    [Tooltip("Number of leaves to spawn instantly when spawnAllAtOnce is enabled")]
    [SerializeField] private int instantSpawnCount = 500;
    [SerializeField] private float spawnRate = 5f;
    [SerializeField] private int maxLeaves = 5000;
    [SerializeField] private Vector2 spawnArea = new Vector2(20f, 5f);

    [Header("Appearance (Global Fallback)")]
    [Tooltip("Used when no Particle Types are defined above")]
    [SerializeField] private Mesh leafMesh;
    [SerializeField] private Material leafMaterial;
    [SerializeField] private Vector2 sizeRange = new Vector2(0.03f, 0.08f);
    [SerializeField] private Gradient leafColorGradient;

    [Header("Physics (Global Fallback)")]
    [Tooltip("Used when no Particle Types are defined above")]
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float mass = 0.0001f;
    [SerializeField] private float drag = 1.5f;
    [SerializeField] private float angularDrag = 2f;
    [Tooltip("Extra vertical drag when falling (global fallback). Higher = floats down more slowly")]
    [Range(0f, 8f)]
    [SerializeField] private float airResistance = 2f;
    [Tooltip("Side-to-side flutter while falling (global fallback). Higher = more sway")]
    [Range(0f, 5f)]
    [SerializeField] private float flutter = 1.5f;
    [Tooltip("Speed of flutter oscillation (global fallback)")]
    [Range(0.5f, 8f)]
    [SerializeField] private float flutterSpeed = 3f;

    [Header("Collision")]
    [SerializeField] private bool enableCollision = true;
    [SerializeField] private LayerMask collisionLayers = ~0;
    [SerializeField] private float collisionBounce = 0.1f;
    [SerializeField] private float groundFriction = 0.8f;
    [SerializeField] private float contactDeleteTime = 15f;
    [SerializeField] private int minContactObjects = 2;

    [Header("Wind Boost for Grounded Leaves")]
    [SerializeField] private float groundedLiftMultiplier = 3f;

    [Header("Wind")]
    [SerializeField] private float windMultiplier = 3f;

    [Header("Sleep Optimization")]
    [SerializeField] private bool enableStickyLeaves = true;
    [Tooltip("Chance (0-1) that a leaf becomes sticky and sleeps permanently on Ground layer")]
    [Range(0f, 1f)]
    [SerializeField] private float stickyLeafChance = 0.3f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Lifetime")]
    [SerializeField] private float minLifetime = 60f;
    [SerializeField] private float maxLifetime = 120f;
    [SerializeField] private float fadeOutDuration = 2f;

    [Header("Performance")]
    [SerializeField] private int batchSize = 1023;
    [SerializeField] private int maxCollisionChecksPerFrame = 200;

    [Header("Debug")]
    [SerializeField] private bool showDebug = true;

    [Header("Environmental Hazards")]
    [SerializeField] private SlimeMoldManager slimeEnvironment;

    // All per-leaf data lives in NativeArrays so the Burst physics job can read and
    // write them without managed-heap overhead or GC pressure each frame.
    private NativeArray<float3> positions;
    private NativeArray<float3> velocities;
    private NativeArray<quaternion> rotations;
    private NativeArray<float> angularVelocities;
    private NativeArray<float> sizes;
    private NativeArray<float4> colors;
    private NativeArray<bool> isAsleep;
    private NativeArray<float3> windForces;

    // Previous frame's wind is stored so we can compute wind delta and apply
    // extra lift when a sudden gust hits a grounded leaf.
    private NativeArray<float3> prevWindForces;
    private NativeArray<float> groundedTime;

    // Per-leaf multiplier so smaller (lighter) leaves are more easily lifted off the ground.
    private NativeArray<float> groundedLiftFactor;

    private NativeArray<float> contactTime;
    private NativeArray<int> contactCount;

    // Sticky leaves are permanently locked to the ground once they land,
    // simulating wet or heavy leaves that won't blow away again.
    private NativeArray<bool> isSticky;
    private NativeArray<bool> isPermanentlySleeping;

    private NativeArray<float> leafAge;
    private NativeArray<float> leafMaxLifetime;
    private NativeArray<float> fadeProgress;

    // Original alpha is captured at the moment fading begins so we lerp from
    // the correct starting opacity rather than always fading from fully opaque.
    private NativeArray<float> originalAlpha;

    // Per-leaf physics properties allow each particle type to behave differently
    // without branching on type index inside the Burst job.
    private NativeArray<float> leafMass;
    private NativeArray<float> leafDrag;
    private NativeArray<int> leafTypeIndex;
    private NativeArray<float> leafAirResistance;
    private NativeArray<float> leafFlutter;
    private NativeArray<float> leafFlutterSpeed;

    // Random phase offset so leaves that spawn at the same time don't all
    // flutter in sync, which would look artificial.
    private NativeArray<float> leafFlutterOffset;

    // Matrices and color blocks are split into fixed-size batches because
    // Graphics.DrawMeshInstanced has a hard limit of 1023 instances per call.
    private Matrix4x4[][] batchMatrices;
    private MaterialPropertyBlock[] propertyBlocks;
    private Vector4[][] batchColors;
    private int activeLeafCount = 0;

    private float spawnTimer = 0f;
    private float totalSpawnWeight = 0f;

    // Cached flag avoids repeated null/length checks inside hot paths.
    private bool useParticleTypes = false;

    public int ParticleTypeCount => useParticleTypes ? particleTypes.Length : 0;

    public string GetParticleTypeName(int index)
    {
        if (index >= 0 && index < particleTypes.Length)
            return particleTypes[index].name;
        return "Global";
    }

    public int ActiveLeafCount => activeLeafCount;

    public NativeArray<float3> Positions => positions;

    public NativeArray<float3> Velocities => velocities;

    public NativeArray<bool> IsPermanentlySleeping => isPermanentlySleeping;

    public NativeArray<bool> IsAsleep => isAsleep;

    public NativeArray<float> GroundedTime => groundedTime;

    public NativeArray<float> Sizes => sizes;

    void Awake()
    {
        if (!allInstances.Contains(this))
        {
            allInstances.Add(this);
        }
    }

    void Start()
    {
        Debug.Log("[BurstLeaf] Starting initialization...");

        Debug.Log($"[BurstLeaf] Size Range from Inspector: {sizeRange.x} to {sizeRange.y}");
        if (sizeRange.x > 0.5f || sizeRange.y > 0.5f)
        {
            Debug.LogWarning($"[BurstLeaf] Size range seems large! For small leaves, use values like 0.03-0.08. Current: {sizeRange}");
        }

        // All arrays are allocated at max capacity upfront to avoid runtime
        // reallocation, which would require disposing and recreating NativeArrays.
        positions = new NativeArray<float3>(maxLeaves, Allocator.Persistent);
        velocities = new NativeArray<float3>(maxLeaves, Allocator.Persistent);
        rotations = new NativeArray<quaternion>(maxLeaves, Allocator.Persistent);
        angularVelocities = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        sizes = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        colors = new NativeArray<float4>(maxLeaves, Allocator.Persistent);
        isAsleep = new NativeArray<bool>(maxLeaves, Allocator.Persistent);
        windForces = new NativeArray<float3>(maxLeaves, Allocator.Persistent);
        prevWindForces = new NativeArray<float3>(maxLeaves, Allocator.Persistent);
        groundedTime = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        groundedLiftFactor = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        contactTime = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        contactCount = new NativeArray<int>(maxLeaves, Allocator.Persistent);
        isSticky = new NativeArray<bool>(maxLeaves, Allocator.Persistent);
        isPermanentlySleeping = new NativeArray<bool>(maxLeaves, Allocator.Persistent);
        leafAge = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        leafMaxLifetime = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        fadeProgress = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        originalAlpha = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        leafMass = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        leafDrag = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        leafTypeIndex = new NativeArray<int>(maxLeaves, Allocator.Persistent);
        leafAirResistance = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        leafFlutter = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        leafFlutterSpeed = new NativeArray<float>(maxLeaves, Allocator.Persistent);
        leafFlutterOffset = new NativeArray<float>(maxLeaves, Allocator.Persistent);

        useParticleTypes = particleTypes != null && particleTypes.Length > 0;
        if (useParticleTypes)
        {
            // Pre-sum spawn weights once so PickRandomParticleType doesn't
            // recompute the total on every leaf spawn.
            totalSpawnWeight = 0f;
            foreach (var pType in particleTypes)
            {
                pType.EnsureGradient();
                totalSpawnWeight += pType.spawnWeight;
            }
            Debug.Log($"[BurstLeaf] {particleTypes.Length} particle types configured, total spawn weight: {totalSpawnWeight:F1}");
            for (int i = 0; i < particleTypes.Length; i++)
            {
                var pt = particleTypes[i];
                Debug.Log($"[BurstLeaf]   Type {i}: '{pt.name}' - Mass:{pt.mass}, Size:{pt.sizeRange}, Drag:{pt.drag}, AirRes:{pt.airResistance}, Flutter:{pt.flutter}, SpawnWeight:{pt.spawnWeight}");
            }
        }
        else
        {
            Debug.Log("[BurstLeaf] No particle types defined - using global Appearance/Physics settings");
        }

        Debug.Log($"[BurstLeaf] Native arrays created for {maxLeaves} leaves");

        // Each batch maps to one DrawMeshInstanced call; pre-allocating arrays here
        // avoids allocations inside LateUpdate's render loop.
        int batchCount = Mathf.CeilToInt((float)maxLeaves / batchSize);
        batchMatrices = new Matrix4x4[batchCount][];
        batchColors = new Vector4[batchCount][];
        propertyBlocks = new MaterialPropertyBlock[batchCount];

        for (int i = 0; i < batchCount; i++)
        {
            int count = Mathf.Min(batchSize, maxLeaves - i * batchSize);
            batchMatrices[i] = new Matrix4x4[count];
            batchColors[i] = new Vector4[count];
            propertyBlocks[i] = new MaterialPropertyBlock();
        }

        Debug.Log($"[BurstLeaf] Created {batchCount} render batches");

        if (leafMesh == null)
        {
            leafMesh = CreateLeafMesh();
            Debug.Log("[BurstLeaf] Created default leaf mesh");
        }
        else
        {
            Debug.Log($"[BurstLeaf] Using provided mesh: {leafMesh.name}");
        }

        if (leafMaterial == null)
        {
            leafMaterial = CreateDefaultMaterial();
            Debug.Log("[BurstLeaf] Created default material with GPU instancing");
        }
        else
        {
            Debug.Log($"[BurstLeaf] Using provided material: {leafMaterial.name}");
            if (!leafMaterial.enableInstancing)
            {
                // GPU instancing is required for DrawMeshInstanced to batch correctly;
                // without it each leaf would be a separate draw call.
                Debug.LogWarning("[BurstLeaf] Material does not have GPU instancing enabled! Enabling it now.");
                leafMaterial.enableInstancing = true;
            }
        }

        if (leafColorGradient == null)
        {
            leafColorGradient = new Gradient();
            leafColorGradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(new Color(0.85f, 0.15f, 0.05f), 0f),
                    new GradientColorKey(new Color(0.95f, 0.5f, 0.1f), 0.33f),
                    new GradientColorKey(new Color(0.95f, 0.75f, 0.2f), 0.66f),
                    new GradientColorKey(new Color(0.6f, 0.35f, 0.1f), 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
            Debug.Log("[BurstLeaf] Created default autumn color gradient");
        }

        Debug.Log($"[BurstLeaf] Initialization complete! Max leaves: {maxLeaves}, Batches: {batchCount}");
        Debug.Log($"[BurstLeaf] Spawn settings - Auto: {autoSpawn}, Rate: {spawnRate}");
        Debug.Log($"[BurstLeaf] Lifetime settings - Min: {minLifetime}s, Max: {maxLifetime}s");

        try
        {
            // Seed a small number of leaves immediately so the scene doesn't look empty
            // on the first few frames while the spawn rate ramps up.
            Debug.Log("[BurstLeaf] About to spawn test leaves...");
            for (int i = 0; i < 10; i++)
            {
                Debug.Log($"[BurstLeaf] Spawning leaf {i}...");
                SpawnLeaf();
            }
            Debug.Log($"[BurstLeaf] Test leaves spawned! Active count: {activeLeafCount}");

            if (spawnAllAtOnce)
            {
                int toSpawn = Mathf.Min(instantSpawnCount, maxLeaves) - activeLeafCount;
                for (int i = 0; i < toSpawn; i++)
                {
                    SpawnLeaf();
                }
                Debug.Log($"[BurstLeaf] Instant spawn complete! Spawned {activeLeafCount} leaves at once.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BurstLeaf] ERROR spawning test leaves: {e.Message}");
            Debug.LogError($"[BurstLeaf] Stack trace: {e.StackTrace}");
        }
    }

    void Update()
    {
        if (autoSpawn && !spawnAllAtOnce && activeLeafCount < maxLeaves)
        {
            spawnTimer += Time.deltaTime;
            int toSpawn = Mathf.FloorToInt(spawnTimer * spawnRate);

            if (showDebug && toSpawn > 0 && Time.frameCount < 300)
            {
                Debug.Log($"[BurstLeaf] Spawning {toSpawn} leaves (timer: {spawnTimer:F3}, rate: {spawnRate})");
            }

            for (int i = 0; i < toSpawn && activeLeafCount < maxLeaves; i++)
            {
                SpawnLeaf();
            }

            // Subtract only the consumed portion of the timer so fractional
            // remainder carries over and keeps the rate accurate at any framerate.
            if (toSpawn > 0)
            {
                spawnTimer -= toSpawn / spawnRate;
            }
        }

        // Sample wind per-leaf on the main thread before the Burst job runs,
        // because the wind manager is a managed object Burst cannot access.
        for (int i = 0; i < activeLeafCount; i++)
        {
            windForces[i] = GetWindForceAtPosition(positions[i]);
        }

        float deltaTime = Time.deltaTime;

        var physicsJob = new LeafPhysicsJob
        {
            velocities = velocities,
            rotations = rotations,
            angularVelocities = angularVelocities,
            isAsleep = isAsleep,
            windForces = windForces,
            deltaTime = deltaTime,
            currentTime = Time.time,
            gravity = new float3(0, gravity, 0),
            masses = leafMass,
            drags = leafDrag,
            angularDrag = angularDrag,
            airResistances = leafAirResistance,
            flutters = leafFlutter,
            flutterSpeeds = leafFlutterSpeed,
            flutterOffsets = leafFlutterOffset
        };

        // Batch size of 64 balances work per job thread vs. scheduling overhead
        // for typical leaf counts in the thousands.
        JobHandle handle = physicsJob.Schedule(activeLeafCount, 64);
        handle.Complete();

        if (enableCollision)
        {
            MoveWithSweepCollision(deltaTime);
        }
        else
        {
            // When collision is disabled, skip the sweep entirely and just integrate
            // position, which is significantly cheaper for background scenes.
            for (int i = 0; i < activeLeafCount; i++)
            {
                if (!isAsleep[i])
                {
                    positions[i] += velocities[i] * deltaTime;
                }
            }
        }

        CheckAndDeleteStuckLeaves();

        ProcessEnvironmentalDeath();

        UpdateLeafLifetimes(deltaTime);

        UpdateRenderingData();

        if (showDebug && Time.frameCount % 60 == 0)
        {
            CountSleeping();
        }
    }

    void UpdateLeafLifetimes(float deltaTime)
    {
        int expiredCount = 0;

        // Iterate in reverse so DeleteLeaf's swap-with-last doesn't skip
        // the leaf that gets moved into the deleted slot.
        for (int i = activeLeafCount - 1; i >= 0; i--)
        {
            leafAge[i] += deltaTime;

            if (leafAge[i] >= leafMaxLifetime[i])
            {
                // Snapshot alpha once at the start of fading so the lerp
                // starts from the actual current opacity, not always 1.
                if (fadeProgress[i] == 0f)
                {
                    originalAlpha[i] = colors[i].w;
                }

                fadeProgress[i] += deltaTime / fadeOutDuration;

                float newAlpha = Mathf.Lerp(originalAlpha[i], 0f, fadeProgress[i]);
                float4 col = colors[i];
                col.w = newAlpha;
                colors[i] = col;

                if (fadeProgress[i] >= 1f)
                {
                    DeleteLeaf(i);
                    expiredCount++;
                }
            }
        }

        if (showDebug && expiredCount > 0 && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[BurstLeaf] {expiredCount} leaves faded out this frame");
        }
    }

    // Leaves that are permanently wedged between two or more colliders won't flow
    // naturally into the lifetime system, so this detects them and triggers their
    // fade-out to prevent an ever-growing pool of invisible stuck leaves.
    void CheckAndDeleteStuckLeaves()
    {
        for (int i = activeLeafCount - 1; i >= 0; i--)
        {
            // Already fading — don't interfere with the active fade.
            if (fadeProgress[i] > 0f) continue;

            float3 pos = positions[i];
            float size = sizes[i];
            float radius = size * 0.4f;
            Vector2 pos2D = new Vector2(pos.x, pos.y);

            Collider2D[] overlaps = Physics2D.OverlapCircleAll(pos2D, radius, collisionLayers);

            int objectCount = 0;
            foreach (var col in overlaps)
            {
                objectCount++;
            }

            if (objectCount >= minContactObjects)
            {
                contactTime[i] += Time.deltaTime;
                contactCount[i] = objectCount;

                if (contactTime[i] >= contactDeleteTime)
                {
                    // Force the leaf into its expired state so UpdateLeafLifetimes
                    // picks it up and runs the normal fade-out path.
                    leafAge[i] = leafMaxLifetime[i];

                    if (showDebug)
                    {
                        Debug.Log($"[BurstLeaf] Starting fade for stuck leaf #{i} after {contactTime[i]:F1}s contact with {objectCount} objects");
                    }
                }
            }
            else
            {
                contactTime[i] = 0f;
                contactCount[i] = 0;
            }
        }
    }

    // Removes a leaf by swapping it with the last active leaf, then decrementing
    // the count. This keeps the active region of every array contiguous without
    // shifting elements, so deletion is O(1) regardless of array size.
    void DeleteLeaf(int index)
    {
        if (index < 0 || index >= activeLeafCount)
            return;

        int lastIndex = activeLeafCount - 1;

        if (index != lastIndex)
        {
            positions[index] = positions[lastIndex];
            velocities[index] = velocities[lastIndex];
            rotations[index] = rotations[lastIndex];
            angularVelocities[index] = angularVelocities[lastIndex];
            sizes[index] = sizes[lastIndex];
            colors[index] = colors[lastIndex];
            isAsleep[index] = isAsleep[lastIndex];
            windForces[index] = windForces[lastIndex];
            prevWindForces[index] = prevWindForces[lastIndex];
            groundedTime[index] = groundedTime[lastIndex];
            groundedLiftFactor[index] = groundedLiftFactor[lastIndex];
            contactTime[index] = contactTime[lastIndex];
            contactCount[index] = contactCount[lastIndex];
            isSticky[index] = isSticky[lastIndex];
            isPermanentlySleeping[index] = isPermanentlySleeping[lastIndex];
            leafAge[index] = leafAge[lastIndex];
            leafMaxLifetime[index] = leafMaxLifetime[lastIndex];
            fadeProgress[index] = fadeProgress[lastIndex];
            originalAlpha[index] = originalAlpha[lastIndex];
            leafMass[index] = leafMass[lastIndex];
            leafDrag[index] = leafDrag[lastIndex];
            leafTypeIndex[index] = leafTypeIndex[lastIndex];
            leafAirResistance[index] = leafAirResistance[lastIndex];
            leafFlutter[index] = leafFlutter[lastIndex];
            leafFlutterSpeed[index] = leafFlutterSpeed[lastIndex];
            leafFlutterOffset[index] = leafFlutterOffset[lastIndex];
        }

        activeLeafCount--;
    }

    // Uses a sweep cast rather than moving first and checking after, so fast-moving
    // leaves don't tunnel through thin colliders between frames.
    void MoveWithSweepCollision(float deltaTime)
    {
        for (int idx = 0; idx < activeLeafCount; idx++)
        {
            if (isPermanentlySleeping[idx]) continue;

            if (isAsleep[idx]) continue;

            float3 pos = positions[idx];
            float3 vel = velocities[idx];
            float size = sizes[idx];
            float radius = size * 0.4f;

            Vector2 pos2D = new Vector2(pos.x, pos.y);
            Vector2 vel2D = new Vector2(vel.x, vel.y);
            float speed = vel2D.magnitude;

            Vector2 moveDir = speed > 0f ? vel2D / speed : Vector2.zero;
            float moveDistance = speed * deltaTime;

            bool isGrounded = false;

            RaycastHit2D hit = Physics2D.CircleCast(pos2D, radius, moveDir, moveDistance, collisionLayers);

            if (hit.collider != null)
            {
                // Place the leaf just touching the surface rather than at the exact hit point
                // to avoid it sinking into the collider on the next frame.
                Vector2 safePos = hit.centroid + hit.normal * 0.001f;
                positions[idx] = new float3(safePos.x, safePos.y, 0);

                float velIntoSurface = Vector2.Dot(vel2D, -hit.normal);
                if (velIntoSurface > 0)
                {
                    vel2D += hit.normal * velIntoSurface;

                    if (hit.normal.y > 0.5f)
                    {
                        // Normal pointing mostly upward means the leaf landed on a floor.
                        // Apply friction to bleed off horizontal sliding velocity.
                        vel2D.x *= (1f - groundFriction * deltaTime * 10f);
                        isGrounded = true;

                        if (enableStickyLeaves && isSticky[idx] && IsOnGroundLayer(hit.collider))
                        {
                            // Sticky leaves on the ground layer stop permanently and
                            // cost nothing in subsequent frames.
                            isPermanentlySleeping[idx] = true;
                            isAsleep[idx] = true;
                            velocities[idx] = float3.zero;
                            angularVelocities[idx] = 0;

                            if (showDebug)
                            {
                                Debug.Log($"[BurstLeaf] Sticky leaf #{idx} permanently attached to ground at {positions[idx]}");
                            }

                            continue;
                        }
                    }

                    velocities[idx] = new float3(vel2D.x, vel2D.y, 0);
                }
            }
            else
            {
                positions[idx] = new float3(pos.x + vel.x * deltaTime, pos.y + vel.y * deltaTime, 0);
            }

            if (isGrounded)
                groundedTime[idx] += deltaTime;
            else
                groundedTime[idx] = 0f;

            float3 wind = windForces[idx];
            float deltaY = wind.y - prevWindForces[idx].y;
            float3 v = velocities[idx];

            float liftFactor = groundedLiftFactor[idx];

            if (isGrounded)
            {
                // Grounded lift ramps down the longer a leaf has been sitting still,
                // so freshly-landed leaves blow away more easily than settled ones.
                float rampDuration = 2f;
                float groundedMultiplier = groundedLiftMultiplier
                                           * math.max(1f - groundedTime[idx] / rampDuration, 0f)
                                           * liftFactor;

                // Boost lift during gusts that exceed the base wind speed, simulating
                // the extra turbulence that lifts leaves in a sudden strong wind.
                if (RealisticWindManager.Instance != null)
                {
                    float baseWindMag = RealisticWindManager.Instance.CurrentWind.magnitude;
                    float actualWindMag = math.length(wind);

                    if (actualWindMag > baseWindMag * 1.2f)
                    {
                        groundedMultiplier *= 1.5f;
                    }
                }

                v.y += wind.y * (groundedMultiplier * 5f);
                v.x += wind.x * (groundedMultiplier * 10f);

                // An upward wind spike this frame (positive deltaY) provides an
                // extra impulse, analogous to a leaf being flicked up by a gust front.
                if (deltaY > 0f)
                    v.y += deltaY * (groundedMultiplier * 5f) * deltaTime;

                v.x += wind.x * (groundedMultiplier * 10f) * deltaTime;

                // Only add spin when the leaf is about to be lifted — spinning a
                // leaf that stays on the ground looks wrong.
                if (v.y > 0.01f)
                {
                    float swirlBase = math.clamp(math.abs(wind.x) * 2f, 0f, 5f);
                    float swirlSpeed = swirlBase / Mathf.Sqrt(size / sizeRange.x);
                    angularVelocities[idx] += (wind.x > 0 ? 1f : -1f) * swirlSpeed * deltaTime;
                }
            }
            else
            {
                float airborneMultiplier = liftFactor;
                v.y += wind.y * deltaTime * airborneMultiplier;
                if (deltaY > 0f)
                    v.y += deltaY * deltaTime * airborneMultiplier;
            }

            velocities[idx] = v;
            prevWindForces[idx] = wind;
        }

        ResolveExistingPenetrations(deltaTime);
    }

    // Handles the case where a leaf is already overlapping a collider at the start
    // of the frame — typically after a solid object moves into it between updates.
    void ResolveExistingPenetrations(float deltaTime)
    {
        for (int idx = 0; idx < activeLeafCount; idx++)
        {
            if (isPermanentlySleeping[idx]) continue;

            if (isAsleep[idx]) continue;

            float3 pos = positions[idx];
            float size = sizes[idx];
            float radius = size * 0.4f;

            Vector2 pos2D = new Vector2(pos.x, pos.y);

            // Use a tiny fraction of the radius for the overlap test so only
            // genuine penetrations are detected, not near-surface grazing contact.
            Collider2D overlap = Physics2D.OverlapCircle(pos2D, radius * 0.001f, collisionLayers);

            if (overlap != null)
            {
                Vector2 closestPoint = overlap.ClosestPoint(pos2D);
                Vector2 toLeaf = pos2D - closestPoint;
                float dist = toLeaf.magnitude;

                // If the leaf is exactly at the surface centre, push it straight up
                // as a safe default rather than trying to normalise a zero vector.
                Vector2 exitDir = dist < 0.001f ? Vector2.up : toLeaf / dist;
                Vector2 safePos = closestPoint + exitDir * (radius + 0.01f);
                positions[idx] = new float3(safePos.x, safePos.y, 0);

                Vector2 vel2D = new Vector2(velocities[idx].x, velocities[idx].y);
                float velInto = Vector2.Dot(vel2D, -exitDir);
                if (velInto > 0)
                    vel2D += exitDir * velInto;

                velocities[idx] = new float3(vel2D.x, vel2D.y, 0);

                float3 wind = windForces[idx];
                float deltaY = wind.y - prevWindForces[idx].y;

                if (exitDir.y > 0.5f)
                {
                    if (enableStickyLeaves && isSticky[idx] && IsOnGroundLayer(overlap))
                    {
                        isPermanentlySleeping[idx] = true;
                        isAsleep[idx] = true;
                        velocities[idx] = float3.zero;
                        angularVelocities[idx] = 0;

                        if (showDebug)
                        {
                            Debug.Log($"[BurstLeaf] Sticky leaf #{idx} permanently attached to ground at {positions[idx]}");
                        }

                        continue;
                    }

                    groundedTime[idx] += deltaTime;

                    float rampDuration = 2f;
                    float groundedMultiplier = groundedLiftMultiplier
                                * math.max(1f - groundedTime[idx] / rampDuration, 0f)
                                * groundedLiftFactor[idx];

                    if (RealisticWindManager.Instance != null)
                    {
                        float baseWindMag = RealisticWindManager.Instance.CurrentWind.magnitude;
                        float actualWindMag = math.length(wind);

                        if (actualWindMag > baseWindMag * 1.2f)
                        {
                            groundedMultiplier *= 1.5f;
                        }
                    }

                    float3 v = velocities[idx];

                    v.y += wind.y * (groundedMultiplier * 5);
                    v.x += wind.x * (groundedMultiplier * 10f);

                    if (deltaY > 0f)
                        v.y += deltaY * groundedMultiplier * deltaTime;
                    v.x += wind.x * (groundedMultiplier * 10f) * deltaTime;

                    velocities[idx] = v;
                }
                else
                {
                    groundedTime[idx] = 0f;
                }

                prevWindForces[idx] = wind;
            }
            else
            {
                groundedTime[idx] = 0f;
            }
        }
    }

    void LateUpdate()
    {
        if (activeLeafCount == 0) return;

        if (showDebug && Time.frameCount == 1)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Debug.Log($"[BurstLeaf] Main Camera - Position: {cam.transform.position}, Orthographic: {cam.orthographic}, Size/FOV: {(cam.orthographic ? cam.orthographicSize : cam.fieldOfView)}");
                Debug.Log($"[BurstLeaf] Spawner position: {transform.position}, Spawn area: {spawnArea}");
            }
            else
            {
                Debug.LogWarning("[BurstLeaf] No main camera found!");
            }
        }

        if (showDebug && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[BurstLeaf] Rendering {activeLeafCount} leaves in {batchMatrices.Length} batches");
        }

        // Submit render batches in LateUpdate so all physics and animation
        // updates are finished before we build the final transform matrices.
        for (int i = 0; i < batchMatrices.Length; i++)
        {
            if (batchMatrices[i].Length == 0) continue;

            int count = Mathf.Min(batchSize, activeLeafCount - i * batchSize);
            if (count <= 0) break;

            // Both _BaseColor and _Color are set because different URP shader
            // variants use different property names for the instance color.
            propertyBlocks[i].SetVectorArray("_BaseColor", batchColors[i]);
            propertyBlocks[i].SetVectorArray("_Color", batchColors[i]);

            if (showDebug && i == 0 && Time.frameCount == 1)
            {
                Debug.Log($"[BurstLeaf] First render - Batch 0 has {count} leaves");
                Debug.Log($"[BurstLeaf] Mesh: {leafMesh.name}, Material: {leafMaterial.name}, Shader: {leafMaterial.shader.name}");
                Debug.Log($"[BurstLeaf] First leaf matrix: {batchMatrices[0][0]}");
                Debug.Log($"[BurstLeaf] First leaf color: {batchColors[0][0]}");
            }

            // Shadows and light probes are disabled because leaves are tiny and
            // the cost of shadow casting per-instance would far outweigh the visual benefit.
            Graphics.DrawMeshInstanced(
                leafMesh,
                0,
                leafMaterial,
                batchMatrices[i],
                count,
                propertyBlocks[i],
                ShadowCastingMode.Off,
                false,
                0,
                null,
                LightProbeUsage.Off
            );
        }
    }

    private System.Random colorRandom = new System.Random();

    void SpawnLeaf()
    {
        SpawnLeafInternal(
            new Vector2(
                transform.position.x + UnityEngine.Random.Range(-spawnArea.x * 0.5f, spawnArea.x * 0.5f),
                transform.position.y + UnityEngine.Random.Range(-spawnArea.y * 0.5f, spawnArea.y * 0.5f)
            ),
            -1
        );
    }

    // Weighted random selection so particle types with higher spawnWeight
    // appear proportionally more often without needing a lookup table.
    int PickRandomParticleType()
    {
        if (!useParticleTypes) return -1;

        float roll = UnityEngine.Random.Range(0f, totalSpawnWeight);
        float cumulative = 0f;
        for (int i = 0; i < particleTypes.Length; i++)
        {
            cumulative += particleTypes[i].spawnWeight;
            if (roll <= cumulative)
                return i;
        }
        return particleTypes.Length - 1;
    }

    void SpawnLeafInternal(Vector2 spawnPos, int typeIndex)
    {
        if (activeLeafCount >= maxLeaves) return;

        int index = activeLeafCount;

        int resolvedType = typeIndex >= 0 ? typeIndex : PickRandomParticleType();

        float leafSizeMin, leafSizeMax, leafMassVal, leafDragVal;
        float leafAirResVal, leafFlutterVal, leafFlutterSpeedVal;
        Gradient leafGradient;

        // Resolve all per-type properties once here so the hot path below
        // doesn't need to branch on type index for every property assignment.
        if (resolvedType >= 0 && resolvedType < particleTypes.Length)
        {
            var pType = particleTypes[resolvedType];
            leafSizeMin = pType.sizeRange.x;
            leafSizeMax = pType.sizeRange.y;
            leafMassVal = pType.mass;
            leafDragVal = pType.drag;
            leafAirResVal = pType.airResistance;
            leafFlutterVal = pType.flutter;
            leafFlutterSpeedVal = pType.flutterSpeed;
            leafGradient = pType.colorGradient;
        }
        else
        {
            leafSizeMin = sizeRange.x;
            leafSizeMax = sizeRange.y;
            leafMassVal = mass;
            leafDragVal = drag;
            leafAirResVal = airResistance;
            leafFlutterVal = flutter;
            leafFlutterSpeedVal = flutterSpeed;
            leafGradient = leafColorGradient;
        }

        float size = UnityEngine.Random.Range(leafSizeMin, leafSizeMax);
        float radius = size * 0.5f;

        // Nudge the spawn position up to 5 times to avoid spawning inside a collider,
        // which would cause an immediate penetration resolution jitter.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (Physics2D.OverlapCircle(spawnPos, radius, collisionLayers) == null)
                break;
            spawnPos += UnityEngine.Random.insideUnitCircle * size * 2f;
        }

        // Small random z offset staggers overlapping leaves in depth so they
        // don't all share the exact same z plane and cause z-fighting artefacts.
        float zOffset = UnityEngine.Random.Range(-0.01f, 0.01f);
        positions[index] = new float3(spawnPos.x, spawnPos.y, zOffset);
        sizes[index] = size;

        leafMass[index] = leafMassVal;
        leafDrag[index] = leafDragVal;
        leafTypeIndex[index] = resolvedType;
        leafAirResistance[index] = leafAirResVal;
        leafFlutter[index] = leafFlutterVal;
        leafFlutterSpeed[index] = leafFlutterSpeedVal;
        leafFlutterOffset[index] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        // Smaller leaves get a stronger ground lift factor so they blow away more
        // easily than large heavy leaves, which matches real-world behaviour.
        float globalMin = useParticleTypes ? GetSmallestSizeMin() : sizeRange.x;
        float globalMax = useParticleTypes ? GetLargestSizeMax() : sizeRange.y;
        float sizeFactor = 1f - Mathf.Clamp01((size - globalMin) / Mathf.Max(globalMax - globalMin, 0.001f));
        groundedLiftFactor[index] = Mathf.Pow(sizeFactor, 0.5f);

        velocities[index] = float3.zero;
        rotations[index] = quaternion.Euler(0, 0, UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad);
        angularVelocities[index] = UnityEngine.Random.Range(-30f, 30f) * Mathf.Deg2Rad;

        float t = (float)colorRandom.NextDouble();
        Color col = leafGradient.Evaluate(t);
        colors[index] = new float4(col.r, col.g, col.b, col.a);

        // Keep batchColors in sync at spawn time so there's no single frame
        // where a leaf renders with the default black color before UpdateRenderingData runs.
        int batchIndex = index / batchSize;
        int indexInBatch = index % batchSize;
        batchColors[batchIndex][indexInBatch] = new Vector4(col.r, col.g, col.b, col.a);

        isSticky[index] = enableStickyLeaves && (UnityEngine.Random.value < stickyLeafChance);
        isPermanentlySleeping[index] = false;

        leafAge[index] = 0f;
        leafMaxLifetime[index] = UnityEngine.Random.Range(minLifetime, maxLifetime);
        fadeProgress[index] = 0f;
        originalAlpha[index] = col.a;

        groundedTime[index] = 0f;
        contactTime[index] = 0f;
        contactCount[index] = 0;
        windForces[index] = float3.zero;
        prevWindForces[index] = float3.zero;

        isAsleep[index] = false;

        activeLeafCount++;

        if (showDebug && activeLeafCount < 230)
        {
            string typeName = resolvedType >= 0 ? particleTypes[resolvedType].name : "Global";
            Debug.Log($"[BurstLeaf] Leaf #{index} type:'{typeName}' at {positions[index]}, Size:{size:F4}, Mass:{leafMassVal}, AirRes:{leafAirResVal:F1}, Flutter:{leafFlutterVal:F1}, Sticky:{isSticky[index]}");
        }
    }

    float GetSmallestSizeMin()
    {
        float min = float.MaxValue;
        foreach (var pt in particleTypes)
            if (pt.sizeRange.x < min) min = pt.sizeRange.x;
        return min;
    }

    float GetLargestSizeMax()
    {
        float max = float.MinValue;
        foreach (var pt in particleTypes)
            if (pt.sizeRange.y > max) max = pt.sizeRange.y;
        return max;
    }

    // Tries preferred shaders in order of quality/compatibility so the system
    // degrades gracefully on hardware or render pipelines that don't support
    // the custom instanced shader.
    Material CreateDefaultMaterial()
    {
        Material mat = null;

        Shader shader = Shader.Find("Custom/InstancedUnlit2D");
        if (shader != null)
        {
            mat = new Material(shader);
            Debug.Log("[BurstLeaf] Using Custom/InstancedUnlit2D shader");

            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetFloat("_ColorMode", 0);
            mat.SetFloat("_SoftParticlesEnabled", 0);
            mat.SetFloat("_CameraFadingEnabled", 0);
            mat.SetFloat("_DistortionEnabled", 0);

            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_SOFT_PARTICLES_ON");
            mat.DisableKeyword("_FADING_ON");
            mat.DisableKeyword("_DISTORTION_ON");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        else
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                mat = new Material(shader);
                mat.SetFloat("_Surface", 1);
                mat.SetFloat("_Blend", 0);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                Debug.Log("[BurstLeaf] Using URP Unlit shader with transparency");
            }
            else
            {
                shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    mat = new Material(shader);
                    Debug.Log("[BurstLeaf] Using Sprites/Default shader");
                }
                else
                {
                    mat = new Material(Shader.Find("Standard"));
                    mat.SetFloat("_Mode", 3);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.renderQueue = 3000;
                    Debug.LogWarning("[BurstLeaf] Using Standard shader as fallback with transparency");
                }
            }
        }

        mat.enableInstancing = true;
        mat.color = Color.white;
        mat.renderQueue = 3000;

        Debug.Log($"[BurstLeaf] Created material with shader: {mat.shader.name}, Instancing: {mat.enableInstancing}, RenderQueue: {mat.renderQueue}");

        return mat;
    }

    float3 GetWindForceAtPosition(float3 pos)
    {
        if (RealisticWindManager.Instance == null)
            return float3.zero;

        Vector2 wind = RealisticWindManager.Instance.GetWindAtPosition(
            new Vector2(pos.x, pos.y)
        );

        return new float3(wind.x, wind.y, 0) * windMultiplier;
    }

    // Rebuilds the GPU-ready transform matrices from the current simulation state
    // every frame. This is intentionally separate from the physics update so the
    // two can run at different rates in future.
    void UpdateRenderingData()
    {
        int batchIndex = 0;
        int indexInBatch = 0;

        for (int i = 0; i < activeLeafCount; i++)
        {
            float size = sizes[i];
            Matrix4x4 matrix = Matrix4x4.TRS(
                new Vector3(positions[i].x, positions[i].y, positions[i].z),
                new Quaternion(rotations[i].value.x, rotations[i].value.y, rotations[i].value.z, rotations[i].value.w),
                new Vector3(size, size, size)
            );

            batchMatrices[batchIndex][indexInBatch] = matrix;
            batchColors[batchIndex][indexInBatch] = new Vector4(colors[i].x, colors[i].y, colors[i].z, colors[i].w);

            indexInBatch++;
            if (indexInBatch >= batchSize)
            {
                indexInBatch = 0;
                batchIndex++;
            }
        }
    }

    void CountSleeping()
    {
        int permanentSleepCount = 0;

        for (int i = 0; i < activeLeafCount; i++)
        {
            if (isPermanentlySleeping[i]) permanentSleepCount++;
        }

        Debug.Log($"[BurstLeaf] Active:{activeLeafCount}, Permanent:{permanentSleepCount}, Awake:{activeLeafCount - permanentSleepCount}, FPS:{(int)(1f / Time.deltaTime)}");
    }

    bool IsOnGroundLayer(Collider2D collider)
    {
        return ((1 << collider.gameObject.layer) & groundLayer) != 0;
    }

    // Generates a simple tapered leaf shape procedurally so the system works
    // out of the box without requiring an art asset.
    Mesh CreateLeafMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "ProceduralLeaf";

        int segments = 8;

        Vector3[] vertices = new Vector3[segments * 2 + 1];

        // Tip vertex at the bottom — the leaf tapers to a point here.
        vertices[0] = new Vector3(0, -0.5f, 0);

        for (int i = 0; i < segments; i++)
        {
            float t = (float)(i + 1) / segments;
            float y = Mathf.Lerp(-0.5f, 0.5f, t);

            // Sin envelope gives the characteristic widest-in-the-middle leaf silhouette.
            float widthT = Mathf.Sin(t * Mathf.PI * 0.85f);
            float width = widthT * 0.35f;

            // Slight asymmetry breaks the perfect symmetry for a more natural look.
            float asymmetry = Mathf.Sin(t * Mathf.PI * 2f) * 0.03f;

            vertices[1 + i] = new Vector3(-width + asymmetry, y, 0);
            vertices[1 + segments + i] = new Vector3(width + asymmetry, y, 0);
        }

        int[] triangles = new int[(segments * 2 - 1) * 3];
        int tri = 0;

        // Base triangle fans from the tip vertex to the first pair of edge vertices.
        triangles[tri++] = 0;
        triangles[tri++] = 1;
        triangles[tri++] = 1 + segments;

        for (int i = 0; i < segments - 1; i++)
        {
            int leftCurr = 1 + i;
            int leftNext = 1 + i + 1;
            int rightCurr = 1 + segments + i;
            int rightNext = 1 + segments + i + 1;

            triangles[tri++] = leftCurr;
            triangles[tri++] = leftNext;
            triangles[tri++] = rightCurr;

            triangles[tri++] = leftNext;
            triangles[tri++] = rightNext;
            triangles[tri++] = rightCurr;
        }

        Vector2[] uvs = new Vector2[vertices.Length];
        uvs[0] = new Vector2(0.5f, 0);
        for (int i = 0; i < segments; i++)
        {
            float v = (float)(i + 1) / segments;
            uvs[1 + i] = new Vector2(0, v);
            uvs[1 + segments + i] = new Vector2(1, v);
        }

        // All normals point toward the camera (Vector3.back) since this is a
        // 2D sprite-style mesh and we don't need accurate 3D lighting.
        Vector3[] normals = new Vector3[vertices.Length];
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.back;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.RecalculateBounds();

        Debug.Log($"[BurstLeaf] Created procedural leaf mesh - Vertices: {mesh.vertexCount}, Triangles: {mesh.triangles.Length / 3}, Bounds: {mesh.bounds}");

        return mesh;
    }

    public void SpawnLeavesInArea(Vector2 center, Vector2 areaSize, int count, SpawnPattern pattern = SpawnPattern.Rectangle, int typeIndex = -1)
    {
        int spawned = 0;
        for (int i = 0; i < count && activeLeafCount < maxLeaves; i++)
        {
            Vector2 offset = Vector2.zero;

            switch (pattern)
            {
                case SpawnPattern.Rectangle:
                    offset = new Vector2(
                        UnityEngine.Random.Range(-areaSize.x * 0.5f, areaSize.x * 0.5f),
                        UnityEngine.Random.Range(-areaSize.y * 0.5f, areaSize.y * 0.5f)
                    );
                    break;

                case SpawnPattern.Circle:
                    float radius = Mathf.Min(areaSize.x, areaSize.y) * 0.5f;
                    Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * radius;
                    offset = randomCircle;
                    break;

                case SpawnPattern.Cluster:
                    // Average three random values to produce a bell-curve distribution,
                    // so leaves clump toward the centre rather than spreading uniformly.
                    float clusterRadius = Mathf.Min(areaSize.x, areaSize.y) * 0.5f;
                    float rx = (UnityEngine.Random.value + UnityEngine.Random.value + UnityEngine.Random.value) / 3f;
                    float ry = (UnityEngine.Random.value + UnityEngine.Random.value + UnityEngine.Random.value) / 3f;
                    offset = new Vector2(
                        (rx - 0.5f) * 2f * clusterRadius,
                        (ry - 0.5f) * 2f * clusterRadius
                    );
                    break;
            }

            Vector2 spawnPos = center + offset;
            SpawnLeafAt(spawnPos, typeIndex);
            spawned++;
        }

        if (showDebug)
        {
            Debug.Log($"[BurstLeaf] SpawnLeavesInArea: Spawned {spawned} leaves at {center} with pattern {pattern}, area {areaSize}");
        }
    }

    // Radial impulse for explosions or area events. Heavier leaves (higher mass)
    // receive less velocity change than lighter ones via the massRatio factor,
    // so a blast scatters petals further than oak leaves.
    public void ApplyBlastForce(Vector2 origin, float radius, float force, float upwardBias = 0.3f)
    {
        int affected = 0;
        float radiusSq = radius * radius;

        for (int i = 0; i < activeLeafCount; i++)
        {
            float3 pos = positions[i];
            Vector2 leafPos = new Vector2(pos.x, pos.y);
            Vector2 delta = leafPos - origin;
            float distSq = delta.sqrMagnitude;

            float dist = Mathf.Sqrt(distSq);

            // Soft outer boundary: tapers force out to 2x radius instead of hard cutoff
            float effectiveRadius = radius * 2f;
            if (distSq > effectiveRadius * effectiveRadius) continue;

            float falloff;
            if (dist <= radius)
            {
                // Inner zone: squared falloff — strong core (same behaviour as before)
                float t = 1f - (dist / radius);
                falloff = t * t;
            }
            else
            {
                // Outer zone: gentle linear fade from radius → 2x radius, capped at 25% peak
                float t = 1f - ((dist - radius) / radius);
                falloff = t * 0.25f;
            }

            // If the leaf is at the exact origin, push it in a random direction
            // to avoid a zero-length direction vector.
            Vector2 blastDir = dist > 0.01f ? delta / dist : UnityEngine.Random.insideUnitCircle.normalized;

            blastDir = (blastDir + Vector2.up * upwardBias).normalized;

            float referenceMass = useParticleTypes ? particleTypes[0].mass : mass;
            float massRatio = Mathf.Max(referenceMass, 0.00001f) / Mathf.Max(leafMass[i], 0.00001f);
            float leafForce = force * falloff * massRatio * UnityEngine.Random.Range(0.7f, 1.3f);

            float3 vel = velocities[i];
            vel.x += blastDir.x * leafForce;
            vel.y += blastDir.y * leafForce;
            velocities[i] = vel;

            angularVelocities[i] += UnityEngine.Random.Range(-15f, 15f) * falloff;

            // Wake any sleeping leaves in the blast radius so they respond.
            isAsleep[i] = false;
            if (isPermanentlySleeping[i])
            {
                isPermanentlySleeping[i] = false;
                groundedTime[i] = 0f;
            }

            affected++;
        }

        if (showDebug)
        {
            Debug.Log($"[BurstLeaf] ApplyBlastForce: {affected} leaves affected by blast at {origin}, radius {radius}, force {force}");
        }
    }

    // Three-phase muzzle blast simulates how a real gunshot disturbs the environment:
    // 1. Forward pressure cone (shockwave), 2. Brief vacuum pull-back, 3. Turbulent wake.
    public void ApplyMuzzleBlast(Vector2 origin, Vector2 fireDirection, MuzzleBlastSettings settings = null)
    {
        if (settings == null) settings = new MuzzleBlastSettings();

        Vector2 dir = fireDirection.normalized;

        int blasted = ApplyConeBurst(origin, dir, settings);

        StartCoroutine(MuzzleBlastSequence(origin, dir, settings));

        if (showDebug)
        {
            Debug.Log($"[BurstLeaf] MuzzleBlast fired at {origin} dir {dir} — Phase 1 hit {blasted} leaves");
        }
    }

    [System.Serializable]
    public class MuzzleBlastSettings
    {
        [Header("Phase 1 — Shockwave Cone")]
        [Tooltip("How far the shockwave reaches in front of the muzzle")]
        public float coneRadius = 6f;
        [Tooltip("Half-angle of the cone in degrees (90 = hemisphere, 45 = tight cone)")]
        [Range(15f, 90f)]
        public float coneHalfAngle = 60f;
        [Tooltip("Peak force applied to leaves directly in front of the muzzle")]
        public float coneForce = 18f;
        [Tooltip("How much leaves get pushed upward on top of the forward blast")]
        [Range(0f, 1f)]
        public float upwardBias = 0.25f;
        [Tooltip("Leaves behind the muzzle still get a fraction of the blast (side-wash)")]
        [Range(0f, 0.5f)]
        public float rearWashFraction = 0.25f;
        [Tooltip("Radius of the weaker rear/side wash zone")]
        public float rearWashRadius = 3f;

        [Header("Phase 1B — Ground Shockwave")]
        [Tooltip("Enable radial ground-level blast that pushes leaves around the muzzle regardless of aim direction")]
        public bool enableGroundShockwave = true;
        [Tooltip("Radius of the ground-level shockwave ring")]
        public float groundShockwaveRadius = 5f;
        [Tooltip("Force of the ground shockwave (pushes outward + up from muzzle position)")]
        public float groundShockwaveForce = 10f;
        [Tooltip("Upward lift on ground-pushed leaves (lifts them off the surface)")]
        [Range(0f, 2f)]
        public float groundShockwaveUpwardLift = 0.6f;

        [Header("Phase 2 — Vacuum Pull-Back")]
        [Tooltip("Delay before the vacuum phase begins (seconds)")]
        public float vacuumDelay = 0.08f;
        [Tooltip("How strong the suck-back toward the muzzle is")]
        public float vacuumForce = 4f;
        [Tooltip("Radius of the vacuum pull zone")]
        public float vacuumRadius = 4f;
        [Tooltip("Duration of the vacuum pull")]
        public float vacuumDuration = 0.15f;

        [Header("Phase 3 — Turbulent Wake")]
        [Tooltip("Delay after blast before turbulence kicks in")]
        public float turbulenceDelay = 0.2f;
        [Tooltip("Radius of turbulent swirling")]
        public float turbulenceRadius = 5f;
        [Tooltip("Strength of random turbulent forces")]
        public float turbulenceForce = 3.5f;
        [Tooltip("How many bursts of turbulence to apply")]
        public int turbulenceBursts = 4;
        [Tooltip("Time between turbulence bursts")]
        public float turbulenceBurstInterval = 0.1f;
    }

    // Applies the initial directional cone of the muzzle blast and an optional
    // ground-level shockwave that lifts leaves regardless of aim direction.
    int ApplyConeBurst(Vector2 origin, Vector2 dir, MuzzleBlastSettings s)
    {
        int affected = 0;
        float coneRadiusSq = s.coneRadius * s.coneRadius;
        float rearRadiusSq = s.rearWashRadius * s.rearWashRadius;
        float cosHalfAngle = Mathf.Cos(s.coneHalfAngle * Mathf.Deg2Rad);

        // Perpendicular vector is used to scatter leaves sideways within the cone.
        Vector2 perp = new Vector2(-dir.y, dir.x);

        for (int i = 0; i < activeLeafCount; i++)
        {
            float3 pos = positions[i];
            Vector2 leafPos = new Vector2(pos.x, pos.y);
            Vector2 delta = leafPos - origin;
            float distSq = delta.sqrMagnitude;

            float maxRadiusSq = Mathf.Max(coneRadiusSq, rearRadiusSq);
            if (distSq > maxRadiusSq) continue;

            float dist = Mathf.Sqrt(distSq);
            if (dist < 0.01f) dist = 0.01f;
            Vector2 toLeaf = delta / dist;

            // Dot product with fire direction tells us whether the leaf is in
            // front of (positive) or behind (negative) the muzzle.
            float alignment = Vector2.Dot(toLeaf, dir);

            float totalForce = 0f;
            Vector2 forceDir = Vector2.zero;

            if (alignment >= cosHalfAngle && distSq <= coneRadiusSq)
            {
                float distFalloff = 1f - (dist / s.coneRadius);
                distFalloff = distFalloff * distFalloff;

                // Leaves directly ahead receive full force; those at the cone edge
                // get proportionally less, shaped by a sqrt curve for a natural feel.
                float angleFactor = Mathf.InverseLerp(cosHalfAngle, 1f, alignment);
                angleFactor = Mathf.Sqrt(angleFactor);

                totalForce = s.coneForce * distFalloff * angleFactor;

                // Add lateral scatter so the blast fans leaves outward, not just forward.
                float scatter = (1f - angleFactor) * 0.5f;
                float sideSign = Vector2.Dot(toLeaf, perp) > 0 ? 1f : -1f;
                forceDir = (dir + perp * sideSign * scatter + Vector2.up * s.upwardBias).normalized;
            }
            else if (distSq <= rearRadiusSq)
            {
                // Leaves behind the muzzle get a weaker radial push from the side-wash.
                float distFalloff = 1f - (dist / s.rearWashRadius);
                distFalloff *= distFalloff;

                totalForce = s.coneForce * s.rearWashFraction * distFalloff;

                forceDir = (toLeaf + Vector2.up * s.upwardBias * 0.5f).normalized;
            }
            else
            {
                continue;
            }

            if (totalForce < 0.01f) continue;

            float refMass = useParticleTypes ? particleTypes[0].mass : mass;
            float massRatio = Mathf.Max(refMass, 0.00001f) / Mathf.Max(leafMass[i], 0.00001f);
            totalForce *= massRatio * UnityEngine.Random.Range(0.75f, 1.25f);

            float3 vel = velocities[i];
            vel.x += forceDir.x * totalForce;
            vel.y += forceDir.y * totalForce;
            velocities[i] = vel;

            // Spin intensity is proportional to blast force so lightly-grazed
            // leaves tumble less dramatically than those at the centre.
            float spinIntensity = totalForce / s.coneForce;
            angularVelocities[i] += UnityEngine.Random.Range(-25f, 25f) * spinIntensity;

            isAsleep[i] = false;
            if (isPermanentlySleeping[i])
            {
                isPermanentlySleeping[i] = false;
                groundedTime[i] = 0f;
            }

            affected++;
        }

        if (s.enableGroundShockwave)
        {
            // Ground shockwave is radial (not directional) to catch leaves the
            // aimed cone might have missed at ground level around the shooter.
            float gsRadiusSq = s.groundShockwaveRadius * s.groundShockwaveRadius;

            for (int i = 0; i < activeLeafCount; i++)
            {
                float3 pos = positions[i];
                Vector2 leafPos = new Vector2(pos.x, pos.y);
                Vector2 delta = leafPos - origin;
                float distSq = delta.sqrMagnitude;

                if (distSq > gsRadiusSq) continue;

                float dist = Mathf.Sqrt(distSq);
                if (dist < 0.01f) dist = 0.01f;

                float falloff = 1f - (dist / s.groundShockwaveRadius);
                falloff = falloff * falloff;

                Vector2 outward = delta / dist;
                Vector2 gsDir = (outward + Vector2.up * s.groundShockwaveUpwardLift).normalized;

                float refMass = useParticleTypes ? particleTypes[0].mass : mass;
                float massRatio = Mathf.Max(refMass, 0.00001f) / Mathf.Max(leafMass[i], 0.00001f);

                float gsForce = s.groundShockwaveForce * falloff * massRatio * UnityEngine.Random.Range(0.7f, 1.3f);

                float3 vel = velocities[i];
                vel.x += gsDir.x * gsForce;
                vel.y += gsDir.y * gsForce;
                velocities[i] = vel;

                angularVelocities[i] += UnityEngine.Random.Range(-20f, 20f) * falloff;

                isAsleep[i] = false;
                if (isPermanentlySleeping[i])
                {
                    isPermanentlySleeping[i] = false;
                    groundedTime[i] = 0f;
                }

                affected++;
            }
        }

        return affected;
    }

    // Runs the delayed phases of the muzzle blast over time.
    // A coroutine is used so the vacuum and turbulence can apply forces across
    // multiple frames without blocking the main update loop.
    IEnumerator MuzzleBlastSequence(Vector2 origin, Vector2 dir, MuzzleBlastSettings s)
    {
        yield return new WaitForSeconds(s.vacuumDelay);

        float vacuumElapsed = 0f;
        float vacuumRadiusSq = s.vacuumRadius * s.vacuumRadius;

        // The vacuum phase sucks leaves back toward the muzzle as the pressure
        // wave passes, using a sin curve so the pull rises and falls smoothly.
        while (vacuumElapsed < s.vacuumDuration)
        {
            float dt = Time.deltaTime;
            vacuumElapsed += dt;

            float vacuumT = vacuumElapsed / s.vacuumDuration;
            float vacuumStrength = Mathf.Sin(vacuumT * Mathf.PI) * s.vacuumForce;

            for (int i = 0; i < activeLeafCount; i++)
            {
                if (isPermanentlySleeping[i]) continue;

                float3 pos = positions[i];
                Vector2 leafPos = new Vector2(pos.x, pos.y);
                Vector2 delta = leafPos - origin;
                float distSq = delta.sqrMagnitude;

                if (distSq > vacuumRadiusSq || distSq < 0.01f) continue;

                float dist = Mathf.Sqrt(distSq);
                float falloff = 1f - (dist / s.vacuumRadius);

                // Pull direction is toward the origin (negative delta).
                Vector2 pullDir = -delta / dist;

                float refMass = useParticleTypes ? particleTypes[0].mass : mass;
                float massRatio = Mathf.Max(refMass, 0.00001f) / Mathf.Max(leafMass[i], 0.00001f);

                float pullForce = vacuumStrength * falloff * massRatio * dt;

                float3 vel = velocities[i];
                vel.x += pullDir.x * pullForce;
                vel.y += pullDir.y * pullForce;
                velocities[i] = vel;
            }

            yield return null;
        }

        if (showDebug)
        {
            Debug.Log($"[BurstLeaf] MuzzleBlast Phase 2 (vacuum) complete at {origin}");
        }

        yield return new WaitForSeconds(s.turbulenceDelay - s.vacuumDelay - s.vacuumDuration);

        float turbRadiusSq = s.turbulenceRadius * s.turbulenceRadius;

        // Multiple turbulence bursts with a shared swirl bias prevent the chaos
        // from looking purely random — leaves tend to orbit in one direction,
        // like the actual air rotation behind a muzzle blast.
        for (int burst = 0; burst < s.turbulenceBursts; burst++)
        {
            float swirlAngle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector2 swirlDir = new Vector2(Mathf.Cos(swirlAngle), Mathf.Sin(swirlAngle));

            // Each successive burst is weaker so the disturbance decays naturally.
            float burstFalloff = 1f - ((float)burst / s.turbulenceBursts);

            for (int i = 0; i < activeLeafCount; i++)
            {
                if (isPermanentlySleeping[i]) continue;

                float3 pos = positions[i];
                Vector2 leafPos = new Vector2(pos.x, pos.y);
                Vector2 delta = leafPos - origin;
                float distSq = delta.sqrMagnitude;

                if (distSq > turbRadiusSq || distSq < 0.01f) continue;

                float dist = Mathf.Sqrt(distSq);
                float falloff = 1f - (dist / s.turbulenceRadius);
                falloff *= falloff;

                // Blend structured swirl with random chaos so the wake isn't
                // perfectly circular, which would look artificial.
                Vector2 chaos = UnityEngine.Random.insideUnitCircle;
                Vector2 turbDir = (swirlDir * 0.4f + chaos * 0.6f).normalized;

                float refMass = useParticleTypes ? particleTypes[0].mass : mass;
                float massRatio = Mathf.Max(refMass, 0.00001f) / Mathf.Max(leafMass[i], 0.00001f);

                float turbForce = s.turbulenceForce * falloff * burstFalloff * massRatio;
                turbForce *= UnityEngine.Random.Range(0.5f, 1.5f);

                float3 vel = velocities[i];
                vel.x += turbDir.x * turbForce;
                vel.y += turbDir.y * turbForce;
                velocities[i] = vel;

                angularVelocities[i] += UnityEngine.Random.Range(-10f, 10f) * falloff * burstFalloff;
            }

            yield return new WaitForSeconds(s.turbulenceBurstInterval);
        }

        if (showDebug)
        {
            Debug.Log($"[BurstLeaf] MuzzleBlast Phase 3 (turbulence) complete at {origin}");
        }
    }

    public void SpawnLeafAt(Vector2 worldPosition, int typeIndex = -1)
    {
        SpawnLeafInternal(worldPosition, typeIndex);
    }

    // Static helpers let external code broadcast to all active leaf systems
    // without needing to hold references to each individual instance.
    public static void BlastAll(Vector2 origin, float radius, float force, float upwardBias = 0.3f)
    {
        for (int i = 0; i < allInstances.Count; i++)
        {
            allInstances[i].ApplyBlastForce(origin, radius, force, upwardBias);
        }
    }

    public static void MuzzleBlastAll(Vector2 origin, Vector2 fireDirection, MuzzleBlastSettings settings = null)
    {
        for (int i = 0; i < allInstances.Count; i++)
        {
            allInstances[i].ApplyMuzzleBlast(origin, fireDirection, settings);
        }
    }

    // NativeArrays must be explicitly disposed because they are allocated outside
    // the GC heap. Failing to dispose them causes memory leaks that persist
    // between editor play sessions.
    void OnDestroy()
    {
        allInstances.Remove(this);

        if (positions.IsCreated) positions.Dispose();
        if (velocities.IsCreated) velocities.Dispose();
        if (rotations.IsCreated) rotations.Dispose();
        if (angularVelocities.IsCreated) angularVelocities.Dispose();
        if (sizes.IsCreated) sizes.Dispose();
        if (colors.IsCreated) colors.Dispose();
        if (isAsleep.IsCreated) isAsleep.Dispose();
        if (windForces.IsCreated) windForces.Dispose();
        if (prevWindForces.IsCreated) prevWindForces.Dispose();
        if (groundedTime.IsCreated) groundedTime.Dispose();
        if (groundedLiftFactor.IsCreated) groundedLiftFactor.Dispose();
        if (contactTime.IsCreated) contactTime.Dispose();
        if (contactCount.IsCreated) contactCount.Dispose();
        if (isSticky.IsCreated) isSticky.Dispose();
        if (isPermanentlySleeping.IsCreated) isPermanentlySleeping.Dispose();
        if (leafAge.IsCreated) leafAge.Dispose();
        if (leafMaxLifetime.IsCreated) leafMaxLifetime.Dispose();
        if (fadeProgress.IsCreated) fadeProgress.Dispose();
        if (originalAlpha.IsCreated) originalAlpha.Dispose();
        if (leafMass.IsCreated) leafMass.Dispose();
        if (leafDrag.IsCreated) leafDrag.Dispose();
        if (leafTypeIndex.IsCreated) leafTypeIndex.Dispose();
        if (leafAirResistance.IsCreated) leafAirResistance.Dispose();
        if (leafFlutter.IsCreated) leafFlutter.Dispose();
        if (leafFlutterSpeed.IsCreated) leafFlutterSpeed.Dispose();
        if (leafFlutterOffset.IsCreated) leafFlutterOffset.Dispose();
    }

    void OnDrawGizmos()
    {
        // Show the spawn volume in the Scene view so designers can see exactly
        // where new leaves will appear without entering play mode.
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, new Vector3(spawnArea.x, spawnArea.y, 0));

        // Visualise a sample of active leaves to diagnose size or position issues
        // without rendering all of them (capped at 100 to keep gizmo overhead low).
        if (Application.isPlaying && activeLeafCount > 0 && positions.IsCreated)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < Mathf.Min(activeLeafCount, 100); i++)
            {
                Vector3 pos = new Vector3(positions[i].x, positions[i].y, positions[i].z);
                Gizmos.DrawWireSphere(pos, sizes[i] * 0.5f);
            }
        }

        if (!Application.isPlaying) return;

        foreach (var manager in SlimeMoldManager.AllInstances)
        {
            Rect bounds = manager.GetWorldBounds();
            Gizmos.color = Color.yellow;
            // Draws the boundary of the Slime Manager's coordinate system
            Gizmos.DrawWireCube(new Vector3(bounds.center.x, bounds.center.y, 0), new Vector3(bounds.width, bounds.height, 1));
        }
    }
}

[BurstCompile(CompileSynchronously = true)]
public struct LeafPhysicsJob : IJobParallelFor
{
    public NativeArray<float3> velocities;
    public NativeArray<quaternion> rotations;
    public NativeArray<float> angularVelocities;

    // ReadOnly so multiple job threads can safely read shared state in parallel.
    [ReadOnly] public NativeArray<bool> isAsleep;

    [ReadOnly] public float deltaTime;
    [ReadOnly] public float currentTime;
    [ReadOnly] public float3 gravity;
    [ReadOnly] public NativeArray<float3> windForces;
    [ReadOnly] public NativeArray<float> masses;
    [ReadOnly] public NativeArray<float> drags;
    [ReadOnly] public float angularDrag;
    [ReadOnly] public NativeArray<float> airResistances;
    [ReadOnly] public NativeArray<float> flutters;
    [ReadOnly] public NativeArray<float> flutterSpeeds;
    [ReadOnly] public NativeArray<float> flutterOffsets;

    public void Execute(int index)
    {
        if (isAsleep[index]) return;

        float leafMass = math.max(masses[index], 0.00001f);
        float3 vel = velocities[index];

        // Wind force is divided by mass so lighter leaves accelerate more from
        // the same wind, matching F = ma (a = F/m).
        float3 force = gravity + windForces[index] / leafMass;
        vel += force * deltaTime;

        // Global drag bleeds velocity uniformly, simulating air resistance
        // in all directions (as opposed to the directional air resistance below).
        float leafDrag = drags[index];
        vel *= (1f - leafDrag * deltaTime);

        // Extra downward drag only activates when the leaf is actually falling,
        // simulating a flat surface cupping air to slow its descent.
        float ar = airResistances[index];
        if (ar > 0f && vel.y < 0f)
        {
            float fallSpeed = -vel.y;
            float resistanceForce = ar * fallSpeed * deltaTime;

            // Cap the resistance to 90% of fall speed so the leaf can never
            // be pushed upward by this term alone.
            resistanceForce = math.min(resistanceForce, fallSpeed * 0.9f);
            vel.y += resistanceForce;
        }

        // Flutter is only active while the leaf is falling — a rising or hovering
        // leaf doesn't sway the same way as one drifting downward.
        float flut = flutters[index];
        if (flut > 0f && vel.y < -0.05f)
        {
            float speed = flutterSpeeds[index];
            float offset = flutterOffsets[index];

            float sway = math.sin(currentTime * speed + offset) * flut;

            // Scale sway by fall speed so fast-falling leaves flutter more
            // visibly, which matches how real leaves behave in freefall.
            float fallFactor = math.clamp(-vel.y * 0.5f, 0.2f, 1.5f);
            vel.x += sway * fallFactor * deltaTime;

            float angVel = angularVelocities[index];
            angVel += sway * 0.5f * deltaTime;
            angularVelocities[index] = angVel;
        }

        velocities[index] = vel;

        float angVelFinal = angularVelocities[index];
        angVelFinal *= (1f - angularDrag * deltaTime);
        angularVelocities[index] = angVelFinal;

        // Normalise after every multiply to prevent floating-point drift from
        // accumulating into a non-unit quaternion over thousands of frames.
        quaternion deltaRotation = quaternion.Euler(0, 0, angVelFinal * deltaTime);
        rotations[index] = math.normalize(math.mul(rotations[index], deltaRotation));
    }
}