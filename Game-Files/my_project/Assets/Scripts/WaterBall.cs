using UnityEngine;
using System.Collections.Generic;

public class WaterBall : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private float ballRadius = 0.3f;
    [SerializeField] private Color ballColor = new Color(0.3f, 0.6f, 1f, 0.9f); // Light blue water color

    [Header("Lifetime")]
    [SerializeField] private float maxLifetime = 10f;

    [Header("Water Spawn Settings")]
    [SerializeField] private float waterAmountToSpawn = 15f;
    [SerializeField] private int waterSpawnRadius = 3;
    [SerializeField] private bool useRegionSpawn = true;
    [SerializeField] private float regionSpawnSize = 1.5f;

    [Header("Splash Effect Settings")]
    [SerializeField] private bool enableSplashEffect = true;
    [Tooltip("Splash intensity (0-1). Higher = more dramatic splash")]
    [Range(0f, 1f)]
    [SerializeField] private float splashIntensity = 0.8f;
    [Tooltip("Multiply splash intensity by impact velocity")]
    [SerializeField] private bool scaleWithVelocity = true;
    [Tooltip("Maximum velocity for splash scaling")]
    [SerializeField] private float maxVelocityForSplash = 15f;

    [Header("Exclusions")]
    [SerializeField] private LayerMask excludedLayers;
    [SerializeField] private List<string> excludedTags = new List<string> { "Player", "Debris", "Fragment" };

    [Header("Visual Feedback")]
    [SerializeField] private bool showImpactEffect = true;
    [SerializeField] private Color impactColor = Color.cyan;
    [SerializeField] private float impactDuration = 0.3f;

    private float lifetime = 0f;
    private bool hasImpacted = false;
    private CellularLiquidSimulation liquidSimulation;
    private WaterSplashSystem splashSystem;

    void Start()
    {
        CreateVisual();

        liquidSimulation = FindObjectOfType<CellularLiquidSimulation>();
        if (liquidSimulation == null)
        {
            Debug.LogWarning("WaterBall: No CellularLiquidSimulation found in scene! Water spawning will not work.");
        }

        splashSystem = FindObjectOfType<WaterSplashSystem>();
        if (splashSystem == null && enableSplashEffect)
        {
            Debug.LogWarning("WaterBall: No WaterSplashSystem found in scene! Splash effects will not work. Add WaterSplashSystem component to the same GameObject as CellularLiquidSimulation.");
        }
    }

    void Update()
    {
        lifetime += Time.deltaTime;

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
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 32f / ballRadius);

            sr.sprite = sprite;
            sr.color = ballColor;
            sr.sortingOrder = 20;
        }

        transform.localScale = Vector3.one * (ballRadius * 2f);

        CircleCollider2D collider = GetComponent<CircleCollider2D>();
        if (collider == null)
        {
            collider = gameObject.AddComponent<CircleCollider2D>();
        }

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
                if (distance <= radius)
                {
                    float alpha = 1f - (distance / radius) * 0.3f; // Slight fade at edges
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
                else
                {
                    pixels[y * size + x] = Color.clear;
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Bilinear;

        return texture;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasImpacted) return;

        if (ShouldExcludeObject(collision.gameObject))
        {
            return;
        }

        Vector2 impactPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : (Vector2)transform.position;
        Vector2 impactVelocity = collision.relativeVelocity;

        SpawnWaterAtImpact(impactPoint);

        if (enableSplashEffect && splashSystem != null)
        {
            CreateSplashWithSystem(impactPoint, impactVelocity);
        }

        if (showImpactEffect)
        {
            ShowImpactEffect(impactPoint);
        }

        hasImpacted = true;
        Destroy(gameObject);
    }

    void SpawnWaterAtImpact(Vector2 impactPoint)
    {
        if (liquidSimulation == null)
        {
            Debug.LogWarning("WaterBall: Cannot spawn water - no liquid simulation found!");
            return;
        }

        if (useRegionSpawn)
        {
            List<Vector2> spawnRegion = CreateImpactRegion(impactPoint, regionSpawnSize);
            liquidSimulation.SpawnWaterInRegion(spawnRegion, waterAmountToSpawn);
        }
        else
        {
            Vector2Int centerCell = liquidSimulation.WorldToGrid(impactPoint);

            for (int x = -waterSpawnRadius; x <= waterSpawnRadius; x++)
            {
                for (int y = -waterSpawnRadius; y <= waterSpawnRadius; y++)
                {
                    if (Mathf.Sqrt(x * x + y * y) <= waterSpawnRadius)
                    {
                        int cellX = centerCell.x + x;
                        int cellY = centerCell.y + y;

                        if (liquidSimulation.IsValidCell(cellX, cellY))
                        {
                            float waterPerCell = waterAmountToSpawn / (waterSpawnRadius * waterSpawnRadius * 3.14f);
                            float currentWater = liquidSimulation.GetWater(cellX, cellY);
                            liquidSimulation.SetWater(cellX, cellY, currentWater + waterPerCell);
                        }
                    }
                }
            }
        }

        Debug.Log($"WaterBall: Spawned {waterAmountToSpawn} water at {impactPoint}");
    }

    void CreateSplashWithSystem(Vector2 impactPoint, Vector2 velocity)
    {
        float intensity = splashIntensity;

        if (scaleWithVelocity)
        {
            float velocityMagnitude = velocity.magnitude;
            float velocityIntensity = Mathf.Clamp01(velocityMagnitude / maxVelocityForSplash);
            intensity *= velocityIntensity;
            intensity = Mathf.Clamp01(intensity);
        }

        splashSystem.TriggerSplash(impactPoint, intensity, velocity);

        if (showImpactEffect)
        {
            Debug.Log($"WaterBall: Created splash at {impactPoint} with intensity {intensity:F2}, velocity {velocity.magnitude:F1}");
        }
    }

    List<Vector2> CreateImpactRegion(Vector2 center, float size)
    {
        List<Vector2> region = new List<Vector2>();

        int segments = 16;
        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * 360f * Mathf.Deg2Rad;
            Vector2 point = center + new Vector2(
                Mathf.Cos(angle) * size,
                Mathf.Sin(angle) * size
            );
            region.Add(point);
        }

        return region;
    }

    void ShowImpactEffect(Vector2 impactPoint)
    {
        GameObject impactVis = new GameObject("WaterBallImpact");
        impactVis.transform.position = impactPoint;

        LineRenderer lineRenderer = impactVis.AddComponent<LineRenderer>();
        lineRenderer.startWidth = 0.2f;
        lineRenderer.endWidth = 0.2f;
        lineRenderer.material = new Material(Shader.Find("Unlit/Color"));
        lineRenderer.material.color = impactColor;
        lineRenderer.startColor = impactColor;
        lineRenderer.endColor = impactColor;
        lineRenderer.sortingOrder = 15;
        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = true;

        int segments = 12;
        lineRenderer.positionCount = segments;

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * 360f * Mathf.Deg2Rad;
            Vector3 point = impactPoint + new Vector2(
                Mathf.Cos(angle) * regionSpawnSize,
                Mathf.Sin(angle) * regionSpawnSize
            );
            lineRenderer.SetPosition(i, point);
        }

        Destroy(impactVis, impactDuration);
    }

    bool ShouldExcludeObject(GameObject obj)
    {
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

        if (obj.name.Contains("Debris") || obj.name.Contains("Fragment"))
        {
            return true;
        }

        return false;
    }


}