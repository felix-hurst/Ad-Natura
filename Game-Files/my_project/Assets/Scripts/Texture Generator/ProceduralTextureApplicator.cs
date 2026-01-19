using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ProceduralTextureApplicator : MonoBehaviour
{
    [Header("Texture Settings")]
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private bool useMaterialTag = true;
    [SerializeField] private string overrideMaterialName = "";
    
    [Header("Sprite Settings")]
    [SerializeField] private int pixelsPerUnit = 64;
    
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

    public void ApplyTexture()
    {
        if (spriteRenderer == null || textureGenerator == null)
        {
            Debug.LogWarning($"Cannot apply texture to {gameObject.name} - missing components");
            return;
        }

        string materialName = GetMaterialName();
        Texture2D texture = textureGenerator.GetTexture(materialName);
        
        if (texture == null)
        {
            Debug.LogError($"Failed to generate texture for material: {materialName}");
            return;
        }

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit
        );

        spriteRenderer.sprite = sprite;
    }

    string GetMaterialName()
    {
        if (!string.IsNullOrEmpty(overrideMaterialName))
        {
            return overrideMaterialName;
        }
        if (useMaterialTag)
        {
            string tag = gameObject.tag;
            if (tag != "Untagged" && !string.IsNullOrEmpty(tag))
            {
                return tag;
            }
        }
        return gameObject.name;
    }

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
