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
    [SerializeField] private float cutPieceLifetime = 30f;
    
    [Tooltip("Enable automatic cleanup of cut pieces")]
    [SerializeField] private bool enableAutoCleanup = true;
    
    [Tooltip("Minimum area threshold - parent objects at or below this size will be cleaned up")]
    [SerializeField] private float minAreaThreshold = 0.15f;
    
    [Tooltip("Enable automatic destruction of too-small parent objects")]
    [SerializeField] private bool enableMinSizeCheck = true;
    
    private LineRenderer edgeLineRenderer;
    private SpriteRenderer spriteRenderer;
    private List<Vector2> currentHighlightedShape;
    
    private ObjectReshape objectReshape;
    private bool isOriginalCutPiece = false; 
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        
        objectReshape = GetComponent<ObjectReshape>();
        if (objectReshape == null)
        {
            objectReshape = gameObject.AddComponent<ObjectReshape>();
        }
    }

    public void MarkAsOriginalCutPiece()
    {
        isOriginalCutPiece = true;
    }
 
    private bool IsParentObject()
    {

        return !isOriginalCutPiece;
    }
    
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
        Vector2 v1 = vertices[i];
        Vector2 v2 = vertices[nextI];
        
        Vector2 intersection;
        float tValue;
        if (LineIntersectionWithT(lineStart, lineEnd, v1, v2, out intersection, out tValue))
        {
            IntersectionData data = new IntersectionData
            {
                point = intersection,
                edgeIndex = i,
                tValue = tValue
            };
            intersections.Add(data);
        }
    }

    if (intersections.Count != 2)
    {
        FallbackSplit(vertices, lineStart, lineEnd, out shape1, out shape2);
        return;
    }

    intersections.Sort((a, b) => a.tValue.CompareTo(b.tValue));
    
    IntersectionData int1 = intersections[0];
    IntersectionData int2 = intersections[1];

    BuildSplitShapes(vertices, int1, int2, out shape1, out shape2);

    shape1 = CleanupPolygon(shape1);
    shape2 = CleanupPolygon(shape2);
}

void BuildSplitShapes(Vector2[] vertices, IntersectionData int1, IntersectionData int2,
                      out List<Vector2> shape1, out List<Vector2> shape2)
{
    shape1 = new List<Vector2>();
    shape2 = new List<Vector2>();
    
    int edge1 = int1.edgeIndex;
    int edge2 = int2.edgeIndex;

    shape1.Add(int1.point);
    
    int current = (edge1 + 1) % vertices.Length;
    while (current != (edge2 + 1) % vertices.Length)
    {
        shape1.Add(vertices[current]);
        current = (current + 1) % vertices.Length;
    }
    
    shape1.Add(int2.point);

    shape2.Add(int2.point);
    
    current = (edge2 + 1) % vertices.Length;
    while (current != (edge1 + 1) % vertices.Length)
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
    
    float x1 = p1.x, y1 = p1.y;
    float x2 = p2.x, y2 = p2.y;
    float x3 = p3.x, y3 = p3.y;
    float x4 = p4.x, y4 = p4.y;
    
    float denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
    
    if (Mathf.Abs(denom) < 0.0001f)
    {
        return false;
    }
    
    float t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
    float u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / denom;

    if (u >= 0f && u <= 1f)
    {
        intersection.x = x1 + t * (x2 - x1);
        intersection.y = y1 + t * (y2 - y1);
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
    shape1 = new List<Vector2>();
    shape2 = new List<Vector2>();
    
    shape1.Add(lineStart);
    shape1.Add(lineEnd);
    shape2.Add(lineStart);
    shape2.Add(lineEnd);
    
    for (int i = 0; i < vertices.Length; i++)
    {
        Vector2 vertex = vertices[i];
        float side = GetSideOfLine(lineStart, lineEnd, vertex);
        
        if (side > 0)
        {
            shape1.Add(vertex);
        }
        else
        {
            shape2.Add(vertex);
        }
    }
    
    shape1 = SortVerticesClockwise(shape1);
    shape2 = SortVerticesClockwise(shape2);
}

List<Vector2> CleanupPolygon(List<Vector2> vertices)
{
    if (vertices.Count < 3) return vertices;
    
    List<Vector2> cleaned = new List<Vector2>();
    float minDistance = 0.05f;
    
    for (int i = 0; i < vertices.Count; i++)
    {
        Vector2 vertex = vertices[i];
        bool isDuplicate = false;

        foreach (Vector2 existing in cleaned)
        {
            if (Vector2.Distance(vertex, existing) < minDistance)
            {
                isDuplicate = true;
                break;
            }
        }
        
        if (!isDuplicate)
        {
            cleaned.Add(vertex);
        }
    }

    if (cleaned.Count > 2)
    {
        if (Vector2.Distance(cleaned[0], cleaned[cleaned.Count - 1]) < minDistance)
        {
            cleaned.RemoveAt(cleaned.Count - 1);
        }
    }

    if (cleaned.Count < 3)
    {
        return vertices; 
    }
    
    return cleaned;
}

private class IntersectionPoint
{
    public Vector2 point;
    public int edgeIndex;
    public float distanceAlongCutLine;
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
    
    public void ExecuteCut(Vector2 entryPoint, Vector2 exitPoint)
    {
        if (currentHighlightedShape == null || currentHighlightedShape.Count < 3)
        {
            return;
        }
        
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

GameObject SpawnLargeCutPiece(List<Vector2> cutOffShape, float targetArea, Vector2 entryPoint, Vector2 exitPoint, string materialTag, CutProfile cutProfile)
{
    Debug.Log($"[RaycastReceiver] SpawnLargeCutPiece called on {gameObject.name} (ID: {gameObject.GetInstanceID()})");
    
    GameObject largePiece = new GameObject($"{gameObject.name}_CutPiece");
    
    Debug.Log($"[RaycastReceiver] Created large piece: {largePiece.name} (ID: {largePiece.GetInstanceID()})");
    try
    {
        largePiece.tag = gameObject.tag;
    }
    catch (UnityException e)
    {
    }
    
    StructuralCollapseManager.ExplosionFragment parentMarker = GetComponent<StructuralCollapseManager.ExplosionFragment>();
    if (parentMarker != null)
    {
        StructuralCollapseManager.ExplosionFragment childMarker = largePiece.AddComponent<StructuralCollapseManager.ExplosionFragment>();
        childMarker.Initialize(parentMarker.materialType);
    }
    
    Vector2 centroid = Vector2.zero;
    foreach (Vector2 v in cutOffShape)
    {
        centroid += v;
    }
    centroid /= cutOffShape.Count;
    
    Vector2 cutDirection = (exitPoint - entryPoint).normalized;
    Vector2 perpendicular = new Vector2(-cutDirection.y, cutDirection.x);
    
    Vector2 separationOffset = perpendicular * 0.1f * (Random.value > 0.5f ? 1f : -1f);
    Vector2 randomJitter = Random.insideUnitCircle * 0.03f;
    centroid += separationOffset + randomJitter;
    
    largePiece.transform.position = new Vector3(centroid.x, centroid.y, transform.position.z);
    
    SpriteRenderer originalSpriteRenderer = GetComponent<SpriteRenderer>();
    SpriteRenderer pieceSpriteRenderer = largePiece.AddComponent<SpriteRenderer>();
    
    if (originalSpriteRenderer != null)
    {
        pieceSpriteRenderer.sortingLayerName = originalSpriteRenderer.sortingLayerName;
        pieceSpriteRenderer.sortingOrder = originalSpriteRenderer.sortingOrder;
        pieceSpriteRenderer.color = originalSpriteRenderer.color;
    }
    
    ObjectReshape pieceReshape = largePiece.AddComponent<ObjectReshape>();
    
    PixelatedCutRenderer piecePixelRenderer = largePiece.AddComponent<PixelatedCutRenderer>();
    
    List<Vector2> localVertices = new List<Vector2>();
    foreach (Vector2 worldVertex in cutOffShape)
    {
        Vector2 localVertex = (Vector2)largePiece.transform.InverseTransformPoint(worldVertex);
        localVertices.Add(localVertex);
    }
    
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
    
    CreateLargePieceMesh(largePiece, pixelatedShape, materialTag);
    
Rigidbody2D rb = largePiece.AddComponent<Rigidbody2D>();
rb.bodyType = RigidbodyType2D.Kinematic;
rb.mass = targetArea * largePieceMassMultiplier;
rb.gravityScale = 1f;

rb.linearVelocity = Vector2.zero;
rb.angularVelocity = 0f;

rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
rb.sleepMode = RigidbodySleepMode2D.StartAwake;

rb.constraints = RigidbodyConstraints2D.None;
    
    RaycastReceiver pieceReceiver = largePiece.AddComponent<RaycastReceiver>();
    pieceReceiver.highlightMode = this.highlightMode;
    pieceReceiver.showCutOutline = this.showCutOutline; 
    pieceReceiver.largePieceMassMultiplier = this.largePieceMassMultiplier;
    pieceReceiver.largePieceForceRange = this.largePieceForceRange;
    pieceReceiver.cutPieceLifetime = this.cutPieceLifetime;
    pieceReceiver.enableAutoCleanup = this.enableAutoCleanup;
    pieceReceiver.minAreaThreshold = this.minAreaThreshold;
    pieceReceiver.enableMinSizeCheck = this.enableMinSizeCheck;

    pieceReceiver.MarkAsOriginalCutPiece();

    if (enableAutoCleanup)
    {
        CutPieceCleanup cleanup = largePiece.AddComponent<CutPieceCleanup>();
        cleanup.Initialize(cutPieceLifetime);
    }

PhysicsMaterialManager physicsManager = FindObjectOfType<PhysicsMaterialManager>();
if (physicsManager != null)
{
    physicsManager.ApplyPhysicsMaterial(largePiece);
}

polyCollider.enabled = true;
rb.constraints = RigidbodyConstraints2D.None;
rb.bodyType = RigidbodyType2D.Dynamic;


    Debug.Log($"[RaycastReceiver] About to invoke LargePieceSpawned event for {largePiece.name}");
    if (LargePieceSpawned != null)
    {
        Debug.Log($"[RaycastReceiver] LargePieceSpawned has {LargePieceSpawned.GetInvocationList().Length} subscribers");
        LargePieceSpawned.Invoke(largePiece);
        Debug.Log($"[RaycastReceiver] LargePieceSpawned event invoked");
    }
    else
    {
        Debug.Log($"[RaycastReceiver] LargePieceSpawned event has no subscribers");
    }
    
    return largePiece;
}

IEnumerator EnablePhysicsAfterDelay(GameObject piece, Rigidbody2D rb, PolygonCollider2D collider, float delay)
{
    yield return new WaitForSeconds(delay);
    
    if (piece != null && rb != null && collider != null)
    {
        collider.enabled = true;
        
        yield return new WaitForFixedUpdate();

        rb.constraints = RigidbodyConstraints2D.None;
        rb.bodyType = RigidbodyType2D.Dynamic;
        
        Debug.Log($"[EnablePhysicsAfterDelay] {piece.name} switched to Dynamic with constraints: {rb.constraints}");
    }
}
    
    void CreateLargePieceMesh(GameObject piece, List<Vector2> localVertices, string materialTag)
    {
        if (localVertices == null || localVertices.Count < 3)
        {
            return;
        }
        
        GameObject meshObject = new GameObject($"{piece.name}_Mesh");
        meshObject.transform.SetParent(piece.transform);
        meshObject.transform.localPosition = Vector3.zero;
        meshObject.transform.localRotation = Quaternion.identity;
        meshObject.transform.localScale = Vector3.one;
        
        MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
        
        MaterialTextureGenerator textureGenerator = FindObjectOfType<MaterialTextureGenerator>();
        Texture2D texture = null;
        
        if (textureGenerator != null && !string.IsNullOrEmpty(materialTag))
        {
            texture = textureGenerator.GetTexture(materialTag);
        }
        
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        
        Material material = new Material(shader);
        if (texture != null)
        {
            material.mainTexture = texture;
        }
        
        SpriteRenderer parentSpriteRenderer = GetComponent<SpriteRenderer>();
        if (parentSpriteRenderer != null)
        {
            meshRenderer.sortingLayerName = parentSpriteRenderer.sortingLayerName;
            meshRenderer.sortingOrder = parentSpriteRenderer.sortingOrder;
            if (texture == null)
            {
                material.color = parentSpriteRenderer.color;
            }
        }
        
        meshRenderer.material = material;
        
        Mesh mesh = CreateMeshFromPolygon(localVertices);
        if (mesh != null)
        {
            meshFilter.mesh = mesh;
        }
        else
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
        mesh.name = "LargePieceMesh";
        
        Vector3[] vertices3D = new Vector3[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices3D[i] = new Vector3(vertices[i].x, vertices[i].y, 0);
        }
        
        List<int> triangles = new List<int>();
        for (int i = 1; i < vertices.Count - 1; i++)
        {
            triangles.Add(0);
            triangles.Add(i);
            triangles.Add(i + 1);
        }
        
        Vector2[] uvs = new Vector2[vertices.Count];
        Bounds bounds = GetLocalBoundsFromVertices(vertices);
        
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
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    Bounds GetLocalBoundsFromVertices(List<Vector2> vertices)
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
    
    List<Vector2> ChooseShapeToHighlight(List<Vector2> shape1, List<Vector2> shape2)
    {
        float avgY1 = 0;
        foreach (Vector2 v in shape1)
        {
            avgY1 += v.y;
        }
        avgY1 /= shape1.Count;
        
        float avgY2 = 0;
        foreach (Vector2 v in shape2)
        {
            avgY2 += v.y;
        }
        avgY2 /= shape2.Count;
        
        switch (highlightMode)
        {
            case HighlightMode.Default:
            case HighlightMode.FarthestFromGround:
                return avgY1 > avgY2 ? shape1 : shape2;
                
            case HighlightMode.ClosestToGround:
                return avgY1 < avgY2 ? shape1 : shape2;
                
            default:
                return avgY1 > avgY2 ? shape1 : shape2;
        }
    }
    
    Vector2[] GetWorldCorners()
    {
        Bounds bounds;
        
        ObjectReshape objectReshape = GetComponent<ObjectReshape>();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        
        if (meshRenderer == null)
        {
            meshRenderer = GetComponentInChildren<MeshRenderer>();
        }
        
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
        {
            edgeLineRenderer.SetPosition(i, new Vector3(vertices[i].x, vertices[i].y, 0));
        }
    }
    
    public void ClearHighlight()
    {
        if (edgeLineRenderer != null)
        {
            Destroy(edgeLineRenderer.gameObject);
            edgeLineRenderer = null;
        }
        
        currentHighlightedShape = null;
    }
    
void CheckAndDestroyIfTooSmall()
{
    if (!IsParentObject())
    {
        return;
    }
    
    Vector2[] currentVertices = GetCurrentShapeVertices();
    
    if (currentVertices.Length < 3)
    {
        Destroy(gameObject);
        return;
    }
    
    List<Vector2> vertexList = new List<Vector2>(currentVertices);
    float currentArea = ObjectReshape.CalculatePolygonArea(vertexList);
    
    if (currentArea <= minAreaThreshold)
    {
        DebugHighlightTooSmallParent(vertexList);

        if (enableAutoCleanup)
        {
            CutPieceCleanup cleanup = GetComponent<CutPieceCleanup>();
            if (cleanup == null)
            {
                cleanup = gameObject.AddComponent<CutPieceCleanup>();
                cleanup.Initialize(cutPieceLifetime);
                Debug.Log($"[RaycastReceiver] Parent object {gameObject.name} is too small (area: {currentArea:F3} <= {minAreaThreshold:F3}). Added cleanup timer.");
            }
        }

        SmallParentCollisionHandler collisionHandler = GetComponent<SmallParentCollisionHandler>();
        if (collisionHandler == null)
        {
            gameObject.AddComponent<SmallParentCollisionHandler>();
        }
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
        debugLine.startWidth = 0.12f;
        debugLine.endWidth = 0.12f;
        debugLine.material = new Material(Shader.Find("Unlit/Color"));
        debugLine.material.color = Color.red;
        debugLine.startColor = Color.red;
        debugLine.endColor = Color.red;
        debugLine.sortingOrder = 20;
        debugLine.useWorldSpace = true;
        debugLine.loop = true;

        lineObj.AddComponent<DebugPulseEffect>();
    }
    
    debugLine.positionCount = vertices.Count;
    for (int i = 0; i < vertices.Count; i++)
    {
        debugLine.SetPosition(i, new Vector3(vertices[i].x, vertices[i].y, 0));
    }
}
    void Update()
    {

        if (enableMinSizeCheck && IsParentObject())
        {

            if (GetComponent<CutPieceCleanup>() == null)
            {
                CheckAndDestroyIfTooSmall();
            }
        }
    }
    
    void OnDestroy()
    {
        ClearHighlight();
    }
}

public class CutPieceCleanup : MonoBehaviour
{
    private float lifetime = 0f;
    private float maxLifetime = 30f;
    
    public void Initialize(float lifeTime)
    {
        maxLifetime = lifeTime;
    }
    
    void Update()
    {
        lifetime += Time.deltaTime;
        
        if (lifetime >= maxLifetime)
        {
            Destroy(gameObject);
        }
    }
}

public class SmallParentCollisionHandler : MonoBehaviour
{
    void OnCollisionEnter2D(Collision2D collision)
    {
        Destroy(gameObject);
    }
}

public class DebugPulseEffect : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private float pulseSpeed = 3f;
    private float minAlpha = 0.3f;
    private float maxAlpha = 1f;
    
    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }
    
    void Update()
    {
        if (lineRenderer != null)
        {
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f);
            Color color = lineRenderer.material.color;
            color.a = alpha;
            lineRenderer.material.color = color;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
        }
    }
}