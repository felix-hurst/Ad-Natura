using System.Collections.Generic;
using UnityEngine;

// Manages slime mold simulation with dual attraction sources
[RequireComponent(typeof(Slime))]
public class SlimeMoldManager : MonoBehaviour
{
    [Range(0f, 100f)]
    public float waterAttractionStrength = 10f;

    [Range(0f, 100f)]
    public float lightAttractionStrength = 10f;

    public bool autoFindSources = true;
    public List<WaterSource> manualWaterSources = new List<WaterSource>();
    public List<LightSource> manualLightSources = new List<LightSource>();
    public Vector2Int attractionMapResolution = new Vector2Int(512, 512);

    [Range(0.01f, 1f)]
    public float attractionMapUpdateRate = 0.1f;

    public Camera mainCamera;
    public Rect worldBounds = new Rect(-50, -50, 100, 100);

    private Slime slimeSimulation;
    private RenderTexture waterAttractionMap;
    private RenderTexture lightAttractionMap;
    private Texture2D waterAttractionTextureCPU;
    private Texture2D lightAttractionTextureCPU;
    private List<WaterSource> activeWaterSources = new List<WaterSource>();
    private List<LightSource> activeLightSources = new List<LightSource>();
    private float timeSinceLastUpdate = 0f;

    private void Start()
    {
        slimeSimulation = GetComponent<Slime>();
        if (mainCamera == null) mainCamera = Camera.main;

        CreateAttractionMaps();
        UpdateSourcesLists();
        UpdateAttractionMaps();
    }

    private void Update()
    {
        timeSinceLastUpdate += Time.deltaTime;

        if (timeSinceLastUpdate >= attractionMapUpdateRate)
        {
            UpdateSourcesLists();
            UpdateAttractionMaps();
            timeSinceLastUpdate = 0f;
        }
    }

    private void FixedUpdate()
    {
        if (slimeSimulation != null && slimeSimulation.shader != null && waterAttractionMap != null && lightAttractionMap != null)
        {
            int kernel = slimeSimulation.shader.FindKernel("Update");
            slimeSimulation.shader.SetTexture(kernel, "WaterAttractionMap", waterAttractionMap);
            slimeSimulation.shader.SetTexture(kernel, "LightAttractionMap", lightAttractionMap);
            slimeSimulation.shader.SetFloat("waterAttractionStrength", waterAttractionStrength);
            slimeSimulation.shader.SetFloat("lightAttractionStrength", lightAttractionStrength);
        }
    }

    private void CreateAttractionMaps()
    {
        if (waterAttractionMap != null) waterAttractionMap.Release();
        if (lightAttractionMap != null) lightAttractionMap.Release();

        waterAttractionMap = new RenderTexture(attractionMapResolution.x, attractionMapResolution.y, 0, RenderTextureFormat.RFloat);
        waterAttractionMap.enableRandomWrite = true;
        waterAttractionMap.filterMode = FilterMode.Bilinear;
        waterAttractionMap.Create();

        lightAttractionMap = new RenderTexture(attractionMapResolution.x, attractionMapResolution.y, 0, RenderTextureFormat.RFloat);
        lightAttractionMap.enableRandomWrite = true;
        lightAttractionMap.filterMode = FilterMode.Bilinear;
        lightAttractionMap.Create();

        waterAttractionTextureCPU = new Texture2D(attractionMapResolution.x, attractionMapResolution.y, TextureFormat.RFloat, false);
        lightAttractionTextureCPU = new Texture2D(attractionMapResolution.x, attractionMapResolution.y, TextureFormat.RFloat, false);
    }

    private void UpdateSourcesLists()
    {
        activeWaterSources.Clear();
        activeLightSources.Clear();

        if (autoFindSources)
        {
            activeWaterSources.AddRange(FindObjectsOfType<WaterSource>());
            activeLightSources.AddRange(FindObjectsOfType<LightSource>());
        }
        else
        {
            activeWaterSources.AddRange(manualWaterSources);
            activeLightSources.AddRange(manualLightSources);
        }

        activeWaterSources.RemoveAll(source => source == null || !source.isActive);
        activeLightSources.RemoveAll(source => source == null || !source.isActive);
    }

    private void UpdateAttractionMaps()
    {
        if (waterAttractionTextureCPU == null || lightAttractionTextureCPU == null) return;

        int width = attractionMapResolution.x;
        int height = attractionMapResolution.y;
        Color[] waterPixels = new Color[width * height];
        Color[] lightPixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 worldPos = TextureToWorld(new Vector2(x, y));
                float waterAttraction = 0f;
                float lightAttraction = 0f;

                foreach (WaterSource source in activeWaterSources)
                {
                    if (source != null && source.isActive)
                    {
                        waterAttraction += source.GetAttractionAt(worldPos);
                    }
                }

                foreach (LightSource source in activeLightSources)
                {
                    if (source != null && source.isActive)
                    {
                        lightAttraction += source.GetAttractionAt(worldPos);
                    }
                }

                waterAttraction = Mathf.Clamp01(waterAttraction);
                lightAttraction = Mathf.Clamp01(lightAttraction);

                waterPixels[y * width + x] = new Color(waterAttraction, 0, 0, 0);
                lightPixels[y * width + x] = new Color(lightAttraction, 0, 0, 0);
            }
        }

        waterAttractionTextureCPU.SetPixels(waterPixels);
        waterAttractionTextureCPU.Apply();
        Graphics.Blit(waterAttractionTextureCPU, waterAttractionMap);

        lightAttractionTextureCPU.SetPixels(lightPixels);
        lightAttractionTextureCPU.Apply();
        Graphics.Blit(lightAttractionTextureCPU, lightAttractionMap);
    }

    private Vector2 TextureToWorld(Vector2 texturePos)
    {
        float normalizedX = texturePos.x / attractionMapResolution.x;
        float normalizedY = texturePos.y / attractionMapResolution.y;
        float worldX = worldBounds.x + normalizedX * worldBounds.width;
        float worldY = worldBounds.y + normalizedY * worldBounds.height;
        return new Vector2(worldX, worldY);
    }

    private Vector2 WorldToTexture(Vector2 worldPos)
    {
        float normalizedX = (worldPos.x - worldBounds.x) / worldBounds.width;
        float normalizedY = (worldPos.y - worldBounds.y) / worldBounds.height;
        float textureX = normalizedX * attractionMapResolution.x;
        float textureY = normalizedY * attractionMapResolution.y;
        return new Vector2(textureX, textureY);
    }

    public void ForceUpdateAttractionMaps()
    {
        UpdateSourcesLists();
        UpdateAttractionMaps();
    }

    private void OnDestroy()
    {
        if (waterAttractionMap != null) waterAttractionMap.Release();
        if (lightAttractionMap != null) lightAttractionMap.Release();
        if (waterAttractionTextureCPU != null) Destroy(waterAttractionTextureCPU);
        if (lightAttractionTextureCPU != null) Destroy(lightAttractionTextureCPU);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(worldBounds.center, new Vector3(worldBounds.width, worldBounds.height, 0));
    }
}
