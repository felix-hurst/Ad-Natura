using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SporeDispersal : MonoBehaviour
{

    [Header("References")]
    [Tooltip("The LeafDetectionZone to read cluster data from.")]
    public LeafDetectionZone detectionZone;

    [Tooltip("Prefab that has the Slime (+ optional SlimeMoldManager) component on it. " +
             "Will be instantiated once per qualifying leaf cluster.")]
    public GameObject slimePrefab;

    [Header("Spawn Thresholds")]
    [Tooltip("Minimum number of grounded leaves in a cluster bucket to trigger a spawn.")]
    [Min(1)]
    public int minLeavesPerCluster = 10;

    [Tooltip("Maximum number of slime entities alive at the same time.")]
    [Min(1)]
    public int maxSimultaneousSlimes = 8;

    [Tooltip("Seconds to wait before re-evaluating after a spawn. Prevents rapid re-spawning.")]
    [Min(0f)]
    public float spawnCooldown = 3f;

    [Header("Spawn Position")]
    [Tooltip("Y world position where slime entities are placed. " +
             "Should match the ground surface inside your detection zone.")]
    public float zoneFloorY = 0f;

    [Tooltip("Small random X jitter applied at spawn so overlapping clusters look natural (world units).")]
    [Min(0f)]
    public float spawnXJitter = 0.1f;

    [Header("Slime Sizing")]
    [Tooltip("When true, the spawned slime's world bounds width scales with the cluster's leaf count. " +
             "A bigger pile = a wider slime.")]
    public bool scaleWithClusterSize = true;

    [Tooltip("Minimum world-space width for a spawned slime.")]
    [Min(0.1f)]
    public float minSlimeWidth = 1f;

    [Tooltip("Maximum world-space width for a spawned slime. " +
             "Reached when a bucket is at 100% density.")]
    [Min(0.1f)]
    public float maxSlimeWidth = 5f;

    [Tooltip("Fixed height for all spawned slime instances.")]
    [Min(0.1f)]
    public float slimeHeight = 2f;

    [Header("Lifetime / Fade")]
    [Tooltip("If true, slimes despawn when their source cluster drops below minLeavesPerCluster.")]
    public bool despawnWhenLeavesGone = true;

    [Tooltip("How long a slime fades out before being destroyed (seconds).")]
    [Min(0f)]
    public float fadeOutDuration = 2f;

    [Tooltip("If > 0, slimes also have a maximum lifetime in seconds, regardless of leaf count.")]
    [Min(0f)]
    public float maxSlimeLifetime = 0f;

    [Header("Polling")]
    [Tooltip("How often to check cluster data for new spawns / despawns (seconds).")]
    [Min(0.05f)]
    public float pollInterval = 0.5f;

    [Header("Debug")]
    public bool showDebugGizmos = true;
    public bool logSpawns = true;


    private struct SpawnedSlime
    {
        public GameObject instance;
        public Slime slimeComponent;
        public int bucketIndex;
        public float spawnTime;
        public bool isFadingOut;
        public float fadeStartTime;
    }

    private readonly List<SpawnedSlime> activeSlimes = new List<SpawnedSlime>();
    private float cooldownTimer;
    private float pollTimer;


    void Start()
    {
        if (detectionZone == null)
        {
            Debug.LogError("[SporeDispersal] detectionZone is not assigned!", this);
            enabled = false;
            return;
        }
        if (slimePrefab == null)
        {
            Debug.LogError("[SporeDispersal] slimePrefab is not assigned!", this);
            enabled = false;
            return;
        }

        if (slimePrefab.GetComponent<Slime>() == null)
            Debug.LogWarning("[SporeDispersal] slimePrefab doesn't have a Slime component! " +
                             "Spawning will still work but SetWorldBounds won't be called.");
    }

    void Update()
    {
        cooldownTimer -= Time.deltaTime;

        pollTimer += Time.deltaTime;
        if (pollTimer < pollInterval) return;
        pollTimer = 0f;

        UpdateExistingSlimes();

        if (cooldownTimer <= 0f)
            EvaluateClusters();
    }


    void UpdateExistingSlimes()
    {
        var clusters = detectionZone.Clusters;

        for (int i = activeSlimes.Count - 1; i >= 0; i--)
        {
            var s = activeSlimes[i];

            if (s.instance == null)
            {
                activeSlimes.RemoveAt(i);
                continue;
            }

            bool lifetimeExpired = maxSlimeLifetime > 0f &&
                                   (Time.time - s.spawnTime) >= maxSlimeLifetime;

            bool clusterDied = false;
            if (despawnWhenLeavesGone && s.bucketIndex < clusters.Count)
                clusterDied = clusters[s.bucketIndex].Count < minLeavesPerCluster;

            bool shouldFade = (lifetimeExpired || clusterDied) && !s.isFadingOut;

            if (shouldFade)
            {
                s.isFadingOut = true;
                s.fadeStartTime = Time.time;
                activeSlimes[i] = s;
                StartCoroutine(FadeOutAndDestroy(s.instance, fadeOutDuration));

                if (logSpawns)
                    Debug.Log($"[SporeDispersal] Slime at bucket {s.bucketIndex} beginning fade out " +
                              $"(leaves gone: {clusterDied}, lifetime: {lifetimeExpired})");
                continue;
            }

            if (s.isFadingOut && s.instance == null)
                activeSlimes.RemoveAt(i);
        }
    }


    void EvaluateClusters()
    {
        if (activeSlimes.Count >= maxSimultaneousSlimes) return;
        if (!detectionZone.HasLeaves) return;

        var clusters = detectionZone.Clusters;

        for (int b = 0; b < clusters.Count; b++)
        {
            if (activeSlimes.Count >= maxSimultaneousSlimes) break;

            var cluster = clusters[b];

            if (cluster.Count < minLeavesPerCluster) continue;

            if (BucketHasActiveSlime(b)) continue;

            SpawnSlimeForCluster(b, cluster);
            cooldownTimer = spawnCooldown;
        }
    }

    bool BucketHasActiveSlime(int bucketIndex)
    {
        foreach (var s in activeSlimes)
        {
            if (s.bucketIndex == bucketIndex && s.instance != null && !s.isFadingOut)
                return true;
        }
        return false;
    }


    void SpawnSlimeForCluster(int bucketIndex, LeafDetectionZone.LeafCluster cluster)
    {
        float spawnX = cluster.worldX + Random.Range(-spawnXJitter, spawnXJitter);
        Vector3 spawnPos = new Vector3(spawnX, zoneFloorY, 0f);

        GameObject instance = Instantiate(slimePrefab, spawnPos, Quaternion.identity);
        instance.name = $"SlimeMold_Bucket{bucketIndex}";

        Slime slimeComp = instance.GetComponent<Slime>();

        activeSlimes.Add(new SpawnedSlime
        {
            instance = instance,
            slimeComponent = slimeComp,
            bucketIndex = bucketIndex,
            spawnTime = Time.time,
            isFadingOut = false,
            fadeStartTime = -1f
        });

        StartCoroutine(ApplyBoundsNextFrame(slimeComp, cluster));

        if (logSpawns)
            Debug.Log($"[SporeDispersal] Spawned slime for bucket {bucketIndex} " +
                      $"at ({spawnX:F2}, {zoneFloorY:F2}) | " +
                      $"Leaves: {cluster.Count} | density: {cluster.density:P0}");
    }

    IEnumerator ApplyBoundsNextFrame(Slime slimeComp, LeafDetectionZone.LeafCluster cluster)
    {
        yield return null;

        if (slimeComp == null) yield break;

        float width = scaleWithClusterSize
            ? Mathf.Lerp(minSlimeWidth, maxSlimeWidth, cluster.density)
            : (minSlimeWidth + maxSlimeWidth) * 0.5f;

        width = Mathf.Clamp(width, minSlimeWidth, maxSlimeWidth);

        Rect bounds = new Rect(
            slimeComp.transform.position.x - width * 0.5f,
            zoneFloorY,
            width,
            slimeHeight
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
        foreach (var s in activeSlimes)
        {
            if (s.instance != null && !s.isFadingOut)
                StartCoroutine(FadeOutAndDestroy(s.instance, fadeOutDuration));
        }
        activeSlimes.Clear();
    }

    public int ActiveSlimeCount => activeSlimes.Count;


    void OnDrawGizmos()
    {
        if (!showDebugGizmos || detectionZone == null) return;

        Gizmos.color = new Color(0.6f, 1f, 0.6f, 0.5f);
        Vector3 floorL = new Vector3(
            detectionZone.transform.position.x - detectionZone.zoneSize.x * 0.5f, zoneFloorY, 0);
        Vector3 floorR = new Vector3(
            detectionZone.transform.position.x + detectionZone.zoneSize.x * 0.5f, zoneFloorY, 0);
        Gizmos.DrawLine(floorL, floorR);

        if (!Application.isPlaying) return;

        foreach (var s in activeSlimes)
        {
            if (s.instance == null) continue;
            Gizmos.color = s.isFadingOut
                ? new Color(1f, 0.4f, 0.1f, 0.6f)
                : new Color(0.2f, 0.9f, 1f, 0.7f);
            Gizmos.DrawWireSphere(s.instance.transform.position, 0.25f);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos) return;

        UnityEditor.Handles.color = new Color(0.5f, 1f, 0.5f, 0.8f);
        Vector3 labelPos = transform.position + Vector3.up * 0.5f;
        UnityEditor.Handles.Label(labelPos,
            Application.isPlaying
                ? $"Slimes active: {ActiveSlimeCount} / {maxSimultaneousSlimes}"
                : $"SporeDispersal | Min leaves/cluster: {minLeavesPerCluster} | Max slimes: {maxSimultaneousSlimes}");
    }
#endif
}