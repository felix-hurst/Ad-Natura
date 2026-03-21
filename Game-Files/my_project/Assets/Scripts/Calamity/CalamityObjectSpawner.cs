using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CalamityObjectSpawner : MonoBehaviour
{
    [System.Serializable]
    public class SpawnZone
    {
        [Tooltip("Center of the spawn area in world space")]
        public Vector2 center;

        [Tooltip("Width and height of the spawn area")]
        public Vector2 size = new Vector2(20f, 2f);

        [Tooltip("The Y position of the ground surface in this zone")]
        public float groundY = 0f;

        [Tooltip("Maximum number of objects that can exist in this zone")]
        public int maxObjects = 5;

        [Tooltip("If true, uses a ground raycast to find the surface instead of a fixed groundY")]
        public bool useGroundRaycast = false;

        [Tooltip("Layer mask for ground detection raycasts")]
        public LayerMask groundLayer = ~0;

        [Header("Debug")]
        public Color gizmoColor = new Color(0.6f, 0.1f, 0.8f, 0.3f);

        [HideInInspector] public List<GameObject> spawnedObjects = new List<GameObject>();
    }

    [System.Serializable]
    public class SpawnParameters
    {
        [Header("Dimensions")]
        public Vector2 baseWidthRange = new Vector2(0.8f, 2.5f);
        public Vector2 heightRange = new Vector2(3f, 9f);       // was 2–7
        public Vector2 topWidthRange = new Vector2(0.1f, 0.5f);  // was 0.2–0.8

        [Header("Surface Detail")]
        public Vector2 roughnessRange    = new Vector2(0.18f, 0.40f); // was 0.05–0.25
        public Vector2Int edgeVerticesRange = new Vector2Int(12, 22); // was 6–15
        public Vector2 asymmetryRange    = new Vector2(0.15f, 0.5f);  // was 0–0.4
        public Vector2Int branchCountRange = new Vector2Int(2, 4);    // was 0–3
        public Vector2 branchSizeRange   = new Vector2(0.3f, 0.7f);  // was 0.15–0.5

        [Header("Visual")]
        public Color minColor = new Color(0.1f, 0.05f, 0.15f, 1f);
        public Color maxColor = new Color(0.25f, 0.1f, 0.35f, 1f);
        public string materialTag = "Calamity";
        public int sortingOrder = 5;
        public string sortingLayer = "Default";

        [Header("Mist")]
public bool enableMist = true;
public Color mistColor    = new Color(0.04f, 0.02f, 0.06f, 1f);
public Color mistColorAlt = new Color(0.08f, 0.05f, 0.10f, 1f);
[Range(0f, 1f)] public float mistOpacity = 0.6f;
[Range(0f, 30f)]  public float mistVorticity       = 18f;
[Range(0f, 0.08f)] public float mistEmitterStrength = 0.03f;
public float mistDensityStrength = 0.018f;  // was 0.06
[Range(64, 256)]  public int   mistResolution      = 48;

public LayerMask mistObstacleLayerMask = 0;  // add this

        [Header("Physics")]
        public Vector2 massRange = new Vector2(3f, 10f);
        public bool spawnAsStatic = true;
        public PhysicsMaterial2D physicsMaterial;

        [Header("Destruction Settings")]
        public RaycastReceiver.HighlightMode highlightMode = RaycastReceiver.HighlightMode.ClosestToGround;
        public bool showCutOutline = true;
        public float largePieceMassMultiplier = 0.5f;
        public float cutPieceLifetime = 30f;
        public float minAreaThreshold = 0.15f;

        [Header("Sprout Animation")]
        public bool animateSprout = false;
        public Vector2 sproutDurationRange = new Vector2(0.8f, 1.8f);
        public float groundShakeMagnitude = 0.05f;
    }

    [Header("Spawn Zones")]
    [SerializeField] private List<SpawnZone> spawnZones = new List<SpawnZone>();

    [Header("Spawn Parameters")]
    [SerializeField] private SpawnParameters spawnParams = new SpawnParameters();

    [Header("Spawn Timing")]
    [Tooltip("How spawning is triggered")]
    [SerializeField] private SpawnMode spawnMode = SpawnMode.OnStart;

    [Tooltip("Delay between each object spawn in a burst")]
    [SerializeField] private float spawnInterval = 0.5f;

    [Tooltip("Initial delay before spawning begins")]
    [SerializeField] private float initialDelay = 0f;

    [Header("Continuous Spawn Settings")]
    [Tooltip("Time between spawn waves (Continuous mode only)")]
    [SerializeField] private float waveCooldown = 10f;

    [Tooltip("Objects per wave (Continuous mode only)")]
    [SerializeField] private Vector2Int objectsPerWave = new Vector2Int(1, 3);

    [Header("Spacing")]
    [Tooltip("Minimum distance between spawned objects")]
    [SerializeField] private float minSpacing = 2f;

    [Tooltip("Maximum placement attempts before skipping")]
    [SerializeField] private int maxPlacementAttempts = 20;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logSpawns = true;

    public enum SpawnMode
    {
        OnStart,
        OnTrigger,
        Continuous,
        Manual
    }

    private Coroutine continuousSpawnCoroutine;
    private int totalSpawned = 0;

    void Start()
    {
        switch (spawnMode)
        {
            case SpawnMode.OnStart:
                StartCoroutine(SpawnAllZones());
                break;
            case SpawnMode.Continuous:
                continuousSpawnCoroutine = StartCoroutine(ContinuousSpawnLoop());
                break;
        }
    }


    public void TriggerSpawn()
    {
        StartCoroutine(SpawnAllZones());
    }

    public GameObject SpawnSingle(Vector2 position, float groundY, int seed = -1)
    {
        return CreateCalamityObject(position, groundY, seed);
    }

    public void SpawnInZone(int zoneIndex)
    {
        if (zoneIndex >= 0 && zoneIndex < spawnZones.Count)
        {
            StartCoroutine(FillZone(spawnZones[zoneIndex]));
        }
    }

    public void StopContinuousSpawn()
    {
        if (continuousSpawnCoroutine != null)
        {
            StopCoroutine(continuousSpawnCoroutine);
            continuousSpawnCoroutine = null;
        }
    }

    public void DestroyAll()
    {
        foreach (SpawnZone zone in spawnZones)
        {
            foreach (GameObject obj in zone.spawnedObjects)
            {
                if (obj != null) Destroy(obj);
            }
            zone.spawnedObjects.Clear();
        }
    }

    public int GetAliveCount()
    {
        int count = 0;
        foreach (SpawnZone zone in spawnZones)
        {
            CleanupDestroyedReferences(zone);
            count += zone.spawnedObjects.Count;
        }
        return count;
    }


    IEnumerator SpawnAllZones()
    {
        if (initialDelay > 0)
            yield return new WaitForSeconds(initialDelay);

        foreach (SpawnZone zone in spawnZones)
        {
            yield return StartCoroutine(FillZone(zone));
        }
    }

    IEnumerator FillZone(SpawnZone zone)
    {
        CleanupDestroyedReferences(zone);

        int toSpawn = zone.maxObjects - zone.spawnedObjects.Count;

        for (int i = 0; i < toSpawn; i++)
        {
            Vector2? position = FindValidPosition(zone);

            if (position.HasValue)
            {
                float groundY = zone.groundY;

                if (zone.useGroundRaycast)
                {
                    float detectedGround = DetectGround(position.Value, zone.groundLayer);
                    if (!float.IsNaN(detectedGround))
                    {
                        groundY = detectedGround;
                    }
                }

                GameObject obj = CreateCalamityObject(position.Value, groundY);
                zone.spawnedObjects.Add(obj);

                if (spawnInterval > 0)
                    yield return new WaitForSeconds(spawnInterval);
            }
        }
    }

    IEnumerator ContinuousSpawnLoop()
    {
        if (initialDelay > 0)
            yield return new WaitForSeconds(initialDelay);

        yield return StartCoroutine(SpawnAllZones());

        while (true)
        {
            yield return new WaitForSeconds(waveCooldown);

            int waveCount = Random.Range(objectsPerWave.x, objectsPerWave.y + 1);

            for (int i = 0; i < waveCount; i++)
            {
                List<SpawnZone> availableZones = new List<SpawnZone>();
                foreach (SpawnZone zone in spawnZones)
                {
                    CleanupDestroyedReferences(zone);
                    if (zone.spawnedObjects.Count < zone.maxObjects)
                    {
                        availableZones.Add(zone);
                    }
                }

                if (availableZones.Count == 0) break;

                SpawnZone chosenZone = availableZones[Random.Range(0, availableZones.Count)];
                Vector2? position = FindValidPosition(chosenZone);

                if (position.HasValue)
                {
                    float groundY = chosenZone.groundY;
                    if (chosenZone.useGroundRaycast)
                    {
                        float detected = DetectGround(position.Value, chosenZone.groundLayer);
                        if (!float.IsNaN(detected)) groundY = detected;
                    }

                    GameObject obj = CreateCalamityObject(position.Value, groundY);
                    chosenZone.spawnedObjects.Add(obj);

                    if (spawnInterval > 0)
                        yield return new WaitForSeconds(spawnInterval);
                }
            }
        }
    }

Vector2? FindValidPosition(SpawnZone zone)
{
    for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
    {
        float x = Random.Range(zone.center.x - zone.size.x * 0.5f, zone.center.x + zone.size.x * 0.5f);
        // Y is irrelevant for overlap — all objects land at groundY.
        // Only return x; we use groundY at placement time.
        Vector2 candidate = new Vector2(x, zone.groundY);

        bool tooClose = false;
        foreach (GameObject existing in zone.spawnedObjects)
        {
            if (existing == null) continue;

            // X-only distance check — objects share the same ground plane
            float xDist = Mathf.Abs(candidate.x - existing.transform.position.x);

            // Account for existing object's actual width if available
            float requiredSpacing = minSpacing;
            CalamityObject co = existing.GetComponent<CalamityObject>();
            if (co != null)
                requiredSpacing = minSpacing + co.baseWidth * 0.5f;

            if (xDist < requiredSpacing)
            {
                tooClose = true;
                break;
            }
        }

        if (!tooClose) return candidate;
    }

    return null;
}

    float DetectGround(Vector2 position, LayerMask groundLayer)
    {
        RaycastHit2D hit = Physics2D.Raycast(
            new Vector2(position.x, position.y + 50f),
            Vector2.down,
            100f,
            groundLayer
        );

        if (hit.collider != null)
        {
            return hit.point.y;
        }

        return float.NaN;
    }


GameObject CreateCalamityObject(Vector2 position, float groundY, int seed = -1)
{
    GameObject obj = new GameObject($"CalamityObject_{totalSpawned}");
    obj.transform.position = new Vector3(position.x, groundY, 0f);

    CalamityObject calamity = obj.AddComponent<CalamityObject>();

    // Randomise width FIRST so FindValidPosition can read it on subsequent spawns
    calamity.baseWidth = Random.Range(spawnParams.baseWidthRange.x, spawnParams.baseWidthRange.y);

    calamity.shapeSeed  = seed;
    calamity.height     = Random.Range(spawnParams.heightRange.x, spawnParams.heightRange.y);
    calamity.topWidth   = Random.Range(spawnParams.topWidthRange.x, spawnParams.topWidthRange.y);

    calamity.edgeVerticesPerSide = Random.Range(spawnParams.edgeVerticesRange.x, spawnParams.edgeVerticesRange.y);
    calamity.surfaceRoughness    = Random.Range(spawnParams.roughnessRange.x, spawnParams.roughnessRange.y);
    calamity.asymmetry           = Random.Range(spawnParams.asymmetryRange.x, spawnParams.asymmetryRange.y);
    calamity.branchCount         = Random.Range(spawnParams.branchCountRange.x, spawnParams.branchCountRange.y + 1);
    calamity.branchSize          = Random.Range(spawnParams.branchSizeRange.x, spawnParams.branchSizeRange.y);

    calamity.baseColor      = Color.Lerp(spawnParams.minColor, spawnParams.maxColor, Random.value);
    calamity.materialTag    = spawnParams.materialTag;
    calamity.sortingOrder   = spawnParams.sortingOrder;
    calamity.sortingLayer   = spawnParams.sortingLayer;

    calamity.mistObstacleLayerMask = spawnParams.mistObstacleLayerMask;

    calamity.enableMist           = spawnParams.enableMist;
calamity.mistColor            = spawnParams.mistColor;
calamity.mistColorAlt         = spawnParams.mistColorAlt;
calamity.mistOpacity          = spawnParams.mistOpacity;
calamity.mistVorticity       = spawnParams.mistVorticity;
calamity.mistEmitterStrength = spawnParams.mistEmitterStrength;
calamity.mistDensityStrength = spawnParams.mistDensityStrength;
calamity.mistResolution      = spawnParams.mistResolution;

    calamity.mass             = Random.Range(spawnParams.massRange.x, spawnParams.massRange.y);
    calamity.isStatic         = spawnParams.spawnAsStatic;
    calamity.physicsMaterial  = spawnParams.physicsMaterial;

    calamity.highlightMode            = spawnParams.highlightMode;
    calamity.showCutOutline           = spawnParams.showCutOutline;
    calamity.largePieceMassMultiplier = spawnParams.largePieceMassMultiplier;
    calamity.cutPieceLifetime         = spawnParams.cutPieceLifetime;
    calamity.minAreaThreshold         = spawnParams.minAreaThreshold;

    calamity.animateSprout       = spawnParams.animateSprout;
    calamity.sproutDuration      = Random.Range(spawnParams.sproutDurationRange.x, spawnParams.sproutDurationRange.y);
    calamity.groundShakeMagnitude = spawnParams.groundShakeMagnitude;

    calamity.Spawn(groundY);

    totalSpawned++;

    if (logSpawns)
    {
        Debug.Log($"[CalamitySpawner] Spawned {obj.name} at ({position.x:F1}, {groundY:F1}) " +
                  $"h={calamity.height:F1} w={calamity.baseWidth:F1} branches={calamity.branchCount}");
    }

    return obj;
}


    void CleanupDestroyedReferences(SpawnZone zone)
    {
        zone.spawnedObjects.RemoveAll(obj => obj == null);
    }


    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        foreach (SpawnZone zone in spawnZones)
        {
            Gizmos.color = zone.gizmoColor;
            Vector3 center3D = new Vector3(zone.center.x, zone.center.y, 0f);
            Vector3 size3D = new Vector3(zone.size.x, zone.size.y, 0.1f);
            Gizmos.DrawCube(center3D, size3D);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(
                new Vector3(zone.center.x - zone.size.x * 0.5f, zone.groundY, 0f),
                new Vector3(zone.center.x + zone.size.x * 0.5f, zone.groundY, 0f)
            );

            Gizmos.color = new Color(zone.gizmoColor.r, zone.gizmoColor.g, zone.gizmoColor.b, 0.8f);
            Gizmos.DrawWireCube(center3D, size3D);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
        foreach (SpawnZone zone in spawnZones)
        {
            foreach (GameObject obj in zone.spawnedObjects)
            {
                if (obj != null)
                {
                    Gizmos.DrawWireSphere(obj.transform.position, minSpacing);
                }
            }
        }
    }
}