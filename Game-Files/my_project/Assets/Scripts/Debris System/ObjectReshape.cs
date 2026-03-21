using UnityEngine;
using System.Collections.Generic;

public class ObjectReshape : MonoBehaviour
{
    [Header("Collider Settings")]
    [SerializeField] private bool useSmoothCollider = true;

    private SpriteRenderer spriteRenderer;
    private PolygonCollider2D polygonCollider;
    private Rigidbody2D rb;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private PixelatedCutRenderer pixelatedCutRenderer;

    private Texture2D originalTexture;
    private string materialTag;

    // Original sprite world bounds captured at Start — used for UV mapping after cuts
    private Bounds originalSpriteBounds;
    private bool hasOriginalSpriteBounds = false;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();

        materialTag = gameObject.tag;
        if (string.IsNullOrEmpty(materialTag) || materialTag == "Untagged")
            materialTag = gameObject.name;

        if (spriteRenderer != null && spriteRenderer.sprite != null)
            originalTexture = spriteRenderer.sprite.texture;

        pixelatedCutRenderer = GetComponent<PixelatedCutRenderer>();
        if (pixelatedCutRenderer == null)
            pixelatedCutRenderer = gameObject.AddComponent<PixelatedCutRenderer>();
    }

    void Start()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (pixelatedCutRenderer == null)
        {
            pixelatedCutRenderer = GetComponent<PixelatedCutRenderer>();
            if (pixelatedCutRenderer == null) pixelatedCutRenderer = gameObject.AddComponent<PixelatedCutRenderer>();
        }
        if (spriteRenderer != null && spriteRenderer.sprite != null && originalTexture == null)
            originalTexture = spriteRenderer.sprite.texture;

        // Capture the original sprite bounds before any cuts happen
        if (!hasOriginalSpriteBounds)
        {
            if (spriteRenderer != null)
            {
                originalSpriteBounds = spriteRenderer.bounds;
                hasOriginalSpriteBounds = true;
            }
            else
            {
                var col = GetComponent<Collider2D>();
                if (col != null) { originalSpriteBounds = col.bounds; hasOriginalSpriteBounds = true; }
            }
        }
    }

    /// <summary>
    /// Called by RaycastReceiver on cut pieces to set the original bounds for UV mapping.
    /// </summary>
    public void SetOriginalSpriteBounds(Bounds bounds, Texture2D texture = null)
    {
        originalSpriteBounds = bounds;
        hasOriginalSpriteBounds = true;
    }

    public List<Vector2> CutOffPortion(Vector2 entryPoint, Vector2 exitPoint, List<Vector2> highlightedShape)
    {
        if (highlightedShape == null || highlightedShape.Count < 3)
        {
            Debug.LogWarning("[ObjectReshape] Invalid highlighted shape");
            return null;
        }

        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (pixelatedCutRenderer == null) pixelatedCutRenderer = GetComponent<PixelatedCutRenderer>();

        // Capture bounds before the cut changes anything
        if (!hasOriginalSpriteBounds)
        {
            if (spriteRenderer != null) { originalSpriteBounds = spriteRenderer.bounds; hasOriginalSpriteBounds = true; }
        }

        Vector2[] corners = GetCurrentShapeVertices();
        if (corners.Length < 3) { Debug.LogWarning("[ObjectReshape] Invalid polygon shape"); return null; }

        List<Vector2> shape1, shape2;
        SplitPolygonByLine(corners, entryPoint, exitPoint, out shape1, out shape2);
        if (shape1.Count < 3 || shape2.Count < 3) { Debug.LogWarning("[ObjectReshape] Split resulted in invalid shapes"); return null; }

        bool shape1IsHighlighted = ShapesMatch(shape1, highlightedShape);
        List<Vector2> remainingShape = shape1IsHighlighted ? shape2 : shape1;
        List<Vector2> cutOffShape = shape1IsHighlighted ? shape1 : shape2;

        CutProfile cutProfile = CutProfileExtensions.GetCutProfileForObject(gameObject);
        CutProfileManager profileManager = FindObjectOfType<CutProfileManager>();
        if (profileManager != null && cutProfile.strength > 0.01f)
        {
            remainingShape = profileManager.ApplyIrregularCut(remainingShape, entryPoint, exitPoint, cutProfile);
            cutOffShape = profileManager.ApplyIrregularCut(cutOffShape, entryPoint, exitPoint, cutProfile);
        }

        List<Vector2> remainingShapePixelated = remainingShape;
        List<Vector2> cutOffShapePixelated = cutOffShape;
        if (pixelatedCutRenderer != null)
        {
            remainingShapePixelated = pixelatedCutRenderer.PixelatePolygonWithCutLine(remainingShape, entryPoint, exitPoint);
            cutOffShapePixelated = pixelatedCutRenderer.PixelatePolygonWithCutLine(cutOffShape, entryPoint, exitPoint);
        }

        ApplyNewShape(remainingShapePixelated, remainingShape);
        UpdateRigidbodyMass(remainingShape);

        return cutOffShapePixelated;
    }

    void SplitPolygonByLine(Vector2[] vertices, Vector2 lineStart, Vector2 lineEnd,
                            out List<Vector2> shape1, out List<Vector2> shape2)
    {
        shape1 = new List<Vector2>();
        shape2 = new List<Vector2>();
        if (vertices.Length < 3) return;

        List<IntersectionData> intersections = new List<IntersectionData>();
        for (int i = 0; i < vertices.Length; i++)
        {
            int nextI = (i + 1) % vertices.Length;
            Vector2 intersection; float tValue;
            if (LineIntersectionWithT(lineStart, lineEnd, vertices[i], vertices[nextI], out intersection, out tValue))
                intersections.Add(new IntersectionData { point = intersection, edgeIndex = i, tValue = tValue });
        }

        if (intersections.Count != 2)
        {
            Debug.LogWarning($"[ObjectReshape] Found {intersections.Count} intersections, expected 2");
            FallbackSplit(vertices, lineStart, lineEnd, out shape1, out shape2);
            return;
        }

        intersections.Sort((a, b) => a.tValue.CompareTo(b.tValue));
        BuildSplitShapes(vertices, intersections[0], intersections[1], out shape1, out shape2);
        shape1 = CleanupPolygon(EnsureClockwiseWinding(shape1));
        shape2 = CleanupPolygon(EnsureClockwiseWinding(shape2));
    }

    void BuildSplitShapes(Vector2[] vertices, IntersectionData int1, IntersectionData int2,
                          out List<Vector2> shape1, out List<Vector2> shape2)
    {
        shape1 = new List<Vector2>();
        shape2 = new List<Vector2>();

        shape1.Add(int1.point);
        int current = (int1.edgeIndex + 1) % vertices.Length;
        while (current != (int2.edgeIndex + 1) % vertices.Length) { shape1.Add(vertices[current]); current = (current + 1) % vertices.Length; }
        shape1.Add(int2.point);

        shape2.Add(int2.point);
        current = (int2.edgeIndex + 1) % vertices.Length;
        while (current != (int1.edgeIndex + 1) % vertices.Length) { shape2.Add(vertices[current]); current = (current + 1) % vertices.Length; }
        shape2.Add(int1.point);
    }

    bool LineIntersectionWithT(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 intersection, out float tValue)
    {
        intersection = Vector2.zero; tValue = 0f;
        float denom = (p1.x - p2.x) * (p3.y - p4.y) - (p1.y - p2.y) * (p3.x - p4.x);
        if (Mathf.Abs(denom) < 0.0001f) return false;
        float t = ((p1.x - p3.x) * (p3.y - p4.y) - (p1.y - p3.y) * (p3.x - p4.x)) / denom;
        float u = -((p1.x - p2.x) * (p1.y - p3.y) - (p1.y - p2.y) * (p1.x - p3.x)) / denom;
        if (u >= 0f && u <= 1f)
        {
            intersection = new Vector2(p1.x + t * (p2.x - p1.x), p1.y + t * (p2.y - p1.y));
            tValue = t; return true;
        }
        return false;
    }

    void FallbackSplit(Vector2[] vertices, Vector2 lineStart, Vector2 lineEnd, out List<Vector2> shape1, out List<Vector2> shape2)
    {
        shape1 = new List<Vector2> { lineStart, lineEnd };
        shape2 = new List<Vector2> { lineStart, lineEnd };
        foreach (Vector2 v in vertices)
        {
            if (GetSideOfLine(lineStart, lineEnd, v) > 0) shape1.Add(v); else shape2.Add(v);
        }
        shape1 = SortVerticesClockwise(shape1);
        shape2 = SortVerticesClockwise(shape2);
    }

    List<Vector2> CleanupPolygon(List<Vector2> vertices)
    {
        if (vertices.Count < 3) return vertices;
        List<Vector2> cleaned = new List<Vector2>();
        float minDist = 0.05f;
        foreach (Vector2 v in vertices)
        {
            bool dup = false;
            foreach (Vector2 e in cleaned) if (Vector2.Distance(v, e) < minDist) { dup = true; break; }
            if (!dup) cleaned.Add(v);
        }
        if (cleaned.Count > 2 && Vector2.Distance(cleaned[0], cleaned[cleaned.Count - 1]) < minDist)
            cleaned.RemoveAt(cleaned.Count - 1);
        return cleaned.Count < 3 ? vertices : cleaned;
    }

    List<Vector2> EnsureClockwiseWinding(List<Vector2> vertices)
    {
        if (vertices.Count < 3) return vertices;
        float signedArea = 0f;
        for (int i = 0; i < vertices.Count; i++)
        {
            int j = (i + 1) % vertices.Count;
            signedArea += (vertices[j].x - vertices[i].x) * (vertices[j].y + vertices[i].y);
        }
        if (signedArea < 0) vertices.Reverse();
        return vertices;
    }

    private class IntersectionData { public Vector2 point; public int edgeIndex; public float tValue; }

    Vector2[] GetCurrentShapeVertices()
    {
        PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
        if (polyCol != null && polyCol.points.Length > 0)
        {
            Vector2[] world = new Vector2[polyCol.points.Length];
            for (int i = 0; i < polyCol.points.Length; i++) world[i] = transform.TransformPoint(polyCol.points[i]);
            return world;
        }
        return GetWorldCorners();
    }

    public void ApplyNewShape(List<Vector2> visualShape, List<Vector2> colliderShape)
    {
        if (visualShape.Count < 3 || colliderShape.Count < 3) return;

        List<Vector2> localVisual = new List<Vector2>();
        foreach (Vector2 w in visualShape) localVisual.Add(transform.InverseTransformPoint(w));

        List<Vector2> localCollider = new List<Vector2>();
        foreach (Vector2 w in colliderShape) localCollider.Add(transform.InverseTransformPoint(w));

        UpdateCollider(useSmoothCollider ? localCollider : localVisual);
        UpdateVisualMesh(localVisual, visualShape); // pass world verts for UV calculation
    }

    public void ApplyNewShape(List<Vector2> worldShape) => ApplyNewShape(worldShape, worldShape);

    void UpdateCollider(List<Vector2> localVertices)
    {
        foreach (Collider2D col in GetComponents<Collider2D>()) Destroy(col);
        polygonCollider = gameObject.AddComponent<PolygonCollider2D>();
        polygonCollider.points = localVertices.ToArray();
        FindObjectOfType<PhysicsMaterialManager>()?.ApplyPhysicsMaterial(gameObject);
    }

    void UpdateVisualMesh(List<Vector2> localVertices, List<Vector2> worldVertices)
    {
        if (localVertices == null || localVertices.Count < 3) return;

        Color spriteColor = Color.white;
        string sortingLayer = "Default";
        int sortingOrder = 0;
        Texture2D textureToUse = originalTexture;

        if (spriteRenderer != null)
        {
            spriteColor = spriteRenderer.color;
            sortingLayer = spriteRenderer.sortingLayerName;
            sortingOrder = spriteRenderer.sortingOrder;
            if (spriteRenderer.sprite?.texture != null)
                textureToUse = spriteRenderer.sprite.texture;
        }

        // NEW: if still no texture, check the existing mesh child's material
        if (textureToUse == null)
        {
            MeshRenderer existingMR = GetComponentInChildren<MeshRenderer>();
            if (existingMR != null && existingMR.sharedMaterial != null)
            {
                textureToUse = existingMR.sharedMaterial.mainTexture as Texture2D;
                // Also inherit sorting layer/order from the existing mesh
                if (sortingLayer == "Default")
                {
                    sortingLayer = existingMR.sortingLayerName;
                    sortingOrder = existingMR.sortingOrder;
                }
            }
        }

        if (textureToUse == null)
        {
            MaterialTextureGenerator gen = FindObjectOfType<MaterialTextureGenerator>();
            if (gen != null) textureToUse = gen.GetTexture(materialTag);
        }

        // Remove old mesh children
        foreach (MeshRenderer mr in GetComponentsInChildren<MeshRenderer>())
            if (mr.gameObject != gameObject) Destroy(mr.gameObject);
        foreach (Transform child in transform)
            if (child.name.Contains("_CutMesh") || child.name.Contains("_Mesh")) Destroy(child.gameObject);

        if (spriteRenderer != null) spriteRenderer.enabled = false;

        GameObject meshObject = new GameObject($"{gameObject.name}_CutMesh");
        meshObject.transform.SetParent(transform);
        meshObject.transform.localPosition = Vector3.zero;
        meshObject.transform.localRotation = Quaternion.identity;
        meshObject.transform.localScale = Vector3.one;

        MeshFilter newMeshFilter = meshObject.AddComponent<MeshFilter>();
        MeshRenderer newMeshRenderer = meshObject.AddComponent<MeshRenderer>();

        Shader shader = null;
        if (spriteRenderer?.sharedMaterial?.shader != null) shader = spriteRenderer.sharedMaterial.shader;
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("UI/Default");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        if (shader == null)
        {
            Debug.LogError($"[ObjectReshape] No shader found for {gameObject.name}");
            Destroy(meshObject);
            if (spriteRenderer != null) spriteRenderer.enabled = true;
            return;
        }

        Material mat = new Material(shader);
        if (textureToUse != null) mat.mainTexture = textureToUse;
        else mat.color = spriteColor;

        newMeshRenderer.material = mat;
        newMeshRenderer.sortingLayerName = sortingLayer;
        newMeshRenderer.sortingOrder = sortingOrder;

        try
        {
            Mesh mesh = CreateMeshWithSpriteBoundsUV(localVertices, worldVertices);
            if (mesh != null && mesh.vertexCount > 0)
            {
                newMeshFilter.mesh = mesh;
                meshFilter = newMeshFilter;
                meshRenderer = newMeshRenderer;
            }
            else
            {
                Destroy(meshObject);
                if (spriteRenderer != null) spriteRenderer.enabled = true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ObjectReshape] Mesh exception on {gameObject.name}: {e.Message}");
            Destroy(meshObject);
            if (spriteRenderer != null) spriteRenderer.enabled = true;
        }
    }

    /// <summary>
    /// Creates a mesh where each vertex's UV is its world position mapped against
    /// the original sprite's world bounds — so the remaining piece shows its correct
    /// portion of the source texture after a cut.
    /// </summary>
    Mesh CreateMeshWithSpriteBoundsUV(List<Vector2> localVertices, List<Vector2> worldVertices)
    {
        if (localVertices == null || localVertices.Count < 3) return null;

        Mesh mesh = new Mesh();
        mesh.name = "CutMesh";

        Vector3[] verts3D = new Vector3[localVertices.Count];
        Vector2[] uvs = new Vector2[localVertices.Count];

        for (int i = 0; i < localVertices.Count; i++)
        {
            verts3D[i] = new Vector3(localVertices[i].x, localVertices[i].y, 0);

            if (hasOriginalSpriteBounds && originalSpriteBounds.size.x > 0 && originalSpriteBounds.size.y > 0)
            {
                // Map world position to UV using original sprite bounds
                Vector2 worldPos = i < worldVertices.Count ? worldVertices[i] : (Vector2)transform.TransformPoint(localVertices[i]);
                uvs[i] = new Vector2(
                    (worldPos.x - originalSpriteBounds.min.x) / originalSpriteBounds.size.x,
                    (worldPos.y - originalSpriteBounds.min.y) / originalSpriteBounds.size.y
                );
            }
            else
            {
                // Fallback: local bounds UV
                Bounds local = GetLocalBounds(localVertices);
                uvs[i] = local.size.x > 0 && local.size.y > 0
                    ? new Vector2((localVertices[i].x - local.min.x) / local.size.x,
                                  (localVertices[i].y - local.min.y) / local.size.y)
                    : new Vector2(0.5f, 0.5f);
            }
        }

        int[] triangles = TriangulatePolygon(localVertices);
        if (triangles == null || triangles.Length < 3) return null;

        mesh.vertices = verts3D;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    int[] TriangulatePolygon(List<Vector2> vertices)
    {
        List<int> triangles = new List<int>();
        List<int> indices = new List<int>();
        for (int i = 0; i < vertices.Count; i++) indices.Add(i);

        while (indices.Count > 3)
        {
            bool earFound = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int prev = indices[(i - 1 + indices.Count) % indices.Count];
                int curr = indices[i];
                int next = indices[(i + 1) % indices.Count];
                if (IsEar(vertices[prev], vertices[curr], vertices[next], vertices, indices))
                {
                    triangles.Add(prev); triangles.Add(curr); triangles.Add(next);
                    indices.RemoveAt(i); earFound = true; break;
                }
            }
            if (!earFound)
            {
                triangles.Clear();
                for (int i = 1; i < vertices.Count - 1; i++) { triangles.Add(0); triangles.Add(i); triangles.Add(i + 1); }
                break;
            }
        }
        if (indices.Count == 3) { triangles.Add(indices[0]); triangles.Add(indices[1]); triangles.Add(indices[2]); }
        return triangles.ToArray();
    }

    bool IsEar(Vector2 prev, Vector2 curr, Vector2 next, List<Vector2> vertices, List<int> indices)
    {
        Vector2 v1 = prev - curr, v2 = next - curr;
        if (v1.x * v2.y - v1.y * v2.x <= 0) return false;
        foreach (int idx in indices)
        {
            Vector2 p = vertices[idx];
            if (p == prev || p == curr || p == next) continue;
            if (IsPointInTriangle(p, prev, curr, next)) return false;
        }
        return true;
    }

    bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float denom = ((b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y));
        if (Mathf.Abs(denom) < 0.0001f) return false;
        float alpha = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / denom;
        float beta  = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / denom;
        float gamma = 1f - alpha - beta;
        return alpha > 0 && beta > 0 && gamma > 0;
    }

    void UpdateRigidbodyMass(List<Vector2> newShape)
    {
        if (rb == null) return;
        float newArea = CalculatePolygonArea(newShape);
        Vector2[] corners = GetWorldCorners();
        float w = Vector2.Distance(corners[0], corners[1]);
        float h = Vector2.Distance(corners[0], corners[3]);
        float origArea = w * h;
        if (origArea > 0) rb.mass = rb.mass * (newArea / origArea);
    }

    Vector2[] GetWorldCorners()
    {
        Bounds bounds;
        if (meshRenderer != null && meshRenderer.enabled) bounds = meshRenderer.bounds;
        else if (spriteRenderer != null) bounds = spriteRenderer.bounds;
        else
        {
            PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
            if (polyCol != null && polyCol.points.Length > 0)
            {
                Vector2 min = transform.TransformPoint(polyCol.points[0]), max = min;
                foreach (Vector2 lp in polyCol.points)
                {
                    Vector2 wp = transform.TransformPoint(lp);
                    min.x = Mathf.Min(min.x, wp.x); min.y = Mathf.Min(min.y, wp.y);
                    max.x = Mathf.Max(max.x, wp.x); max.y = Mathf.Max(max.y, wp.y);
                }
                bounds = new Bounds((min + max) / 2f, max - min);
            }
            else bounds = new Bounds(transform.position, Vector3.one);
        }
        Vector2 c = bounds.center, e = bounds.extents;
        return new Vector2[]
        {
            new Vector2(c.x - e.x, c.y - e.y), new Vector2(c.x + e.x, c.y - e.y),
            new Vector2(c.x + e.x, c.y + e.y), new Vector2(c.x - e.x, c.y + e.y)
        };
    }

    Bounds GetLocalBounds(List<Vector2> vertices)
    {
        if (vertices.Count == 0) return new Bounds();
        Vector2 min = vertices[0], max = vertices[0];
        foreach (Vector2 v in vertices)
        {
            min.x = Mathf.Min(min.x, v.x); min.y = Mathf.Min(min.y, v.y);
            max.x = Mathf.Max(max.x, v.x); max.y = Mathf.Max(max.y, v.y);
        }
        return new Bounds((min + max) / 2f, max - min);
    }

    float GetSideOfLine(Vector2 s, Vector2 e, Vector2 p)
        => (e.x - s.x) * (p.y - s.y) - (e.y - s.y) * (p.x - s.x);

    List<Vector2> SortVerticesClockwise(List<Vector2> vertices)
    {
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 v in vertices) centroid += v;
        centroid /= vertices.Count;
        vertices.Sort((a, b) =>
            Mathf.Atan2(a.y - centroid.y, a.x - centroid.x)
                .CompareTo(Mathf.Atan2(b.y - centroid.y, b.x - centroid.x)));
        return vertices;
    }

    bool ShapesMatch(List<Vector2> shape1, List<Vector2> shape2, float tolerance = 0.1f)
    {
        if (shape1.Count != shape2.Count) return false;
        Vector2 c1 = Vector2.zero, c2 = Vector2.zero;
        foreach (Vector2 v in shape1) c1 += v;
        foreach (Vector2 v in shape2) c2 += v;
        return Vector2.Distance(c1 / shape1.Count, c2 / shape2.Count) < tolerance;
    }

    public static float CalculatePolygonArea(List<Vector2> vertices)
    {
        float area = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            int j = (i + 1) % vertices.Count;
            area += vertices[i].x * vertices[j].y - vertices[j].x * vertices[i].y;
        }
        return Mathf.Abs(area / 2f);
    }
}