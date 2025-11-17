using UnityEngine;
using System.Collections.Generic;

public class DebrisSpawner : MonoBehaviour
{
    [Header("Debris Settings")]
    [SerializeField] private int numberOfFragments = 10;
    [SerializeField] private GameObject debrisPrefab;
    [SerializeField] private float explosionForceMin = 2f;
    [SerializeField] private float explosionForceMax = 5f;
    [SerializeField] private float angularVelocityMin = -180f;
    [SerializeField] private float angularVelocityMax = 180f;
    [SerializeField] private float debrisLifetime = 5f;
    [SerializeField] private float massMultiplier = 0.1f;
    
    [Header("Debris Appearance")]
    [SerializeField] private bool useIrregularShapes = true;
    
    [Header("Size Variation")]
    [SerializeField] private float minSizeMultiplier = 0.3f;
    [SerializeField] private float maxSizeMultiplier = 2.5f;
    [SerializeField] private AnimationCurve sizeDistribution;
    
    [Header("Shape Variation")]
    [SerializeField] private float aspectRatioMin = 0.4f;
    [SerializeField] private float aspectRatioMax = 2.5f;
    
    private SpriteRenderer spriteRenderer;
    private string parentMaterialTag;
    
    void Awake()
    {
        if (sizeDistribution == null || sizeDistribution.keys.Length == 0)
        {
            sizeDistribution = new AnimationCurve(
                new Keyframe(0f, 0.1f),
                new Keyframe(0.3f, 0.8f),
                new Keyframe(0.5f, 1f),
                new Keyframe(0.7f, 0.8f),
                new Keyframe(1f, 0.3f)
            );
        }
    }
    
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        parentMaterialTag = gameObject.tag;
    }
    
    public void SpawnDebris(List<Vector2> cutOffShape, float totalArea, string materialTag = null)
    {
        if (cutOffShape == null || cutOffShape.Count < 3)
        {
            Debug.LogWarning("Invalid cut-off shape for debris spawning!");
            return;
        }
        
        string debrisMaterialTag = string.IsNullOrEmpty(materialTag) ? parentMaterialTag : materialTag;
        Debug.Log($"Spawning {numberOfFragments} debris pieces with total area {totalArea:F2} using material: {debrisMaterialTag}");
        
        Vector2 centroid = CalculateCentroid(cutOffShape);
        Bounds shapeBounds = GetShapeBounds(cutOffShape);
        
        CutProfile cutProfile = null;
        CutProfileManager profileManager = FindObjectOfType<CutProfileManager>();
        if (useIrregularShapes && profileManager != null)
        {
            cutProfile = profileManager.GetProfile(debrisMaterialTag);
            Debug.Log($"Using cut profile for debris: {cutProfile.materialName} (Softness: {cutProfile.softness}, Strength: {cutProfile.strength})");
        }
        
        List<float> fragmentAreas = GenerateVariedFragmentSizes(totalArea, numberOfFragments, cutProfile);
        float totalDebrisArea = 0f;
        
        for (int i = 0; i < numberOfFragments; i++)
        {
            float fragmentArea = fragmentAreas[i];
            float aspectRatio = Random.Range(aspectRatioMin, aspectRatioMax);
            
            if (cutProfile != null)
            {
                if (cutProfile.softness > 0.7f)
                {
                    aspectRatio = Mathf.Lerp(aspectRatio, 1f, 0.5f);
                }
                else if (cutProfile.softness < 0.3f)
                {
                    aspectRatio = Mathf.Lerp(aspectRatio, aspectRatio > 1f ? aspectRatioMax : 1f / aspectRatioMax, 0.3f);
                }
            }
            
            GameObject fragment = CreateFragment(fragmentArea, aspectRatio, cutProfile, debrisMaterialTag);
            
            float fragmentSize = Mathf.Sqrt(fragmentArea);
            Vector2 fragmentPosition = GeneratePositionInShape(cutOffShape, shapeBounds, centroid, fragmentSize);
            fragment.transform.position = new Vector3(fragmentPosition.x, fragmentPosition.y, transform.position.z);
            fragment.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            
            SetupFragmentPhysics(fragment, fragmentArea, centroid, fragmentPosition);
            fragment.tag = debrisMaterialTag;
            fragment.name = $"{gameObject.name}_Debris_{i}";
            
            totalDebrisArea += fragmentArea;
            Destroy(fragment, debrisLifetime);
        }
        
        Debug.Log($"Successfully spawned {numberOfFragments} debris fragments. Target total area: {totalArea:F2}, Actual total area: {totalDebrisArea:F2}, Material: {debrisMaterialTag}");
    }
    
    List<float> GenerateVariedFragmentSizes(float totalArea, int numFragments, CutProfile cutProfile)
    {
        List<float> sizes = new List<float>();
        float averageArea = totalArea / numFragments;
        float totalGenerated = 0f;
        
        for (int i = 0; i < numFragments; i++)
        {
            float t = Random.Range(0f, 1f);
            float sizeMultiplier = sizeDistribution.Evaluate(t);
            sizeMultiplier = Mathf.Lerp(minSizeMultiplier, maxSizeMultiplier, sizeMultiplier);
            
            if (cutProfile != null)
            {
                float variationFactor = 1f - cutProfile.softness;
                if (variationFactor > 0.5f)
                {
                    if (sizeMultiplier > 1f)
                    {
                        sizeMultiplier = Mathf.Lerp(sizeMultiplier, maxSizeMultiplier, variationFactor);
                    }
                    else
                    {
                        sizeMultiplier = Mathf.Lerp(sizeMultiplier, minSizeMultiplier, variationFactor);
                    }
                }
                else
                {
                    sizeMultiplier = Mathf.Lerp(sizeMultiplier, 1f, cutProfile.softness);
                }
            }
            
            float fragmentArea = averageArea * sizeMultiplier;
            sizes.Add(fragmentArea);
            totalGenerated += fragmentArea;
        }
        
        float scale = totalArea / totalGenerated;
        for (int i = 0; i < sizes.Count; i++)
        {
            sizes[i] *= scale;
        }
        
        return sizes;
    }
    
    GameObject CreateFragment(float targetArea, float aspectRatio, CutProfile cutProfile = null, string materialTag = null)
    {
        GameObject fragment;
        float width = Mathf.Sqrt(targetArea * aspectRatio);
        float height = Mathf.Sqrt(targetArea / aspectRatio);
        
        if (debrisPrefab != null)
        {
            fragment = Instantiate(debrisPrefab);
            fragment.transform.localScale = new Vector3(width, height, 1f);
        }
        else
        {
            fragment = new GameObject("Fragment");
            
            // Generate irregular shape points FIRST
            List<Vector2> shapePoints = null;
            if (cutProfile != null && useIrregularShapes && cutProfile.strength > 0.01f)
            {
                List<Vector2> baseShape = GenerateBaseFragmentShape(width, height, cutProfile);
                shapePoints = ApplyProfileToShape(baseShape, cutProfile);
            }
            else
            {
                // Default rectangular shape
                shapePoints = new List<Vector2>
                {
                    new Vector2(-0.5f, -0.5f),
                    new Vector2(0.5f, -0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(-0.5f, 0.5f)
                };
            }
            
            // Get texture
            string textureMaterialTag = string.IsNullOrEmpty(materialTag) ? parentMaterialTag : materialTag;
            MaterialTextureGenerator textureGenerator = FindObjectOfType<MaterialTextureGenerator>();
            Texture2D texture = null;
            
            if (textureGenerator != null && !string.IsNullOrEmpty(textureMaterialTag))
            {
                texture = textureGenerator.GetTexture(textureMaterialTag);
            }
            
            if (texture == null)
            {
                texture = new Texture2D(1, 1);
                texture.SetPixel(0, 0, spriteRenderer != null ? spriteRenderer.color : Color.white);
                texture.Apply();
            }
            
            // Create mesh-based sprite renderer for irregular shape
            CreateMeshSprite(fragment, shapePoints, texture, width, height);
            
            // Copy sorting layer settings
            if (spriteRenderer != null)
            {
                SpriteRenderer fragmentRenderer = fragment.GetComponent<SpriteRenderer>();
                if (fragmentRenderer != null)
                {
                    fragmentRenderer.sortingLayerName = spriteRenderer.sortingLayerName;
                    fragmentRenderer.sortingOrder = spriteRenderer.sortingOrder;
                }
            }
            
            // Add collider matching the shape
            PolygonCollider2D polyCollider = fragment.AddComponent<PolygonCollider2D>();
            polyCollider.points = shapePoints.ToArray();
            
            // Scale the fragment
            fragment.transform.localScale = new Vector3(width, height, 1f);
        }
        
        return fragment;
    }
    
    /// <summary>
    /// Creates a mesh-based sprite for irregular shapes
    /// </summary>
    void CreateMeshSprite(GameObject fragment, List<Vector2> shapePoints, Texture2D texture, float width, float height)
    {
        // Add MeshFilter and MeshRenderer
        MeshFilter meshFilter = fragment.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = fragment.AddComponent<MeshRenderer>();
        
        // Create material with the texture
        Material material = new Material(Shader.Find("Sprites/Default"));
        material.mainTexture = texture;
        meshRenderer.material = material;
        
        // Create mesh from polygon points
        Mesh mesh = new Mesh();
        
        // Triangulate the polygon
        int[] triangles = TriangulatePolygon(shapePoints);
        
        // Convert 2D points to 3D vertices
        Vector3[] vertices = new Vector3[shapePoints.Count];
        Vector2[] uvs = new Vector2[shapePoints.Count];
        
        for (int i = 0; i < shapePoints.Count; i++)
        {
            vertices[i] = new Vector3(shapePoints[i].x, shapePoints[i].y, 0);
            // Map UVs from -0.5 to 0.5 range to 0 to 1 range
            uvs[i] = new Vector2(shapePoints[i].x + 0.5f, shapePoints[i].y + 0.5f);
        }
        
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        meshFilter.mesh = mesh;
    }
    
    /// <summary>
    /// Simple ear clipping triangulation for convex/simple polygons
    /// </summary>
    int[] TriangulatePolygon(List<Vector2> points)
    {
        List<int> triangles = new List<int>();
        
        // Simple fan triangulation from first vertex
        for (int i = 1; i < points.Count - 1; i++)
        {
            triangles.Add(0);
            triangles.Add(i);
            triangles.Add(i + 1);
        }
        
        return triangles.ToArray();
    }
    
    List<Vector2> GenerateBaseFragmentShape(float width, float height, CutProfile cutProfile)
    {
        List<Vector2> shape = new List<Vector2>();
        
        int minSides = 3;
        int maxSides = 8;
        int sides;
        
        if (cutProfile != null)
        {
            if (cutProfile.softness > 0.7f)
            {
                sides = Random.Range(6, maxSides + 1);
            }
            else if (cutProfile.softness < 0.3f)
            {
                sides = Random.Range(minSides, 5);
            }
            else
            {
                sides = Random.Range(4, 7);
            }
        }
        else
        {
            sides = Random.Range(4, 7);
        }
        
        float radiusX = 0.5f;
        float radiusY = 0.5f;
        
        for (int i = 0; i < sides; i++)
        {
            float angle = (i / (float)sides) * Mathf.PI * 2f;
            float randomFactorX = Random.Range(0.7f, 1.2f);
            float randomFactorY = Random.Range(0.7f, 1.2f);
            
            Vector2 vertex = new Vector2(
                Mathf.Cos(angle) * radiusX * randomFactorX,
                Mathf.Sin(angle) * radiusY * randomFactorY
            );
            shape.Add(vertex);
        }
        
        return shape;
    }
    
    List<Vector2> ApplyProfileToShape(List<Vector2> baseShape, CutProfile cutProfile)
    {
        List<Vector2> irregularShape = new List<Vector2>();
        
        for (int i = 0; i < baseShape.Count; i++)
        {
            Vector2 current = baseShape[i];
            Vector2 next = baseShape[(i + 1) % baseShape.Count];
            
            irregularShape.Add(current);
            
            int intermediatePoints = Mathf.RoundToInt(Mathf.Lerp(0, 4, 1f - cutProfile.softness));
            
            for (int j = 1; j <= intermediatePoints; j++)
            {
                float t = j / (float)(intermediatePoints + 1);
                Vector2 interpolated = Vector2.Lerp(current, next, t);
                
                Vector2 direction = (next - current).normalized;
                Vector2 perpendicular = new Vector2(-direction.y, direction.x);
                
                float maxOffset = Vector2.Distance(current, next) * cutProfile.strength * 0.5f;
                float offset = Random.Range(-maxOffset, maxOffset);
                
                interpolated += perpendicular * offset;
                irregularShape.Add(interpolated);
            }
        }
        
        return irregularShape;
    }
    
    Vector2 GeneratePositionInShape(List<Vector2> shape, Bounds bounds, Vector2 centroid, float fragmentSize)
    {
        Vector2 position;
        int maxAttempts = 10;
        int attempts = 0;
        
        do
        {
            position = new Vector2(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y)
            );
            attempts++;
            
            if (IsPointInPolygon(position, shape))
            {
                return position;
            }
        }
        while (attempts < maxAttempts);
        
        return centroid + Random.insideUnitCircle * (fragmentSize * 2);
    }
    
    void SetupFragmentPhysics(GameObject fragment, float area, Vector2 centroid, Vector2 position)
    {
        Rigidbody2D rb = fragment.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = fragment.AddComponent<Rigidbody2D>();
        }
        
        rb.gravityScale = 1f;
        rb.mass = area * massMultiplier;
        
        Vector2 explosionDir = (position - centroid).normalized;
        if (explosionDir.magnitude < 0.1f)
        {
            explosionDir = Random.insideUnitCircle.normalized;
        }
        
        float explosionForce = Random.Range(explosionForceMin, explosionForceMax);
        rb.linearVelocity = explosionDir * explosionForce;
        rb.angularVelocity = Random.Range(angularVelocityMin, angularVelocityMax);
        
        // Apply physics material based on material tag
        PhysicsMaterialManager physicsManager = FindObjectOfType<PhysicsMaterialManager>();
        if (physicsManager != null)
        {
            physicsManager.ApplyPhysicsMaterial(fragment);
        }
    }
    
    Vector2 CalculateCentroid(List<Vector2> vertices)
    {
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 v in vertices)
        {
            centroid += v;
        }
        return centroid / vertices.Count;
    }
    
    Bounds GetShapeBounds(List<Vector2> vertices)
    {
        if (vertices.Count == 0) return new Bounds();
        
        Vector2 min = vertices[0];
        Vector2 max = vertices[0];
        
        foreach (Vector2 v in vertices)
        {
            min.x = Mathf.Min(min.x, v.x);
            min.y = Mathf.Min(min.y, v.y);
            max.x = Mathf.Max(max.x, v.x);
            max.y = Mathf.Max(max.y, v.y);
        }
        
        Vector2 center = (min + max) / 2f;
        Vector2 size = max - min;
        
        return new Bounds(center, size);
    }
    
    bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        int n = polygon.Count;
        
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / 
                (polygon[j].y - polygon[i].y) + polygon[i].x))
            {
                inside = !inside;
            }
        }
        
        return inside;
    }
}