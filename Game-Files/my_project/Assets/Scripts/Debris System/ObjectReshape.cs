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

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();

        materialTag = gameObject.tag;
        if (string.IsNullOrEmpty(materialTag) || materialTag == "Untagged")
        {
            materialTag = gameObject.name;
        }

        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            originalTexture = spriteRenderer.sprite.texture;
        }

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

        if (spriteRenderer != null && spriteRenderer.sprite != null && originalTexture == null)
        {
            originalTexture = spriteRenderer.sprite.texture;
        }
    }

    public List<Vector2> CutOffPortion(Vector2 entryPoint, Vector2 exitPoint, List<Vector2> highlightedShape)
    {
        if (highlightedShape == null || highlightedShape.Count < 3)
        {
            return null;
        }

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

        Vector2[] corners = GetCurrentShapeVertices();

        List<Vector2> shape1 = new List<Vector2>();
        List<Vector2> shape2 = new List<Vector2>();

        shape1.Add(entryPoint);
        shape1.Add(exitPoint);
        shape2.Add(entryPoint);
        shape2.Add(exitPoint);

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

        shape1 = SortVerticesClockwise(shape1);
        shape2 = SortVerticesClockwise(shape2);

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

        List<Vector2> remainingShapeSmooth = new List<Vector2>(remainingShape);
        List<Vector2> cutOffShapeSmooth = new List<Vector2>(cutOffShape);

        List<Vector2> remainingShapePixelated = remainingShape;
        List<Vector2> cutOffShapePixelated = cutOffShape;

        if (pixelatedCutRenderer != null)
        {
            remainingShapePixelated = pixelatedCutRenderer.PixelatePolygonWithCutLine(remainingShape, entryPoint, exitPoint);
            cutOffShapePixelated = pixelatedCutRenderer.PixelatePolygonWithCutLine(cutOffShape, entryPoint, exitPoint);
        }

        ApplyNewShape(remainingShapePixelated, remainingShapeSmooth);
        UpdateRigidbodyMass(remainingShapeSmooth);

        return cutOffShapePixelated;
    }

    Vector2[] GetCurrentShapeVertices()
    {
        PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
        if (polyCol != null && polyCol.points.Length > 0)
        {
            Vector2[] worldVertices = new Vector2[polyCol.points.Length];
            for (int i = 0; i < polyCol.points.Length; i++)
            {
                worldVertices[i] = transform.TransformPoint(polyCol.points[i]);
            }
            return worldVertices;
        }

        return GetWorldCorners();
    }

    void ApplyNewShape(List<Vector2> visualShape, List<Vector2> colliderShape)
    {
        if (visualShape.Count < 3 || colliderShape.Count < 3) return;

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

        UpdateCollider(useSmoothCollider ? localColliderVertices : localVisualVertices);
        UpdateVisualMesh(localVisualVertices);
    }

    void UpdateCollider(List<Vector2> localVertices)
    {
        Collider2D[] existingColliders = GetComponents<Collider2D>();
        foreach (Collider2D col in existingColliders)
        {
            Destroy(col);
        }

        polygonCollider = gameObject.AddComponent<PolygonCollider2D>();
        polygonCollider.points = localVertices.ToArray();

        PhysicsMaterialManager physicsManager = FindObjectOfType<PhysicsMaterialManager>();
        if (physicsManager != null)
        {
            physicsManager.ApplyPhysicsMaterial(gameObject);
        }
    }

    void UpdateVisualMesh(List<Vector2> localVertices)
    {
        if (localVertices == null || localVertices.Count < 3)
        {
            return;
        }

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        Color spriteColor = Color.white;
        string sortingLayer = "Default";
        int sortingOrder = 0;
        Texture2D textureToUse = originalTexture;

        if (spriteRenderer != null)
        {
            spriteColor = spriteRenderer.color;
            sortingLayer = spriteRenderer.sortingLayerName;
            sortingOrder = spriteRenderer.sortingOrder;

            if (spriteRenderer.sprite != null && spriteRenderer.sprite.texture != null)
            {
                textureToUse = spriteRenderer.sprite.texture;
            }

            spriteRenderer.enabled = false;
        }

        if (textureToUse == null)
        {
            MaterialTextureGenerator textureGenerator = FindObjectOfType<MaterialTextureGenerator>();
            if (textureGenerator != null)
            {
                textureToUse = textureGenerator.GetTexture(materialTag);
            }
        }

        foreach (Transform child in transform)
        {
            if (child.name.Contains("_CutMesh"))
            {
                Destroy(child.gameObject);
            }
        }

        GameObject meshObject = new GameObject($"{gameObject.name}_CutMesh");
        meshObject.transform.SetParent(transform);
        meshObject.transform.localPosition = Vector3.zero;
        meshObject.transform.localRotation = Quaternion.identity;
        meshObject.transform.localScale = Vector3.one;

        MeshFilter newMeshFilter = meshObject.AddComponent<MeshFilter>();
        MeshRenderer newMeshRenderer = meshObject.AddComponent<MeshRenderer>();

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
            Destroy(meshObject);
            return;
        }

        Material newMaterial = new Material(shader);

        if (textureToUse != null)
        {
            newMaterial.mainTexture = textureToUse;
        }
        else
        {
            newMaterial.color = spriteColor;
        }

        newMeshRenderer.material = newMaterial;
        newMeshRenderer.sortingLayerName = sortingLayer;
        newMeshRenderer.sortingOrder = sortingOrder;

        try
        {
            Mesh mesh = CreateMeshFromPolygon(localVertices);
            if (mesh != null && newMeshFilter != null)
            {
                newMeshFilter.mesh = mesh;
                meshFilter = newMeshFilter;
                meshRenderer = newMeshRenderer;
            }
            else
            {
                Destroy(meshObject);
            }
        }
        catch (System.Exception e)
        {
            Destroy(meshObject);
        }
    }

    Mesh CreateMeshFromPolygon(List<Vector2> vertices)
    {
        if (vertices == null || vertices.Count < 3)
        {
            return null;
        }

        Mesh mesh = new Mesh();
        mesh.name = "CutMesh";

        Vector3[] vertices3D = new Vector3[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices3D[i] = new Vector3(vertices[i].x, vertices[i].y, 0);
        }

        int[] triangles = TriangulatePolygon(vertices);

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

    int[] TriangulatePolygon(List<Vector2> vertices)
    {
        List<int> triangles = new List<int>();

        List<int> indices = new List<int>();
        for (int i = 0; i < vertices.Count; i++)
        {
            indices.Add(i);
        }

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

                if (IsEar(prev, curr, next, vertices, indices))
                {
                    triangles.Add(prevIndex);
                    triangles.Add(currIndex);
                    triangles.Add(nextIndex);

                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }

            if (!earFound)
            {
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

        if (indices.Count == 3)
        {
            triangles.Add(indices[0]);
            triangles.Add(indices[1]);
            triangles.Add(indices[2]);
        }

        return triangles.ToArray();
    }

    bool IsEar(Vector2 prev, Vector2 curr, Vector2 next, List<Vector2> vertices, List<int> indices)
    {
        Vector2 v1 = prev - curr;
        Vector2 v2 = next - curr;
        float cross = v1.x * v2.y - v1.y * v2.x;

        if (cross <= 0)
        {
            return false;
        }

        for (int i = 0; i < indices.Count; i++)
        {
            int index = indices[i];
            Vector2 point = vertices[index];

            if (point == prev || point == curr || point == next)
            {
                continue;
            }

            if (IsPointInTriangle(point, prev, curr, next))
            {
                return false;
            }
        }

        return true;
    }

    bool IsPointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float denominator = ((b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y));

        if (Mathf.Abs(denominator) < 0.0001f)
        {
            return false;
        }

        float alpha = ((b.y - c.y) * (point.x - c.x) + (c.x - b.x) * (point.y - c.y)) / denominator;
        float beta = ((c.y - a.y) * (point.x - c.x) + (a.x - c.x) * (point.y - c.y)) / denominator;
        float gamma = 1.0f - alpha - beta;

        return alpha > 0 && beta > 0 && gamma > 0;
    }

    void UpdateRigidbodyMass(List<Vector2> newShape)
    {
        if (rb == null) return;

        float newArea = CalculatePolygonArea(newShape);
        float oldMass = rb.mass;

        Vector2[] originalCorners = GetWorldCorners();
        float width = Vector2.Distance(originalCorners[0], originalCorners[1]);
        float height = Vector2.Distance(originalCorners[0], originalCorners[3]);
        float originalArea = width * height;

        if (originalArea > 0)
        {
            float areaRatio = newArea / originalArea;
            rb.mass = oldMass * areaRatio;
        }
    }

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

    float GetSideOfLine(Vector2 lineStart, Vector2 lineEnd, Vector2 point)
    {
        return (lineEnd.x - lineStart.x) * (point.y - lineStart.y) -
               (lineEnd.y - lineStart.y) * (point.x - lineStart.x);
    }

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