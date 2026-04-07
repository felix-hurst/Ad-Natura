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

    // Event-based notification so external systems (e.g. IncendiaryBall) can
    // receive the spawned piece reference synchronously during the cut call,
    // without needing a polling mechanism or a direct coupling to this class.
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

    // Cached between HighlightCutEdges and ExecuteCut so the cut operation
    // uses exactly the same shape the player saw previewed, even if the
    // collider changes between the two calls.
    private List<Vector2> currentHighlightedShape;

    private ObjectReshape objectReshape;

    // Distinguishes cut-off pieces from the original object they were cut from.
    // The parent object needs different post-cut behaviour (no cleanup timer,
    // gravity enabled) compared to the spawned cut piece.
    private bool isOriginalCutPiece = false;

    // Bounds are captured in world space before any cut modifies the object,
    // so UV coordinates on spawned pieces map to the correct region of the
    // source texture regardless of how the object has been resized since.
    private Bounds originalSpriteBounds;
    private bool hasOriginalSpriteBounds = false;
    private Texture2D originalSpriteTexture;

    // Cached separately because the SpriteRenderer may be destroyed or replaced
    // during a cut, and cut pieces need the colour at the moment of cutting.
    private Color cachedObjectColor = Color.white;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Only capture bounds here if they haven't already been set externally —
        // SetOriginalSpriteBounds may have been called before Start by SpawnLargeCutPiece,
        // and overwriting it here would lose the original object's world-space extent.
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
                // Objects using a procedural mesh (e.g. CalamityObject) have no SpriteRenderer,
                // so fall through to MeshRenderer then collider as progressively weaker sources.
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

        // Colour must be captured from whatever renderer is actually present —
        // cut pieces inherit this colour so they visually match their parent.
        if (spriteRenderer != null)
        {
            cachedObjectColor = spriteRenderer.color;
            Debug.Log($"[RR.Start] {gameObject.name} | source=SpriteRenderer | cachedObjectColor={cachedObjectColor}");
        }
        else
        {
            MeshRenderer mr = GetComponent<MeshRenderer>() ?? GetComponentInChildren<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != null)
            {
                cachedObjectColor = mr.sharedMaterial.color;
                Debug.Log($"[RR.Start] {gameObject.name} | source=MeshRenderer ({mr.gameObject.name}) | sharedMat.color={mr.sharedMaterial.color} | cachedObjectColor={cachedObjectColor}");
            }
            else
            {
                Debug.LogWarning($"[RR.Start] {gameObject.name} | NO renderer found — cachedObjectColor stays WHITE");
            }
        }

        objectReshape = GetComponent<ObjectReshape>();
        if (objectReshape == null)
        {
            objectReshape = gameObject.AddComponent<ObjectReshape>();

            // Sync the captured bounds to ObjectReshape so that if this piece is
            // cut again later it still UV-maps relative to the original object extent.
            if (hasOriginalSpriteBounds)
                objectReshape.SetOriginalSpriteBounds(originalSpriteBounds);
        }
    }

    public void SetCachedColor(Color color)
    {
        Debug.Log($"[RR.SetCachedColor] {gameObject.name} | color={color}");
        cachedObjectColor = color;
    }

    public void MarkAsOriginalCutPiece()
    {
        isOriginalCutPiece = true;
    }

    // Called by the parent before Start so the piece knows the original sprite
    // bounds for correct UV mapping even before Unity has initialised it.
    public void SetOriginalSpriteBounds(Bounds bounds, Texture2D texture = null)
    {
        originalSpriteBounds = bounds;
        hasOriginalSpriteBounds = true;
        if (texture != null)
            originalSpriteTexture = texture;
    }

    // A parent object is one that has never been cut off from another object.
    // Parents get different post-cut treatment (gravity, no cleanup timer)
    // compared to cut pieces that are already detached fragments.
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

        // A valid cut through a convex or simple concave polygon will always
        // produce exactly two edge intersections. Any other count means the cut
        // line didn't cleanly bisect the shape, so fall back to a side-sorting split.
        if (intersections.Count != 2)
        {
            FallbackSplit(vertices, lineStart, lineEnd, out shape1, out shape2);
            return;
        }

        // Sort by t so intersection[0] is always the "earlier" crossing along the
        // cut line, giving BuildSplitShapes a consistent winding direction.
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

        // shape1 walks the polygon boundary from int1 → int2
        shape1.Add(int1.point);
        int current = (int1.edgeIndex + 1) % vertices.Length;
        while (current != (int2.edgeIndex + 1) % vertices.Length)
        {
            shape1.Add(vertices[current]);
            current = (current + 1) % vertices.Length;
        }
        shape1.Add(int2.point);

        // shape2 walks the remaining boundary from int2 back to int1,
        // together forming the two halves of the original polygon.
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

        // Near-zero denominator means the lines are parallel — no intersection exists.
        if (Mathf.Abs(denom) < 0.0001f)
        {
            return false;
        }

        float t = ((p1.x - p3.x) * (p3.y - p4.y) - (p1.y - p3.y) * (p3.x - p4.x)) / denom;
        float u = -((p1.x - p2.x) * (p1.y - p3.y) - (p1.y - p2.y) * (p1.x - p3.x)) / denom;

        // u must be in [0,1] for the intersection to lie on the polygon edge segment
        // rather than on an extension beyond it.
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

        // Negative signed area means counter-clockwise — reverse to match the
        // winding expected by the polygon collider and triangulator.
        if (signedArea < 0)
        {
            vertices.Reverse();
        }

        return vertices;
    }

    // tValue is stored alongside each intersection so the two crossing points
    // can be sorted by their position along the cut line, giving BuildSplitShapes
    // a deterministic ordering that produces correct winding on both halves.
    private class IntersectionData
    {
        public Vector2 point;
        public int edgeIndex;
        public float tValue;
    }

    // Used when the cut line doesn't produce exactly two intersections — classifies
    // every vertex by which side of the cut line it falls on, then sorts each group
    // into a convex-hull-like order so they form a valid polygon.
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

    // Removes near-duplicate vertices that can appear at intersection points,
    // which would otherwise confuse the triangulator or produce degenerate edges.
    List<Vector2> CleanupPolygon(List<Vector2> vertices)
    {
        if (vertices.Count < 3) return vertices;
        List<Vector2> cleaned = new List<Vector2>();
        float minDist = 0.001f;
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

    // Prefers the PolygonCollider2D shape over the renderer bounds because
    // the collider reflects the actual current geometry, including any previous
    // cuts, while a renderer's bounds can only represent an axis-aligned rectangle.
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

    // Player-facing cut entry point — uses the shape that was previewed via
    // HighlightCutEdges so what the player saw is exactly what gets cut.
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

        // ObjectReshape.CutOffPortion may rename the object internally; restore it
        // so the parent retains its identity for later queries and debug output.
        gameObject.name = originalParentName;

        if (cutOffShape != null && cutOffShape.Count >= 3)
        {
            SpawnLargeCutPiece(cutOffShape, totalCutOffArea, entryPoint, exitPoint, materialTag, cutProfile);

            // Move to CutPiece layer so projectile exclusion masks ignore the
            // parent remnant just as they do any other cut fragment.
            int cutPieceLayer = LayerMask.NameToLayer("CutPiece");
            if (cutPieceLayer != -1) gameObject.layer = cutPieceLayer;

            // Make parent dynamic so it responds to gravity after being cut (except Roof)
            if (!gameObject.name.Contains("Roof"))
            {
                Rigidbody2D parentRb = GetComponent<Rigidbody2D>();
                if (parentRb != null && parentRb.bodyType != RigidbodyType2D.Dynamic)
                {
                    parentRb.bodyType = RigidbodyType2D.Dynamic;
                }
            }

            gameObject.name = originalParentName;

            {
                // If this object previously had a cleanup timer (because it was
                // itself a cut piece), remove it now — the parent remnant should
                // persist until explicitly destroyed, not auto-expire.
                CutPieceCleanup existingCleanup = GetComponent<CutPieceCleanup>();
                if (existingCleanup != null)
                {
                    Destroy(existingCleanup);
                    Debug.Log($"[RaycastReceiver] Removed cleanup timer from {gameObject.name} - it's now a parent object");
                }
            }
            isOriginalCutPiece = false;
        }

        // After a cut the parent may have become a sliver too small to interact
        // with meaningfully — clean it up rather than leaving invisible geometry.
        if (enableMinSizeCheck && IsParentObject())
        {
            CheckAndDestroyIfTooSmall();
        }
    }

    // Programmatic cut entry point — skips the highlight preview step so
    // weapons like IncendiaryBall can trigger a cut without the player having
    // aimed at the object first.
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

        // Push the captured bounds to ObjectReshape in case this piece was
        // spawned after the ObjectReshape component was added, when bounds
        // might not have been transferred yet.
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

            // Fire the optional callback so the caller (e.g. IncendiaryBall) can
            // target the new piece without polling or searching the scene.
            if (largePiece != null && onPieceSpawned != null)
                onPieceSpawned.Invoke(largePiece);

            int cutPieceLayer = LayerMask.NameToLayer("CutPiece");
            if (cutPieceLayer != -1) gameObject.layer = cutPieceLayer;

            // Make parent dynamic so it responds to gravity after being cut (except Roof)
            if (!gameObject.name.Contains("Roof"))
            {
                Rigidbody2D parentRb = GetComponent<Rigidbody2D>();
                if (parentRb != null && parentRb.bodyType != RigidbodyType2D.Dynamic)
                {
                    parentRb.bodyType = RigidbodyType2D.Dynamic;
                }
            }

            gameObject.name = originalParentName;
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

        if (enableMinSizeCheck && IsParentObject())
        {
            CheckAndDestroyIfTooSmall();
        }
    }

    GameObject SpawnLargeCutPiece(List<Vector2> cutOffShape, float targetArea, Vector2 entryPoint, Vector2 exitPoint,
                                   string materialTag, CutProfile cutProfile)
    {
        Debug.Log($"[RR.Spawn] SpawnLargeCutPiece on {gameObject.name} | cachedObjectColor={cachedObjectColor}");

        // Capture bounds before the cut modifies the source object, so UV mapping
        // on the new piece is correct even if the parent's renderer changes.
        Bounds spriteBoundsForUV = hasOriginalSpriteBounds ? originalSpriteBounds : GetObjectRendererBounds();
        Texture2D textureForPiece = originalSpriteTexture;
        if (textureForPiece == null && spriteRenderer != null && spriteRenderer.sprite != null)
            textureForPiece = spriteRenderer.sprite.texture;

        GameObject largePiece = new GameObject($"{gameObject.name}_CutPiece");
        try { largePiece.tag = gameObject.tag; } catch (UnityException) { }
        int cutPieceLayer = LayerMask.NameToLayer("CutPiece");
        if (cutPieceLayer != -1)
            largePiece.layer = cutPieceLayer;
        else
            Debug.LogWarning("[RaycastReceiver] 'CutPiece' layer not found — add it in Project Settings > Tags and Layers.");

        // Propagate the structural material type so explosion systems know
        // what kind of debris to spawn if this piece is destroyed later.
        StructuralCollapseManager.ExplosionFragment parentMarker = GetComponent<StructuralCollapseManager.ExplosionFragment>();
        if (parentMarker != null)
        {
            var childMarker = largePiece.AddComponent<StructuralCollapseManager.ExplosionFragment>();
            childMarker.Initialize(parentMarker.materialType);
        }

        Vector2 centroid = Vector2.zero;
        foreach (Vector2 v in cutOffShape) centroid += v;
        centroid /= cutOffShape.Count;

        // Slight random offset from the exact centroid prevents the new piece
        // from spawning perfectly centred on the cut line, which looks unnatural
        // and can cause immediate re-overlap with the parent.
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
            Debug.Log($"[RR.Spawn] {largePiece.name} | pieceSR color from originalSR = {pieceSR.color}");
        }
        else
        {
            // Parent uses a MeshRenderer — copy the cached colour so the piece
            // still matches the original material visually.
            pieceSR.color = cachedObjectColor;
            Debug.Log($"[RR.Spawn] {largePiece.name} | no originalSR — pieceSR color from cachedObjectColor = {pieceSR.color}");
        }

        ObjectReshape pieceReshape = largePiece.AddComponent<ObjectReshape>();
        pieceReshape.SetOriginalSpriteBounds(spriteBoundsForUV);
        pieceReshape.SetRenderColor(cachedObjectColor);

        PixelatedCutRenderer piecePixelRenderer = largePiece.AddComponent<PixelatedCutRenderer>();

        // Convert the cut shape from world space to the new piece's local space
        // before passing it to the mesh and collider builders.
        List<Vector2> currentShape = new List<Vector2>();
        foreach (Vector2 worldVertex in cutOffShape)
            currentShape.Add(largePiece.transform.InverseTransformPoint(worldVertex));

        Vector2 localEntry = largePiece.transform.InverseTransformPoint(entryPoint);
        Vector2 localExit = largePiece.transform.InverseTransformPoint(exitPoint);

        CutProfileManager profileManager = FindObjectOfType<CutProfileManager>();

        // Apply material-specific cut irregularities (e.g. wood splinters, stone chips)
        // to give each material a distinct feel when sliced.
        if (profileManager != null && cutProfile.strength > 0.01f)
        {
            currentShape = profileManager.ApplyIrregularCut(currentShape, localEntry, localExit, cutProfile);
        }

        // Pixelate the polygon outline along the cut edge to give the cut a
        // chunky low-resolution aesthetic consistent with the rest of the visual style.
        if (piecePixelRenderer != null)
        {
            currentShape = piecePixelRenderer.PixelatePolygonWithCutLine(currentShape, localEntry, localExit);
        }

        PolygonCollider2D polyCollider = largePiece.AddComponent<PolygonCollider2D>();
        polyCollider.points = currentShape.ToArray();

        // Disable the collider temporarily while the Rigidbody2D is still Kinematic
        // to avoid it generating collision events before the piece is fully configured.
        polyCollider.enabled = false;

        Debug.Log($"[RR.Spawn] Calling CreateLargePieceMesh | fallbackColor={cachedObjectColor} | texture={(textureForPiece != null ? textureForPiece.name : "NULL")} | parentSR={(originalSR != null ? originalSR.color.ToString() : "NULL")}");
        CreateLargePieceMesh(largePiece, currentShape, materialTag, spriteBoundsForUV, textureForPiece, originalSR, cachedObjectColor);

        // Start kinematic with zero velocity so the piece doesn't inherit any
        // residual solver velocity; it goes dynamic at the end of this method.
        Rigidbody2D rb = largePiece.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.mass = targetArea * largePieceMassMultiplier;
        rb.gravityScale = 1f;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.sleepMode = RigidbodySleepMode2D.StartAwake;
        rb.constraints = RigidbodyConstraints2D.None;

        // Copy all cut settings to the new piece so it can be cut again with
        // the same rules as the original object.
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
        pieceReceiver.SetCachedColor(cachedObjectColor);

        // Flag this piece as a detached cut fragment so it gets different
        // post-cut treatment than the original parent object.
        pieceReceiver.MarkAsOriginalCutPiece();

        Debug.Log($"[RR.Spawn] pieceReceiver on {largePiece.name} | SetCachedColor({cachedObjectColor}) called");

        if (enableAutoCleanup)
        {
            CutPieceCleanup cleanup = largePiece.AddComponent<CutPieceCleanup>();
            cleanup.Initialize(cutPieceLifetime);
        }

        PhysicsMaterialManager physicsManager = FindObjectOfType<PhysicsMaterialManager>();
        if (physicsManager != null) physicsManager.ApplyPhysicsMaterial(largePiece);

        // Re-enable collider and switch to Dynamic now that the piece is fully
        // set up — doing it here avoids any mid-setup physics interactions.
        polyCollider.enabled = true;
        rb.constraints = RigidbodyConstraints2D.None;
        rb.bodyType = RigidbodyType2D.Dynamic;

        // Notify any subscribers (e.g. IncendiaryBall) that the piece is ready.
        if (LargePieceSpawned != null)
            LargePieceSpawned.Invoke(largePiece);

        return largePiece;
    }

    // Resolves world-space bounds from whichever renderer type is present,
    // used as a last resort when the pre-captured originalSpriteBounds are not available.
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
                            Bounds originalBounds, Texture2D sourceTexture, SpriteRenderer parentSR,
                            Color fallbackColor)
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
            // Fall back to the material library when no source texture was
            // captured — this handles objects whose texture was procedurally generated.
            MaterialTextureGenerator textureGenerator = FindObjectOfType<MaterialTextureGenerator>();
            if (textureGenerator != null && !string.IsNullOrEmpty(materialTag))
                texture = textureGenerator.GetTexture(materialTag);
        }

        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Texture");
        Material material = new Material(shader);

        if (texture != null)
            material.mainTexture = texture;

        // Always apply color — Sprites/Default multiplies texture by color.
        // Previously this was inside an else-if, so a blank white texture
        // from MaterialTextureGenerator prevented the color from ever being set.
        if (parentSR != null)
            material.color = fallbackColor;
        else
            material.color = fallbackColor;

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

            // UV coordinates are derived from world position relative to the
            // original object bounds so the texture sample aligns with where
            // the vertex sat on the source object, not where it is in isolation.
            Vector2 worldPos = pieceTransform.TransformPoint(localVertices[i]);
            float u = (worldPos.x - originalBounds.min.x) / originalBounds.size.x;
            float v = (worldPos.y - originalBounds.min.y) / originalBounds.size.y;
            uvs[i] = new Vector2(u, v);
        }

        // Simple fan triangulation from vertex 0 — sufficient for convex and
        // mildly concave cut shapes, and cheaper than a full ear-clip pass.
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

    // Decides which of the two cut halves to detach based on the highlight mode
    // configured for this object — e.g. closest to ground detaches the lower piece,
    // which is the natural expectation for cutting the base off something.
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

    // Returns world-space bounding box corners for objects that have no
    // PolygonCollider2D — used as a coarse rectangular fallback for the split.
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

    // Cross product sign indicates which side of the directed line a point sits on —
    // positive = left, negative = right. Used by FallbackSplit to partition vertices.
    float GetSideOfLine(Vector2 s, Vector2 e, Vector2 p)
        => (e.x - s.x) * (p.y - s.y) - (e.y - s.y) * (p.x - s.x);

    // Sorts an unordered set of vertices into angular order around their centroid,
    // producing a valid non-self-intersecting polygon from the fallback split result.
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
            // Rather than destroying immediately (which could cause mid-frame
            // issues), attach a cleanup timer so the object fades out on the
            // normal lifetime path — and add a collision handler so a physical
            // nudge can clean it up even sooner.
            if (enableAutoCleanup && GetComponent<CutPieceCleanup>() == null)
            {
                var cleanup = gameObject.AddComponent<CutPieceCleanup>();
                cleanup.Initialize(cutPieceLifetime);
            }
            if (GetComponent<SmallParentCollisionHandler>() == null)
                gameObject.AddComponent<SmallParentCollisionHandler>();
        }
    }

    void Update()
    {
        // Continuously check size in Update rather than only after a cut so that
        // objects modified by other systems (e.g. fluid erosion) are also caught.
        if (enableMinSizeCheck && IsParentObject() && GetComponent<CutPieceCleanup>() == null)
            CheckAndDestroyIfTooSmall();
    }

    void OnDestroy() => ClearHighlight();
}

// Minimal timer component kept separate so it can be added and removed from
// objects independently without coupling lifetime logic to RaycastReceiver.
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

// Destroys a too-small parent remnant on the first physical collision rather
// than waiting for the cleanup timer, so barely-visible slivers don't linger
// after being nudged by a falling piece.
public class SmallParentCollisionHandler : MonoBehaviour
{
    void OnCollisionEnter2D(Collision2D collision) => Destroy(gameObject);
}

// Pulses the LineRenderer alpha on the cut-outline preview so it draws the
// eye to the highlighted piece without being a static unreadable line.
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