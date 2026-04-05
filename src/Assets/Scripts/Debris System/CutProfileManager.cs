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

    public List<Vector2> ApplyIrregularCut(List<Vector2> shape, Vector2 entry, Vector2 exit, CutProfile profile)
    {
        List<Vector2> result = new List<Vector2>();
        bool injected = false;

        for (int i = 0; i < shape.Count; i++)
        {
            Vector2 a = shape[i];
            Vector2 b = shape[(i + 1) % shape.Count];

            if (!injected && IsMatchingEdge(a, b, entry, exit))
            {
                result.Add(a);
                List<Vector2> irregular = GenerateIrregularEdge(entry, exit, profile);

                if (irregular.Count > 2)
                {
                    for (int j = 1; j < irregular.Count - 1; j++) result.Add(irregular[j]);
                }
                injected = true;
            }
            else
            {
                result.Add(a);
            }
        }
        return result;
    }

    private bool IsMatchingEdge(Vector2 a, Vector2 b, Vector2 entry, Vector2 exit)
    {
        float d1 = Vector2.Distance(a, entry) + Vector2.Distance(b, exit);
        float d2 = Vector2.Distance(a, exit) + Vector2.Distance(b, entry);
        return d1 < 0.05f || d2 < 0.05f;
    }

    private bool IsPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        float dist = Vector2.Distance(a, b);
        return Vector2.Distance(a, p) + Vector2.Distance(p, b) <= dist + 0.01f;
    }

    List<Vector2> GenerateIrregularEdge(Vector2 start, Vector2 end, CutProfile profile)
    {
        List<Vector2> points = new List<Vector2>();
        points.Add(start);

        float edgeLength = Vector2.Distance(start, end);
        Vector2 direction = (end - start).normalized;
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);

        float maxOffset = Mathf.Min(edgeLength * profile.strength, 0.15f);

        int numSegments = Mathf.RoundToInt(Mathf.Lerp(15, 5, profile.softness));

        for (int i = 1; i < numSegments; i++)
        {
            float t = (float)i / numSegments;

            float taper = Mathf.Sin(t * Mathf.PI);

            Vector2 basePoint = Vector2.Lerp(start, end, t);
            float offset = 0;

            float noise = Mathf.PerlinNoise(basePoint.x * 10f, basePoint.y * 10f) * 2f - 1f;
            offset = noise * maxOffset;

            float cutBias = Random.Range(-1f, 0.3f);
            offset *= cutBias * taper;

            Vector2 irregularPoint = basePoint + perpendicular * offset;
            points.Add(irregularPoint);
        }

        points.Add(end);

        for (int i = 0; i < points.Count - 1; i++)
            Debug.DrawLine(points[i], points[i + 1], Color.red, 3f);

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