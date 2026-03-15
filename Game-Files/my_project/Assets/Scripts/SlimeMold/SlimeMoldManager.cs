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
    [SerializeField] private float waterAttractionStrength = 40f;

    [Header("Source Detection")]
    [Tooltip("Automatically find all WaterSource components in scene")]
    [SerializeField] private bool autoFindSources = true;
    [Tooltip("How often to scan for new water sources (seconds)")]
    [Range(0.1f, 2f)]
    [SerializeField] private float sourceRefreshInterval = 0.5f;
    [Tooltip("Manual list of water sources (used when autoFindSources is false)")]
    [SerializeField] private List<WaterSource> manualWaterSources = new List<WaterSource>();

    [Header("Light Aversion")]
    public bool enableLightAversion = true;
    private List<LightSource> cachedLightSources = new List<LightSource>();

    [Header("Performance")]
    [Tooltip("How often to update attraction map (seconds). Lower = more responsive")]
    [Range(0.016f, 0.5f)]
    [SerializeField] private float mapUpdateInterval = 0.066f;

    [Header("Liquid Simulation Integration")]
    [Tooltip("Reference to CellularLiquidSimulation (auto-finds if not set)")]
    [SerializeField] private CellularLiquidSimulation liquidSimulation;
    [Tooltip("Enable attraction from liquid simulation water")]
    [SerializeField] private bool useLiquidSimulation = true;
    [Tooltip("Auto-sync slime bounds to match liquid simulation area")]
    [SerializeField] private bool autoSyncBounds = true;
    [Tooltip("Strength multiplier for liquid simulation water attraction")]
    [Range(0f, 2f)]
    [SerializeField] private float liquidAttractionMultiplier = 1f;
    [Tooltip("Minimum water amount to create attraction (filters noise)")]
    [Range(0f, 0.5f)]
    [SerializeField] private float minWaterThreshold = 0.1f;

    [Header("Hazard (Calamity Objects)")]
    [Tooltip("Wither trail pixels that touch objects tagged 'Calamity'")]
    public bool enableCalamityHazard = true;
    [Tooltip("How often to refresh the hazard map (seconds)")]
    [Range(0.1f, 2f)]
    public float hazardRefreshInterval = 0.25f;

    private Texture2D hazardTextureCPU;
    private Color[] hazardPixelBuffer;
    private float[] hazardRawBuffer;
    private float[]  stainBuffer;          // CPU-baked stain falloff, uploaded to HazardMap G channel
    private float timeSinceHazardRefresh;
    private Collider2D[] cachedCalamityColliders;

    private Slime slimeSimulation;
    private SlimeDecomposer slimeDecomposer;
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

        if (liquidSimulation == null && useLiquidSimulation)
        {
            liquidSimulation = FindAnyObjectByType<CellularLiquidSimulation>();
        }

        if (autoSyncBounds && liquidSimulation != null)
        {
            Rect liquidBounds = liquidSimulation.GetWorldBounds();
            slimeSimulation.SetWorldBounds(liquidBounds);
            worldBounds = liquidBounds;
        }

        resolution = slimeSimulation.GetSimulationResolution();
        slimeDecomposer = GetComponent<SlimeDecomposer>();

        CreateAttractionMap();
        RefreshSources();
        UpdateAttractionMap();
        CreateHazardMap();
        RefreshCalamityColliders();
        UpdateHazardMap();
    }

    private void Update()
    {
        // Re-read bounds every frame — SporeDispersal sets them one frame after
        // spawn via ApplyBoundsNextFrame so must stay in sync.
        worldBounds = slimeSimulation.GetWorldBounds();

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

        if (enableCalamityHazard)
        {
            timeSinceHazardRefresh += Time.deltaTime;
            if (timeSinceHazardRefresh >= hazardRefreshInterval)
            {
                RefreshCalamityColliders();
                UpdateHazardMap();
                timeSinceHazardRefresh = 0f;
            }

            if (slimeSimulation != null && hazardTextureCPU != null)
            {
                slimeSimulation.SetHazardMap(hazardTextureCPU);
            }
        }
    }

    private void CreateAttractionMap()
    {
        waterAttractionMap = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGBFloat);
        waterAttractionMap.enableRandomWrite = true;
        waterAttractionMap.filterMode = FilterMode.Bilinear;
        waterAttractionMap.Create();

        waterAttractionTextureCPU = new Texture2D(resolution.x, resolution.y, TextureFormat.RGBAFloat, false);
        pixelBuffer = new Color[resolution.x * resolution.y];
    }

    private void RefreshSources()
    {
        cachedWaterSources.Clear();
        if (autoFindSources)
            cachedWaterSources.AddRange(FindObjectsByType<WaterSource>(FindObjectsSortMode.None));
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

        for (int i = 0; i < pixelBuffer.Length; i++)
            pixelBuffer[i] = Color.black;

        foreach (WaterSource source in cachedWaterSources)
        {
            if (source == null || !source.isActive) continue;

            Vector2 sourcePos = source.GetPosition();
            float radius = source.attractionRadius;
            float strength = source.attractionStrength;

            bool outsideLeft = sourcePos.x < worldBounds.xMin;
            bool outsideRight = sourcePos.x > worldBounds.xMax;
            bool outsideBottom = sourcePos.y < worldBounds.yMin;
            bool outsideTop = sourcePos.y > worldBounds.yMax;
            bool isOutside = outsideLeft || outsideRight || outsideBottom || outsideTop;

            if (isOutside)
            {
                float distToEdge = 0f;
                if (outsideTop) distToEdge = sourcePos.y - worldBounds.yMax;
                else if (outsideBottom) distToEdge = worldBounds.yMin - sourcePos.y;
                else if (outsideLeft) distToEdge = worldBounds.xMin - sourcePos.x;
                else if (outsideRight) distToEdge = sourcePos.x - worldBounds.xMax;

                float maxSenseRange = radius * 2f;
                if (distToEdge > maxSenseRange) continue;

                float edgeAttraction = strength * (1f - distToEdge / maxSenseRange);

                float normSourceX = (sourcePos.x - worldBounds.x) / worldBounds.width;
                float normSourceY = (sourcePos.y - worldBounds.y) / worldBounds.height;
                int sourcePosX = Mathf.Clamp(Mathf.RoundToInt(normSourceX * w), 0, w - 1);
                int sourcePosY = Mathf.Clamp(Mathf.RoundToInt(normSourceY * h), 0, h - 1);

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
                            pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + edgeAttraction * depthFalloff * horizFalloff);
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
                            pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + edgeAttraction * depthFalloff * horizFalloff);
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
                            pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + edgeAttraction * depthFalloff * vertFalloff);
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
                            pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + edgeAttraction * depthFalloff * vertFalloff);
                        }
                    }
                }
            }
            else
            {
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
                            pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + attraction);
                        }
                    }
                }
            }
        }

        if (enableLightAversion)
        {
            cachedLightSources.Clear();
            cachedLightSources.AddRange(FindObjectsByType<LightSource>(FindObjectsSortMode.None));

            foreach (LightSource light in cachedLightSources)
            {
                if (!light.isActive) continue;
                DrawLocalizedRepulsion(light, w, h);
            }
        }

        if (useLiquidSimulation && liquidSimulation != null)
        {
            SampleLiquidSimulation(w, h);
        }

        if (slimeDecomposer != null)
        {
            slimeDecomposer.GetDecomposableAttractions(pixelBuffer, w, h, worldBounds);
        }

        waterAttractionTextureCPU.SetPixels(pixelBuffer);
        waterAttractionTextureCPU.Apply();
        Graphics.Blit(waterAttractionTextureCPU, waterAttractionMap);
    }

    private void DrawLocalizedRepulsion(LightSource light, int w, int h)
    {
        Vector2 center = light.GetPosition();
        float strength = light.repulsionStrength;
        float margin = light.ghostPointOffset;

        bool isCircle = light.shape == LightSource.LightShape.Circle;

        float rX = isCircle ? light.fearRadius : (light.rectSize.x * 0.5f);
        float rY = isCircle ? light.fearRadius : (light.rectSize.y * 0.5f);

        int minX = WorldToTextureX(center.x - rX - margin);
        int maxX = WorldToTextureX(center.x + rX + margin);
        int minY = WorldToTextureY(center.y - rY - margin);
        int maxY = WorldToTextureY(center.y + rY + margin);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 worldPos = TextureToWorld(x, y);
                float normDist;

                if (isCircle)
                    normDist = Mathf.Clamp01(Vector2.Distance(worldPos, center) / (light.fearRadius + margin));
                else
                {
                    float dx = Mathf.Abs(worldPos.x - center.x) / (rX + margin);
                    float dy = Mathf.Abs(worldPos.y - center.y) / (rY + margin);
                    normDist = Mathf.Clamp01(Mathf.Max(dx, dy));
                }

                float hill = Mathf.Pow(1.0f - normDist, 3);
                int idx = y * w + x;
                pixelBuffer[idx].r = Mathf.Max(pixelBuffer[idx].r, hill * strength);
            }
        }
    }

    private int WorldToTextureX(float worldX)
    {
        Rect bounds = slimeSimulation.GetWorldBounds();
        Vector2Int res = slimeSimulation.GetSimulationResolution();
        float t = (worldX - bounds.xMin) / bounds.width;
        return Mathf.Clamp(Mathf.FloorToInt(t * res.x), 0, res.x - 1);
    }

    private int WorldToTextureY(float worldY)
    {
        Rect bounds = slimeSimulation.GetWorldBounds();
        Vector2Int res = slimeSimulation.GetSimulationResolution();
        float t = (worldY - bounds.yMin) / bounds.height;
        return Mathf.Clamp(Mathf.FloorToInt(t * res.y), 0, res.y - 1);
    }

    private void SampleLiquidSimulation(int w, int h)
    {
        if (liquidSimulation.TotalWaterCells == 0) return;
        Rect liquidBounds = liquidSimulation.GetWorldBounds();
        SampleLiquidOverlap(w, h, liquidBounds);
        SampleLiquidEdgeAttraction(w, h, liquidBounds);
    }

    private void SampleLiquidOverlap(int w, int h, Rect liquidBounds)
    {
        float overlapMinX = Mathf.Max(worldBounds.xMin, liquidBounds.xMin);
        float overlapMaxX = Mathf.Min(worldBounds.xMax, liquidBounds.xMax);
        float overlapMinY = Mathf.Max(worldBounds.yMin, liquidBounds.yMin);
        float overlapMaxY = Mathf.Min(worldBounds.yMax, liquidBounds.yMax);

        if (overlapMinX >= overlapMaxX || overlapMinY >= overlapMaxY) return;

        int startX = Mathf.Max(0, Mathf.FloorToInt((overlapMinX - worldBounds.x) / worldBounds.width * w));
        int endX = Mathf.Min(w, Mathf.CeilToInt ((overlapMaxX - worldBounds.x) / worldBounds.width * w));
        int startY = Mathf.Max(0, Mathf.FloorToInt((overlapMinY - worldBounds.y) / worldBounds.height * h));
        int endY = Mathf.Min(h, Mathf.CeilToInt ((overlapMaxY - worldBounds.y) / worldBounds.height * h));

        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                Vector2 worldPos = TextureToWorld(x, y);
                Vector2Int gridPos = liquidSimulation.WorldToGrid(worldPos);

                if (liquidSimulation.IsValidCell(gridPos.x, gridPos.y))
                {
                    float waterAmount = liquidSimulation.GetWater(gridPos.x, gridPos.y);
                    if (waterAmount > minWaterThreshold)
                    {
                        float attraction = Mathf.Clamp01(waterAmount) * liquidAttractionMultiplier;
                        int idx = y * w + x;
                        pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + attraction);
                    }
                }
            }
        }
    }

    private void SampleLiquidEdgeAttraction(int w, int h, Rect liquidBounds)
    {
        float senseRange = Mathf.Min(worldBounds.width, worldBounds.height) * 0.5f;
        int gradientDepth = Mathf.Max(4, h / 8);

        if (liquidBounds.yMax > worldBounds.yMax)
        {
            float scanYMin = worldBounds.yMax;
            float scanYMax = Mathf.Min(liquidBounds.yMax, worldBounds.yMax + senseRange);

            for (int texX = 0; texX < w; texX++)
            {
                float worldX = worldBounds.x + (texX / (float)w) * worldBounds.width;
                float totalWater = 0f;

                for (float scanY = scanYMin; scanY < scanYMax; scanY += liquidSimulation.CellSize)
                {
                    Vector2Int gridPos = liquidSimulation.WorldToGrid(new Vector2(worldX, scanY));
                    if (liquidSimulation.IsValidCell(gridPos.x, gridPos.y))
                    {
                        float water = liquidSimulation.GetWater(gridPos.x, gridPos.y);
                        if (water > minWaterThreshold)
                        {
                            float distFactor = 1f - (scanY - worldBounds.yMax) / senseRange;
                            totalWater += water * distFactor;
                        }
                    }
                }

                if (totalWater > 0)
                {
                    float attraction = Mathf.Clamp01(totalWater) * liquidAttractionMultiplier;
                    for (int depth = 0; depth < gradientDepth; depth++)
                    {
                        int texY = h - 1 - depth;
                        if (texY < 0) break;
                        float depthFalloff = 1f - (float)depth / gradientDepth;
                        int idx = texY * w + texX;
                        pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + attraction * depthFalloff);
                    }
                }
            }
        }

        if (liquidBounds.yMin < worldBounds.yMin)
        {
            float scanYMin = Mathf.Max(liquidBounds.yMin, worldBounds.yMin - senseRange);
            float scanYMax = worldBounds.yMin;

            for (int texX = 0; texX < w; texX++)
            {
                float worldX = worldBounds.x + (texX / (float)w) * worldBounds.width;
                float totalWater = 0f;

                for (float scanY = scanYMin; scanY < scanYMax; scanY += liquidSimulation.CellSize)
                {
                    Vector2Int gridPos = liquidSimulation.WorldToGrid(new Vector2(worldX, scanY));
                    if (liquidSimulation.IsValidCell(gridPos.x, gridPos.y))
                    {
                        float water = liquidSimulation.GetWater(gridPos.x, gridPos.y);
                        if (water > minWaterThreshold)
                        {
                            float distFactor = 1f - (worldBounds.yMin - scanY) / senseRange;
                            totalWater += water * distFactor;
                        }
                    }
                }

                if (totalWater > 0)
                {
                    float attraction = Mathf.Clamp01(totalWater) * liquidAttractionMultiplier;
                    for (int depth = 0; depth < gradientDepth; depth++)
                    {
                        int texY = depth;
                        if (texY >= h) break;
                        float depthFalloff = 1f - (float)depth / gradientDepth;
                        int idx = texY * w + texX;
                        pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + attraction * depthFalloff);
                    }
                }
            }
        }

        if (liquidBounds.xMin < worldBounds.xMin)
        {
            float scanXMin = Mathf.Max(liquidBounds.xMin, worldBounds.xMin - senseRange);
            float scanXMax = worldBounds.xMin;
            int horizGradient = Mathf.Max(4, w / 8);

            for (int texY = 0; texY < h; texY++)
            {
                float worldY = worldBounds.y + (texY / (float)h) * worldBounds.height;
                float totalWater = 0f;

                for (float scanX = scanXMin; scanX < scanXMax; scanX += liquidSimulation.CellSize)
                {
                    Vector2Int gridPos = liquidSimulation.WorldToGrid(new Vector2(scanX, worldY));
                    if (liquidSimulation.IsValidCell(gridPos.x, gridPos.y))
                    {
                        float water = liquidSimulation.GetWater(gridPos.x, gridPos.y);
                        if (water > minWaterThreshold)
                        {
                            float distFactor = 1f - (worldBounds.xMin - scanX) / senseRange;
                            totalWater += water * distFactor;
                        }
                    }
                }

                if (totalWater > 0)
                {
                    float attraction = Mathf.Clamp01(totalWater) * liquidAttractionMultiplier;
                    for (int depth = 0; depth < horizGradient; depth++)
                    {
                        int texX = depth;
                        if (texX >= w) break;
                        float depthFalloff = 1f - (float)depth / horizGradient;
                        int idx = texY * w + texX;
                        pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + attraction * depthFalloff);
                    }
                }
            }
        }

        if (liquidBounds.xMax > worldBounds.xMax)
        {
            float scanXMin = worldBounds.xMax;
            float scanXMax = Mathf.Min(liquidBounds.xMax, worldBounds.xMax + senseRange);
            int horizGradient = Mathf.Max(4, w / 8);

            for (int texY = 0; texY < h; texY++)
            {
                float worldY = worldBounds.y + (texY / (float)h) * worldBounds.height;
                float totalWater = 0f;

                for (float scanX = scanXMin; scanX < scanXMax; scanX += liquidSimulation.CellSize)
                {
                    Vector2Int gridPos = liquidSimulation.WorldToGrid(new Vector2(scanX, worldY));
                    if (liquidSimulation.IsValidCell(gridPos.x, gridPos.y))
                    {
                        float water = liquidSimulation.GetWater(gridPos.x, gridPos.y);
                        if (water > minWaterThreshold)
                        {
                            float distFactor = 1f - (scanX - worldBounds.xMax) / senseRange;
                            totalWater += water * distFactor;
                        }
                    }
                }

                if (totalWater > 0)
                {
                    float attraction = Mathf.Clamp01(totalWater) * liquidAttractionMultiplier;
                    for (int depth = 0; depth < horizGradient; depth++)
                    {
                        int texX = w - 1 - depth;
                        if (texX < 0) break;
                        float depthFalloff = 1f - (float)depth / horizGradient;
                        int idx = texY * w + texX;
                        pixelBuffer[idx].g = Mathf.Min(1f, pixelBuffer[idx].g + attraction * depthFalloff);
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Hazard map
    // Two channels:
    //   R = solid hazard (binary 0/1): rasterised from Calamity colliders
    //   G = spreading stain falloff (0-1): CPU-baked quadratic gradient, radius 12px
    //       Replaces the old 25x25 + 13x13 per-pixel GPU neighbor search in Postprocess
    //       which was ~1,225 texture reads per pixel. CPU version only iterates outward
    //       from actual hazard pixels so cost is O(hazardPixels × stainRadius²) instead
    //       of O(allPixels × stainRadius²).
    // -------------------------------------------------------------------------

    private void CreateHazardMap()
    {
        int w = resolution.x;
        int h = resolution.y;

        // RGFloat: R = solid hazard (binary), G = CPU-baked stain falloff
        hazardTextureCPU       = new Texture2D(w, h, TextureFormat.RGFloat, false);
        hazardTextureCPU.filterMode = FilterMode.Point;
        hazardPixelBuffer = new Color[w * h];
        hazardRawBuffer = new float[w * h];
        stainBuffer = new float[w * h];
    }

    private void RefreshCalamityColliders()
    {
        var colliders = new List<Collider2D>();

        GameObject[] calamityObjects = null;
        try { calamityObjects = GameObject.FindGameObjectsWithTag("Calamity"); }
        catch (UnityException)
        {
            Debug.LogError("SlimeMoldManager: Tag 'Calamity' is not defined in Project Settings > Tags & Layers. Add it there first.");
            enableCalamityHazard = false;
            cachedCalamityColliders = new Collider2D[0];
            return;
        }

        foreach (GameObject obj in calamityObjects)
            foreach (Collider2D col in obj.GetComponents<Collider2D>())
                if (col.enabled) colliders.Add(col);

        cachedCalamityColliders = colliders.ToArray();
    }

    private void UpdateHazardMap()
    {
        if (hazardTextureCPU == null || hazardRawBuffer == null) return;

        int w = resolution.x;
        int h = resolution.y;

        for (int i = 0; i < hazardRawBuffer.Length; i++)
            hazardRawBuffer[i] = 0f;

        if (cachedCalamityColliders == null || cachedCalamityColliders.Length == 0)
        {
            // Still need to clear stain and write, so GPU sees zeroes
            ComputeStainFalloff(w, h);
            WriteHazardToGPU(w, h);
            return;
        }


        foreach (Collider2D col in cachedCalamityColliders)
        {
            if (col == null || !col.enabled) continue;

            Bounds colBounds = col.bounds;

            float minX = Mathf.Max(colBounds.min.x, worldBounds.xMin);
            float maxX = Mathf.Min(colBounds.max.x, worldBounds.xMax);
            float minY = Mathf.Max(colBounds.min.y, worldBounds.yMin);
            float maxY = Mathf.Min(colBounds.max.y, worldBounds.yMax);

            if (minX >= maxX || minY >= maxY) continue;

            int pxMinX = Mathf.Max(0, Mathf.FloorToInt((minX - worldBounds.x) / worldBounds.width * w));
            int pxMaxX = Mathf.Min(w - 1, Mathf.CeilToInt((maxX - worldBounds.x) / worldBounds.width * w));
            int pxMinY = Mathf.Max(0, Mathf.FloorToInt((minY - worldBounds.y) / worldBounds.height * h));
            int pxMaxY = Mathf.Min(h - 1, Mathf.CeilToInt((maxY - worldBounds.y) / worldBounds.height * h));

            for (int py = pxMinY; py <= pxMaxY; py++)
                for (int px = pxMinX; px <= pxMaxX; px++)
                    if (col.OverlapPoint(TextureToWorld(px, py)))
                        hazardRawBuffer[py * w + px] = 1f;
        }

        ComputeStainFalloff(w, h);
        WriteHazardToGPU(w, h);
    }

    /// <summary>
    /// For every solid hazard pixel, paints a quadratic falloff into stainBuffer
    /// within stainRadius pixels. Runs in O(hazardPixels × stainRadius²) — cheap
    /// for sparse hazard objects — rather than the old GPU approach of
    /// O(allPixels × searchRadius²) (~1,225 reads per pixel in Postprocess).
    /// Result is uploaded to HazardMap G channel and read in the Postprocess kernel.
    /// </summary>
    private void ComputeStainFalloff(int w, int h)
    {
        const int stainRadius = 12; // must match the old GPU decayRadius value

        System.Array.Clear(stainBuffer, 0, stainBuffer.Length);

        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                if (hazardRawBuffer[py * w + px] < 0.5f) continue;

                // This pixel is solid hazard — paint stain outward
                int yMin = Mathf.Max(0,     py - stainRadius);
                int yMax = Mathf.Min(h - 1, py + stainRadius);
                int xMin = Mathf.Max(0,     px - stainRadius);
                int xMax = Mathf.Min(w - 1, px + stainRadius);

                for (int ny = yMin; ny <= yMax; ny++)
                {
                    int dy = ny - py;
                    for (int nx = xMin; nx <= xMax; nx++)
                    {
                        int   dx      = nx - px;
                        float dist    = Mathf.Sqrt(dx * dx + dy * dy);
                        if (dist >= stainRadius) continue;

                        float t       = 1f - (dist / stainRadius);
                        float falloff = t * t; // quadratic: strong near source, soft at edge

                        int idx = ny * w + nx;
                        if (falloff > stainBuffer[idx])
                            stainBuffer[idx] = falloff; // keep max so overlapping hazards accumulate correctly
                    }
                }
            }
        }
    }

    private void WriteHazardToGPU(int w, int h)
    {
        for (int i = 0; i < hazardRawBuffer.Length; i++)
        {
            hazardPixelBuffer[i].r = hazardRawBuffer[i]; // solid hazard (binary)
            hazardPixelBuffer[i].g = stainBuffer[i];     // spreading stain falloff
        }

        hazardTextureCPU.SetPixels(hazardPixelBuffer);
        hazardTextureCPU.Apply();
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
        if (hazardTextureCPU != null) Destroy(hazardTextureCPU);
    }

    void OnDrawGizmosSelected()
    {
        if (waterAttractionTextureCPU == null) return;

        Gizmos.color = new Color(1f, 1f, 1f, 0.2f);
        Gizmos.DrawGUITexture(
            new Rect(worldBounds.x, worldBounds.y, worldBounds.width, worldBounds.height),
            waterAttractionTextureCPU
        );
    }
}