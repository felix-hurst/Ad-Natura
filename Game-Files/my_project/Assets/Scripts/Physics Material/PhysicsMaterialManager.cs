using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Data structure for physics material properties
/// </summary>
[System.Serializable]
public class PhysicsMaterialData
{
    public string materialName;
    [Range(0f, 1f)] public float friction = 0.4f;
    [Range(0f, 1f)] public float bounciness = 0f;
    
    public PhysicsMaterialData(string name, float frict, float bounce)
    {
        materialName = name;
        friction = frict;
        bounciness = bounce;
    }
}

/// <summary>
/// List container for serialization
/// </summary>
[System.Serializable]
public class PhysicsMaterialList
{
    public List<PhysicsMaterialData> materials = new List<PhysicsMaterialData>();
}

/// <summary>
/// Manages physics materials for different object types
/// Creates and applies PhysicsMaterial2D based on material tags
/// </summary>
public class PhysicsMaterialManager : MonoBehaviour
{
    [Header("Physics Material Settings")]
    [SerializeField] private TextAsset physicsMaterialJson;
    [SerializeField] private PhysicsMaterialData defaultMaterial = new PhysicsMaterialData("Default", 0.4f, 0f);
    
    private Dictionary<string, PhysicsMaterialData> materialDataDictionary = new Dictionary<string, PhysicsMaterialData>();
    private Dictionary<string, PhysicsMaterial2D> physicsMaterialCache = new Dictionary<string, PhysicsMaterial2D>();
    
    void Awake()
    {
        LoadMaterialData();
    }
    
    /// <summary>
    /// Loads physics material data from JSON
    /// </summary>
    void LoadMaterialData()
    {
        if (physicsMaterialJson != null)
        {
            try
            {
                PhysicsMaterialList materialList = JsonUtility.FromJson<PhysicsMaterialList>(physicsMaterialJson.text);
                
                foreach (PhysicsMaterialData data in materialList.materials)
                {
                    materialDataDictionary[data.materialName] = data;
                    Debug.Log($"Loaded physics material: {data.materialName} (Friction: {data.friction}, Bounciness: {data.bounciness})");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load physics material data: {e.Message}");
            }
        }
        
        // Add default material if not present
        if (!materialDataDictionary.ContainsKey("Default"))
        {
            materialDataDictionary["Default"] = defaultMaterial;
        }
    }
    
    /// <summary>
    /// Gets the physics material data for a given material name
    /// </summary>
    public PhysicsMaterialData GetMaterialData(string materialName)
    {
        if (materialDataDictionary.ContainsKey(materialName))
        {
            return materialDataDictionary[materialName];
        }
        
        Debug.LogWarning($"No physics material data found for '{materialName}', using default");
        return defaultMaterial;
    }
    
    /// <summary>
    /// Gets or creates a PhysicsMaterial2D for the given material name
    /// </summary>
    public PhysicsMaterial2D GetPhysicsMaterial(string materialName)
    {
        // Check cache first
        if (physicsMaterialCache.ContainsKey(materialName))
        {
            return physicsMaterialCache[materialName];
        }
        
        // Get material data
        PhysicsMaterialData data = GetMaterialData(materialName);
        
        // Create new PhysicsMaterial2D
        PhysicsMaterial2D physicsMaterial = new PhysicsMaterial2D($"PM_{materialName}");
        physicsMaterial.friction = data.friction;
        physicsMaterial.bounciness = data.bounciness;
        
        // Cache it
        physicsMaterialCache[materialName] = physicsMaterial;
        
        Debug.Log($"Created PhysicsMaterial2D for {materialName}: Friction={data.friction}, Bounciness={data.bounciness}");
        
        return physicsMaterial;
    }
    
    /// <summary>
    /// Applies physics material to a game object's colliders based on its tag
    /// </summary>
    public void ApplyPhysicsMaterial(GameObject obj)
    {
        string materialName = GetMaterialNameFromObject(obj);
        PhysicsMaterial2D physicsMaterial = GetPhysicsMaterial(materialName);
        
        // Apply to all 2D colliders on the object
        Collider2D[] colliders = obj.GetComponents<Collider2D>();
        foreach (Collider2D collider in colliders)
        {
            collider.sharedMaterial = physicsMaterial;
        }
        
        Debug.Log($"Applied physics material '{materialName}' to {obj.name}");
    }
    
    /// <summary>
    /// Gets the material name from an object (tag or name)
    /// </summary>
    string GetMaterialNameFromObject(GameObject obj)
    {
        string materialName = obj.tag;
        
        if (string.IsNullOrEmpty(materialName) || materialName == "Untagged")
        {
            materialName = obj.name;
        }
        
        return materialName;
    }
    
    /// <summary>
    /// Clears the physics material cache
    /// </summary>
    public void ClearCache()
    {
        physicsMaterialCache.Clear();
        Debug.Log("Physics material cache cleared");
    }
}

/// <summary>
/// Extension methods for easy physics material application
/// </summary>
public static class PhysicsMaterialExtensions
{
    /// <summary>
    /// Applies the appropriate physics material to an object based on its tag
    /// </summary>
    public static void ApplyMaterialPhysics(this GameObject obj)
    {
        PhysicsMaterialManager manager = Object.FindObjectOfType<PhysicsMaterialManager>();
        
        if (manager == null)
        {
            Debug.LogWarning("No PhysicsMaterialManager found in scene!");
            return;
        }
        
        manager.ApplyPhysicsMaterial(obj);
    }
    
    /// <summary>
    /// Gets the friction value for an object's material
    /// </summary>
    public static float GetMaterialFriction(this GameObject obj)
    {
        PhysicsMaterialManager manager = Object.FindObjectOfType<PhysicsMaterialManager>();
        
        if (manager == null)
        {
            return 0.4f; // Default friction
        }
        
        string materialName = obj.tag;
        if (string.IsNullOrEmpty(materialName) || materialName == "Untagged")
        {
            materialName = obj.name;
        }
        
        PhysicsMaterialData data = manager.GetMaterialData(materialName);
        return data.friction;
    }
}