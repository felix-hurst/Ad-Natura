using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(CellularLiquidSimulation))]
public class WaterSplashSystem : MonoBehaviour
{
    [Header("Splash Detection")]
    [Tooltip("Minimum impact velocity to create a splash")]
    [SerializeField] private float minSplashVelocity = 2f;
    [Tooltip("Velocity for maximum splash effect")]
    [SerializeField] private float maxSplashVelocity = 15f;
    [Tooltip("Layers that can create splashes")]
    [SerializeField] private LayerMask splashLayers = ~0;
    [Tooltip("How often to check for splashes (seconds)")]
    [SerializeField] private float checkInterval = 0.05f;
    
    [Header("Splash Particles")]
    [Tooltip("Maximum splash particles per impact")]
    [SerializeField] private int maxParticlesPerSplash = 20;
    [Tooltip("Minimum splash particles per impact")]
    [SerializeField] private int minParticlesPerSplash = 3;
    [Tooltip("How high particles can fly")]
    [SerializeField] private float maxParticleHeight = 3f;
    [Tooltip("Horizontal spread of particles")]
    [SerializeField] private float horizontalSpread = 2f;
    [Tooltip("Particle size")]
    [SerializeField] private float particleSize = 0.08f;
    [Tooltip("How long particles live")]
    [SerializeField] private float particleLifetime = 1.5f;
    
    [Header("Splash Water")]
    [Tooltip("How much water each splash particle carries")]
    [SerializeField] private float waterPerParticle = 0.1f;
    [Tooltip("Particles return water when they land")]
    [SerializeField] private bool particlesReturnWater = true;
    
    [Header("Visual Settings")]
    [SerializeField] private Color splashColor = new Color(0.5f, 0.75f, 1f, 0.8f);
    [SerializeField] private bool fadeParticles = true;
    
    [Header("Performance")]
    [SerializeField] private int maxActiveParticles = 100;
    [SerializeField] private int maxSplashesPerFrame = 3;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    private CellularLiquidSimulation liquidSim;
    private List<SplashParticle> activeParticles = new List<SplashParticle>();
    private Queue<SplashParticle> particlePool = new Queue<SplashParticle>();
    private Dictionary<Rigidbody2D, TrackedObject> trackedObjects = new Dictionary<Rigidbody2D, TrackedObject>();
    private float checkTimer = 0f;
    private int splashesThisFrame = 0;

    private Sprite particleSprite;
    
    private class SplashParticle
    {
        public GameObject gameObject;
        public SpriteRenderer renderer;
        public Vector2 velocity;
        public float lifetime;
        public float maxLifetime;
        public float waterCarried;
        public bool hasLanded;
    }
    
    private class TrackedObject
    {
        public Vector2 lastPosition;
        public Vector2 lastVelocity;
        public bool wasInWater;
        public int waterCellsBelow;
    }
    
    void Start()
    {
        liquidSim = GetComponent<CellularLiquidSimulation>();
        
        if (liquidSim == null)
        {
            Debug.LogError("WaterSplashSystem requires CellularLiquidSimulation!");
            enabled = false;
            return;
        }

        particleSprite = CreateCircleSprite(16);

        for (int i = 0; i < 20; i++)
        {
            SplashParticle particle = CreateParticle();
            particle.gameObject.SetActive(false);
            particlePool.Enqueue(particle);
        }

    }
    
    void Update()
    {
        splashesThisFrame = 0;

        checkTimer += Time.deltaTime;
        if (checkTimer >= checkInterval)
        {
            checkTimer = 0f;
            CheckForSplashes();
        }

        UpdateParticles();
    }

    void CheckForSplashes()
    {
        Rigidbody2D[] allRigidbodies = FindObjectsOfType<Rigidbody2D>();
        Dictionary<Rigidbody2D, TrackedObject> newTracked = new Dictionary<Rigidbody2D, TrackedObject>();
        
        foreach (Rigidbody2D rb in allRigidbodies)
        {
            if (rb == null || rb.bodyType == RigidbodyType2D.Static) continue;

            int layerMask = 1 << rb.gameObject.layer;
            if ((layerMask & splashLayers) == 0) continue;

            Vector2 currentPos = rb.position;
            Vector2 currentVelocity = rb.linearVelocity;
            bool currentlyInWater = IsObjectInWater(rb, out int waterCellsBelow);
            TrackedObject tracked;
            if (trackedObjects.TryGetValue(rb, out tracked))
            {
                if (!tracked.wasInWater && currentlyInWater && currentVelocity.y < -minSplashVelocity)
                {
                    float impactVelocity = Mathf.Abs(currentVelocity.y);
                    CreateSplash(rb, currentPos, currentVelocity, impactVelocity);
                }

                if (currentlyInWater && tracked.wasInWater)
                {
                    float horizontalSpeed = Mathf.Abs(currentVelocity.x);
                    if (horizontalSpeed > minSplashVelocity * 1.5f)
                    {
                        CreateSideSplash(rb, currentPos, currentVelocity, horizontalSpeed);
                    }
                }
            }

            newTracked[rb] = new TrackedObject
            {
                lastPosition = currentPos,
                lastVelocity = currentVelocity,
                wasInWater = currentlyInWater,
                waterCellsBelow = waterCellsBelow
            };
        }
        
        trackedObjects = newTracked;
    }
    bool IsObjectInWater(Rigidbody2D rb, out int waterCellsBelow)
    {
        waterCellsBelow = 0;
        
        Collider2D col = rb.GetComponent<Collider2D>();
        if (col == null) return false;
        
        Bounds bounds = col.bounds;
        
        Vector2 bottomCenter = new Vector2(bounds.center.x, bounds.min.y);
        Vector2Int gridPos = liquidSim.WorldToGrid(bottomCenter);
        
        if (!liquidSim.IsValidCell(gridPos.x, gridPos.y)) return false;

        for (int y = gridPos.y; y >= 0 && y > gridPos.y - 5; y--)
        {
            if (liquidSim.IsValidCell(gridPos.x, y))
            {
                float water = liquidSim.GetWater(gridPos.x, y);
                if (water > 0.1f)
                {
                    waterCellsBelow++;
                }
            }
        }

        Vector2Int centerGrid = liquidSim.WorldToGrid(bounds.center);
        if (liquidSim.IsValidCell(centerGrid.x, centerGrid.y))
        {
            float waterAtCenter = liquidSim.GetWater(centerGrid.x, centerGrid.y);
            if (waterAtCenter > 0.1f)
            {
                return true;
            }
        }
        
        return waterCellsBelow > 0;
    }

    void CreateSplash(Rigidbody2D rb, Vector2 position, Vector2 velocity, float impactVelocity)
    {
        if (splashesThisFrame >= maxSplashesPerFrame) return;
        splashesThisFrame++;
        float intensity = Mathf.InverseLerp(minSplashVelocity, maxSplashVelocity, impactVelocity);
        intensity = Mathf.Clamp01(intensity);
        Collider2D col = rb.GetComponent<Collider2D>();
        float sizeMultiplier = 1f;

        if (col != null)
        {
            sizeMultiplier = Mathf.Clamp(col.bounds.size.magnitude, 0.5f, 3f);
        }
        
        int particleCount = Mathf.RoundToInt(Mathf.Lerp(minParticlesPerSplash, maxParticlesPerSplash, intensity) * sizeMultiplier);
        particleCount = Mathf.Min(particleCount, maxActiveParticles - activeParticles.Count);
        
        if (showDebugInfo)
        {
            Debug.Log($"SPLASH! {rb.name} hit water at {impactVelocity:F1} m/s, creating {particleCount} particles");
        }

        Vector2 splashCenter = position + Vector2.down * (col != null ? col.bounds.extents.y : 0.5f);

        float waterToRemove = particleCount * waterPerParticle;
        RemoveWaterForSplash(splashCenter, waterToRemove, col != null ? col.bounds.size.x : 1f);

        for (int i = 0; i < particleCount; i++)
        {
            SpawnSplashParticle(splashCenter, velocity, intensity, false);
        }
    }

    void CreateSideSplash(Rigidbody2D rb, Vector2 position, Vector2 velocity, float speed)
    {
        if (splashesThisFrame >= maxSplashesPerFrame) return;

        float intensity = Mathf.InverseLerp(minSplashVelocity * 1.5f, maxSplashVelocity, speed) * 0.5f;
        
        int particleCount = Mathf.RoundToInt(Mathf.Lerp(1, maxParticlesPerSplash * 0.3f, intensity));
        particleCount = Mathf.Min(particleCount, maxActiveParticles - activeParticles.Count);
        
        if (particleCount <= 0) return;
        
        splashesThisFrame++;
        
        Collider2D col = rb.GetComponent<Collider2D>();
        Vector2 splashCenter = position;
        if (col != null)
        {
            float side = Mathf.Sign(velocity.x);
            splashCenter = new Vector2(
                position.x + side * col.bounds.extents.x,
                position.y
            );
        }
        
        for (int i = 0; i < particleCount; i++)
        {
            SpawnSplashParticle(splashCenter, velocity, intensity, true);
        }
    }

    void SpawnSplashParticle(Vector2 origin, Vector2 objectVelocity, float intensity, bool isSideSplash)
    {
        SplashParticle particle;

        if (particlePool.Count > 0)
        {
            particle = particlePool.Dequeue();
        }
        else if (activeParticles.Count < maxActiveParticles)
        {
            particle = CreateParticle();
        }
        else
        {
            return;
        }

        Vector2 particleVelocity;
        
        if (isSideSplash)
        {
            float angle = Random.Range(45f, 135f) * Mathf.Deg2Rad;
            float speed = Random.Range(2f, 5f) * intensity;

            float horizontalBias = -Mathf.Sign(objectVelocity.x) * Random.Range(0.5f, 1.5f);
            
            particleVelocity = new Vector2(
                Mathf.Cos(angle) * speed + horizontalBias,
                Mathf.Sin(angle) * speed
            );
        }
        else
        {
            float angle = Random.Range(30f, 150f) * Mathf.Deg2Rad;
            float heightMultiplier = Mathf.Lerp(0.3f, 1f, intensity);
            float speed = Random.Range(3f, maxParticleHeight * 2f) * heightMultiplier;
            float inheritedHorizontal = objectVelocity.x * Random.Range(0.1f, 0.3f);
            
            particleVelocity = new Vector2(
                Mathf.Cos(angle) * speed * horizontalSpread * 0.5f + inheritedHorizontal,
                Mathf.Abs(Mathf.Sin(angle)) * speed
            );
        }

        Vector2 spawnPos = origin + new Vector2(
            Random.Range(-0.2f, 0.2f),
            Random.Range(0f, 0.1f)
        );

        particle.gameObject.transform.position = spawnPos;
        particle.velocity = particleVelocity;
        particle.lifetime = 0f;
        particle.maxLifetime = particleLifetime * Random.Range(0.7f, 1.3f);
        particle.waterCarried = waterPerParticle;
        particle.hasLanded = false;

        particle.renderer.color = splashColor;
        float size = particleSize * Random.Range(0.7f, 1.3f);
        particle.gameObject.transform.localScale = Vector3.one * size;
        
        particle.gameObject.SetActive(true);
        activeParticles.Add(particle);
    }

    void UpdateParticles()
    {
        float gravity = -15f;
        
        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            SplashParticle particle = activeParticles[i];
            
            particle.lifetime += Time.deltaTime;

            if (particle.lifetime >= particle.maxLifetime)
            {
                ReturnParticle(particle, i);
                continue;
            }

            particle.velocity.y += gravity * Time.deltaTime;

            Vector2 newPos = (Vector2)particle.gameObject.transform.position + particle.velocity * Time.deltaTime;

            if (!particle.hasLanded && particle.velocity.y < 0)
            {
                Vector2Int gridPos = liquidSim.WorldToGrid(newPos);
                
                if (liquidSim.IsValidCell(gridPos.x, gridPos.y))
                {
                    float waterHere = liquidSim.GetWater(gridPos.x, gridPos.y);

                    if (waterHere > 0.05f)
                    {
                        particle.hasLanded = true;

                        if (particlesReturnWater)
                        {
                            liquidSim.SpawnWater(newPos, particle.waterCarried);
                        }

                        ReturnParticle(particle, i);
                        continue;
                    }
                }

                if (newPos.y < liquidSim.GridToWorld(0, 0).y - 1f)
                {
                    ReturnParticle(particle, i);
                    continue;
                }
            }
            
            particle.gameObject.transform.position = newPos;

            if (fadeParticles)
            {
                float lifeRatio = particle.lifetime / particle.maxLifetime;
                float alpha = splashColor.a * (1f - lifeRatio * lifeRatio);
                Color col = particle.renderer.color;
                col.a = alpha;
                particle.renderer.color = col;
            }

            if (particle.lifetime > particle.maxLifetime * 0.7f)
            {
                float shrinkRatio = (particle.lifetime - particle.maxLifetime * 0.7f) / (particle.maxLifetime * 0.3f);
                float scale = particleSize * (1f - shrinkRatio * 0.5f);
                particle.gameObject.transform.localScale = Vector3.one * scale;
            }
        }
    }

    void RemoveWaterForSplash(Vector2 center, float totalAmount, float width)
    {
        int cellsWide = Mathf.CeilToInt(width / 0.1f);
        float amountPerCell = totalAmount / Mathf.Max(cellsWide, 1);
        
        Vector2Int centerGrid = liquidSim.WorldToGrid(center);
        
        for (int dx = -cellsWide / 2; dx <= cellsWide / 2; dx++)
        {
            int x = centerGrid.x + dx;
            int y = centerGrid.y;

            for (int dy = -2; dy <= 2; dy++)
            {
                if (liquidSim.IsValidCell(x, y + dy))
                {
                    float water = liquidSim.GetWater(x, y + dy);
                    if (water > 0.1f)
                    {
                        float remove = Mathf.Min(amountPerCell, water * 0.5f);
                        liquidSim.SetWater(x, y + dy, water - remove);
                        break;
                    }
                }
            }
        }
    }
    
    SplashParticle CreateParticle()
    {
        GameObject obj = new GameObject("SplashParticle");
        obj.transform.SetParent(transform);
        
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = particleSprite;
        sr.color = splashColor;
        sr.sortingOrder = 10;
        
        obj.transform.localScale = Vector3.one * particleSize;
        
        return new SplashParticle
        {
            gameObject = obj,
            renderer = sr,
            velocity = Vector2.zero,
            lifetime = 0f,
            maxLifetime = particleLifetime,
            waterCarried = waterPerParticle,
            hasLanded = false
        };
    }
    
    void ReturnParticle(SplashParticle particle, int index)
    {
        particle.gameObject.SetActive(false);
        particlePool.Enqueue(particle);
        activeParticles.RemoveAt(index);
    }
    
    Sprite CreateCircleSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size);
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.5f;
        
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(1f - dist / radius);
                alpha = alpha * alpha;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
    
    public void TriggerSplash(Vector2 position, float intensity, Vector2 direction)
    {
        intensity = Mathf.Clamp01(intensity);
        int particleCount = Mathf.RoundToInt(Mathf.Lerp(minParticlesPerSplash, maxParticlesPerSplash, intensity));
        
        for (int i = 0; i < particleCount; i++)
        {
            SpawnSplashParticle(position, direction, intensity, false);
        }
    }
    
    void OnDestroy()
    {
        foreach (var particle in activeParticles)
        {
            if (particle.gameObject != null)
                Destroy(particle.gameObject);
        }
        
        while (particlePool.Count > 0)
        {
            var particle = particlePool.Dequeue();
            if (particle.gameObject != null)
                Destroy(particle.gameObject);
        }
        
        if (particleSprite != null && particleSprite.texture != null)
        {
            Destroy(particleSprite.texture);
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = Color.cyan;
        foreach (var particle in activeParticles)
        {
            if (particle.gameObject != null && particle.gameObject.activeInHierarchy)
            {
                Gizmos.DrawWireSphere(particle.gameObject.transform.position, 0.05f);
                Gizmos.DrawLine(
                    particle.gameObject.transform.position,
                    (Vector2)particle.gameObject.transform.position + particle.velocity * 0.1f
                );
            }
        }
    }
}