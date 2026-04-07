using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CalamityObject : MonoBehaviour
{
    [Header("Shape Identity")]
    [Tooltip("Unique seed for this object's shape. -1 = random.")]
    public int shapeSeed = -1;

    [Header("Dimensions")]
    public float baseWidth = 1.5f;
    public float height = 4f;
    public float topWidth = 0.4f;

    [Header("Surface Detail")]
    [Range(3, 30)] public int edgeVerticesPerSide = 10;
    [Range(0f, 0.5f)] public float surfaceRoughness = 0.15f;
    [Range(0f, 1f)] public float asymmetry = 0.3f;
    [Range(0, 5)] public int branchCount = 1;
    [Range(0f, 1f)] public float branchSize = 0.3f;

    [Header("Sprout Animation")]
    public bool animateSprout = false;
    public float sproutDuration = 1.2f;
    public AnimationCurve sproutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public float groundShakeMagnitude = 0.05f;
    public float groundShakeDuration = 0.3f;

    [Header("Visual")]
    public Color baseColor = new Color(0.15f, 0.08f, 0.2f, 1f);
    public string materialTag = "Calamity";
    public int sortingOrder = 5;
    public string sortingLayer = "Default";

    [Header("Pixel Visual")]
    [Range(4, 32)] public int pixelTextureSize = 16;
    [Range(0f, 1f)] public float pixelColorVariance = 0.12f;

    [Header("Mist (Fluid Simulation)")]
    public bool enableMist = true;
    [Tooltip("Primary mist color. Keep dark.")]
    public Color mistColor = new Color(0.04f, 0.02f, 0.06f, 1f);
    [Tooltip("Secondary mist color blended for variety.")]
    public Color mistColorAlt = new Color(0.06f, 0.03f, 0.08f, 1f);
    [Range(0f, 1f)]
    public float mistOpacity = 0.75f;
    [Range(0f, 30f)]
    [Tooltip("Vorticity confinement — controls wispy curl amount.")]
    public float mistVorticity = 25f;
    [Range(0f, 0.05f)]
    [Tooltip("Strength of the upward velocity impulse each frame.")]
    public float mistEmitterStrength = 0.025f;
    [Range(0f, 0.12f)]
    [Tooltip("How much density is injected per frame.")]
    public float mistDensityStrength = 0.06f;
    [Range(64, 256)]
    [Tooltip("Fluid grid resolution. 128 is a good balance.")]
    public int mistResolution = 64;
    [Tooltip("Layers the mist treats as solid obstacles to flow around.")]
    public LayerMask mistObstacleLayerMask = ~0;

    [Header("Physics")]
    public float mass = 5f;
    public bool isStatic = true;
    public PhysicsMaterial2D physicsMaterial;

    [Header("Destruction Integration")]
    public RaycastReceiver.HighlightMode highlightMode = RaycastReceiver.HighlightMode.ClosestToGround;
    public bool showCutOutline = true;
    public float largePieceMassMultiplier = 0.5f;
    public float cutPieceLifetime = 30f;
    public float minAreaThreshold = 0.15f;

    private List<Vector2> generatedShape;
    private Mesh visualMesh;

    // Guards against double-spawning if Spawn is called more than once.
    private bool hasSpawned = false;
    private Vector3 targetScale;
    private float groundY;

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public void Spawn(float groundYPosition)
    {
        if (hasSpawned) return;
        hasSpawned = true;
        groundY = groundYPosition;

        // Save and restore Unity's global random state so shape generation with
        // a fixed seed produces identical geometry regardless of what other code
        // has been calling Random before this point.
        int seed = shapeSeed == -1 ? Random.Range(0, 999999) : shapeSeed;
        Random.State previousState = Random.state;
        Random.InitState(seed);
        generatedShape = GenerateMonolithShape();
        Random.state = previousState;

        BuildObject();

        if (animateSprout)
            StartCoroutine(SproutAnimation());
    }

    // Instant variant skips the sprout coroutine — useful for objects that
    // need to be fully collidable from the first frame (e.g. pre-placed scenery).
    public void SpawnInstant(float groundYPosition)
    {
        if (hasSpawned) return;
        hasSpawned = true;
        groundY = groundYPosition;

        int seed = shapeSeed == -1 ? Random.Range(0, 999999) : shapeSeed;
        Random.State previousState = Random.state;
        Random.InitState(seed);
        generatedShape = GenerateMonolithShape();
        Random.state = previousState;

        BuildObject();
    }

    public List<Vector2> GetShape() => generatedShape;
    public bool HasSpawned() => hasSpawned;

    // Allows a previously static object to start participating in physics,
    // for example when it is cut free from the ground by a destruction event.
    public void MakeDynamic()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 1f;
            isStatic = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shape generation
    // ─────────────────────────────────────────────────────────────────────────

    List<Vector2> GenerateMonolithShape()
    {
        List<Vector2> vertices = new List<Vector2>();
        float halfBase = baseWidth * 0.5f;
        float halfTop = topWidth * 0.5f;

        // Lean gives each monolith a slight tilt so they don't all look identical
        // even when their seeds are close together.
        float leanAmount = Random.Range(-asymmetry, asymmetry) * height * 0.35f;

        // Horizontal constriction bands create the organic "waist" indentations
        // that prevent the silhouette from looking like a plain trapezoid.
        int bandCount = Random.Range(2, 5);
        float[] bandT = new float[bandCount];
        float[] bandDepth = new float[bandCount];
        for (int b = 0; b < bandCount; b++)
        {
            bandT[b] = Random.Range(0.12f, 0.88f);
            bandDepth[b] = Random.Range(0.08f, 0.22f);
        }

        // Bottom-left corner — the contour is built counter-clockwise starting here.
        vertices.Add(new Vector2(-halfBase, 0f));

        // Left edge — built bottom-to-top so the winding order stays consistent
        // with Unity's polygon collider expectations.
        for (int i = 1; i <= edgeVerticesPerSide; i++)
        {
            float t = (float)i / (edgeVerticesPerSide + 1);
            float y = t * height;
            float baseProfile = Mathf.Lerp(halfBase, halfTop, EaseInMonolith(t));

            // Gaussian squeeze around each band centre — strong at the band,
            // fading to zero away from it, so indentations blend naturally.
            float squeeze = 0f;
            for (int b = 0; b < bandCount; b++)
            {
                float influence = Mathf.Exp(-Mathf.Pow((t - bandT[b]) * 9f, 2f));
                squeeze += influence * bandDepth[b] * baseProfile;
            }
            float w = Mathf.Max(baseProfile - squeeze, halfTop * 0.25f);

            // Fractal Perlin noise adds organic surface roughness. Multiple
            // octaves give both large-scale undulation and fine-grained detail.
            float noise = 0f, amp = surfaceRoughness * w * 0.9f, freq = 1f;
            for (int oct = 0; oct < 4; oct++)
            {
                noise += (Mathf.PerlinNoise(t * 4f * freq + 0.5f, oct * 4.1f) - 0.5f) * 2f * amp;
                amp *= 0.55f; freq *= 2.1f;
            }
            vertices.Add(new Vector2(-(w + noise) + leanAmount * t, y));
        }

        // Crown forms the jagged top silhouette between the left and right edges.
        vertices.AddRange(GenerateCrown(leanAmount, halfTop));

        // Right edge — built top-to-bottom to complete the closed polygon.
        // A different noise seed offset (+10f) ensures the right side doesn't
        // mirror the left, which would look unnatural.
        for (int i = edgeVerticesPerSide; i >= 1; i--)
        {
            float t = (float)i / (edgeVerticesPerSide + 1);
            float y = t * height;
            float baseProfile = Mathf.Lerp(halfBase, halfTop, EaseInMonolith(t));

            float squeeze = 0f;
            for (int b = 0; b < bandCount; b++)
            {
                float influence = Mathf.Exp(-Mathf.Pow((t - bandT[b]) * 9f, 2f));
                squeeze += influence * bandDepth[b] * baseProfile;
            }
            float w = Mathf.Max(baseProfile - squeeze, halfTop * 0.25f);

            float noise = 0f, amp = surfaceRoughness * w * 2.0f, freq = 1f;
            for (int oct = 0; oct < 4; oct++)
            {
                noise += (Mathf.PerlinNoise(t * 4f * freq + 0.5f, oct * 4.1f + 10f) - 0.5f) * 2f * amp;
                amp *= 0.55f; freq *= 2.1f;
            }
            vertices.Add(new Vector2((w + noise) + leanAmount * t, y));
        }

        // Bottom-right corner closes the polygon back at ground level.
        vertices.Add(new Vector2(halfBase, 0f));

        if (branchCount > 0)
        {
            vertices = AddBranches(vertices);
        }

        return vertices;
    }

    List<Vector2> GenerateCrown(float leanAmount, float halfTop)
    {
        List<Vector2> crown = new List<Vector2>();

        // Odd point count so there is always a centre peak directly above the
        // monolith's axis, which reads as the dominant highest point.
        int crownPoints = Random.Range(1, 3) * 2 + 1;
        float spread = halfTop * Random.Range(0.9f, 1.4f);

        for (int i = 0; i < crownPoints; i++)
        {
            float t = (float)i / (crownPoints - 1);
            float x = Mathf.Lerp(-spread, spread, t) + leanAmount;

            // Alternate between peaks (even index) and valleys (odd index) to
            // create the serrated top silhouette characteristic of the aesthetic.
            bool isPeak = (i % 2 == 0);

            // Centre peaks are taller — the monolith's highest point is in the
            // middle, which draws the eye upward and looks more imposing.
            float centreFalloff = Mathf.Clamp01(1f - Mathf.Abs(t - 0.5f) * 1.6f);
            float peakY = isPeak
                ? height + Random.Range(0.03f, 0.10f) * height * (0.1f + centreFalloff * 0.15f)
                : height - Random.Range(0.02f, 0.06f) * height * 0.15f;
            x += Random.Range(-halfTop * 0.1f, halfTop * 0.1f);
            crown.Add(new Vector2(x, peakY));
        }
        return crown;
    }

    List<Vector2> AddBranches(List<Vector2> shape)
    {
        List<Vector2> result = new List<Vector2>(shape);
        for (int b = 0; b < branchCount; b++)
        {
            // Reject edge vertices at the very top or bottom — branches near
            // the ground would clip into the floor, and crown branches would
            // overlap the intentional silhouette.
            int edgeIndex = -1;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                int idx = Random.Range(1, result.Count - 1);
                if (result[idx].y > height * 0.18f && result[idx].y < height * 0.88f)
                { edgeIndex = idx; break; }
            }
            if (edgeIndex == -1) continue;

            Vector2 anchor = result[edgeIndex];
            // Branch always grows away from the body centre, not inward.
            Vector2 outward = (anchor.x < 0f ? Vector2.left : Vector2.right);
            outward = (outward + Vector2.up * Random.Range(0.1f, 0.5f)).normalized;
            Vector2 perp = new Vector2(-outward.y, outward.x);
            float shelfLen = baseWidth * branchSize * Random.Range(0.7f, 1.3f);
            float shelfThick = shelfLen * Random.Range(0.25f, 0.55f);
            int segments = Random.Range(3, 6);

            // Build the branch outline as a sub-list of vertices that will be
            // inserted directly into the parent polygon, keeping it a single
            // non-holed shape that the triangulator and collider can handle.
            List<Vector2> shelf = new List<Vector2>();
            shelf.Add(anchor + perp * (-shelfThick * 0.4f));
            for (int s = 0; s <= segments; s++)
            {
                float st = (float)s / segments;
                // Sin envelope gives the branch a natural bulge in the middle
                // rather than a constant width.
                float reach = shelfLen * Mathf.Sin(st * Mathf.PI) * Random.Range(0.75f, 1.05f);
                reach = Mathf.Max(reach, shelfLen * 0.08f);
                float hVar = Random.Range(-shelfThick * 0.35f, shelfThick * 0.35f);
                shelf.Add(anchor + outward * reach + perp * (shelfThick * 0.5f + hVar));
            }
            shelf.Add(anchor + perp * (shelfThick * 0.4f));
            for (int s = 0; s < shelf.Count; s++)
                result.Insert(edgeIndex + 1 + s, shelf[s]);
        }
        return result;
    }

    // Smoothstep variant — keeps the profile narrow near the base and wider higher up,
    // so the monolith reads as rooted and heavy rather than a simple tapered rectangle.
    float EaseInMonolith(float t) => t * t * (3f - 2f * t);

    // ─────────────────────────────────────────────────────────────────────────
    // Object construction
    // ─────────────────────────────────────────────────────────────────────────

    void BuildObject()
    {
        if (generatedShape == null || generatedShape.Count < 3) return;

        gameObject.tag = materialTag;
        gameObject.layer = LayerMask.NameToLayer("Default");

        // The polygon collider is driven entirely by the procedural shape so
        // physics matches the visual silhouette exactly.
        PolygonCollider2D polyCollider = gameObject.GetComponent<PolygonCollider2D>();
        if (polyCollider == null) polyCollider = gameObject.AddComponent<PolygonCollider2D>();
        polyCollider.points = generatedShape.ToArray();
        if (physicsMaterial != null) polyCollider.sharedMaterial = physicsMaterial;

        // Continuous detection prevents fast-moving projectiles from tunnelling
        // through the monolith between physics steps.
        Rigidbody2D rb = gameObject.GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.mass = mass;
        rb.bodyType = isStatic ? RigidbodyType2D.Static : RigidbodyType2D.Dynamic;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // The SpriteRenderer is disabled because the actual visual comes from
        // the procedural mesh child object, not a sprite atlas cell.
        SpriteRenderer sr = gameObject.GetComponent<SpriteRenderer>();
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
        sr.color = baseColor; sr.sortingOrder = sortingOrder;
        sr.sortingLayerName = sortingLayer; sr.enabled = false;

        BuildVisualMesh();

        if (enableMist)
            BuildFluidMist();

        ObjectReshape reshape = gameObject.GetComponent<ObjectReshape>();
        if (reshape == null) reshape = gameObject.AddComponent<ObjectReshape>();

        // The RaycastReceiver (cut/destruction logic) is deferred until after the
        // sprout animation finishes so the object can't be destroyed mid-animation.
        if (!animateSprout)
            AttachRaycastReceiver();

        PhysicsMaterialManager physicsManager = FindObjectOfType<PhysicsMaterialManager>();
        if (physicsManager != null) physicsManager.ApplyPhysicsMaterial(gameObject);
    }

    void AttachRaycastReceiver()
    {
        RaycastReceiver receiver = gameObject.GetComponent<RaycastReceiver>();
        if (receiver == null) receiver = gameObject.AddComponent<RaycastReceiver>();
        receiver.highlightMode = highlightMode;
        receiver.showCutOutline = showCutOutline;
        receiver.largePieceMassMultiplier = largePieceMassMultiplier;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Visual mesh
    // ─────────────────────────────────────────────────────────────────────────

    void BuildVisualMesh()
    {
        // Destroy any stale mesh child from a previous build so hot-reloading
        // in the editor doesn't accumulate duplicate renderers.
        foreach (Transform child in transform)
            if (child.name.Contains("_CalamityMesh")) Destroy(child.gameObject);

        GameObject meshObj = new GameObject($"{gameObject.name}_CalamityMesh");
        meshObj.transform.SetParent(transform);
        meshObj.transform.localPosition = Vector3.zero;
        meshObj.transform.localRotation = Quaternion.identity;
        meshObj.transform.localScale = Vector3.one;

        MeshFilter meshFilter = meshObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshObj.AddComponent<MeshRenderer>();

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Texture");

        Material mat = new Material(shader);
        mat.mainTexture = GeneratePixelTexture();
        mat.color = Color.white; // tint lives in the texture now

        meshRenderer.material = mat;
        meshRenderer.sortingLayerName = sortingLayer;
        meshRenderer.sortingOrder = sortingOrder;

        visualMesh = CreateMeshFromPolygon(generatedShape);
        if (visualMesh != null) meshFilter.mesh = visualMesh;
    }

    Texture2D GeneratePixelTexture()
    {
        int size = pixelTextureSize;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

        // Point filtering keeps pixel edges sharp, preserving the intentional
        // lo-fi aesthetic instead of blurring the colour variation.
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;

        // Use the same seeded random state so the texture matches the shape
        int seed = shapeSeed == -1 ? 0 : shapeSeed;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float v = (float)y / size;

                // Two octaves of Perlin noise for surface colour variation —
                // low frequency gives broad patches, high frequency adds grit.
                float n = Mathf.PerlinNoise(u * 3.7f + seed * 0.001f, v * 3.7f) * 0.65f
                         + Mathf.PerlinNoise(u * 8.1f + seed * 0.002f, v * 8.1f) * 0.35f;

                // Darken toward the edges to give a subtle rim that makes the
                // object read as three-dimensional without needing a real light source.
                float rim = Mathf.Clamp01(Mathf.Min(u, 1f - u, v, 1f - v) * 4f);
                n *= 0.85f + rim * 0.15f;

                // Shift the base colour slightly per pixel so the surface has
                // organic variation rather than being a flat fill.
                float shift = (n - 0.5f) * pixelColorVariance;
                Color c = new Color(
                    Mathf.Clamp01(baseColor.r + shift),
                    Mathf.Clamp01(baseColor.g + shift * 0.8f),
                    // Blue channel gets a stronger shift to add a slight cool/warm
                    // variation that reads as subsurface depth on dark materials.
                    Mathf.Clamp01(baseColor.b + shift * 1.2f),
                    baseColor.a);

                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return tex;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fluid mist — two layers: behind and in front of the rock
    // ─────────────────────────────────────────────────────────────────────────

    void BuildFluidMist()
    {
        foreach (Transform child in transform)
            if (child.name.Contains("_FluidMist")) Destroy(child.gameObject);

        // Two layers are used so mist appears to wrap around the rock —
        // the back layer reads as mist rising from behind, the front layer
        // as mist pooling at the base in front of it. Together they sell
        // depth without needing volumetric rendering.

        // Both quads are IDENTICAL in size and position so their edge fades
        // overlap perfectly and blend at the rock silhouette. The only difference
        // is sorting order — one renders behind the rock, one in front.

        // Back layer — behind the rock
        CreateFluidInstance(
            label: "_FluidMistBack",
            sortOrder: sortingOrder - 1,
            opacity: mistOpacity * 0.6f,
            quadWidthMult: 7.0f,
            quadHeightMult: 7.0f,
            quadYOff: -0.05f,
            emitY: 0.10f,
            emitStrength: mistEmitterStrength * 0.8f,
            densStrength: mistDensityStrength * 0.8f,
            vorticity: mistVorticity * 1.1f
        );

        // Front layer — same quad dimensions as back layer so silhouette edges
        // align. Slightly stronger emitter so it reads as the primary mist source.
        CreateFluidInstance(
            label: "_FluidMistFront",
            sortOrder: sortingOrder + 1,
            opacity: mistOpacity * 0.6f,
            quadWidthMult: 4.0f,
            quadHeightMult: 2.0f,
            quadYOff: -0.05f,
            emitY: 0.12f,
            emitStrength: mistEmitterStrength,
            densStrength: mistDensityStrength,
            vorticity: mistVorticity
        );
    }

    void CreateFluidInstance(string label, int sortOrder, float opacity,
                             float quadWidthMult, float quadHeightMult, float quadYOff,
                             float emitY, float emitStrength, float densStrength,
                             float vorticity)
    {
        GameObject mistObj = new GameObject($"{gameObject.name}{label}");
        mistObj.transform.SetParent(transform);
        mistObj.transform.localPosition = Vector3.zero;
        mistObj.transform.localRotation = Quaternion.identity;
        mistObj.transform.localScale = Vector3.one;

        CalamityMistFluid fluid = mistObj.AddComponent<CalamityMistFluid>();
        fluid.resolution = mistResolution;
        fluid.obstacleLayerMask = mistObstacleLayerMask;

        fluid.Initialise(
            width: baseWidth,
            height: height,
            color: mistColor,
            colorAlt: mistColorAlt,
            opacity: opacity,
            sortOrder: sortOrder,
            sortLayer: sortingLayer,
            quadWidthMult: quadWidthMult,
            quadHeightMult: quadHeightMult,
            quadYOff: quadYOff,
            emitY: emitY,
            emitStrength: emitStrength,
            densStrength: densStrength,
            vorticity: vorticity
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mesh utilities
    // ─────────────────────────────────────────────────────────────────────────

    Mesh CreateMeshFromPolygon(List<Vector2> vertices)
    {
        if (vertices == null || vertices.Count < 3) return null;
        Mesh mesh = new Mesh { name = "CalamityMesh" };

        Vector3[] verts3D = new Vector3[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
            verts3D[i] = new Vector3(vertices[i].x, vertices[i].y, 0f);

        int[] triangles = TriangulatePolygon(vertices);
        if (triangles == null || triangles.Length < 3) return null;

        // UV coordinates are normalised to the polygon's bounding box so the
        // tiling texture maps to the full extent of the shape regardless of size.
        Vector2[] uvs = new Vector2[vertices.Count];
        Bounds bounds = CalculateBounds(vertices);
        if (bounds.size.x > 0 && bounds.size.y > 0)
        {
            for (int i = 0; i < vertices.Count; i++)
                uvs[i] = new Vector2(
                    (vertices[i].x - bounds.min.x) / bounds.size.x,
                    (vertices[i].y - bounds.min.y) / bounds.size.y);
        }

        mesh.vertices = verts3D; mesh.triangles = triangles; mesh.uv = uvs;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // Ear-clipping triangulation converts the arbitrary concave polygon into
    // triangles the GPU can render. An ear is a triangle whose interior contains
    // no other polygon vertices, making it safe to cut away.
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

                Vector2 a = vertices[prev], b = vertices[curr], c = vertices[next];

                // Cross product sign determines winding — negative cross means the
                // vertex is reflex (concave) and cannot be an ear tip.
                float cross = (a.x - b.x) * (c.y - b.y) - (a.y - b.y) * (c.x - b.x);
                if (cross <= 0) continue;

                bool containsPoint = false;
                for (int j = 0; j < indices.Count; j++)
                {
                    int idx = indices[j];
                    if (idx == prev || idx == curr || idx == next) continue;
                    if (PointInTriangle(vertices[idx], a, b, c)) { containsPoint = true; break; }
                }

                if (!containsPoint)
                {
                    triangles.Add(prev); triangles.Add(curr); triangles.Add(next);
                    indices.RemoveAt(i); earFound = true; break;
                }
            }

            // If no ear can be found the polygon may be self-intersecting or
            // degenerate. Fall back to a simple fan from vertex 0, which at
            // least produces a renderable result rather than an empty mesh.
            if (!earFound)
            {
                triangles.Clear();
                for (int i = 1; i < vertices.Count - 1; i++)
                { triangles.Add(0); triangles.Add(i); triangles.Add(i + 1); }
                break;
            }
        }

        if (indices.Count == 3)
        { triangles.Add(indices[0]); triangles.Add(indices[1]); triangles.Add(indices[2]); }

        return triangles.ToArray();
    }

    // Barycentric coordinates test — a point is inside the triangle when all
    // three weights are positive, meaning it lies on the correct side of every edge.
    bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d = ((b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y));
        if (Mathf.Abs(d) < 0.0001f) return false;
        float alpha = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / d;
        float beta = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / d;
        float gamma = 1f - alpha - beta;
        return alpha > 0 && beta > 0 && gamma > 0;
    }

    Bounds CalculateBounds(List<Vector2> vertices)
    {
        Vector2 min = vertices[0], max = vertices[0];
        foreach (Vector2 v in vertices)
        {
            min.x = Mathf.Min(min.x, v.x); min.y = Mathf.Min(min.y, v.y);
            max.x = Mathf.Max(max.x, v.x); max.y = Mathf.Max(max.y, v.y);
        }
        return new Bounds((min + max) / 2f, (Vector3)(max - min));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sprout animation
    // ─────────────────────────────────────────────────────────────────────────

    IEnumerator SproutAnimation()
    {
        targetScale = transform.localScale;

        // Start with zero Y scale so the object appears to rise from the ground.
        transform.localScale = new Vector3(targetScale.x, 0f, targetScale.z);

        // Disable the collider while growing so the monolith doesn't push
        // physics objects away during the animation — it should look like it's
        // emerging through the ground, not shoving things aside.
        PolygonCollider2D col = GetComponent<PolygonCollider2D>();
        if (col != null) col.enabled = false;

        if (groundShakeMagnitude > 0)
            StartCoroutine(GroundShake());

        float elapsed = 0f;
        while (elapsed < sproutDuration)
        {
            elapsed += Time.deltaTime;
            float curveValue = sproutCurve.Evaluate(Mathf.Clamp01(elapsed / sproutDuration));
            transform.localScale = new Vector3(targetScale.x, targetScale.y * curveValue, targetScale.z);
            yield return null;
        }

        transform.localScale = targetScale;

        // Re-enable the collider and attach the destruction receiver only after
        // the animation finishes so the object is fully formed before it can be cut.
        if (col != null) col.enabled = true;
        AttachRaycastReceiver();
    }

    // Lateral shake during the sprout implies the monolith is bursting through
    // the ground with force. Intensity ramps down so the shake decays naturally.
    IEnumerator GroundShake()
    {
        Vector3 originalPos = transform.position;
        float elapsed = 0f;
        while (elapsed < groundShakeDuration)
        {
            elapsed += Time.deltaTime;
            float intensity = (1f - elapsed / groundShakeDuration) * groundShakeMagnitude;
            transform.position = originalPos + new Vector3(Random.Range(-intensity, intensity), 0f, 0f);
            yield return null;
        }
        transform.position = originalPos;
    }
}