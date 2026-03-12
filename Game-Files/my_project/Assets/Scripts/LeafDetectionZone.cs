using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class LeafDetectionZone : MonoBehaviour
{

    [Header("Zone Shape")]
    [Tooltip("Width and height of the detection rectangle in world units.")]
    public Vector2 zoneSize = new Vector2(5f, 3f);

    [Header("Ground Filter")]
    [Tooltip("Only leaves touching objects on THESE layers are counted. Assign your 'Ground' layer here.")]
    public LayerMask groundLayers;

    [Tooltip("Small extra radius added when checking ground contact, on top of the leaf's own radius. " +
             "Increase slightly if leaves that look grounded aren't being detected.")]
    [Min(0f)]
    public float groundCheckPadding = 0.02f;

    [Tooltip("Downward offset from the leaf center for the ground check. " +
             "Helps detect leaves sitting flush on a surface.")]
    [Min(0f)]
    public float groundCheckDownOffset = 0.02f;

    [Header("Cluster Analysis")]
    [Tooltip("Number of horizontal buckets across the zone for cluster tracking.")]
    [Range(1, 50)]
    public int clusterBuckets = 10;

    [Tooltip("How often to recalculate cluster data (seconds). 0 = every frame.")]
    [Min(0f)]
    public float clusterRefreshInterval = 0.1f;

    [Header("Detection Timing")]
    [Tooltip("How often to scan for leaf enter/exit (seconds). 0 = every frame.")]
    [Min(0f)]
    public float scanInterval = 0.05f;

    [Header("Debug")]
    public bool ShowDebugGizmos = true;
    public bool LogEvents = false;


    public int LeafCount => leavesInZone.Count;

    public int TotalEntered { get; private set; }

    public int TotalExited { get; private set; }

    public IReadOnlyList<LeafCluster> Clusters => clusters;

    public bool HasLeaves => leavesInZone.Count > 0;


    public event System.Action<int> OnLeafEntered;

    public event System.Action<int> OnLeafExited;

    public event System.Action<IReadOnlyList<LeafCluster>> OnClustersUpdated;


    [System.Serializable]
    public class LeafCluster
    {
        public float worldX;

        public int Count;

        public float density;

        public override string ToString() =>
            $"[X:{worldX:F2} Count:{Count} density:{density:P0}]";
    }


    private readonly Dictionary<(int, int), TrackedLeaf> leavesInZone =
        new Dictionary<(int, int), TrackedLeaf>();

    private struct TrackedLeaf
    {
        public BurstLeafSystem System;
        public int leafIndex;
    }

    private readonly Dictionary<(int, int), bool> prevState =
        new Dictionary<(int, int), bool>();

    private readonly List<LeafCluster> clusters = new List<LeafCluster>();
    private readonly List<(int, int)> staleKeys = new List<(int, int)>();
    private readonly HashSet<(int, int)> seenThisScan = new HashSet<(int, int)>();

    private float scanTimer;
    private float clusterTimer;


    void Start()
    {
        InitClusters();

        if (groundLayers.value == 0)
            Debug.LogWarning("[LeafDetectionZone] groundLayers mask is empty — " +
                             "no leaves will ever be detected. Please set it to include your 'Ground' layer.");
    }

    void Update()
    {
        scanTimer += Time.deltaTime;
        if (scanTimer >= scanInterval || scanInterval == 0f)
        {
            scanTimer = 0f;
            ScanAllSystems();
        }

        clusterTimer += Time.deltaTime;
        if (clusterTimer >= clusterRefreshInterval || clusterRefreshInterval == 0f)
        {
            clusterTimer = 0f;
            RecalculateClusters();
        }
    }


    void ScanAllSystems()
    {
        var instances = BurstLeafSystem.AllInstances;
        if (instances == null || instances.Count == 0) return;

        Vector2 worldCenter = transform.position;
        float halfW = zoneSize.x * 0.5f;
        float halfH = zoneSize.y * 0.5f;

        float minX = worldCenter.x - halfW;
        float maxX = worldCenter.x + halfW;
        float minY = worldCenter.y - halfH;
        float maxY = worldCenter.y + halfH;

        seenThisScan.Clear();

        foreach (var system in instances)
        {
            if (system == null || !system.Positions.IsCreated) continue;

            int sysId = system.GetInstanceID();
            int count = system.ActiveLeafCount;

            for (int i = 0; i < count; i++)
            {
                float3 pos = system.Positions[i];
                var key = (sysId, i);
                seenThisScan.Add(key);

                bool inBounds = pos.x >= minX && pos.x <= maxX &&
                                pos.y >= minY && pos.y <= maxY;

                bool qualifies = inBounds && IsOnGround(pos, system.Sizes[i]);

                bool wasTracked = prevState.TryGetValue(key, out bool prev) && prev;

                if (qualifies && !wasTracked)
                {
                    leavesInZone[key] = new TrackedLeaf { System = system, leafIndex = i };
                    TotalEntered++;
                    OnLeafEntered?.Invoke(i);
                    if (LogEvents)
                        Debug.Log($"[LeafZone] Leaf #{i} ENTERED (grounded). Inside: {leavesInZone.Count}");
                }
                else if (!qualifies && wasTracked)
                {
                    leavesInZone.Remove(key);
                    TotalExited++;
                    OnLeafExited?.Invoke(i);
                    if (LogEvents)
                        Debug.Log($"[LeafZone] Leaf #{i} EXITED. Inside: {leavesInZone.Count}");
                }

                prevState[key] = qualifies;
            }
        }

        staleKeys.Clear();
        foreach (var kvp in prevState)
        {
            if (!seenThisScan.Contains(kvp.Key))
                staleKeys.Add(kvp.Key);
        }
        foreach (var key in staleKeys)
        {
            if (prevState.TryGetValue(key, out bool wasIn) && wasIn)
            {
                leavesInZone.Remove(key);
                TotalExited++;
            }
            prevState.Remove(key);
        }
    }

    bool IsOnGround(float3 pos, float leafSize)
    {
        float radius = leafSize * 0.4f + groundCheckPadding;
        var checkPos = new Vector2(pos.x, pos.y - groundCheckDownOffset);
        return Physics2D.OverlapCircle(checkPos, radius, groundLayers) != null;
    }


    void InitClusters()
    {
        clusters.Clear();
        int buckets = Mathf.Max(1, clusterBuckets);
        float halfW = zoneSize.x * 0.5f;
        float bucketWidth = zoneSize.x / buckets;

        for (int b = 0; b < buckets; b++)
        {
            clusters.Add(new LeafCluster
            {
                worldX = transform.position.x - halfW + bucketWidth * (b + 0.5f),
                Count = 0,
                density = 0f
            });
        }
    }

    void RecalculateClusters()
    {
        int buckets = Mathf.Max(1, clusterBuckets);
        float halfW = zoneSize.x * 0.5f;
        float bucketWidth = zoneSize.x / buckets;
        float zoneMinX = transform.position.x - halfW;

        while (clusters.Count < buckets) clusters.Add(new LeafCluster());
        while (clusters.Count > buckets) clusters.RemoveAt(clusters.Count - 1);

        for (int b = 0; b < buckets; b++)
        {
            clusters[b].worldX = zoneMinX + bucketWidth * (b + 0.5f);
            clusters[b].Count = 0;
        }

        int total = 0;
        foreach (var kvp in leavesInZone)
        {
            var t = kvp.Value;
            if (t.System == null || !t.System.Positions.IsCreated) continue;
            if (t.leafIndex >= t.System.ActiveLeafCount) continue;

            float leafX = t.System.Positions[t.leafIndex].x;
            int bucket = Mathf.Clamp(
                Mathf.FloorToInt((leafX - zoneMinX) / bucketWidth),
                0, buckets - 1
            );
            clusters[bucket].Count++;
            total++;
        }

        for (int b = 0; b < buckets; b++)
            clusters[b].density = total > 0 ? (float)clusters[b].Count / total : 0f;

        OnClustersUpdated?.Invoke(clusters);
    }


    public void ResetZone()
    {
        leavesInZone.Clear();
        prevState.Clear();
        TotalEntered = 0;
        TotalExited = 0;
        InitClusters();
    }

    public int GetDensestClusterIndex()
    {
        int best = -1, bestCount = 0;
        for (int i = 0; i < clusters.Count; i++)
        {
            if (clusters[i].Count > bestCount) { bestCount = clusters[i].Count; best = i; }
        }
        return best;
    }

    public float GetDensestClusterworldX()
    {
        int idx = GetDensestClusterIndex();
        return idx >= 0 ? clusters[idx].worldX : float.NaN;
    }


    void OnDrawGizmos()
    {
        if (!ShowDebugGizmos) return;

        Vector3 center = transform.position;

        Gizmos.color = Application.isPlaying && LeafCount > 0
            ? new Color(0.2f, 1f, 0.4f, 0.9f)
            : new Color(0.2f, 0.8f, 0.4f, 0.5f);
        Gizmos.DrawWireCube(center, new Vector3(zoneSize.x, zoneSize.y, 0));

        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.06f);
        Gizmos.DrawCube(center, new Vector3(zoneSize.x, zoneSize.y, 0));

        if (!Application.isPlaying) return;

        float halfW = zoneSize.x * 0.5f;
        float halfH = zoneSize.y * 0.5f;
        int buckets = Mathf.Max(1, clusterBuckets);
        float bucketWidth = zoneSize.x / buckets;
        float zoneMinX = center.x - halfW;

        for (int b = 0; b < clusters.Count; b++)
        {
            if (clusters[b].Count == 0) continue;

            float fillH = zoneSize.y * clusters[b].density;
            float bCenterX = zoneMinX + bucketWidth * (b + 0.5f);
            Gizmos.color = Color.Lerp(
                new Color(1f, 1f, 0f, 0.3f),
                new Color(1f, 0.2f, 0f, 0.55f),
                clusters[b].density
            );
            Gizmos.DrawCube(
                new Vector3(bCenterX, center.y - halfH + fillH * 0.5f, 0),
                new Vector3(bucketWidth * 0.88f, fillH, 0)
            );

            Gizmos.color = new Color(1f, 1f, 1f, 0.12f);
            Gizmos.DrawLine(
                new Vector3(bCenterX - bucketWidth * 0.5f, center.y - halfH, 0),
                new Vector3(bCenterX - bucketWidth * 0.5f, center.y + halfH, 0)
            );
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!ShowDebugGizmos) return;
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(transform.position, new Vector3(zoneSize.x, zoneSize.y, 0.01f));

#if UNITY_EDITOR
        Vector3 labelPos = transform.position + Vector3.up * (zoneSize.y * 0.5f + 0.3f);
        string stats = Application.isPlaying
            ? $"Grounded Leaves: {LeafCount}  |  In: {TotalEntered}  |  Out: {TotalExited}"
            : $"Zone {zoneSize.x:F1}x{zoneSize.y:F1} | Buckets: {clusterBuckets} | Ground filter ON";
        UnityEditor.Handles.Label(labelPos, stats);

        if (Application.isPlaying && LeafCount > 0)
        {
            float denseX = GetDensestClusterworldX();
            if (!float.IsNaN(denseX))
            {
                float halfH = zoneSize.y * 0.5f;
                UnityEditor.Handles.color = Color.red;
                UnityEditor.Handles.DrawLine(
                    new Vector3(denseX, transform.position.y - halfH, 0),
                    new Vector3(denseX, transform.position.y + halfH, 0)
                );
                UnityEditor.Handles.Label(
                    new Vector3(denseX, transform.position.y + halfH + 0.15f, 0),
                    $"Peak X: {denseX:F2}"
                );
            }
        }
#endif
    }
}