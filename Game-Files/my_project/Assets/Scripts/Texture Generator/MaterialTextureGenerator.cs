using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class ColorData
{
    public float r = 1f;
    public float g = 1f;
    public float b = 1f;
    public float a = 1f;

    public Color ToColor()
    {
        return new Color(r, g, b, a);
    }
}

[System.Serializable]
public class ClusterDefinition
{
    public ColorData color = new ColorData();
    public int count = 10;
    public int minSize = 2;
    public int maxSize = 5;
    public bool hasShading = true;
}

[System.Serializable]
public class MaterialTextureProfile
{
    public string materialName;
    public int textureSize = 64;
    
    // Base material colors
    public ColorData baseColor = new ColorData();
    public ColorData lightColor = new ColorData();
    public ColorData darkColor = new ColorData();
    
    // Pixel clusters (like different minerals/grains)
    public List<ClusterDefinition> clusters = new List<ClusterDefinition>();
    
    [Range(1f, 64f)] public float noiseScale = 12f;
    [Range(0f, 1f)] public float noiseStrength = 0.2f;
    
    public bool enableShading = true;
    [Range(0f, 1f)] public float shadingStrength = 0.35f;
    
    [Range(0f, 1f)] public float smoothness = 0f;
    public string patternType = "MinecraftSimple";
}

[System.Serializable]
public class MaterialTextureList
{
    public List<MaterialTextureProfile> materials = new List<MaterialTextureProfile>();
}

public class MaterialTextureGenerator : MonoBehaviour
{
    [Header("Texture Settings")]
    [SerializeField] private TextAsset materialTexturesJson;
    [SerializeField] private MaterialTextureProfile defaultProfile = new MaterialTextureProfile();
    
    private Dictionary<string, MaterialTextureProfile> profileDictionary = new Dictionary<string, MaterialTextureProfile>();
    private Dictionary<string, Texture2D> generatedTextures = new Dictionary<string, Texture2D>();
    
    void Awake()
    {
        LoadProfiles();
    }
    
    void LoadProfiles()
    {
        if (materialTexturesJson != null)
        {
            try
            {
                MaterialTextureList profileList = JsonUtility.FromJson<MaterialTextureList>(materialTexturesJson.text);
                
                foreach (MaterialTextureProfile profile in profileList.materials)
                {
                    profileDictionary[profile.materialName] = profile;
                    Debug.Log($"Loaded material texture profile: {profile.materialName} with {profile.clusters.Count} cluster types");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load material texture profiles: {e.Message}");
            }
        }
        
        if (!profileDictionary.ContainsKey("Default"))
        {
            profileDictionary["Default"] = defaultProfile;
        }
    }
    
    public MaterialTextureProfile GetProfile(string materialName)
    {
        if (profileDictionary.ContainsKey(materialName))
        {
            return profileDictionary[materialName];
        }
        
        Debug.LogWarning($"No texture profile found for '{materialName}', using default");
        return defaultProfile;
    }
    
    public Texture2D GetTexture(string materialName)
    {
        if (generatedTextures.ContainsKey(materialName))
        {
            return generatedTextures[materialName];
        }
        
        MaterialTextureProfile profile = GetProfile(materialName);
        Texture2D texture = GenerateMinecraftTexture(profile);
        
        generatedTextures[materialName] = texture;
        
        Debug.Log($"Generated Minecraft-style texture for: {materialName} ({profile.textureSize}x{profile.textureSize})");
        
        return texture;
    }
    
    /// <summary>
    /// Generates a Minecraft-style texture with pixel clusters
    /// </summary>
    Texture2D GenerateMinecraftTexture(MaterialTextureProfile profile)
    {
        int size = profile.textureSize;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Repeat;
        
        Color baseColor = profile.baseColor.ToColor();
        Color lightColor = profile.lightColor.ToColor();
        Color darkColor = profile.darkColor.ToColor();
        
        // Generate base noise
        float[,] noiseMap = GenerateNoiseMap(size, profile.noiseScale);
        
        // Generate lighting map
        float[,] lightingMap = GenerateMinecraftLighting(size, profile.shadingStrength);
        
        // Generate pixel clusters
        List<PixelCluster> clusters = GeneratePixelClusters(size, profile.clusters);
        
        // Create cluster lookup map for fast pixel checking
        int[,] clusterMap = new int[size, size]; // -1 = base, 0+ = cluster index
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                clusterMap[i, j] = -1; // Initialize to base
            }
        }
        
        // Fill cluster map
        for (int clusterIndex = 0; clusterIndex < clusters.Count; clusterIndex++)
        {
            PixelCluster cluster = clusters[clusterIndex];
            foreach (Vector2Int pixel in cluster.pixels)
            {
                if (pixel.x >= 0 && pixel.x < size && pixel.y >= 0 && pixel.y < size)
                {
                    clusterMap[pixel.x, pixel.y] = clusterIndex;
                }
            }
        }
        
        // Composite texture
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color pixelColor;
                bool applyShading = false;
                
                int clusterIndex = clusterMap[x, y];
                
                if (clusterIndex >= 0)
                {
                    // Pixel is part of a cluster
                    PixelCluster cluster = clusters[clusterIndex];
                    pixelColor = cluster.color;
                    applyShading = cluster.hasShading;
                    
                    // Add slight variation within cluster
                    float noise = noiseMap[x, y];
                    float variation = (noise - 0.5f) * 0.1f;
                    pixelColor.r = Mathf.Clamp01(pixelColor.r + variation);
                    pixelColor.g = Mathf.Clamp01(pixelColor.g + variation);
                    pixelColor.b = Mathf.Clamp01(pixelColor.b + variation);
                }
                else
                {
                    // Base material
                    pixelColor = baseColor;
                    applyShading = profile.enableShading;
                    
                    // Apply noise to base
                    float noise = noiseMap[x, y];
                    pixelColor = Color.Lerp(pixelColor, baseColor * 1.1f, noise * profile.noiseStrength);
                }
                
                // Apply Minecraft-style lighting/shading
                if (applyShading)
                {
                    float lighting = lightingMap[x, y];
                    
                    if (clusterIndex >= 0)
                    {
                        // Clusters use their own shading
                        Color clusterLight = pixelColor * 1.3f;
                        clusterLight.a = pixelColor.a;
                        Color clusterDark = pixelColor * 0.7f;
                        clusterDark.a = pixelColor.a;
                        
                        if (lighting > 0.5f)
                        {
                            float lightAmount = (lighting - 0.5f) * 2f;
                            pixelColor = Color.Lerp(pixelColor, clusterLight, lightAmount);
                        }
                        else
                        {
                            float darkAmount = (0.5f - lighting) * 2f;
                            pixelColor = Color.Lerp(pixelColor, clusterDark, darkAmount);
                        }
                    }
                    else
                    {
                        // Base material uses defined light/dark colors
                        if (lighting > 0.5f)
                        {
                            float lightAmount = (lighting - 0.5f) * 2f;
                            pixelColor = Color.Lerp(pixelColor, lightColor, lightAmount);
                        }
                        else
                        {
                            float darkAmount = (0.5f - lighting) * 2f;
                            pixelColor = Color.Lerp(pixelColor, darkColor, darkAmount);
                        }
                    }
                }
                
                texture.SetPixel(x, y, pixelColor);
            }
        }
        
        texture.Apply();
        return texture;
    }
    
    /// <summary>
    /// Generates pixel clusters (groups of connected pixels with same color)
    /// </summary>
    List<PixelCluster> GeneratePixelClusters(int size, List<ClusterDefinition> definitions)
    {
        List<PixelCluster> clusters = new List<PixelCluster>();
        
        foreach (ClusterDefinition def in definitions)
        {
            for (int i = 0; i < def.count; i++)
            {
                PixelCluster cluster = new PixelCluster();
                cluster.color = def.color.ToColor();
                cluster.hasShading = def.hasShading;
                cluster.pixels = new List<Vector2Int>();
                
                // Random starting position
                int startX = Random.Range(0, size);
                int startY = Random.Range(0, size);
                
                // Random cluster size
                int clusterSize = Random.Range(def.minSize, def.maxSize + 1);
                
                // Generate cluster using flood-fill-like algorithm
                Queue<Vector2Int> toProcess = new Queue<Vector2Int>();
                HashSet<Vector2Int> processed = new HashSet<Vector2Int>();
                
                toProcess.Enqueue(new Vector2Int(startX, startY));
                
                while (toProcess.Count > 0 && cluster.pixels.Count < clusterSize * clusterSize)
                {
                    Vector2Int current = toProcess.Dequeue();
                    
                    if (processed.Contains(current)) continue;
                    if (current.x < 0 || current.x >= size || current.y < 0 || current.y >= size) continue;
                    
                    processed.Add(current);
                    cluster.pixels.Add(current);
                    
                    // Randomly add neighbors to create organic shape
                    if (cluster.pixels.Count < clusterSize * clusterSize)
                    {
                        Vector2Int[] neighbors = new Vector2Int[]
                        {
                            new Vector2Int(current.x + 1, current.y),
                            new Vector2Int(current.x - 1, current.y),
                            new Vector2Int(current.x, current.y + 1),
                            new Vector2Int(current.x, current.y - 1),
                        };
                        
                        foreach (Vector2Int neighbor in neighbors)
                        {
                            if (!processed.Contains(neighbor) && Random.value > 0.4f)
                            {
                                toProcess.Enqueue(neighbor);
                            }
                        }
                    }
                }
                
                clusters.Add(cluster);
            }
        }
        
        return clusters;
    }
    
    /// <summary>
    /// Generates Minecraft-style lighting
    /// </summary>
    float[,] GenerateMinecraftLighting(int size, float strength)
    {
        float[,] lightingMap = new float[size, size];
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size;
                float ny = y / (float)size;
                
                // Top-right = light, bottom-left = dark
                float lighting = (nx + ny) / 2f;
                
                lighting = Mathf.Lerp(0.5f, lighting, strength);
                
                // Add noise for variation
                float lightNoise = Mathf.PerlinNoise(x * 0.1f, y * 0.1f) * 0.1f;
                lighting += lightNoise;
                
                lighting = Mathf.Clamp01(lighting);
                lightingMap[x, y] = lighting;
            }
        }
        
        return lightingMap;
    }
    
    /// <summary>
    /// Generates Perlin noise map
    /// </summary>
    float[,] GenerateNoiseMap(int size, float scale)
    {
        float[,] noiseMap = new float[size, size];
        
        float offsetX = Random.Range(0f, 1000f);
        float offsetY = Random.Range(0f, 1000f);
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float sampleX = (x / (float)size * scale) + offsetX;
                float sampleY = (y / (float)size * scale) + offsetY;
                
                float noise = Mathf.PerlinNoise(sampleX, sampleY);
                noiseMap[x, y] = noise;
            }
        }
        
        return noiseMap;
    }
    
    public void ClearCache()
    {
        foreach (var texture in generatedTextures.Values)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
        generatedTextures.Clear();
        Debug.Log("Texture cache cleared");
    }
}

/// <summary>
/// Represents a cluster of pixels with the same base color
/// </summary>
public class PixelCluster
{
    public Color color;
    public bool hasShading;
    public List<Vector2Int> pixels;
}

// Extension methods
public static class MaterialTextureExtensions
{
    public static void ApplyProceduralTexture(GameObject obj)
    {
        MaterialTextureGenerator generator = Object.FindObjectOfType<MaterialTextureGenerator>();
        
        if (generator == null)
        {
            Debug.LogWarning("No MaterialTextureGenerator found in scene!");
            return;
        }
        
        string materialName = obj.tag;
        if (materialName == "Untagged" || string.IsNullOrEmpty(materialName))
        {
            materialName = obj.name;
        }
        
        Texture2D texture = generator.GetTexture(materialName);
        
        SpriteRenderer spriteRenderer = obj.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            MaterialTextureProfile profile = generator.GetProfile(materialName);
            if (profile.baseColor.a < 1.0f)
            {
                spriteRenderer.color = new Color(1f, 1f, 1f, profile.baseColor.a);
            }
            
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width
            );
            
            spriteRenderer.sprite = sprite;
            Debug.Log($"Applied Minecraft-style texture to {obj.name} using material: {materialName}");
        }
        else
        {
            Debug.LogWarning($"No SpriteRenderer found on {obj.name}");
        }
    }
}