using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class CutProfile
{
    public string materialName;
    [Range(0f, 1f)] public float softness = 0.5f;  // 0 = very jagged, 1 = smooth curves
    [Range(0f, 1f)] public float strength = 0.3f;  // 0 = no extra cutting, 1 = maxextra cutting
    
    public CutProfile(string name, float soft, float str)
    {
        materialName = name;
        softness = soft;
        strength = str;
    }
}

[System.Serializable]
public class CutProfileList
{
    public List<CutProfile> profiles = new List<CutProfile>();
}

public class CutProfileManager : MonoBehaviour
{
    [Header("Cut Profile Settings")]
    [SerializeField] private TextAsset profileJsonFile;
    [SerializeField] private CutProfile defaultProfile = new CutProfile("Default", 0.5f, 0.3f);
    
    private Dictionary<string, CutProfile> profileDictionary = new Dictionary<string, CutProfile>();
    
    void Awake()
    {
        LoadProfiles();
    }
    
    void LoadProfiles()
    {
        if (profileJsonFile != null)
        {
            try
            {
                CutProfileList profileList = JsonUtility.FromJson<CutProfileList>(profileJsonFile.text);
                
                foreach (CutProfile profile in profileList.profiles)
                {
                    profileDictionary[profile.materialName] = profile;
                    Debug.Log($"Loaded cut profile: {profile.materialName} (Softness: {profile.softness}, Strength: {profile.strength})");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load cut profiles: {e.Message}");
            }
        }
        if (!profileDictionary.ContainsKey("Default"))
        {
            profileDictionary["Default"] = defaultProfile;
        }
    }
    
    public CutProfile GetProfile(string materialName)
    {
        if (profileDictionary.ContainsKey(materialName))
        {
            return profileDictionary[materialName];
        }
        
        Debug.LogWarning($"No cut profile found for '{materialName}', using default");
        return defaultProfile;
    }

    public List<Vector2> ApplyIrregularCut(List<Vector2> originalShape, Vector2 entryPoint, Vector2 exitPoint, CutProfile profile)
    {
        if (originalShape == null || originalShape.Count < 3 || profile == null)
        {
            return originalShape;
        }
        
        List<Vector2> irregularShape = new List<Vector2>();

        int entryIndex = FindClosestVertexIndex(originalShape, entryPoint);
        int exitIndex = FindClosestVertexIndex(originalShape, exitPoint);

        for (int i = 0; i < originalShape.Count; i++)
        {
            Vector2 currentVertex = originalShape[i];
            Vector2 nextVertex = originalShape[(i + 1) % originalShape.Count];

            bool isEntryEdge = (i == entryIndex || i == (entryIndex - 1 + originalShape.Count) % originalShape.Count);
            bool isExitEdge = (i == exitIndex || i == (exitIndex - 1 + originalShape.Count) % originalShape.Count);
            bool isCutEdge = (isEntryEdge && Vector2.Distance(nextVertex, exitPoint) < 0.1f) ||
                            (isExitEdge && Vector2.Distance(nextVertex, entryPoint) < 0.1f) ||
                            (Vector2.Distance(currentVertex, entryPoint) < 0.1f && Vector2.Distance(nextVertex, exitPoint) < 0.1f) ||
                            (Vector2.Distance(currentVertex, exitPoint) < 0.1f && Vector2.Distance(nextVertex, entryPoint) < 0.1f);
            
            if (isCutEdge)
            {
                List<Vector2> irregularEdge = GenerateIrregularEdge(currentVertex, nextVertex, profile);
                irregularShape.AddRange(irregularEdge);
            }
            else
            {
                irregularShape.Add(currentVertex);
            }
        }
        
        return irregularShape;
    }

    List<Vector2> GenerateIrregularEdge(Vector2 start, Vector2 end, CutProfile profile)
    {
        List<Vector2> points = new List<Vector2>();
        points.Add(start);
        
        float edgeLength = Vector2.Distance(start, end);
        Vector2 direction = (end - start).normalized;
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        
        int minSegments = 3;
        int maxSegments = 15;
        int numSegments = Mathf.RoundToInt(Mathf.Lerp(maxSegments, minSegments, profile.softness));

        for (int i = 1; i < numSegments; i++)
        {
            float t = (float)i / numSegments;
            Vector2 basePoint = Vector2.Lerp(start, end, t);
            float maxOffset = edgeLength * profile.strength * 0.5f;

            float offset;
            if (profile.softness > 0.7f)
            {
                float frequency = Random.Range(1f, 3f);
                float amplitude = Random.Range(0.5f, 1f) * maxOffset;
                offset = Mathf.Sin(t * Mathf.PI * frequency) * amplitude;
            }
            else if (profile.softness > 0.3f)
            {
                float smoothOffset = Mathf.Sin(t * Mathf.PI * Random.Range(2f, 4f)) * maxOffset * 0.5f;
                float randomOffset = Random.Range(-maxOffset * 0.5f, maxOffset * 0.5f);
                offset = smoothOffset + randomOffset;
            }
            else
            {
                offset = Random.Range(-maxOffset, maxOffset);
            }
            
            float cutBias = Random.Range(-1f, 0.3f);
            offset *= cutBias;
            
            Vector2 irregularPoint = basePoint + perpendicular * offset;
            points.Add(irregularPoint);
        }
        
        points.Add(end);
        
        return points;
    }
    
    int FindClosestVertexIndex(List<Vector2> vertices, Vector2 point)
    {
        int closestIndex = 0;
        float closestDistance = Vector2.Distance(vertices[0], point);
        
        for (int i = 1; i < vertices.Count; i++)
        {
            float distance = Vector2.Distance(vertices[i], point);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }
        
        return closestIndex;
    }
}

public static class CutProfileExtensions
{
    public static CutProfile GetCutProfileForObject(GameObject obj)
    {
        CutProfileManager manager = Object.FindObjectOfType<CutProfileManager>();
        
        if (manager == null)
        {
            Debug.LogWarning("No CutProfileManager found in scene!");
            return new CutProfile("Default", 0.5f, 0.3f);
        }

        string profileKey = obj.tag;
        if (profileKey == "Untagged" || string.IsNullOrEmpty(profileKey))
        {
            profileKey = obj.name;
        }
        
        return manager.GetProfile(profileKey);
    }
}