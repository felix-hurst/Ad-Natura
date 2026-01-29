using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages water attraction for slime mold simulation.
/// Caches WaterSource components and generates attraction map for Slime component.
/// </summary>
[RequireComponent(typeof(Slime))]
[DefaultExecutionOrder(100)]
public class SlimeMoldManager : MonoBehaviour
{
    [Header("Water Attraction")]
    [Tooltip("Global multiplier for water attraction. Higher = slime responds faster to water")]
    [Range(0f, 100f)]
    public float waterAttractionStrength = 40f;

    [Header("Source Detection")]
    [Tooltip("Automatically find all WaterSource components in scene")]
    public bool autoFindSources = true;
    [Tooltip("How often to scan for new water sources (seconds)")]
    [Range(0.1f, 2f)]
    public float sourceRefreshInterval = 0.5f;
    [Tooltip("Manual list of water sources (used when autoFindSources is false)")]
    public List<WaterSource> manualWaterSources = new List<WaterSource>();

    [Header("Performance")]
    [Tooltip("How often to update attraction map (seconds). Lower = more responsive")]
    [Range(0.016f, 0.5f)]
    public float mapUpdateInterval = 0.066f; // ~15 updates/sec

    private Slime slimeSimulation;
    private RenderTexture waterAttractionMap;
    private Texture2D waterAttractionTextureCPU;
    private Color[] pixelBuffer;
    private List<WaterSource> cachedWaterSources = new List<WaterSource>();
    private Rect worldBounds;
    private Vector2Int resolution;
    private float timeSinceSourceRefresh;
    private float timeSinceMapUpdate;

    private void Start()
    {
        slimeSimulation = GetComponent<Slime>();
        worldBounds = slimeSimulation.GetWorldBounds();
        resolution = slimeSimulation.GetSimulationResolution();

        CreateAttractionMap();
        RefreshSources();
        UpdateAttractionMap();
    }

    private void Update()
    {
        timeSinceSourceRefresh += Time.deltaTime;
        if (timeSinceSourceRefresh >= sourceRefreshInterval)
        {
            RefreshSources();
            timeSinceSourceRefresh = 0f;
        }

        timeSinceMapUpdate += Time.deltaTime;
        if (timeSinceMapUpdate >= mapUpdateInterval)
        {
            UpdateAttractionMap();
            timeSinceMapUpdate = 0f;
        }

        if (slimeSimulation != null && waterAttractionMap != null)
        {
            slimeSimulation.SetAttractionMaps(waterAttractionMap, waterAttractionStrength);
        }
    }

    private void CreateAttractionMap()
    {
        waterAttractionMap = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.RFloat);
        waterAttractionMap.enableRandomWrite = true;
        waterAttractionMap.filterMode = FilterMode.Bilinear;
        waterAttractionMap.Create();

        waterAttractionTextureCPU = new Texture2D(resolution.x, resolution.y, TextureFormat.RFloat, false);
        pixelBuffer = new Color[resolution.x * resolution.y];
    }

    private void RefreshSources()
    {
        cachedWaterSources.Clear();

        if (autoFindSources)
        {
            cachedWaterSources.AddRange(FindObjectsByType<WaterSource>(FindObjectsSortMode.None));
        }
        else
        {
            cachedWaterSources.AddRange(manualWaterSources);
        }

        cachedWaterSources.RemoveAll(s => s == null);
    }

    private void UpdateAttractionMap()
    {
        if (waterAttractionTextureCPU == null || pixelBuffer == null) return;

        int w = resolution.x;
        int h = resolution.y;

        // Clear buffer
        for (int i = 0; i < pixelBuffer.Length; i++)
            pixelBuffer[i] = Color.black;

        foreach (WaterSource source in cachedWaterSources)
        {
            if (source == null || !source.isActive) continue;

            Vector2 sourcePos = source.GetPosition();
            float radius = source.attractionRadius;
            float strength = source.attractionStrength;

            // Check if source is outside bounds
            bool outsideLeft = sourcePos.x < worldBounds.xMin;
            bool outsideRight = sourcePos.x > worldBounds.xMax;
            bool outsideBottom = sourcePos.y < worldBounds.yMin;
            bool outsideTop = sourcePos.y > worldBounds.yMax;
            bool isOutside = outsideLeft || outsideRight || outsideBottom || outsideTop;

            if (isOutside)
            {
                // Source is outside bounds - create LOCALIZED attraction at nearest edge
                float distToEdge = 0f;
                if (outsideTop) distToEdge = sourcePos.y - worldBounds.yMax;
                else if (outsideBottom) distToEdge = worldBounds.yMin - sourcePos.y;
                else if (outsideLeft) distToEdge = worldBounds.xMin - sourcePos.x;
                else if (outsideRight) distToEdge = sourcePos.x - worldBounds.xMax;

                float maxSenseRange = radius * 2f;
                if (distToEdge > maxSenseRange) continue;

                float edgeAttraction = strength * (1f - distToEdge / maxSenseRange);

                // Calculate the horizontal/vertical position on the edge closest to water
                float normSourceX = (sourcePos.x - worldBounds.x) / worldBounds.width;
                float normSourceY = (sourcePos.y - worldBounds.y) / worldBounds.height;
                int sourcePosX = Mathf.Clamp(Mathf.RoundToInt(normSourceX * w), 0, w - 1);
                int sourcePosY = Mathf.Clamp(Mathf.RoundToInt(normSourceY * h), 0, h - 1);

                // Use correct axis for gradient depth and spread based on which edge
                bool isVerticalEdge = outsideLeft || outsideRight;
                int gradientDepth = isVerticalEdge
                    ? Mathf.CeilToInt((radius / worldBounds.width) * w)
                    : Mathf.CeilToInt((radius / worldBounds.height) * h);
                int spreadPixels = isVerticalEdge
                    ? Mathf.CeilToInt((radius / worldBounds.height) * h)
                    : Mathf.CeilToInt((radius / worldBounds.width) * w);

                if (outsideTop)
                {
                    int xMin = Mathf.Max(0, sourcePosX - spreadPixels);
                    int xMax = Mathf.Min(w - 1, sourcePosX + spreadPixels);
                    for (int y = h - 1; y >= Mathf.Max(0, h - gradientDepth); y--)
                    {
                        float depthFalloff = 1f - (float)(h - 1 - y) / gradientDepth;
                        for (int x = xMin; x <= xMax; x++)
                        {
                            float horizDist = Mathf.Abs(x - sourcePosX) / (float)spreadPixels;
                            float horizFalloff = 1f - horizDist;
                            int idx = y * w + x;
                            pixelBuffer[idx].r = Mathf.Min(1f, pixelBuffer[idx].r + edgeAttraction * depthFalloff * horizFalloff);
                        }
                    }
                }
                else if (outsideBottom)
                {
                    int xMin = Mathf.Max(0, sourcePosX - spreadPixels);
                    int xMax = Mathf.Min(w - 1, sourcePosX + spreadPixels);
                    for (int y = 0; y < Mathf.Min(h, gradientDepth); y++)
                    {
                        float depthFalloff = 1f - (float)y / gradientDepth;
                        for (int x = xMin; x <= xMax; x++)
                        {
                            float horizDist = Mathf.Abs(x - sourcePosX) / (float)spreadPixels;
                            float horizFalloff = 1f - horizDist;
                            int idx = y * w + x;
                            pixelBuffer[idx].r = Mathf.Min(1f, pixelBuffer[idx].r + edgeAttraction * depthFalloff * horizFalloff);
                        }
                    }
                }
                else if (outsideLeft)
                {
                    int yMin = Mathf.Max(0, sourcePosY - spreadPixels);
                    int yMax = Mathf.Min(h - 1, sourcePosY + spreadPixels);
                    for (int x = 0; x < Mathf.Min(w, gradientDepth); x++)
                    {
                        float depthFalloff = 1f - (float)x / gradientDepth;
                        for (int y = yMin; y <= yMax; y++)
                        {
                            float vertDist = Mathf.Abs(y - sourcePosY) / (float)spreadPixels;
                            float vertFalloff = 1f - vertDist;
                            int idx = y * w + x;
                            pixelBuffer[idx].r = Mathf.Min(1f, pixelBuffer[idx].r + edgeAttraction * depthFalloff * vertFalloff);
                        }
                    }
                }
                else if (outsideRight)
                {
                    int yMin = Mathf.Max(0, sourcePosY - spreadPixels);
                    int yMax = Mathf.Min(h - 1, sourcePosY + spreadPixels);
                    for (int x = w - 1; x >= Mathf.Max(0, w - gradientDepth); x--)
                    {
                        float depthFalloff = 1f - (float)(w - 1 - x) / gradientDepth;
                        for (int y = yMin; y <= yMax; y++)
                        {
                            float vertDist = Mathf.Abs(y - sourcePosY) / (float)spreadPixels;
                            float vertFalloff = 1f - vertDist;
                            int idx = y * w + x;
                            pixelBuffer[idx].r = Mathf.Min(1f, pixelBuffer[idx].r + edgeAttraction * depthFalloff * vertFalloff);
                        }
                    }
                }
            }
            else
            {
                // Source is inside bounds - use normal radial attraction
                float normX = (sourcePos.x - worldBounds.x) / worldBounds.width;
                float normY = (sourcePos.y - worldBounds.y) / worldBounds.height;
                int centerX = Mathf.RoundToInt(normX * w);
                int centerY = Mathf.RoundToInt(normY * h);

                float texRadiusX = (radius / worldBounds.width) * w;
                float texRadiusY = (radius / worldBounds.height) * h;
                int radiusPixels = Mathf.CeilToInt(Mathf.Max(texRadiusX, texRadiusY));

                int minX = Mathf.Max(0, centerX - radiusPixels);
                int maxX = Mathf.Min(w - 1, centerX + radiusPixels);
                int minY = Mathf.Max(0, centerY - radiusPixels);
                int maxY = Mathf.Min(h - 1, centerY + radiusPixels);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector2 worldPos = TextureToWorld(x, y);
                        float attraction = source.GetAttractionAt(worldPos);
                        if (attraction > 0)
                        {
                            int idx = y * w + x;
                            pixelBuffer[idx].r = Mathf.Min(1f, pixelBuffer[idx].r + attraction);
                        }
                    }
                }
            }
        }

        waterAttractionTextureCPU.SetPixels(pixelBuffer);
        waterAttractionTextureCPU.Apply();
        Graphics.Blit(waterAttractionTextureCPU, waterAttractionMap);
    }

    private Vector2 TextureToWorld(int x, int y)
    {
        float normX = (float)x / resolution.x;
        float normY = (float)y / resolution.y;
        return new Vector2(
            worldBounds.x + normX * worldBounds.width,
            worldBounds.y + normY * worldBounds.height
        );
    }

    private void OnDestroy()
    {
        if (waterAttractionMap != null) waterAttractionMap.Release();
        if (waterAttractionTextureCPU != null) Destroy(waterAttractionTextureCPU);
    }
}
