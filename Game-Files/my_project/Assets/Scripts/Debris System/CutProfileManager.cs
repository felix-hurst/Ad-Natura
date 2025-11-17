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
        
        // in case default profile is not defined in JSON
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
    
    /// <summary>
    /// Applies procedural irregular cutting to a shape based on the cut profile
    /// </summary>
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
            
            // Check if this is the cut edge (between entry and exit points)
            bool isEntryEdge = (i == entryIndex || i == (entryIndex - 1 + originalShape.Count) % originalShape.Count);
            bool isExitEdge = (i == exitIndex || i == (exitIndex - 1 + originalShape.Count) % originalShape.Count);
            bool isCutEdge = (isEntryEdge && Vector2.Distance(nextVertex, exitPoint) < 0.1f) ||
                            (isExitEdge && Vector2.Distance(nextVertex, entryPoint) < 0.1f) ||
                            (Vector2.Distance(currentVertex, entryPoint) < 0.1f && Vector2.Distance(nextVertex, exitPoint) < 0.1f) ||
                            (Vector2.Distance(currentVertex, exitPoint) < 0.1f && Vector2.Distance(nextVertex, entryPoint) < 0.1f);
            
            if (isCutEdge)
            {
                // Apply irregular cutting to this edge
                List<Vector2> irregularEdge = GenerateIrregularEdge(currentVertex, nextVertex, profile);
                irregularShape.AddRange(irregularEdge);
            }
            else
            {
                //keep original vertex
                irregularShape.Add(currentVertex);
            }
        }
        
        return irregularShape;
    }
    
    /// <summary>
    /// Generates an irregular edge between two points based on cut profile
    /// </summary>
    List<Vector2> GenerateIrregularEdge(Vector2 start, Vector2 end, CutProfile profile)
    {
        List<Vector2> points = new List<Vector2>();
        points.Add(start);
        
        float edgeLength = Vector2.Distance(start, end);
        Vector2 direction = (end - start).normalized;
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        
        // Calculates number of segments based on softness
        // Lower softness = more segments = more jagged
        int minSegments = 3;
        int maxSegments = 15;
        int numSegments = Mathf.RoundToInt(Mathf.Lerp(maxSegments, minSegments, profile.softness));
        
        // Generate points along the edge with random offsets
        for (int i = 1; i < numSegments; i++)
        {
            float t = (float)i / numSegments;
            Vector2 basePoint = Vector2.Lerp(start, end, t);
            
            // Calculate max offset based on strength
            float maxOffset = edgeLength * profile.strength * 0.5f;
            
            // Generate offset with varying frequency based on softness
            float offset;
            if (profile.softness > 0.7f)
            {
                // Smooth: use sine wave with random amplitude
                float frequency = Random.Range(1f, 3f);
                float amplitude = Random.Range(0.5f, 1f) * maxOffset;
                offset = Mathf.Sin(t * Mathf.PI * frequency) * amplitude;
            }
            else if (profile.softness > 0.3f)
            {
                // Medium: mix of smooth and random
                float smoothOffset = Mathf.Sin(t * Mathf.PI * Random.Range(2f, 4f)) * maxOffset * 0.5f;
                float randomOffset = Random.Range(-maxOffset * 0.5f, maxOffset * 0.5f);
                offset = smoothOffset + randomOffset;
            }
            else
            {
                // Jagged: pure random
                offset = Random.Range(-maxOffset, maxOffset);
            }
            
            // Apply the offset perpendicular to the edge direction
            // Bias towards cutting INTO the material (negative offset)
            float cutBias = Random.Range(-1f, 0.3f); // More likely to cut in than out
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

// Extension for ObjectReshape to use cut profiles
public static class CutProfileExtensions
{
    /// <summary>
    /// Gets the cut profile for a game object based on its tag or name
    /// </summary>
    public static CutProfile GetCutProfileForObject(GameObject obj)
    {
        CutProfileManager manager = Object.FindObjectOfType<CutProfileManager>();
        
        if (manager == null)
        {
            Debug.LogWarning("No CutProfileManager found in scene!");
            return new CutProfile("Default", 0.5f, 0.3f);
        }
        
        // Try to get profile by tag first
        string profileKey = obj.tag;
        
        // If tag is "Untagged", try using the object's name
        if (profileKey == "Untagged" || string.IsNullOrEmpty(profileKey))
        {
            profileKey = obj.name;
        }
        
        return manager.GetProfile(profileKey);
    }
}