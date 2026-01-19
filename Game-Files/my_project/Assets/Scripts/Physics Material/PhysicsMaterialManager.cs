using UnityEngine;
using System.Collections.Generic;

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

[System.Serializable]
public class PhysicsMaterialList
{
    public List<PhysicsMaterialData> materials = new List<PhysicsMaterialData>();
}

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

        if (!materialDataDictionary.ContainsKey("Default"))
        {
            materialDataDictionary["Default"] = defaultMaterial;
        }
    }

    public PhysicsMaterialData GetMaterialData(string materialName)
    {
        if (materialDataDictionary.ContainsKey(materialName))
        {
            return materialDataDictionary[materialName];
        }
        
        Debug.LogWarning($"No physics material data found for '{materialName}', using default");
        return defaultMaterial;
    }

    public PhysicsMaterial2D GetPhysicsMaterial(string materialName)
    {
        if (physicsMaterialCache.ContainsKey(materialName))
        {
            return physicsMaterialCache[materialName];
        }

        PhysicsMaterialData data = GetMaterialData(materialName);

        PhysicsMaterial2D physicsMaterial = new PhysicsMaterial2D($"PM_{materialName}");
        physicsMaterial.friction = data.friction;
        physicsMaterial.bounciness = data.bounciness;

        physicsMaterialCache[materialName] = physicsMaterial;
        
        Debug.Log($"Created PhysicsMaterial2D for {materialName}: Friction={data.friction}, Bounciness={data.bounciness}");
        
        return physicsMaterial;
    }

    public void ApplyPhysicsMaterial(GameObject obj)
    {
        string materialName = GetMaterialNameFromObject(obj);
        PhysicsMaterial2D physicsMaterial = GetPhysicsMaterial(materialName);

        Collider2D[] colliders = obj.GetComponents<Collider2D>();
        foreach (Collider2D collider in colliders)
        {
            collider.sharedMaterial = physicsMaterial;
        }
        
        Debug.Log($"Applied physics material '{materialName}' to {obj.name}");
    }

    string GetMaterialNameFromObject(GameObject obj)
    {
        string materialName = obj.tag;
        
        if (string.IsNullOrEmpty(materialName) || materialName == "Untagged")
        {
            materialName = obj.name;
        }
        
        return materialName;
    }

    public void ClearCache()
    {
        physicsMaterialCache.Clear();
        Debug.Log("Physics material cache cleared");
    }
}

public static class PhysicsMaterialExtensions
{
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

    public static float GetMaterialFriction(this GameObject obj)
    {
        PhysicsMaterialManager manager = Object.FindObjectOfType<PhysicsMaterialManager>();
        
        if (manager == null)
        {
            return 0.4f;
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