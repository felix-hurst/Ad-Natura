using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class LeafDetectionZone : MonoBehaviour
{

    [Header("Zone Shape")]
    [Tooltip("Width and height of the detection rectangle in world units.")]
    public Vector2 ZoneSize = new Vector2(5f, 3f);

    [Header("Ground Filter")]
    [Tooltip("Only leaves touching objects on THESE layers are counted. Assign your 'Ground' layer here.")]
    public LayerMask GroundLayers;

    [Tooltip("Small extra radius added when checking ground contact, on top of the leaf's own radius. " +
             "Increase slightly if leaves that look grounded aren't being detected.")]
    [Min(0f)]
    public float GroundCheckPadding = 0.02f;

    [Tooltip("Downward offset from the leaf center for the ground check. " +
             "Helps detect leaves sitting flush on a surface.")]
    [Min(0f)]
    public float GroundCheckDownOffset = 0.02f;

    [Header("Cluster Analysis")]
    [Tooltip("Number of horizontal buckets across the zone for cluster tracking.")]
    [Range(1, 50)]
    public int ClusterBuckets = 10;

    [Tooltip("How often to recalculate cluster data (seconds). 0 = every frame.")]
    [Min(0f)]
    public float ClusterRefreshInterval = 0.1f;

    [Header("Detection Timing")]
    [Tooltip("How often to scan for leaf enter/exit (seconds). 0 = every frame.")]
    [Min(0f)]
    public float ScanInterval = 0.05f;

    [Header("Debug")]
    public bool ShowDebugGizmos = true;
    public bool LogEvents = false;


    public int LeafCount => _leavesInZone.Count;

    public int TotalEntered { get; private set; }

    public int TotalExited { get; private set; }

    public IReadOnlyList<LeafCluster> Clusters => _clusters;

    public bool HasLeaves => _leavesInZone.Count > 0;


    public event System.Action<int> OnLeafEntered;

    public event System.Action<int> OnLeafExited;

    public event System.Action<IReadOnlyList<LeafCluster>> OnClustersUpdated;


    [System.Serializable]
    public class LeafCluster
    {
        public float WorldX;

        public int Count;

        public float Density;

        public override string ToString() =>
            $"[X:{WorldX:F2} Count:{Count} Density:{Density:P0}]";
    }


    private readonly Dictionary<(int, int), TrackedLeaf> _leavesInZone =
        new Dictionary<(int, int), TrackedLeaf>();

    private struct TrackedLeaf
    {
        public BurstLeafSystem System;
        public int LeafIndex;
    }

    private readonly Dictionary<(int, int), bool> _prevState =
        new Dictionary<(int, int), bool>();

    private readonly List<LeafCluster> _clusters = new List<LeafCluster>();
    private readonly List<(int, int)> _staleKeys = new List<(int, int)>();
    private readonly HashSet<(int, int)> _seenThisScan = new HashSet<(int, int)>();

    private float _scanTimer;
    private float _clusterTimer;


    void Start()
    {
        InitClusters();

        if (GroundLayers.value == 0)
            Debug.LogWarning("[LeafDetectionZone] GroundLayers mask is empty — " +
                             "no leaves will ever be detected. Please set it to include your 'Ground' layer.");
    }

    void Update()
    {
        _scanTimer += Time.deltaTime;
        if (_scanTimer >= ScanInterval || ScanInterval == 0f)
        {
            _scanTimer = 0f;
            ScanAllSystems();
        }

        _clusterTimer += Time.deltaTime;
        if (_clusterTimer >= ClusterRefreshInterval || ClusterRefreshInterval == 0f)
        {
            _clusterTimer = 0f;
            RecalculateClusters();
        }
    }


    void ScanAllSystems()
    {
        var instances = BurstLeafSystem.AllInstances;
        if (instances == null || instances.Count == 0) return;

        Vector2 worldCenter = transform.position;
        float halfW = ZoneSize.x * 0.5f;
        float halfH = ZoneSize.y * 0.5f;

        float minX = worldCenter.x - halfW;
        float maxX = worldCenter.x + halfW;
        float minY = worldCenter.y - halfH;
        float maxY = worldCenter.y + halfH;

        _seenThisScan.Clear();

        foreach (var system in instances)
        {
            if (system == null || !system.Positions.IsCreated) continue;

            int sysId = system.GetInstanceID();
            int count = system.ActiveLeafCount;

            for (int i = 0; i < count; i++)
            {
                float3 pos = system.Positions[i];
                var key = (sysId, i);
                _seenThisScan.Add(key);

                bool inBounds = pos.x >= minX && pos.x <= maxX &&
                                pos.y >= minY && pos.y <= maxY;

                bool qualifies = inBounds && IsOnGround(pos, system.Sizes[i]);

                bool wasTracked = _prevState.TryGetValue(key, out bool prev) && prev;

                if (qualifies && !wasTracked)
                {
                    _leavesInZone[key] = new TrackedLeaf { System = system, LeafIndex = i };
                    TotalEntered++;
                    OnLeafEntered?.Invoke(i);
                    if (LogEvents)
                        Debug.Log($"[LeafZone] Leaf #{i} ENTERED (grounded). Inside: {_leavesInZone.Count}");
                }
                else if (!qualifies && wasTracked)
                {
                    _leavesInZone.Remove(key);
                    TotalExited++;
                    OnLeafExited?.Invoke(i);
                    if (LogEvents)
                        Debug.Log($"[LeafZone] Leaf #{i} EXITED. Inside: {_leavesInZone.Count}");
                }

                _prevState[key] = qualifies;
            }
        }

        _staleKeys.Clear();
        foreach (var kvp in _prevState)
        {
            if (!_seenThisScan.Contains(kvp.Key))
                _staleKeys.Add(kvp.Key);
        }
        foreach (var key in _staleKeys)
        {
            if (_prevState.TryGetValue(key, out bool wasIn) && wasIn)
            {
                _leavesInZone.Remove(key);
                TotalExited++;
            }
            _prevState.Remove(key);
        }
    }

    bool IsOnGround(float3 pos, float leafSize)
    {
        float radius = leafSize * 0.4f + GroundCheckPadding;
        var checkPos = new Vector2(pos.x, pos.y - GroundCheckDownOffset);
        return Physics2D.OverlapCircle(checkPos, radius, GroundLayers) != null;
    }


    void InitClusters()
    {
        _clusters.Clear();
        int buckets = Mathf.Max(1, ClusterBuckets);
        float halfW = ZoneSize.x * 0.5f;
        float bucketWidth = ZoneSize.x / buckets;

        for (int b = 0; b < buckets; b++)
        {
            _clusters.Add(new LeafCluster
            {
                WorldX = transform.position.x - halfW + bucketWidth * (b + 0.5f),
                Count = 0,
                Density = 0f
            });
        }
    }

    void RecalculateClusters()
    {
        int buckets = Mathf.Max(1, ClusterBuckets);
        float halfW = ZoneSize.x * 0.5f;
        float bucketWidth = ZoneSize.x / buckets;
        float zoneMinX = transform.position.x - halfW;

        while (_clusters.Count < buckets) _clusters.Add(new LeafCluster());
        while (_clusters.Count > buckets) _clusters.RemoveAt(_clusters.Count - 1);

        for (int b = 0; b < buckets; b++)
        {
            _clusters[b].WorldX = zoneMinX + bucketWidth * (b + 0.5f);
            _clusters[b].Count = 0;
        }

        int total = 0;
        foreach (var kvp in _leavesInZone)
        {
            var t = kvp.Value;
            if (t.System == null || !t.System.Positions.IsCreated) continue;
            if (t.LeafIndex >= t.System.ActiveLeafCount) continue;

            float leafX = t.System.Positions[t.LeafIndex].x;
            int bucket = Mathf.Clamp(
                Mathf.FloorToInt((leafX - zoneMinX) / bucketWidth),
                0, buckets - 1
            );
            _clusters[bucket].Count++;
            total++;
        }

        for (int b = 0; b < buckets; b++)
            _clusters[b].Density = total > 0 ? (float)_clusters[b].Count / total : 0f;

        OnClustersUpdated?.Invoke(_clusters);
    }


    public void ResetZone()
    {
        _leavesInZone.Clear();
        _prevState.Clear();
        TotalEntered = 0;
        TotalExited = 0;
        InitClusters();
    }

    public int GetDensestClusterIndex()
    {
        int best = -1, bestCount = 0;
        for (int i = 0; i < _clusters.Count; i++)
        {
            if (_clusters[i].Count > bestCount) { bestCount = _clusters[i].Count; best = i; }
        }
        return best;
    }

    public float GetDensestClusterWorldX()
    {
        int idx = GetDensestClusterIndex();
        return idx >= 0 ? _clusters[idx].WorldX : float.NaN;
    }


    void OnDrawGizmos()
    {
        if (!ShowDebugGizmos) return;

        Vector3 center = transform.position;

        Gizmos.color = Application.isPlaying && LeafCount > 0
            ? new Color(0.2f, 1f, 0.4f, 0.9f)
            : new Color(0.2f, 0.8f, 0.4f, 0.5f);
        Gizmos.DrawWireCube(center, new Vector3(ZoneSize.x, ZoneSize.y, 0));

        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.06f);
        Gizmos.DrawCube(center, new Vector3(ZoneSize.x, ZoneSize.y, 0));

        if (!Application.isPlaying) return;

        float halfW = ZoneSize.x * 0.5f;
        float halfH = ZoneSize.y * 0.5f;
        int buckets = Mathf.Max(1, ClusterBuckets);
        float bucketWidth = ZoneSize.x / buckets;
        float zoneMinX = center.x - halfW;

        for (int b = 0; b < _clusters.Count; b++)
        {
            if (_clusters[b].Count == 0) continue;

            float fillH = ZoneSize.y * _clusters[b].Density;
            float bCenterX = zoneMinX + bucketWidth * (b + 0.5f);
            Gizmos.color = Color.Lerp(
                new Color(1f, 1f, 0f, 0.3f),
                new Color(1f, 0.2f, 0f, 0.55f),
                _clusters[b].Density
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
        Gizmos.DrawWireCube(transform.position, new Vector3(ZoneSize.x, ZoneSize.y, 0.01f));

#if UNITY_EDITOR
        Vector3 labelPos = transform.position + Vector3.up * (ZoneSize.y * 0.5f + 0.3f);
        string stats = Application.isPlaying
            ? $"Grounded Leaves: {LeafCount}  |  In: {TotalEntered}  |  Out: {TotalExited}"
            : $"Zone {ZoneSize.x:F1}x{ZoneSize.y:F1} | Buckets: {ClusterBuckets} | Ground filter ON";
        UnityEditor.Handles.Label(labelPos, stats);

        if (Application.isPlaying && LeafCount > 0)
        {
            float denseX = GetDensestClusterWorldX();
            if (!float.IsNaN(denseX))
            {
                float halfH = ZoneSize.y * 0.5f;
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