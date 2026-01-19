using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class ApplyPhysicsMaterial : MonoBehaviour
{
    [Header("Physics Material Settings")]
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private bool useMaterialTag = true; 
    [SerializeField] private string overrideMaterialName = "";
    
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
        string materialName = GetMaterialName();

        PhysicsMaterial2D physicsMaterial = physicsManager.GetPhysicsMaterial(materialName);
        
        if (physicsMaterial == null)
        {
            Debug.LogError($"Failed to get physics material for: {materialName}");
            return;
        }

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

    [ContextMenu("Reapply Physics Material")]
    public void ReapplyMaterial()
    {
        ApplyMaterial();
    }

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
        
        return 0.4f;
    }

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
        
        return 0f;
    }
}