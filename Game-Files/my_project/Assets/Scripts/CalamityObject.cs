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
    public Color edgeGlowColor = new Color(0.6f, 0.1f, 0.8f, 1f);
    [Range(0f, 1f)] public float glowIntensity = 0.3f;
    public string materialTag = "Calamity";
    public int sortingOrder = 5;
    public string sortingLayer = "Default";

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
    private bool hasSpawned = false;
    private Vector3 targetScale;
    private float groundY;

    public void Spawn(float groundYPosition)
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

        if (animateSprout)
        {
            StartCoroutine(SproutAnimation());
        }
    }

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


    List<Vector2> GenerateMonolithShape()
    {
        List<Vector2> vertices = new List<Vector2>();


        float halfBase = baseWidth * 0.5f;
        float halfTop = topWidth * 0.5f;

        float topOffset = Random.Range(-asymmetry, asymmetry) * halfBase;

        vertices.Add(new Vector2(-halfBase, 0f));

        for (int i = 1; i <= edgeVerticesPerSide; i++)
        {
            float t = (float)i / (edgeVerticesPerSide + 1);
            float y = t * height;

            float widthAtHeight = Mathf.Lerp(halfBase, halfTop, EaseInMonolith(t));

            float noise = (Mathf.PerlinNoise(t * 5f + 0.5f, 0f) - 0.5f) * 2f * surfaceRoughness * widthAtHeight;

            float waist = Mathf.Sin(t * Mathf.PI) * surfaceRoughness * 0.5f * halfBase;

            float x = -(widthAtHeight + noise - waist) + topOffset * t;
            vertices.Add(new Vector2(x, y));
        }

        List<Vector2> crown = GenerateCrown(topOffset, halfTop);
        vertices.AddRange(crown);

        for (int i = edgeVerticesPerSide; i >= 1; i--)
        {
            float t = (float)i / (edgeVerticesPerSide + 1);
            float y = t * height;

            float widthAtHeight = Mathf.Lerp(halfBase, halfTop, EaseInMonolith(t));
            float noise = (Mathf.PerlinNoise(t * 5f + 0.5f, 1f) - 0.5f) * 2f * surfaceRoughness * widthAtHeight;
            float waist = Mathf.Sin(t * Mathf.PI) * surfaceRoughness * 0.5f * halfBase;

            float x = (widthAtHeight + noise - waist) + topOffset * t;
            vertices.Add(new Vector2(x, y));
        }

        vertices.Add(new Vector2(halfBase, 0f));

        if (branchCount > 0)
        {
            vertices = AddBranches(vertices);
        }

        return vertices;
    }

    List<Vector2> GenerateCrown(float topOffset, float halfTop)
    {
        List<Vector2> crown = new List<Vector2>();

        int crownPoints = Random.Range(1, 4) * 2 + 1;
        float crownWidth = halfTop * 2f;

        for (int i = 0; i < crownPoints; i++)
        {
            float t = (float)i / (crownPoints - 1);
            float x = Mathf.Lerp(-halfTop, halfTop, t) + topOffset;

            bool isPeak = (i % 2 == crownPoints / 2 % 2);
            float peakHeight = isPeak
                ? height + Random.Range(0.1f, 0.4f) * height * 0.2f
                : height - Random.Range(0.05f, 0.15f) * height * 0.15f;

            float centerInfluence = 1f - Mathf.Abs(t - 0.5f) * 2f;
            peakHeight += centerInfluence * height * 0.08f;

            crown.Add(new Vector2(x, peakHeight));
        }

        return crown;
    }

    List<Vector2> AddBranches(List<Vector2> shape)
    {
        List<Vector2> result = new List<Vector2>(shape);

        for (int b = 0; b < branchCount; b++)
        {
            int edgeIndex = -1;
            int attempts = 0;
            while (attempts < 50)
            {
                int idx = Random.Range(1, result.Count - 1);
                if (result[idx].y > height * 0.3f && result[idx].y < height * 0.85f)
                {
                    edgeIndex = idx;
                    break;
                }
                attempts++;
            }

            if (edgeIndex == -1) continue;

            Vector2 basePoint = result[edgeIndex];
            Vector2 edgeDir = (result[(edgeIndex + 1) % result.Count] - result[edgeIndex]).normalized;
            Vector2 outward = new Vector2(-edgeDir.y, edgeDir.x);

            if (basePoint.x > 0) outward = new Vector2(Mathf.Abs(outward.x), outward.y);
            else outward = new Vector2(-Mathf.Abs(outward.x), outward.y);

            outward = (outward + Vector2.up * 0.5f).normalized;

            float bLength = baseWidth * branchSize * Random.Range(0.5f, 1f);
            float bWidth = bLength * Random.Range(0.15f, 0.35f);

            Vector2 tip = basePoint + outward * bLength;
            Vector2 perpendicular = new Vector2(-outward.y, outward.x);

            Vector2 branchLeft = basePoint + perpendicular * bWidth * 0.5f;
            Vector2 branchRight = basePoint - perpendicular * bWidth * 0.5f;

            result.Insert(edgeIndex + 1, branchRight);
            result.Insert(edgeIndex + 2, tip);
            result.Insert(edgeIndex + 3, branchLeft);
        }

        return result;
    }

    float EaseInMonolith(float t)
    {
        return t * t * (3f - 2f * t);
    }


    void BuildObject()
    {
        if (generatedShape == null || generatedShape.Count < 3) return;

        gameObject.tag = materialTag;
        gameObject.layer = LayerMask.NameToLayer("Default");

        PolygonCollider2D polyCollider = gameObject.GetComponent<PolygonCollider2D>();
        if (polyCollider == null)
            polyCollider = gameObject.AddComponent<PolygonCollider2D>();

        polyCollider.points = generatedShape.ToArray();

        if (physicsMaterial != null)
            polyCollider.sharedMaterial = physicsMaterial;

        Rigidbody2D rb = gameObject.GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();

        rb.mass = mass;
        rb.bodyType = isStatic ? RigidbodyType2D.Static : RigidbodyType2D.Dynamic;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        SpriteRenderer sr = gameObject.GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = gameObject.AddComponent<SpriteRenderer>();

        sr.color = baseColor;
        sr.sortingOrder = sortingOrder;
        sr.sortingLayerName = sortingLayer;
        sr.enabled = false;

        BuildVisualMesh();

        ObjectReshape reshape = gameObject.GetComponent<ObjectReshape>();
        if (reshape == null)
            reshape = gameObject.AddComponent<ObjectReshape>();

        if (!animateSprout)
        {
            AttachRaycastReceiver();
        }

        PhysicsMaterialManager physicsManager = FindObjectOfType<PhysicsMaterialManager>();
        if (physicsManager != null)
        {
            physicsManager.ApplyPhysicsMaterial(gameObject);
        }
    }

    void AttachRaycastReceiver()
    {
        RaycastReceiver receiver = gameObject.GetComponent<RaycastReceiver>();
        if (receiver == null)
            receiver = gameObject.AddComponent<RaycastReceiver>();

        receiver.highlightMode = highlightMode;
        receiver.showCutOutline = showCutOutline;
        receiver.largePieceMassMultiplier = largePieceMassMultiplier;
    }

    void BuildVisualMesh()
    {
        foreach (Transform child in transform)
        {
            if (child.name.Contains("_CalamityMesh"))
            {
                Destroy(child.gameObject);
            }
        }

        GameObject meshObj = new GameObject($"{gameObject.name}_CalamityMesh");
        meshObj.transform.SetParent(transform);
        meshObj.transform.localPosition = Vector3.zero;
        meshObj.transform.localRotation = Quaternion.identity;
        meshObj.transform.localScale = Vector3.one;

        MeshFilter meshFilter = meshObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshObj.AddComponent<MeshRenderer>();

        Texture2D texture = null;
        MaterialTextureGenerator textureGen = FindObjectOfType<MaterialTextureGenerator>();
        if (textureGen != null)
        {
            texture = textureGen.GetTexture(materialTag);
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Texture");

        Material mat = new Material(shader);
        if (texture != null)
        {
            mat.mainTexture = texture;
        }
        else
        {
            mat.color = baseColor;
        }

        meshRenderer.material = mat;
        meshRenderer.sortingLayerName = sortingLayer;
        meshRenderer.sortingOrder = sortingOrder;

        visualMesh = CreateMeshFromPolygon(generatedShape);
        if (visualMesh != null)
        {
            meshFilter.mesh = visualMesh;
        }
    }

    Mesh CreateMeshFromPolygon(List<Vector2> vertices)
    {
        if (vertices == null || vertices.Count < 3) return null;

        Mesh mesh = new Mesh();
        mesh.name = "CalamityMesh";

        Vector3[] verts3D = new Vector3[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            verts3D[i] = new Vector3(vertices[i].x, vertices[i].y, 0f);
        }

        int[] triangles = TriangulatePolygon(vertices);
        if (triangles == null || triangles.Length < 3) return null;

        Vector2[] uvs = new Vector2[vertices.Count];
        Bounds bounds = CalculateBounds(vertices);
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

                Vector2 a = vertices[prev], b = vertices[curr], c = vertices[next];
                float cross = (a.x - b.x) * (c.y - b.y) - (a.y - b.y) * (c.x - b.x);

                if (cross <= 0) continue;

                bool containsPoint = false;
                for (int j = 0; j < indices.Count; j++)
                {
                    int idx = indices[j];
                    if (idx == prev || idx == curr || idx == next) continue;
                    if (PointInTriangle(vertices[idx], a, b, c))
                    {
                        containsPoint = true;
                        break;
                    }
                }

                if (!containsPoint)
                {
                    triangles.Add(prev);
                    triangles.Add(curr);
                    triangles.Add(next);
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
            min.x = Mathf.Min(min.x, v.x);
            min.y = Mathf.Min(min.y, v.y);
            max.x = Mathf.Max(max.x, v.x);
            max.y = Mathf.Max(max.y, v.y);
        }
        return new Bounds((min + max) / 2f, (Vector3)(max - min));
    }


    IEnumerator SproutAnimation()
    {
        targetScale = transform.localScale;
        transform.localScale = new Vector3(targetScale.x, 0f, targetScale.z);

        PolygonCollider2D col = GetComponent<PolygonCollider2D>();
        if (col != null) col.enabled = false;

        if (groundShakeMagnitude > 0)
        {
            StartCoroutine(GroundShake());
        }

        float elapsed = 0f;
        while (elapsed < sproutDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / sproutDuration);
            float curveValue = sproutCurve.Evaluate(t);

            transform.localScale = new Vector3(
                targetScale.x,
                targetScale.y * curveValue,
                targetScale.z
            );

            yield return null;
        }

        transform.localScale = targetScale;

        if (col != null) col.enabled = true;

        AttachRaycastReceiver();
    }

    IEnumerator GroundShake()
    {
        Vector3 originalPos = transform.position;
        float elapsed = 0f;

        while (elapsed < groundShakeDuration)
        {
            elapsed += Time.deltaTime;
            float intensity = (1f - elapsed / groundShakeDuration) * groundShakeMagnitude;
            float offsetX = Random.Range(-intensity, intensity);

            transform.position = originalPos + new Vector3(offsetX, 0f, 0f);
            yield return null;
        }

        transform.position = originalPos;
    }


    public List<Vector2> GetShape()
    {
        return generatedShape;
    }

    public bool HasSpawned()
    {
        return hasSpawned;
    }

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
}