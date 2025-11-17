using UnityEngine;

/// <summary>
/// Attach this to any GameObject that should have material-specific physics properties applied
/// based on its tag. Works in conjunction with PhysicsMaterialManager.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ApplyPhysicsMaterial : MonoBehaviour
{
    [Header("Physics Material Settings")]
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private bool useMaterialTag = true; // Use object's tag, otherwise use object name
    [SerializeField] private string overrideMaterialName = ""; // Override the automatic material detection
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
    
    private PhysicsMaterialManager physicsManager;
    
    void Start()
    {
        physicsManager = FindObjectOfType<PhysicsMaterialManager>();
        
        if (physicsManager == null)
        {
            Debug.LogError("PhysicsMaterialManager not found in scene! Please add one.");
            return;
        }
        
        if (applyOnStart)
        {
            ApplyMaterial();
        }
    }
    
    /// <summary>
    /// Applies the physics material to this object's colliders
    /// </summary>
    public void ApplyMaterial()
    {
        if (physicsManager == null)
        {
            physicsManager = FindObjectOfType<PhysicsMaterialManager>();
            
            if (physicsManager == null)
            {
                Debug.LogWarning($"Cannot apply physics material to {gameObject.name} - PhysicsMaterialManager not found");
                return;
            }
        }
        
        // Determine material name
        string materialName = GetMaterialName();
        
        // Get the physics material
        PhysicsMaterial2D physicsMaterial = physicsManager.GetPhysicsMaterial(materialName);
        
        if (physicsMaterial == null)
        {
            Debug.LogError($"Failed to get physics material for: {materialName}");
            return;
        }
        
        // Apply to all 2D colliders
        Collider2D[] colliders = GetComponents<Collider2D>();
        foreach (Collider2D collider in colliders)
        {
            collider.sharedMaterial = physicsMaterial;
        }
        
        if (showDebugInfo)
        {
            PhysicsMaterialData data = physicsManager.GetMaterialData(materialName);
            Debug.Log($"Applied physics material '{materialName}' to {gameObject.name} " +
                     $"(Friction: {data.friction}, Bounciness: {data.bounciness})");
        }
    }
    
    /// <summary>
    /// Gets the material name to use for physics material
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
    /// Reapplies the physics material (useful after collider changes)
    /// </summary>
    [ContextMenu("Reapply Physics Material")]
    public void ReapplyMaterial()
    {
        ApplyMaterial();
    }
    
    /// <summary>
    /// Gets the current friction value for this object
    /// </summary>
    public float GetCurrentFriction()
    {
        if (physicsManager == null)
        {
            physicsManager = FindObjectOfType<PhysicsMaterialManager>();
        }
        
        if (physicsManager != null)
        {
            string materialName = GetMaterialName();
            PhysicsMaterialData data = physicsManager.GetMaterialData(materialName);
            return data.friction;
        }
        
        return 0.4f; // Default
    }
    
    /// <summary>
    /// Gets the current bounciness value for this object
    /// </summary>
    public float GetCurrentBounciness()
    {
        if (physicsManager == null)
        {
            physicsManager = FindObjectOfType<PhysicsMaterialManager>();
        }
        
        if (physicsManager != null)
        {
            string materialName = GetMaterialName();
            PhysicsMaterialData data = physicsManager.GetMaterialData(materialName);
            return data.bounciness;
        }
        
        return 0f; // Default
    }
}