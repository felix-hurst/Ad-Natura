using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SporeDispersal : MonoBehaviour
{

    [Header("References")]
    [Tooltip("The LeafDetectionZone to read cluster data from.")]
    public LeafDetectionZone DetectionZone;

    [Tooltip("Prefab that has the Slime (+ optional SlimeMoldManager) component on it. " +
             "Will be instantiated once per qualifying leaf cluster.")]
    public GameObject SlimePrefab;

    [Header("Spawn Thresholds")]
    [Tooltip("Minimum number of grounded leaves in a cluster bucket to trigger a spawn.")]
    [Min(1)]
    public int MinLeavesPerCluster = 10;

    [Tooltip("Maximum number of slime entities alive at the same time.")]
    [Min(1)]
    public int MaxSimultaneousSlimes = 8;

    [Tooltip("Seconds to wait before re-evaluating after a spawn. Prevents rapid re-spawning.")]
    [Min(0f)]
    public float SpawnCooldown = 3f;

    [Header("Spawn Position")]
    [Tooltip("Y world position where slime entities are placed. " +
             "Should match the ground surface inside your detection zone.")]
    public float ZoneFloorY = 0f;

    [Tooltip("Small random X jitter applied at spawn so overlapping clusters look natural (world units).")]
    [Min(0f)]
    public float SpawnXJitter = 0.1f;

    [Header("Slime Sizing")]
    [Tooltip("When true, the spawned slime's world bounds width scales with the cluster's leaf count. " +
             "A bigger pile = a wider slime.")]
    public bool ScaleWithClusterSize = true;

    [Tooltip("Minimum world-space width for a spawned slime.")]
    [Min(0.1f)]
    public float MinSlimeWidth = 1f;

    [Tooltip("Maximum world-space width for a spawned slime. " +
             "Reached when a bucket is at 100% density.")]
    [Min(0.1f)]
    public float MaxSlimeWidth = 5f;

    [Tooltip("Fixed height for all spawned slime instances.")]
    [Min(0.1f)]
    public float SlimeHeight = 2f;

    [Header("Lifetime / Fade")]
    [Tooltip("If true, slimes despawn when their source cluster drops below MinLeavesPerCluster.")]
    public bool DespawnWhenLeavesGone = true;

    [Tooltip("How long a slime fades out before being destroyed (seconds).")]
    [Min(0f)]
    public float FadeOutDuration = 2f;

    [Tooltip("If > 0, slimes also have a maximum lifetime in seconds, regardless of leaf count.")]
    [Min(0f)]
    public float MaxSlimeLifetime = 0f;

    [Header("Polling")]
    [Tooltip("How often to check cluster data for new spawns / despawns (seconds).")]
    [Min(0.05f)]
    public float PollInterval = 0.5f;

    [Header("Debug")]
    public bool ShowDebugGizmos = true;
    public bool LogSpawns = true;


    private struct SpawnedSlime
    {
        public GameObject Instance;
        public Slime SlimeComponent;
        public int BucketIndex;
        public float SpawnTime;
        public bool IsFadingOut;
        public float FadeStartTime;
    }

    private readonly List<SpawnedSlime> _activeSlimes = new List<SpawnedSlime>();
    private float _cooldownTimer;
    private float _pollTimer;


    void Start()
    {
        if (DetectionZone == null)
        {
            Debug.LogError("[SporeDispersal] DetectionZone is not assigned!", this);
            enabled = false;
            return;
        }
        if (SlimePrefab == null)
        {
            Debug.LogError("[SporeDispersal] SlimePrefab is not assigned!", this);
            enabled = false;
            return;
        }

        if (SlimePrefab.GetComponent<Slime>() == null)
            Debug.LogWarning("[SporeDispersal] SlimePrefab doesn't have a Slime component! " +
                             "Spawning will still work but SetWorldBounds won't be called.");
    }

    void Update()
    {
        _cooldownTimer -= Time.deltaTime;

        _pollTimer += Time.deltaTime;
        if (_pollTimer < PollInterval) return;
        _pollTimer = 0f;

        UpdateExistingSlimes();

        if (_cooldownTimer <= 0f)
            EvaluateClusters();
    }


    void UpdateExistingSlimes()
    {
        var clusters = DetectionZone.Clusters;

        for (int i = _activeSlimes.Count - 1; i >= 0; i--)
        {
            var s = _activeSlimes[i];

            if (s.Instance == null)
            {
                _activeSlimes.RemoveAt(i);
                continue;
            }

            bool lifetimeExpired = MaxSlimeLifetime > 0f &&
                                   (Time.time - s.SpawnTime) >= MaxSlimeLifetime;

            bool clusterDied = false;
            if (DespawnWhenLeavesGone && s.BucketIndex < clusters.Count)
                clusterDied = clusters[s.BucketIndex].Count < MinLeavesPerCluster;

            bool shouldFade = (lifetimeExpired || clusterDied) && !s.IsFadingOut;

            if (shouldFade)
            {
                s.IsFadingOut = true;
                s.FadeStartTime = Time.time;
                _activeSlimes[i] = s;
                StartCoroutine(FadeOutAndDestroy(s.Instance, FadeOutDuration));

                if (LogSpawns)
                    Debug.Log($"[SporeDispersal] Slime at bucket {s.BucketIndex} beginning fade out " +
                              $"(leaves gone: {clusterDied}, lifetime: {lifetimeExpired})");
                continue;
            }

            if (s.IsFadingOut && s.Instance == null)
                _activeSlimes.RemoveAt(i);
        }
    }


    void EvaluateClusters()
    {
        if (_activeSlimes.Count >= MaxSimultaneousSlimes) return;
        if (!DetectionZone.HasLeaves) return;

        var clusters = DetectionZone.Clusters;

        for (int b = 0; b < clusters.Count; b++)
        {
            if (_activeSlimes.Count >= MaxSimultaneousSlimes) break;

            var cluster = clusters[b];

            if (cluster.Count < MinLeavesPerCluster) continue;

            if (BucketHasActiveSlime(b)) continue;

            SpawnSlimeForCluster(b, cluster);
            _cooldownTimer = SpawnCooldown;
        }
    }

    bool BucketHasActiveSlime(int bucketIndex)
    {
        foreach (var s in _activeSlimes)
        {
            if (s.BucketIndex == bucketIndex && s.Instance != null && !s.IsFadingOut)
                return true;
        }
        return false;
    }


void SpawnSlimeForCluster(int bucketIndex, LeafDetectionZone.LeafCluster cluster)
{
    float spawnX = cluster.WorldX + Random.Range(-SpawnXJitter, SpawnXJitter);
    Vector3 spawnPos = new Vector3(spawnX, ZoneFloorY, 0f);

    GameObject instance = Instantiate(SlimePrefab, spawnPos, Quaternion.identity);
    instance.name = $"SlimeMold_Bucket{bucketIndex}";

    Slime slimeComp = instance.GetComponent<Slime>();

    _activeSlimes.Add(new SpawnedSlime
    {
        Instance       = instance,
        SlimeComponent = slimeComp,
        BucketIndex    = bucketIndex,
        SpawnTime      = Time.time,
        IsFadingOut    = false,
        FadeStartTime  = -1f
    });

    StartCoroutine(ApplyBoundsNextFrame(slimeComp, cluster));

    if (LogSpawns)
        Debug.Log($"[SporeDispersal] Spawned slime for bucket {bucketIndex} " +
                  $"at ({spawnX:F2}, {ZoneFloorY:F2}) | " +
                  $"Leaves: {cluster.Count} | Density: {cluster.Density:P0}");
}

IEnumerator ApplyBoundsNextFrame(Slime slimeComp, LeafDetectionZone.LeafCluster cluster)
{
    yield return null;

    if (slimeComp == null) yield break;

    float width = ScaleWithClusterSize
        ? Mathf.Lerp(MinSlimeWidth, MaxSlimeWidth, cluster.Density)
        : (MinSlimeWidth + MaxSlimeWidth) * 0.5f;

    width = Mathf.Clamp(width, MinSlimeWidth, MaxSlimeWidth);

    Rect bounds = new Rect(
        slimeComp.transform.position.x - width * 0.5f,
        ZoneFloorY,
        width,
        SlimeHeight
    );

    slimeComp.SetWorldBounds(bounds);
}


    IEnumerator FadeOutAndDestroy(GameObject target, float duration)
    {
        if (duration <= 0f)
        {
            if (target != null) Destroy(target);
            yield break;
        }

        var renderers = target != null
            ? target.GetComponentsInChildren<SpriteRenderer>()
            : new SpriteRenderer[0];

        Color[] startColors = new Color[renderers.Length];
        for (int r = 0; r < renderers.Length; r++)
            startColors[r] = renderers[r].color;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (target == null) yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null) continue;
                Color c = startColors[r];
                c.a = Mathf.Lerp(1f, 0f, t);
                renderers[r].color = c;
            }

            yield return null;
        }

        if (target != null)
            Destroy(target);
    }


    public void DespawnAll()
    {
        foreach (var s in _activeSlimes)
        {
            if (s.Instance != null && !s.IsFadingOut)
                StartCoroutine(FadeOutAndDestroy(s.Instance, FadeOutDuration));
        }
        _activeSlimes.Clear();
    }

    public int ActiveSlimeCount => _activeSlimes.Count;


    void OnDrawGizmos()
    {
        if (!ShowDebugGizmos || DetectionZone == null) return;

        Gizmos.color = new Color(0.6f, 1f, 0.6f, 0.5f);
        Vector3 floorL = new Vector3(
            DetectionZone.transform.position.x - DetectionZone.ZoneSize.x * 0.5f, ZoneFloorY, 0);
        Vector3 floorR = new Vector3(
            DetectionZone.transform.position.x + DetectionZone.ZoneSize.x * 0.5f, ZoneFloorY, 0);
        Gizmos.DrawLine(floorL, floorR);

        if (!Application.isPlaying) return;

        foreach (var s in _activeSlimes)
        {
            if (s.Instance == null) continue;
            Gizmos.color = s.IsFadingOut
                ? new Color(1f, 0.4f, 0.1f, 0.6f)
                : new Color(0.2f, 0.9f, 1f, 0.7f);
            Gizmos.DrawWireSphere(s.Instance.transform.position, 0.25f);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!ShowDebugGizmos) return;

        UnityEditor.Handles.color = new Color(0.5f, 1f, 0.5f, 0.8f);
        Vector3 labelPos = transform.position + Vector3.up * 0.5f;
        UnityEditor.Handles.Label(labelPos,
            Application.isPlaying
                ? $"Slimes active: {ActiveSlimeCount} / {MaxSimultaneousSlimes}"
                : $"SporeDispersal | Min leaves/cluster: {MinLeavesPerCluster} | Max slimes: {MaxSimultaneousSlimes}");
    }
#endif
}