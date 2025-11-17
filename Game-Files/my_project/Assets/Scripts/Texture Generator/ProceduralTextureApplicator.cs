using UnityEngine;

/// <summary>
/// Attach this to any GameObject that should have a procedural texture applied based on its tag
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class ProceduralTextureApplicator : MonoBehaviour
{
    [Header("Texture Settings")]
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private bool useMaterialTag = true; // Use object's tag, otherwise use object name
    [SerializeField] private string overrideMaterialName = ""; // Override the automatic material detection
    
    [Header("Sprite Settings")]
    [SerializeField] private int pixelsPerUnit = 64; // How many pixels = 1 Unity unit
    
    private SpriteRenderer spriteRenderer;
    private MaterialTextureGenerator textureGenerator;
    
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        textureGenerator = FindObjectOfType<MaterialTextureGenerator>();
        
        if (textureGenerator == null)
        {
            Debug.LogError("MaterialTextureGenerator not found in scene! Please add one.");
            return;
        }
        
        if (applyOnStart)
        {
            ApplyTexture();
        }
    }
    
    /// <summary>
    /// Applies the procedural texture to this object
    /// </summary>
    public void ApplyTexture()
    {
        if (spriteRenderer == null || textureGenerator == null)
        {
            Debug.LogWarning($"Cannot apply texture to {gameObject.name} - missing components");
            return;
        }
        
        // Determine material name
        string materialName = GetMaterialName();
        
        // Generate texture
        Texture2D texture = textureGenerator.GetTexture(materialName);
        
        if (texture == null)
        {
            Debug.LogError($"Failed to generate texture for material: {materialName}");
            return;
        }
        
        // Create sprite from texture
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit
        );
        
        // Apply to renderer
        spriteRenderer.sprite = sprite;
        
        Debug.Log($"Applied procedural texture to {gameObject.name} using material: {materialName}");
    }
    
    /// <summary>
    /// Gets the material name to use for texture generation
    /// </summary>
    string GetMaterialName()
    {
        // Check for override first
        if (!string.IsNullOrEmpty(overrideMaterialName))
        {
            return overrideMaterialName;
        }
        
        // Use tag or name
        if (useMaterialTag)
        {
            string tag = gameObject.tag;
            if (tag != "Untagged" && !string.IsNullOrEmpty(tag))
            {
                return tag;
            }
        }
        
        // Fallback to object name
        return gameObject.name;
    }
    
    /// <summary>
    /// Regenerates the texture (useful for testing different materials)
    /// </summary>
    [ContextMenu("Regenerate Texture")]
    public void RegenerateTexture()
    {
        if (textureGenerator == null)
        {
            textureGenerator = FindObjectOfType<MaterialTextureGenerator>();
        }
        
        if (textureGenerator != null)
        {
            textureGenerator.ClearCache();
            ApplyTexture();
        }
    }
}
