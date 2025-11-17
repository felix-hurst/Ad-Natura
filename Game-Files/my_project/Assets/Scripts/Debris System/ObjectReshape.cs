using UnityEngine;
using System.Collections.Generic;

public class ObjectReshape : MonoBehaviour
{
    [Header("Collider Settings")]
    [SerializeField] private bool useSmoothCollider = true; // Use smooth diagonal for physics
    
    private SpriteRenderer spriteRenderer;
    private PolygonCollider2D polygonCollider;
    private Rigidbody2D rb;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private PixelatedCutRenderer pixelatedCutRenderer;
    
    // Store the original texture for the cut pieces
    private Texture2D originalTexture;
    private string materialTag;
    
    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        
        // Store material tag
        materialTag = gameObject.tag;
        if (string.IsNullOrEmpty(materialTag) || materialTag == "Untagged")
        {
            materialTag = gameObject.name;
        }
        
        // Store original texture if available
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            originalTexture = spriteRenderer.sprite.texture;
        }
        
        // Get or add PixelatedCutRenderer
        pixelatedCutRenderer = GetComponent<PixelatedCutRenderer>();
        if (pixelatedCutRenderer == null)
        {
            pixelatedCutRenderer = gameObject.AddComponent<PixelatedCutRenderer>();
        }
    }
    
    void Start()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }
        if (pixelatedCutRenderer == null)
        {
            pixelatedCutRenderer = GetComponent<PixelatedCutRenderer>();
            if (pixelatedCutRenderer == null)
            {
                pixelatedCutRenderer = gameObject.AddComponent<PixelatedCutRenderer>();
            }
        }
        
        // Update texture reference
        if (spriteRenderer != null && spriteRenderer.sprite != null && originalTexture == null)
        {
            originalTexture = spriteRenderer.sprite.texture;
        }
    }
    
    /// <summary>
    /// Cuts off the highlighted portion from the object and returns the cut shape vertices
    /// </summary>
    public List<Vector2> CutOffPortion(Vector2 entryPoint, Vector2 exitPoint, List<Vector2> highlightedShape)
    {
        if (highlightedShape == null || highlightedShape.Count < 3)
        {
            Debug.LogWarning("Invalid highlighted shape for cutting!");
            return null;
        }
        
        // Ensure we have references
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }
        if (pixelatedCutRenderer == null)
        {
            pixelatedCutRenderer = GetComponent<PixelatedCutRenderer>();
        }
        
        // Get the current shape vertices (could be from polygon collider if already cut)
        Vector2[] corners = GetCurrentShapeVertices();
        
        // Determine which shape to keep (the non-highlighted one)
        List<Vector2> shape1 = new List<Vector2>();
        List<Vector2> shape2 = new List<Vector2>();
        
        // Add entry and exit points
        shape1.Add(entryPoint);
        shape1.Add(exitPoint);
        shape2.Add(entryPoint);
        shape2.Add(exitPoint);
        
        // Check each vertex of the current shape
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 corner = corners[i];
            float side = GetSideOfLine(entryPoint, exitPoint, corner);
            
            if (side > 0)
            {
                shape1.Add(corner);
            }
            else
            {
                shape2.Add(corner);
            }
        }
        
        // Sort both shapes
        shape1 = SortVerticesClockwise(shape1);
        shape2 = SortVerticesClockwise(shape2);
        
        // Determine which shape is the highlighted one
        bool shape1IsHighlighted = ShapesMatch(shape1, highlightedShape);
        List<Vector2> remainingShape = shape1IsHighlighted ? shape2 : shape1;
        List<Vector2> cutOffShape = shape1IsHighlighted ? shape1 : shape2;
        
        // Get the cut profile for this object
        CutProfile cutProfile = CutProfileExtensions.GetCutProfileForObject(gameObject);
        Debug.Log($"Using cut profile for {gameObject.name}: Softness={cutProfile.softness}, Strength={cutProfile.strength}");
        
        // Apply irregular cutting based on profile
        CutProfileManager profileManager = FindObjectOfType<CutProfileManager>();
        if (profileManager != null && cutProfile.strength > 0.01f)
        {
            remainingShape = profileManager.ApplyIrregularCut(remainingShape, entryPoint, exitPoint, cutProfile);
            cutOffShape = profileManager.ApplyIrregularCut(cutOffShape, entryPoint, exitPoint, cutProfile);
        }
        
        // Keep the SMOOTH versions for collider
        List<Vector2> remainingShapeSmooth = new List<Vector2>(remainingShape);
        List<Vector2> cutOffShapeSmooth = new List<Vector2>(cutOffShape);
        
        // Apply pixelation ONLY for visuals
        List<Vector2> remainingShapePixelated = remainingShape;
        List<Vector2> cutOffShapePixelated = cutOffShape;
        
        if (pixelatedCutRenderer != null)
        {
            remainingShapePixelated = pixelatedCutRenderer.PixelatePolygonWithCutLine(remainingShape, entryPoint, exitPoint);
            cutOffShapePixelated = pixelatedCutRenderer.PixelatePolygonWithCutLine(cutOffShape, entryPoint, exitPoint);
        }
        
        // Apply the shape to this object
        // Use SMOOTH shape for collider, PIXELATED shape for visual
        ApplyNewShape(remainingShapePixelated, remainingShapeSmooth);
        
        // Update the Rigidbody2D mass based on new area (use smooth shape for accurate area)
        UpdateRigidbodyMass(remainingShapeSmooth);
        
        Debug.Log($"Cut complete - Remaining shape has {remainingShapePixelated.Count} visual vertices, {remainingShapeSmooth.Count} collider vertices");
        
        return cutOffShapePixelated;
    }
    
    /// <summary>
    /// Gets the current shape vertices - either from polygon collider or original bounds
    /// </summary>
    Vector2[] GetCurrentShapeVertices()
    {
        // First, try to get vertices from the polygon collider (if object was already cut)
        PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
        if (polyCol != null && polyCol.points.Length > 0)
        {
            // Convert local points to world space
            Vector2[] worldVertices = new Vector2[polyCol.points.Length];
            for (int i = 0; i < polyCol.points.Length; i++)
            {
                worldVertices[i] = transform.TransformPoint(polyCol.points[i]);
            }
            Debug.Log($"Using polygon vertices: {worldVertices.Length} points");
            return worldVertices;
        }
        
        // Otherwise, fall back to the rectangular bounds
        Debug.Log("Using rectangular bounds (4 corners)");
        return GetWorldCorners();
    }
    
    /// <summary>
    /// Applies a new polygon shape to the object
    /// visualShape: pixelated vertices for the mesh
    /// colliderShape: smooth vertices for the physics collider
    /// </summary>
    void ApplyNewShape(List<Vector2> visualShape, List<Vector2> colliderShape)
    {
        if (visualShape.Count < 3 || colliderShape.Count < 3) return;
        
        // Convert world space vertices to local space
        List<Vector2> localVisualVertices = new List<Vector2>();
        foreach (Vector2 worldVertex in visualShape)
        {
            Vector2 localVertex = transform.InverseTransformPoint(worldVertex);
            localVisualVertices.Add(localVertex);
        }
        
        List<Vector2> localColliderVertices = new List<Vector2>();
        foreach (Vector2 worldVertex in colliderShape)
        {
            Vector2 localVertex = transform.InverseTransformPoint(worldVertex);
            localColliderVertices.Add(localVertex);
        }
        
        // Update collider with SMOOTH shape (for physics)
        UpdateCollider(useSmoothCollider ? localColliderVertices : localVisualVertices);
        
        // Update visual mesh with PIXELATED shape (for appearance)
        UpdateVisualMesh(localVisualVertices);
        
        Debug.Log($"Applied new shape - Visual: {localVisualVertices.Count} vertices, Collider: {localColliderVertices.Count} vertices");
    }
    
    /// <summary>
    /// Updates the collider to match the new shape
    /// </summary>
    void UpdateCollider(List<Vector2> localVertices)
    {
        // Remove existing colliders
        Collider2D[] existingColliders = GetComponents<Collider2D>();
        foreach (Collider2D col in existingColliders)
        {
            Destroy(col);
        }
        
        // Create new PolygonCollider2D
        polygonCollider = gameObject.AddComponent<PolygonCollider2D>();
        polygonCollider.points = localVertices.ToArray();
        
        // Re-apply physics material to the new collider
        PhysicsMaterialManager physicsManager = FindObjectOfType<PhysicsMaterialManager>();
        if (physicsManager != null)
        {
            physicsManager.ApplyPhysicsMaterial(gameObject);
        }
        
        Debug.Log($"Updated collider with {localVertices.Count} points (Smooth: {useSmoothCollider})");
    }
    
    /// <summary>
    /// Creates a visual mesh to represent the new shape on a child GameObject
    /// </summary>
    void UpdateVisualMesh(List<Vector2> localVertices)
    {
        if (localVertices == null || localVertices.Count < 3)
        {
            Debug.LogError("Invalid vertices for mesh creation");
            return;
        }
        
        // Get spriteRenderer if we don't have it yet
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        
        // Store the sprite color and sorting info
        Color spriteColor = Color.white;
        string sortingLayer = "Default";
        int sortingOrder = 0;
        Texture2D textureToUse = originalTexture;
        
        if (spriteRenderer != null)
        {
            spriteColor = spriteRenderer.color;
            sortingLayer = spriteRenderer.sortingLayerName;
            sortingOrder = spriteRenderer.sortingOrder;
            
            // Try to get texture from sprite
            if (spriteRenderer.sprite != null && spriteRenderer.sprite.texture != null)
            {
                textureToUse = spriteRenderer.sprite.texture;
            }
            
            spriteRenderer.enabled = false;
        }
        
        // If we still don't have a texture, try to generate one
        if (textureToUse == null)
        {
            MaterialTextureGenerator textureGenerator = FindObjectOfType<MaterialTextureGenerator>();
            if (textureGenerator != null)
            {
                textureToUse = textureGenerator.GetTexture(materialTag);
                Debug.Log($"Generated procedural texture for cut mesh: {materialTag}");
            }
        }
        
        // Find and destroy ALL existing mesh children from previous cuts
        foreach (Transform child in transform)
        {
            if (child.name.Contains("_CutMesh"))
            {
                Debug.Log($"Destroying old mesh child: {child.name}");
                Destroy(child.gameObject);
            }
        }
        
        // Create a new child GameObject for the mesh
        GameObject meshObject = new GameObject($"{gameObject.name}_CutMesh");
        meshObject.transform.SetParent(transform);
        meshObject.transform.localPosition = Vector3.zero;
        meshObject.transform.localRotation = Quaternion.identity;
        meshObject.transform.localScale = Vector3.one;
        
        Debug.Log($"Creating new mesh child with {localVertices.Count} vertices");
        
        // Add MeshFilter and MeshRenderer to the child
        MeshFilter newMeshFilter = meshObject.AddComponent<MeshFilter>();
        MeshRenderer newMeshRenderer = meshObject.AddComponent<MeshRenderer>();
        
        // Find a working shader
        Shader shader = null;
        if (spriteRenderer != null && spriteRenderer.sharedMaterial != null && spriteRenderer.sharedMaterial.shader != null)
        {
            shader = spriteRenderer.sharedMaterial.shader;
        }
        
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }
        if (shader == null)
        {
            shader = Shader.Find("UI/Default");
        }
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Texture");
        }
        
        if (shader == null)
        {
            Debug.LogError("Could not find any shader!");
            Destroy(meshObject);
            return;
        }
        
        // Create material with the shader we found
        Material newMaterial = new Material(shader);
        
        // Apply texture if we have one
        if (textureToUse != null)
        {
            newMaterial.mainTexture = textureToUse;
            Debug.Log($"Applied texture to cut mesh: {textureToUse.name}");
        }
        else
        {
            // Fallback to color only
            newMaterial.color = spriteColor;
        }
        
        newMeshRenderer.material = newMaterial;
        newMeshRenderer.sortingLayerName = sortingLayer;
        newMeshRenderer.sortingOrder = sortingOrder;
        
        // Create and assign mesh
        try
        {
            Mesh mesh = CreateMeshFromPolygon(localVertices);
            if (mesh != null && newMeshFilter != null)
            {
                newMeshFilter.mesh = mesh;
                
                // Store references for next cut
                meshFilter = newMeshFilter;
                meshRenderer = newMeshRenderer;
                
                Debug.Log($"Successfully created visual mesh with {localVertices.Count} vertices, has texture: {textureToUse != null}");
            }
            else
            {
                Debug.LogError("Failed to create or assign mesh");
                Destroy(meshObject);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Exception while creating mesh: {e.Message}\n{e.StackTrace}");
            Destroy(meshObject);
        }
    }
    
    /// <summary>
    /// Creates a mesh from polygon vertices using triangulation
    /// </summary>
    Mesh CreateMeshFromPolygon(List<Vector2> vertices)
    {
        if (vertices == null || vertices.Count < 3)
        {
            Debug.LogError("Not enough vertices to create mesh");
            return null;
        }
        
        Mesh mesh = new Mesh();
        mesh.name = "CutMesh";
        
        // Convert 2D vertices to 3D
        Vector3[] vertices3D = new Vector3[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices3D[i] = new Vector3(vertices[i].x, vertices[i].y, 0);
        }
        
        // Triangulate the polygon
        int[] triangles = TriangulatePolygon(vertices);
        
        // Create UVs (simple mapping based on bounds)
        Vector2[] uvs = new Vector2[vertices.Count];
        Bounds bounds = GetLocalBounds(vertices);
        
        if (bounds.size.x > 0 && bounds.size.y > 0)
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                uvs[i] = new Vector2(
                    (vertices[i].x - bounds.min.x) / bounds.size.x,
                    (vertices[i].y - bounds.min.y) / bounds.size.y
                );
            }
        }
        else
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                uvs[i] = new Vector2(0.5f, 0.5f);
            }
        }
        
        mesh.vertices = vertices3D;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    /// <summary>
    /// Ear-clipping triangulation algorithm for proper polygon triangulation
    /// </summary>
    int[] TriangulatePolygon(List<Vector2> vertices)
    {
        List<int> triangles = new List<int>();
        
        // Create a list of vertex indices
        List<int> indices = new List<int>();
        for (int i = 0; i < vertices.Count; i++)
        {
            indices.Add(i);
        }
        
        // Keep removing ears until we have a triangle
        while (indices.Count > 3)
        {
            bool earFound = false;
            
            for (int i = 0; i < indices.Count; i++)
            {
                int prevIndex = indices[(i - 1 + indices.Count) % indices.Count];
                int currIndex = indices[i];
                int nextIndex = indices[(i + 1) % indices.Count];
                
                Vector2 prev = vertices[prevIndex];
                Vector2 curr = vertices[currIndex];
                Vector2 next = vertices[nextIndex];
                
                // Check if this is an ear (convex vertex with no other vertices inside)
                if (IsEar(prev, curr, next, vertices, indices))
                {
                    // Add triangle
                    triangles.Add(prevIndex);
                    triangles.Add(currIndex);
                    triangles.Add(nextIndex);
                    
                    // Remove the ear
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }
            
            // Safety check to prevent infinite loop
            if (!earFound)
            {
                Debug.LogWarning("Could not find ear in polygon, using simple triangulation");
                // Fall back to simple fan triangulation
                triangles.Clear();
                for (int i = 1; i < vertices.Count - 1; i++)
                {
                    triangles.Add(0);
                    triangles.Add(i);
                    triangles.Add(i + 1);
                }
                break;
            }
        }
        
        // Add the final triangle
        if (indices.Count == 3)
        {
            triangles.Add(indices[0]);
            triangles.Add(indices[1]);
            triangles.Add(indices[2]);
        }
        
        return triangles.ToArray();
    }
    
    /// <summary>
    /// Check if three consecutive vertices form an ear
    /// </summary>
    bool IsEar(Vector2 prev, Vector2 curr, Vector2 next, List<Vector2> vertices, List<int> indices)
    {
        // Check if the angle is convex (cross product > 0)
        Vector2 v1 = prev - curr;
        Vector2 v2 = next - curr;
        float cross = v1.x * v2.y - v1.y * v2.x;
        
        if (cross <= 0)
        {
            return false; // Not convex
        }
        
        // Check if any other vertex is inside this triangle
        for (int i = 0; i < indices.Count; i++)
        {
            int index = indices[i];
            Vector2 point = vertices[index];
            
            // Skip the three vertices of the triangle
            if (point == prev || point == curr || point == next)
            {
                continue;
            }
            
            // Check if point is inside the triangle
            if (IsPointInTriangle(point, prev, curr, next))
            {
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Check if a point is inside a triangle using barycentric coordinates
    /// </summary>
    bool IsPointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float denominator = ((b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y));
        
        if (Mathf.Abs(denominator) < 0.0001f)
        {
            return false; // Degenerate triangle
        }
        
        float alpha = ((b.y - c.y) * (point.x - c.x) + (c.x - b.x) * (point.y - c.y)) / denominator;
        float beta = ((c.y - a.y) * (point.x - c.x) + (a.x - c.x) * (point.y - c.y)) / denominator;
        float gamma = 1.0f - alpha - beta;
        
        return alpha > 0 && beta > 0 && gamma > 0;
    }
    
    /// <summary>
    /// Updates the Rigidbody2D mass based on the new shape area
    /// </summary>
    void UpdateRigidbodyMass(List<Vector2> newShape)
    {
        if (rb == null) return;
        
        float newArea = CalculatePolygonArea(newShape);
        float oldMass = rb.mass;
        
        // Adjust mass proportionally to area change
        Vector2[] originalCorners = GetWorldCorners();
        float width = Vector2.Distance(originalCorners[0], originalCorners[1]);
        float height = Vector2.Distance(originalCorners[0], originalCorners[3]);
        float originalArea = width * height;
        
        if (originalArea > 0)
        {
            float areaRatio = newArea / originalArea;
            rb.mass = oldMass * areaRatio;
            
            Debug.Log($"Updated Rigidbody2D mass from {oldMass:F2} to {rb.mass:F2} (area ratio: {areaRatio:F2})");
        }
    }
    
    /// <summary>
    /// Gets the 4 corners of the sprite/mesh in world space
    /// </summary>
    Vector2[] GetWorldCorners()
    {
        Bounds bounds;
        
        if (meshRenderer != null && meshRenderer.enabled)
        {
            bounds = meshRenderer.bounds;
        }
        else if (spriteRenderer != null)
        {
            bounds = spriteRenderer.bounds;
        }
        else
        {
            PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
            if (polyCol != null && polyCol.points.Length > 0)
            {
                Vector2 minPoint = transform.TransformPoint(polyCol.points[0]);
                Vector2 maxPoint = minPoint;
                
                foreach (Vector2 localPoint in polyCol.points)
                {
                    Vector2 worldPoint = transform.TransformPoint(localPoint);
                    minPoint.x = Mathf.Min(minPoint.x, worldPoint.x);
                    minPoint.y = Mathf.Min(minPoint.y, worldPoint.y);
                    maxPoint.x = Mathf.Max(maxPoint.x, worldPoint.x);
                    maxPoint.y = Mathf.Max(maxPoint.y, worldPoint.y);
                }
                
                Vector2 boundsCenter = (minPoint + maxPoint) / 2f;
                Vector2 boundsSize = maxPoint - minPoint;
                bounds = new Bounds(boundsCenter, boundsSize);
            }
            else
            {
                bounds = new Bounds(transform.position, Vector3.one);
            }
        }
        
        Vector2 center = bounds.center;
        Vector2 extents = bounds.extents;
        
        return new Vector2[]
        {
            new Vector2(center.x - extents.x, center.y - extents.y),
            new Vector2(center.x + extents.x, center.y - extents.y),
            new Vector2(center.x + extents.x, center.y + extents.y),
            new Vector2(center.x - extents.x, center.y + extents.y)
        };
    }
    
    /// <summary>
    /// Gets local bounds of vertices
    /// </summary>
    Bounds GetLocalBounds(List<Vector2> vertices)
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
    
    /// <summary>
    /// Determines which side of a line a point is on
    /// </summary>
    float GetSideOfLine(Vector2 lineStart, Vector2 lineEnd, Vector2 point)
    {
        return (lineEnd.x - lineStart.x) * (point.y - lineStart.y) - 
               (lineEnd.y - lineStart.y) * (point.x - lineStart.x);
    }
    
    /// <summary>
    /// Sorts vertices in clockwise order around their centroid
    /// </summary>
    List<Vector2> SortVerticesClockwise(List<Vector2> vertices)
    {
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 v in vertices)
        {
            centroid += v;
        }
        centroid /= vertices.Count;
        
        vertices.Sort((a, b) =>
        {
            float angleA = Mathf.Atan2(a.y - centroid.y, a.x - centroid.x);
            float angleB = Mathf.Atan2(b.y - centroid.y, b.x - centroid.x);
            return angleA.CompareTo(angleB);
        });
        
        return vertices;
    }
    
    /// <summary>
    /// Checks if two shapes approximately match (within tolerance)
    /// </summary>
    bool ShapesMatch(List<Vector2> shape1, List<Vector2> shape2, float tolerance = 0.1f)
    {
        if (shape1.Count != shape2.Count) return false;
        
        Vector2 centroid1 = Vector2.zero;
        Vector2 centroid2 = Vector2.zero;
        
        foreach (Vector2 v in shape1) centroid1 += v;
        foreach (Vector2 v in shape2) centroid2 += v;
        
        centroid1 /= shape1.Count;
        centroid2 /= shape2.Count;
        
        return Vector2.Distance(centroid1, centroid2) < tolerance;
    }
    
    /// <summary>
    /// Calculates the area of a polygon
    /// </summary>
    public static float CalculatePolygonArea(List<Vector2> vertices)
    {
        float area = 0;
        int n = vertices.Count;
        
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += vertices[i].x * vertices[j].y;
            area -= vertices[j].x * vertices[i].y;
        }
        
        return Mathf.Abs(area / 2f);
    }
}