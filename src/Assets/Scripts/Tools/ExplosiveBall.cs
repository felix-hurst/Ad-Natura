using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ExplosiveBall : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private float ballRadius = 0.3f;
    [SerializeField] private Color ballColor = Color.red;

    [Header("Lifetime")]
    [SerializeField] private float maxLifetime = 10f;

    [Header("Explosion Settings")]
    [SerializeField] private int minRayCount = 4;
    [SerializeField] private int maxRayCount = 9;
    [SerializeField] private float explosionRayDistance = 5f;
    [SerializeField] private float minAngle = 0f;
    [SerializeField] private float maxAngle = 360f;

    [Header("Structural Weakness Settings")]
    [SerializeField] private bool enableStructuralWeakness = true;
    [SerializeField] private float weaknessDelay = 2f;
    [SerializeField] private int minFractureCount = 3;
    [SerializeField] private int maxFractureCount = 7;
    [SerializeField] private float fractureRayDistance = 10f;
    [SerializeField] private bool showFractureWarning = true;
    [SerializeField] private float warningDuration = 0.5f;

    [Header("Exclusions")]
    [SerializeField] private LayerMask excludedLayers;
    [SerializeField] private List<string> excludedTags = new List<string> { "Player", "Debris", "Fragment" };

    [Header("Visual Feedback")]
    [SerializeField] private bool showExplosionRays = true;
    [SerializeField] private float rayVisualizationDuration = 0.5f;
    [SerializeField] private Color explosionRayColor = Color.yellow;
    [SerializeField] private Color fractureRayColor = Color.red;
    [SerializeField] private Color warningColor = Color.red;

    private float lifetime = 0f;

    // Guards against OnCollisionEnter2D firing more than once in the same
    // frame if multiple contacts are detected simultaneously.
    private bool hasExploded = false;
    private List<GameObject> weaknessTargets = new List<GameObject>();

    void Start()
    {
        CreateVisual();
    }

    void Update()
    {
        lifetime += Time.deltaTime;

        // Destroy the ball if it never hits anything, preventing it from
        // persisting indefinitely off-screen or in a corner.
        if (lifetime >= maxLifetime)
        {
            Destroy(gameObject);
        }
    }

    void CreateVisual()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            sr = gameObject.AddComponent<SpriteRenderer>();

            Texture2D texture = CreateCircleTexture(64);
            // Pixels-per-unit is set relative to ballRadius so the sprite scales
            // correctly to the intended world-space size regardless of the texture resolution.
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 32f / ballRadius);

            sr.sprite = sprite;
            sr.color = ballColor;
            sr.sortingOrder = 10;
        }

        transform.localScale = Vector3.one * (ballRadius * 2f);

        CircleCollider2D collider = GetComponent<CircleCollider2D>();
        if (collider == null)
        {
            collider = gameObject.AddComponent<CircleCollider2D>();
        }

        // Radius is 0.5 in local space — the world-space radius is driven by
        // localScale above, which keeps the collider and visual in sync.
        collider.radius = 0.5f;
    }

    Texture2D CreateCircleTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];

        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                // Hard cutoff at the radius produces a clean circle edge;
                // outside pixels are fully transparent so the background shows through.
                pixels[y * size + x] = distance <= radius ? Color.white : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Bilinear;

        return texture;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasExploded) return;

        // Excluded objects (player, already-broken debris) should not trigger
        // an explosion — the ball passes through or bounces off them instead.
        if (ShouldExcludeObject(collision.gameObject))
        {
            return;
        }

        GameObject hitObject = collision.gameObject;

        // Delegate the explosion timing and fracture logic to the manager so this
        // ball class stays focused on detection and doesn't need to know the details
        // of how structural collapse works.
        StructuralCollapseManager.Instance.ScheduleDelayedExplosion(
            hitObject,
            transform.position,
            weaknessDelay,
            minRayCount,
            maxRayCount,
            explosionRayDistance,
            minAngle,
            maxAngle,
            showExplosionRays,
            rayVisualizationDuration,
            explosionRayColor,
            showFractureWarning,
            warningDuration,
            warningColor);

        hasExploded = true;
        Destroy(gameObject);
    }

    bool ShouldExcludeObject(GameObject obj)
    {
        // Layer exclusion handles broad categories (e.g. a whole "Water" layer)
        // without needing a tag on every individual object.
        if (((1 << obj.layer) & excludedLayers) != 0)
        {
            return true;
        }

        foreach (string tag in excludedTags)
        {
            if (obj.CompareTag(tag))
            {
                return true;
            }
        }

        // Name-based fallback catches dynamically spawned debris that may not
        // have been tagged correctly at spawn time.
        if (obj.name.Contains("Debris") || obj.name.Contains("Fragment"))
        {
            return true;
        }

        return false;
    }

    // Resolves the world-space bounds of an object regardless of whether its
    // visual comes from a Renderer on itself, a child, or only a collider.
    Bounds GetObjectBounds(GameObject obj)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            return renderer.bounds;
        }

        renderer = obj.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            return renderer.bounds;
        }

        Collider2D collider = obj.GetComponent<Collider2D>();
        if (collider != null)
        {
            return collider.bounds;
        }

        // Last resort — a unit cube centred on the object so callers always
        // get a valid Bounds and don't need to null-check the return value.
        return new Bounds(obj.transform.position, Vector3.one);
    }
}