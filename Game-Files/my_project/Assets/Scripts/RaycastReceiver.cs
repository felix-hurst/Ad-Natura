using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RaycastReceiver : MonoBehaviour
{
    [Header("Highlight Settings")]
    [Tooltip("Choose which piece to highlight after the cut")]
    public HighlightMode highlightMode = HighlightMode.Default;

    [Tooltip("Enable/disable the shape outline when aiming with the cut tool")]
    public bool showCutOutline = true;

    public enum HighlightMode
    {
        Default,
        ClosestToGround,
        FarthestFromGround
    }

    public delegate void OnLargePieceSpawned(GameObject piece);
    public event OnLargePieceSpawned LargePieceSpawned;

    [Header("Large Piece Settings")]
    [Tooltip("Entire cut piece becomes a large piece (no debris generation)")]
    public float largePieceMassMultiplier = 0.5f;

    [Tooltip("Force range applied to large cut pieces")]
    public Vector2 largePieceForceRange = new Vector2(1f, 3f);

    [Header("Cleanup Settings")]
    [Tooltip("Time in seconds before cut pieces are automatically destroyed")]
    [SerializeField] public float cutPieceLifetime = 30f;

    [Tooltip("Enable automatic cleanup of cut pieces")]
    [SerializeField] public bool enableAutoCleanup = true;

    [Tooltip("Minimum area threshold - parent objects at or below this size will be cleaned up")]
    [SerializeField] public float minAreaThreshold = 0.15f;

    [Tooltip("Enable automatic destruction of too-small parent objects")]
    [SerializeField] public bool enableMinSizeCheck = true;

    private LineRenderer edgeLineRenderer;
    private SpriteRenderer spriteRenderer;
    private List<Vector2> currentHighlightedShape;

    private ObjectReshape objectReshape;
    private bool isOriginalCutPiece = false;

    // Stores the original sprite world bounds so cut pieces can UV-map correctly
    private Bounds originalSpriteBounds;
    private bool hasOriginalSpriteBounds = false;
    private Texture2D originalSpriteTexture;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Only capture bounds here if they haven't already been set externally
        // (SetOriginalSpriteBounds may have been called before Start by SpawnLargeCutPiece)
        if (!hasOriginalSpriteBounds)
        {
            if (spriteRenderer != null)
            {
                originalSpriteBounds = spriteRenderer.bounds;
                hasOriginalSpriteBounds = true;
                if (spriteRenderer.sprite != null)
                    originalSpriteTexture = spriteRenderer.sprite.texture;
            }
            else
            {
                // Fallback for objects using a mesh renderer instead of a sprite
                MeshRenderer mr = GetComponent<MeshRenderer>() ?? GetComponentInChildren<MeshRenderer>();
                if (mr != null)
                {
                    originalSpriteBounds = mr.bounds;
                    hasOriginalSpriteBounds = true;
                }
                else
                {
                    Collider2D col = GetComponent<Collider2D>();
                    if (col != null) { originalSpriteBounds = col.bounds; hasOriginalSpriteBounds = true; }
                }
            }
        }

        objectReshape = GetComponent<ObjectReshape>();
        if (objectReshape == null)
        {
            objectReshape = gameObject.AddComponent<ObjectReshape>();

        // Sync bounds to ObjectReshape so it also UV-maps correctly when this piece is cut again
        if (hasOriginalSpriteBounds)
            objectReshape.SetOriginalSpriteBounds(originalSpriteBounds);
        }
    }

    public void MarkAsOriginalCutPiece()
    {
        isOriginalCutPiece = true;
    }

    /// <summary>
    /// Called by the parent when spawning a cut piece so it knows the original sprite bounds
    /// for correct UV mapping.
    /// </summary>
    public void SetOriginalSpriteBounds(Bounds bounds, Texture2D texture = null)
    {
        originalSpriteBounds = bounds;
        hasOriginalSpriteBounds = true;
        if (texture != null)
            originalSpriteTexture = texture;
    }

    private bool IsParentObject() => !isOriginalCutPiece;

    public void HighlightCutEdges(Vector2 entryPoint, Vector2 exitPoint)
    {
        ClearHighlight();
        Vector2[] corners = GetCurrentShapeVertices();
        if (corners.Length < 3)
        {
            return;
        }

        List<Vector2> shape1, shape2;
        SplitPolygonByLine(corners, entryPoint, exitPoint, out shape1, out shape2);

        if (shape1.Count < 3 || shape2.Count < 3)
        {
            return;
        }

        shape1 = EnsureClockwiseWinding(shape1);
        shape2 = EnsureClockwiseWinding(shape2);
        currentHighlightedShape = ChooseShapeToHighlight(shape1, shape2);

        if (showCutOutline)
        {
            DrawShapeOutline(currentHighlightedShape);
        }
    }

    void SplitPolygonByLine(Vector2[] vertices, Vector2 lineStart, Vector2 lineEnd,
                            out List<Vector2> shape1, out List<Vector2> shape2)
    {
        shape1 = new List<Vector2>();
        shape2 = new List<Vector2>();
        if (vertices.Length < 3)
        {
            return;
        }

        List<IntersectionData> intersections = new List<IntersectionData>();
        for (int i = 0; i < vertices.Length; i++)
        {
            int nextI = (i + 1) % vertices.Length;
            Vector2 intersection;
            float tValue;
            if (LineIntersectionWithT(lineStart, lineEnd, vertices[i], vertices[nextI], out intersection, out tValue))
                intersections.Add(new IntersectionData { point = intersection, edgeIndex = i, tValue = tValue });
        }

        if (intersections.Count != 2)
        {
            FallbackSplit(vertices, lineStart, lineEnd, out shape1, out shape2);
            return;
        }

        intersections.Sort((a, b) => a.tValue.CompareTo(b.tValue));
        BuildSplitShapes(vertices, intersections[0], intersections[1], out shape1, out shape2);
        shape1 = CleanupPolygon(shape1);
        shape2 = CleanupPolygon(shape2);
    }

    void BuildSplitShapes(Vector2[] vertices, IntersectionData int1, IntersectionData int2,
                          out List<Vector2> shape1, out List<Vector2> shape2)
    {
        shape1 = new List<Vector2>();
        shape2 = new List<Vector2>();

        shape1.Add(int1.point);
        int current = (int1.edgeIndex + 1) % vertices.Length;
        while (current != (int2.edgeIndex + 1) % vertices.Length)
        {
            shape1.Add(vertices[current]);
            current = (current + 1) % vertices.Length;
        }

        shape1.Add(int2.point);

        shape2.Add(int2.point);

        current = (int2.edgeIndex + 1) % vertices.Length;
        while (current != (int1.edgeIndex + 1) % vertices.Length)
        {
            shape2.Add(vertices[current]);
            current = (current + 1) % vertices.Length;
        }

        shape2.Add(int1.point);
    }

    bool LineIntersectionWithT(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4,
                               out Vector2 intersection, out float tValue)
    {
        intersection = Vector2.zero;
        tValue = 0f;
        float denom = (p1.x - p2.x) * (p3.y - p4.y) - (p1.y - p2.y) * (p3.x - p4.x);
        if (Mathf.Abs(denom) < 0.0001f)
        {
            return false;
        }

        float t = ((p1.x - p3.x) * (p3.y - p4.y) - (p1.y - p3.y) * (p3.x - p4.x)) / denom;
        float u = -((p1.x - p2.x) * (p1.y - p3.y) - (p1.y - p2.y) * (p1.x - p3.x)) / denom;

        if (u >= 0f && u <= 1f)
        {
            intersection = new Vector2(p1.x + t * (p2.x - p1.x), p1.y + t * (p2.y - p1.y));
            tValue = t;
            return true;
        }

        return false;
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

        if (signedArea < 0)
        {
            vertices.Reverse();
        }

        return vertices;
    }

    private class IntersectionData
    {
        public Vector2 point;
        public int edgeIndex;
        public float tValue;
    }

    void FallbackSplit(Vector2[] vertices, Vector2 lineStart, Vector2 lineEnd,
                       out List<Vector2> shape1, out List<Vector2> shape2)
    {
        shape1 = new List<Vector2> { lineStart, lineEnd };
        shape2 = new List<Vector2> { lineStart, lineEnd };
        foreach (Vector2 v in vertices)
        {
            float side = GetSideOfLine(lineStart, lineEnd, v);
            if (side > 0) shape1.Add(v);
            else shape2.Add(v);
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
            bool isDuplicate = false;
            foreach (Vector2 e in cleaned)
                if (Vector2.Distance(v, e) < minDist) { isDuplicate = true; break; }
            if (!isDuplicate) cleaned.Add(v);
        }
        if (cleaned.Count > 2 && Vector2.Distance(cleaned[0], cleaned[cleaned.Count - 1]) < minDist)
            cleaned.RemoveAt(cleaned.Count - 1);
        return cleaned.Count < 3 ? vertices : cleaned;
    }

    Vector2[] GetCurrentShapeVertices()
    {
        PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
        if (polyCol != null && polyCol.points.Length > 0)
        {
            Vector2[] world = new Vector2[polyCol.points.Length];
            for (int i = 0; i < polyCol.points.Length; i++)
                world[i] = transform.TransformPoint(polyCol.points[i]);
            return world;
        }
        return GetWorldCorners();
    }

    public void ExecuteCut(Vector2 entryPoint, Vector2 exitPoint)
    {
        if (currentHighlightedShape == null || currentHighlightedShape.Count < 3) return;

        float totalCutOffArea = ObjectReshape.CalculatePolygonArea(currentHighlightedShape);
        string materialTag = gameObject.tag;
        if (string.IsNullOrEmpty(materialTag) || materialTag == "Untagged")
        {
            materialTag = gameObject.name;
        }

        string originalParentName = gameObject.name;

        CutProfile cutProfile = CutProfileExtensions.GetCutProfileForObject(gameObject);

        List<Vector2> cutOffShape = objectReshape.CutOffPortion(entryPoint, exitPoint, currentHighlightedShape);

        gameObject.name = originalParentName;

        if (cutOffShape != null && cutOffShape.Count >= 3)
        {
            SpawnLargeCutPiece(cutOffShape, totalCutOffArea, entryPoint, exitPoint, materialTag, cutProfile);

            gameObject.name = originalParentName;

            if (isOriginalCutPiece)
            {

                CutPieceCleanup existingCleanup = GetComponent<CutPieceCleanup>();
                if (existingCleanup != null)
                {
                    Destroy(existingCleanup);
                    Debug.Log($"[RaycastReceiver] Removed cleanup timer from {gameObject.name} - it's now a parent object");
                }
            }
            isOriginalCutPiece = false;
        }

        // Check if the remaining PARENT object is too small after the cut
        if (enableMinSizeCheck && IsParentObject())
        {
            CheckAndDestroyIfTooSmall();
        }
    }

    public void ExecuteCutDirect(Vector2 entryPoint, Vector2 exitPoint, OnLargePieceSpawned onPieceSpawned = null)
    {
        if (objectReshape == null)
        {
            objectReshape = GetComponent<ObjectReshape>();
            if (objectReshape == null)
            {
                return;
            }
        }

        // Make sure ObjectReshape has the correct bounds before the cut happens
        if (hasOriginalSpriteBounds)
            objectReshape.SetOriginalSpriteBounds(originalSpriteBounds);

        Vector2[] corners = GetCurrentShapeVertices();
        List<Vector2> shape1, shape2;
        SplitPolygonByLine(corners, entryPoint, exitPoint, out shape1, out shape2);
        if (shape1.Count < 3 || shape2.Count < 3)
        {
            return;
        }

        List<Vector2> cutOffShape = ChooseShapeToHighlight(shape1, shape2);

        float totalCutOffArea = ObjectReshape.CalculatePolygonArea(cutOffShape);

        string materialTag = gameObject.tag;
        if (string.IsNullOrEmpty(materialTag) || materialTag == "Untagged")
        {
            materialTag = gameObject.name;
        }

        string originalParentName = gameObject.name;

        CutProfile cutProfile = CutProfileExtensions.GetCutProfileForObject(gameObject);

        List<Vector2> actualCutShape = objectReshape.CutOffPortion(entryPoint, exitPoint, cutOffShape);

        gameObject.name = originalParentName;

        if (actualCutShape != null && actualCutShape.Count >= 3)
        {
            GameObject largePiece = SpawnLargeCutPiece(actualCutShape, totalCutOffArea, entryPoint, exitPoint, materialTag, cutProfile);

            if (largePiece != null && onPieceSpawned != null)
            {
                onPieceSpawned.Invoke(largePiece);
            }

            gameObject.name = originalParentName;

            if (isOriginalCutPiece)
            {
                CutPieceCleanup existingCleanup = GetComponent<CutPieceCleanup>();
                if (existingCleanup != null)
                {
                    Destroy(existingCleanup);
                    Debug.Log($"[RaycastReceiver] Removed cleanup timer from {gameObject.name} - it's now a parent object");
                }
            }
            isOriginalCutPiece = false;
        }

        // Check if the remaining PARENT object is too small after the cut
        if (enableMinSizeCheck && IsParentObject())
        {
            CheckAndDestroyIfTooSmall();
        }
    }

    GameObject SpawnLargeCutPiece(List<Vector2> cutOffShape, float targetArea, Vector2 entryPoint, Vector2 exitPoint,
                                   string materialTag, CutProfile cutProfile)
    {
        Debug.Log($"[RaycastReceiver] SpawnLargeCutPiece on {gameObject.name}");

        // Capture the original sprite bounds (world space) before the object changes
        Bounds spriteBoundsForUV = hasOriginalSpriteBounds ? originalSpriteBounds : GetObjectRendererBounds();
        Texture2D textureForPiece = originalSpriteTexture;
        if (textureForPiece == null && spriteRenderer != null && spriteRenderer.sprite != null)
            textureForPiece = spriteRenderer.sprite.texture;

GameObject largePiece = new GameObject($"{gameObject.name}_CutPiece");
try { largePiece.tag = gameObject.tag; } catch (UnityException) { }
int cutPieceLayer = LayerMask.NameToLayer("CutPiece");
Debug.Log($"[RaycastReceiver] CutPiece layer index = {cutPieceLayer}, setting on '{largePiece.name}'");
if (cutPieceLayer != -1)
{
    largePiece.layer = cutPieceLayer;
    Debug.Log($"[RaycastReceiver] '{largePiece.name}' layer is now: {LayerMask.LayerToName(largePiece.layer)}");
}
else
    Debug.LogWarning("[RaycastReceiver] 'CutPiece' layer not found — add it in Project Settings > Tags and Layers.");

        StructuralCollapseManager.ExplosionFragment parentMarker = GetComponent<StructuralCollapseManager.ExplosionFragment>();
        if (parentMarker != null)
        {
            var childMarker = largePiece.AddComponent<StructuralCollapseManager.ExplosionFragment>();
            childMarker.Initialize(parentMarker.materialType);
        }

        Vector2 centroid = Vector2.zero;
        foreach (Vector2 v in cutOffShape) centroid += v;
        centroid /= cutOffShape.Count;

        Vector2 cutDirection = (exitPoint - entryPoint).normalized;
        Vector2 perpendicular = new Vector2(-cutDirection.y, cutDirection.x);
        centroid += perpendicular * 0.1f * (Random.value > 0.5f ? 1f : -1f);
        centroid += Random.insideUnitCircle * 0.03f;

        largePiece.transform.position = new Vector3(centroid.x, centroid.y, transform.position.z);

        SpriteRenderer originalSR = GetComponent<SpriteRenderer>();
        SpriteRenderer pieceSR = largePiece.AddComponent<SpriteRenderer>();
        if (originalSR != null)
        {
            pieceSR.sortingLayerName = originalSR.sortingLayerName;
            pieceSR.sortingOrder = originalSR.sortingOrder;
            pieceSR.color = originalSR.color;
        }

        // KEY FIX: store reference and immediately set original bounds so ObjectReshape
        // uses correct UVs when this piece is later cut again (producing _CutMesh)
        ObjectReshape pieceReshape = largePiece.AddComponent<ObjectReshape>();
        pieceReshape.SetOriginalSpriteBounds(spriteBoundsForUV);

        PixelatedCutRenderer piecePixelRenderer = largePiece.AddComponent<PixelatedCutRenderer>();

        List<Vector2> localVertices = new List<Vector2>();
        foreach (Vector2 worldVertex in cutOffShape)
            localVertices.Add(largePiece.transform.InverseTransformPoint(worldVertex));

        CutProfileManager profileManager = FindObjectOfType<CutProfileManager>();
        List<Vector2> irregularShape = localVertices;
        if (profileManager != null && cutProfile.strength > 0.01f)
        {
            Vector2 localEntry = largePiece.transform.InverseTransformPoint(entryPoint);
            Vector2 localExit = largePiece.transform.InverseTransformPoint(exitPoint);
            irregularShape = profileManager.ApplyIrregularCut(localVertices, localEntry, localExit, cutProfile);
        }

        List<Vector2> pixelatedShape = irregularShape;
        if (piecePixelRenderer != null)
        {
            Vector2 localEntry = largePiece.transform.InverseTransformPoint(entryPoint);
            Vector2 localExit = largePiece.transform.InverseTransformPoint(exitPoint);
            pixelatedShape = piecePixelRenderer.PixelatePolygonWithCutLine(irregularShape, localEntry, localExit);
        }

        PolygonCollider2D polyCollider = largePiece.AddComponent<PolygonCollider2D>();
        polyCollider.points = irregularShape.ToArray();
        polyCollider.enabled = false;

        CreateLargePieceMesh(largePiece, pixelatedShape, materialTag, spriteBoundsForUV, textureForPiece, originalSR);

        Rigidbody2D rb = largePiece.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.mass = targetArea * largePieceMassMultiplier;
        rb.gravityScale = 1f;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.sleepMode = RigidbodySleepMode2D.StartAwake;
        rb.constraints = RigidbodyConstraints2D.None;

        // KEY FIX: pass original bounds to the piece's RaycastReceiver so any further
        // cuts on this piece (producing _CutPiece_CutMesh) also UV correctly
        RaycastReceiver pieceReceiver = largePiece.AddComponent<RaycastReceiver>();
        pieceReceiver.highlightMode = this.highlightMode;
        pieceReceiver.showCutOutline = this.showCutOutline;
        pieceReceiver.largePieceMassMultiplier = this.largePieceMassMultiplier;
        pieceReceiver.largePieceForceRange = this.largePieceForceRange;
        pieceReceiver.cutPieceLifetime = this.cutPieceLifetime;
        pieceReceiver.enableAutoCleanup = this.enableAutoCleanup;
        pieceReceiver.minAreaThreshold = this.minAreaThreshold;
        pieceReceiver.enableMinSizeCheck = this.enableMinSizeCheck;
        pieceReceiver.SetOriginalSpriteBounds(spriteBoundsForUV, textureForPiece);
        pieceReceiver.MarkAsOriginalCutPiece();

        if (enableAutoCleanup)
        {
            CutPieceCleanup cleanup = largePiece.AddComponent<CutPieceCleanup>();
            cleanup.Initialize(cutPieceLifetime);
        }

        PhysicsMaterialManager physicsManager = FindObjectOfType<PhysicsMaterialManager>();
        if (physicsManager != null) physicsManager.ApplyPhysicsMaterial(largePiece);

        polyCollider.enabled = true;
        rb.constraints = RigidbodyConstraints2D.None;
        rb.bodyType = RigidbodyType2D.Dynamic;

        if (LargePieceSpawned != null)
            LargePieceSpawned.Invoke(largePiece);

        return largePiece;
    }

    Bounds GetObjectRendererBounds()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null) return sr.bounds;
        var mr = GetComponentInChildren<MeshRenderer>();
        if (mr != null) return mr.bounds;
        var col = GetComponent<Collider2D>();
        if (col != null) return col.bounds;
        return new Bounds(transform.position, Vector3.one);
    }

    void CreateLargePieceMesh(GameObject piece, List<Vector2> localVertices, string materialTag,
                               Bounds originalBounds, Texture2D sourceTexture, SpriteRenderer parentSR)
    {
        if (localVertices == null || localVertices.Count < 3) return;

        GameObject meshObject = new GameObject($"{piece.name}_Mesh");
        meshObject.transform.SetParent(piece.transform);
        meshObject.transform.localPosition = Vector3.zero;
        meshObject.transform.localRotation = Quaternion.identity;
        meshObject.transform.localScale = Vector3.one;

        MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();

        Texture2D texture = sourceTexture;
        if (texture == null)
        {
            MaterialTextureGenerator textureGenerator = FindObjectOfType<MaterialTextureGenerator>();
            if (textureGenerator != null && !string.IsNullOrEmpty(materialTag))
                texture = textureGenerator.GetTexture(materialTag);
        }

        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Texture");
        Material material = new Material(shader);
        if (texture != null) material.mainTexture = texture;
        else if (parentSR != null) material.color = parentSR.color;

        if (parentSR != null)
        {
            meshRenderer.sortingLayerName = parentSR.sortingLayerName;
            meshRenderer.sortingOrder = parentSR.sortingOrder;
        }
        meshRenderer.material = material;

        Mesh mesh = CreateMeshFromPolygonWithSpriteBounds(localVertices, piece.transform, originalBounds);
        if (mesh != null) meshFilter.mesh = mesh;
        else Destroy(meshObject);
    }

    Mesh CreateMeshFromPolygonWithSpriteBounds(List<Vector2> localVertices, Transform pieceTransform, Bounds originalBounds)
    {
        if (localVertices == null || localVertices.Count < 3) return null;

        Mesh mesh = new Mesh();
        mesh.name = "LargePieceMesh";

        Vector3[] vertices3D = new Vector3[localVertices.Count];
        Vector2[] uvs = new Vector2[localVertices.Count];

        for (int i = 0; i < localVertices.Count; i++)
        {
            vertices3D[i] = new Vector3(localVertices[i].x, localVertices[i].y, 0);
            Vector2 worldPos = pieceTransform.TransformPoint(localVertices[i]);
            float u = (worldPos.x - originalBounds.min.x) / originalBounds.size.x;
            float v = (worldPos.y - originalBounds.min.y) / originalBounds.size.y;
            uvs[i] = new Vector2(u, v);
        }

        List<int> triangles = new List<int>();
        for (int i = 1; i < localVertices.Count - 1; i++)
        {
            triangles.Add(0);
            triangles.Add(i);
            triangles.Add(i + 1);
        }

        mesh.vertices = vertices3D;
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    List<Vector2> ChooseShapeToHighlight(List<Vector2> shape1, List<Vector2> shape2)
    {
        float avgY1 = 0, avgY2 = 0;
        foreach (Vector2 v in shape1) avgY1 += v.y;
        avgY1 /= shape1.Count;
        foreach (Vector2 v in shape2) avgY2 += v.y;
        avgY2 /= shape2.Count;
        switch (highlightMode)
        {
            case HighlightMode.ClosestToGround: return avgY1 < avgY2 ? shape1 : shape2;
            default: return avgY1 > avgY2 ? shape1 : shape2;
        }
    }

    Vector2[] GetWorldCorners()
    {
        Bounds bounds;
        MeshRenderer mr = GetComponent<MeshRenderer>() ?? GetComponentInChildren<MeshRenderer>();
        if (mr != null && mr.enabled) bounds = mr.bounds;
        else if (spriteRenderer != null) bounds = spriteRenderer.bounds;
        else
        {
            PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
            if (polyCol != null && polyCol.points.Length > 0)
            {
                Vector2 min = transform.TransformPoint(polyCol.points[0]);
                Vector2 max = min;
                foreach (Vector2 p in polyCol.points)
                {
                    Vector2 w = transform.TransformPoint(p);
                    min.x = Mathf.Min(min.x, w.x); min.y = Mathf.Min(min.y, w.y);
                    max.x = Mathf.Max(max.x, w.x); max.y = Mathf.Max(max.y, w.y);
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

    void DrawShapeOutline(List<Vector2> vertices)
    {
        if (vertices.Count < 2) return;
        if (edgeLineRenderer == null)
        {
            GameObject lineObj = new GameObject($"{gameObject.name}_Outline");
            lineObj.transform.SetParent(transform);
            edgeLineRenderer = lineObj.AddComponent<LineRenderer>();
            edgeLineRenderer.startWidth = 0.08f;
            edgeLineRenderer.endWidth = 0.08f;
            edgeLineRenderer.material = new Material(Shader.Find("Unlit/Color"));
            edgeLineRenderer.material.color = Color.white;
            edgeLineRenderer.startColor = Color.white;
            edgeLineRenderer.endColor = Color.white;
            edgeLineRenderer.sortingOrder = 15;
            edgeLineRenderer.useWorldSpace = true;
            edgeLineRenderer.loop = true;
        }
        edgeLineRenderer.positionCount = vertices.Count;
        for (int i = 0; i < vertices.Count; i++)
            edgeLineRenderer.SetPosition(i, new Vector3(vertices[i].x, vertices[i].y, 0));
    }

    public void ClearHighlight()
    {
        if (edgeLineRenderer != null) { Destroy(edgeLineRenderer.gameObject); edgeLineRenderer = null; }
        currentHighlightedShape = null;
    }

    void CheckAndDestroyIfTooSmall()
    {
        if (!IsParentObject()) return;
        Vector2[] currentVertices = GetCurrentShapeVertices();
        if (currentVertices.Length < 3) { Destroy(gameObject); return; }

        float currentArea = ObjectReshape.CalculatePolygonArea(new List<Vector2>(currentVertices));
        if (currentArea <= minAreaThreshold)
        {
            DebugHighlightTooSmallParent(new List<Vector2>(currentVertices));
            if (enableAutoCleanup && GetComponent<CutPieceCleanup>() == null)
            {
                var cleanup = gameObject.AddComponent<CutPieceCleanup>();
                cleanup.Initialize(cutPieceLifetime);
            }
            if (GetComponent<SmallParentCollisionHandler>() == null)
                gameObject.AddComponent<SmallParentCollisionHandler>();
        }
    }

    void DebugHighlightTooSmallParent(List<Vector2> vertices)
    {
        if (vertices.Count < 2) return;
        LineRenderer debugLine = GetComponent<LineRenderer>();
        if (debugLine == null)
        {
            GameObject lineObj = new GameObject($"{gameObject.name}_TooSmallDebug");
            lineObj.transform.SetParent(transform);
            debugLine = lineObj.AddComponent<LineRenderer>();
            debugLine.startWidth = 0.12f; debugLine.endWidth = 0.12f;
            debugLine.material = new Material(Shader.Find("Unlit/Color"));
            debugLine.material.color = Color.red;
            debugLine.startColor = Color.red; debugLine.endColor = Color.red;
            debugLine.sortingOrder = 20; debugLine.useWorldSpace = true; debugLine.loop = true;
            lineObj.AddComponent<DebugPulseEffect>();
        }
        debugLine.positionCount = vertices.Count;
        for (int i = 0; i < vertices.Count; i++)
            debugLine.SetPosition(i, new Vector3(vertices[i].x, vertices[i].y, 0));
    }

    void Update()
    {
        if (enableMinSizeCheck && IsParentObject() && GetComponent<CutPieceCleanup>() == null)
            CheckAndDestroyIfTooSmall();
    }

    void OnDestroy() => ClearHighlight();
}

public class CutPieceCleanup : MonoBehaviour
{
    private float lifetime = 0f;
    private float maxLifetime = 30f;

    public void Initialize(float lifeTime) => maxLifetime = lifeTime;

    void Update()
    {
        lifetime += Time.deltaTime;
        if (lifetime >= maxLifetime) Destroy(gameObject);
    }
}

public class SmallParentCollisionHandler : MonoBehaviour
{
    void OnCollisionEnter2D(Collision2D collision) => Destroy(gameObject);
}

public class DebugPulseEffect : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private float pulseSpeed = 3f;

    void Start() => lineRenderer = GetComponent<LineRenderer>();

    void Update()
    {
        if (lineRenderer == null) return;
        float alpha = Mathf.Lerp(0.3f, 1f, (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f);
        Color color = lineRenderer.material.color;
        color.a = alpha;
        lineRenderer.material.color = color;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
    }
}