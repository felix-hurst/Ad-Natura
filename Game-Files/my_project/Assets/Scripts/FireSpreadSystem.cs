using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Ember and smoke system for simulating fire spread on flammable objects. Handles ignition, burning, ember/smoke emission, and spreading to nearby objects.
/// </summary>
public class FireSpreadSystem : MonoBehaviour
{
    [Header("Fire Spread Configuration")]
    [Tooltip("Layers that can catch fire")]
    [SerializeField] private LayerMask flammableLayerMask = -1;
    
    [Tooltip("Minimum temperature to ignite objects (Celsius)")]
    [SerializeField] private float ignitionTemperature = 200f;
    
    [Tooltip("How long objects burn before extinguishing (seconds)")]
    [SerializeField] private float burnDuration = 10f;
    
    [Header("Fire Spread Behavior")]
    [Tooltip("Enable fire to spread to nearby objects")]
    [SerializeField] private bool enableFireSpread = true;
    
    [Tooltip("Radius to check for nearby flammable objects")]
    [SerializeField] private float fireSpreadRadius = 1.5f;
    
    [Tooltip("How often to check for fire spread (seconds)")]
    [SerializeField] private float spreadCheckInterval = 0.5f;
    
    [Tooltip("Chance per check that fire spreads to nearby object (0-1)")]
    [Range(0f, 1f)]
    [SerializeField] private float spreadChance = 0.3f;
    
    [Header("Ember Configuration")]
    [Tooltip("Embers spawned per emission")]
    [SerializeField] private int embersPerEmission = 2;
    
    [Tooltip("How often to emit embers (seconds)")]
    [SerializeField] private float emberEmissionInterval = 0.25f;
    
    [Tooltip("Ember size")]
    [SerializeField] private float emberSize = 0.05f;
    
    [Tooltip("Ember color (bright orange/yellow)")]
    [SerializeField] private Color emberColor = new Color(1f, 0.75f, 0.2f, 1f);
    
    [Tooltip("Ember rise speed")]
    [SerializeField] private float emberRiseSpeed = 0.6f;
    
    [Tooltip("Ember horizontal drift")]
    [SerializeField] private float emberDrift = 0.15f;
    
    [Tooltip("Ember lifetime (seconds)")]
    [SerializeField] private float emberLifetime = 1.2f;
    
    [Tooltip("Enable ember twinkling effect")]
    [SerializeField] private bool enableEmberTwinkle = true;
    
    [Header("Smoke Configuration")]
    [Tooltip("Enable smoke emission")]
    [SerializeField] private bool enableSmoke = true;
    
    [Tooltip("Smoke particles per emission")]
    [SerializeField] private int smokePerEmission = 1;
    
    [Tooltip("Smoke emission interval (seconds)")]
    [SerializeField] private float smokeEmissionInterval = 0.4f;
    
    [Tooltip("Smoke size")]
    [SerializeField] private float smokeSize = 0.18f;
    
    [Tooltip("Smoke color")]
    [SerializeField] private Color smokeColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
    
    [Tooltip("Smoke rise speed")]
    [SerializeField] private float smokeRiseSpeed = 0.4f;
    
    [Tooltip("Smoke lifetime (seconds)")]
    [SerializeField] private float smokeLifetime = 2.5f;
    
    [Header("Spawn Area")]
    [Tooltip("Horizontal spread of embers/smoke around object")]
    [SerializeField] private float spawnSpread = 0.3f;
    
    [Tooltip("Vertical offset from object top")]
    [SerializeField] private float spawnHeightOffset = 0.1f;
    
    [Header("Performance")]
    [Tooltip("Maximum ember particles")]
    [SerializeField] private int maxEmberParticles = 200;
    
    [Tooltip("Maximum smoke particles")]
    [SerializeField] private int maxSmokeParticles = 150;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
    [SerializeField] private bool showFireSpreadRadius = false;

    private Dictionary<GameObject, BurningObject> burningObjects = new Dictionary<GameObject, BurningObject>();

    private List<EmberParticle> activeEmbers = new List<EmberParticle>();
    private List<SmokeParticle> activeSmoke = new List<SmokeParticle>();

    private Queue<EmberParticle> emberPool = new Queue<EmberParticle>();
    private Queue<SmokeParticle> smokePool = new Queue<SmokeParticle>();

    private Sprite emberSprite;
    private Sprite smokeSprite;
    
    private class BurningObject
    {
        public GameObject gameObject;
        public float burnTimer;
        public float spreadCheckTimer;
        public float emberEmissionTimer;
        public float smokeEmissionTimer;
        public float temperature;
        public bool isFullyIgnited;
        public Bounds bounds;
    }
    
    private class EmberParticle
    {
        public GameObject gameObject;
        public SpriteRenderer renderer;
        public Vector2 velocity;
        public float lifetime;
        public float maxLifetime;
        public float baseSize;
        public float twinklePhase;
    }
    
    private class SmokeParticle
    {
        public GameObject gameObject;
        public SpriteRenderer renderer;
        public Vector2 velocity;
        public float lifetime;
        public float maxLifetime;
        public float baseSize;
        public float driftPhase;
    }
    
    void Start()
    {
        emberSprite = CreateEmberSprite(8);
        smokeSprite = CreateSmokeSprite(14);

        for (int i = 0; i < 20; i++)
        {
            EmberParticle e = AllocateEmberParticle();
            e.gameObject.SetActive(false);
            emberPool.Enqueue(e);
            
            SmokeParticle s = AllocateSmokeParticle();
            s.gameObject.SetActive(false);
            smokePool.Enqueue(s);
        }
    }
    
    void Update()
    {
        UpdateBurningObjects();
        UpdateEmberParticles();
        UpdateSmokeParticles();
    }
    
    void OnDestroy()
    {
        foreach (var e in activeEmbers)
            if (e.gameObject != null) Destroy(e.gameObject);
        foreach (var s in activeSmoke)
            if (s.gameObject != null) Destroy(s.gameObject);
        
        while (emberPool.Count > 0)
        {
            var e = emberPool.Dequeue();
            if (e.gameObject != null) Destroy(e.gameObject);
        }
        while (smokePool.Count > 0)
        {
            var s = smokePool.Dequeue();
            if (s.gameObject != null) Destroy(s.gameObject);
        }
        
        if (emberSprite?.texture != null) Destroy(emberSprite.texture);
        if (smokeSprite?.texture != null) Destroy(smokeSprite.texture);
    }

    public void TryIgniteObject(GameObject target, float temperature)
    {
        if (burningObjects.ContainsKey(target))
        {
            burningObjects[target].burnTimer = 0f; // Refresh burn duration
            burningObjects[target].temperature = Mathf.Max(burningObjects[target].temperature, temperature);
            return;
        }

        if (!IsFlammable(target))
        {
            if (showDebugInfo)
                Debug.Log($"FireSpread: {target.name} is not flammable");
            return;
        }

        if (temperature < ignitionTemperature)
        {
            if (showDebugInfo)
                Debug.Log($"FireSpread: {target.name} not hot enough to ignite");
            return;
        }

        IgniteObject(target, temperature);
    }
    
    void IgniteObject(GameObject obj, float temperature)
    {
        if (showDebugInfo)
            Debug.Log($"FireSpread: Igniting {obj.name} at {temperature}°C");
        
        Bounds bounds = GetObjectBounds(obj);
        
        BurningObject burning = new BurningObject
        {
            gameObject = obj,
            burnTimer = 0f,
            spreadCheckTimer = 0f,
            emberEmissionTimer = 0f,
            smokeEmissionTimer = 0f,
            temperature = temperature,
            isFullyIgnited = false,
            bounds = bounds
        };
        
        burningObjects.Add(obj, burning);
    }
    
    void UpdateBurningObjects()
    {
        List<GameObject> toRemove = new List<GameObject>();
        BurningObject[] burningCopy = new BurningObject[burningObjects.Count];
        burningObjects.Values.CopyTo(burningCopy, 0);
        
        foreach (BurningObject burning in burningCopy)
        {
            if (burning.gameObject == null || !burningObjects.ContainsKey(burning.gameObject))
                continue;
            
            burning.burnTimer += Time.deltaTime;
            burning.spreadCheckTimer += Time.deltaTime;
            burning.emberEmissionTimer += Time.deltaTime;
            burning.smokeEmissionTimer += Time.deltaTime;

            if (!burning.isFullyIgnited && burning.burnTimer >= 0.3f)
                burning.isFullyIgnited = true;

            if (burning.isFullyIgnited && burning.emberEmissionTimer >= emberEmissionInterval)
            {
                burning.emberEmissionTimer = 0f;
                EmitEmbers(burning);
            }

            if (enableSmoke && burning.isFullyIgnited && burning.smokeEmissionTimer >= smokeEmissionInterval)
            {
                burning.smokeEmissionTimer = 0f;
                EmitSmoke(burning);
            }

            if (enableFireSpread && burning.isFullyIgnited && burning.spreadCheckTimer >= spreadCheckInterval)
            {
                burning.spreadCheckTimer = 0f;
                TrySpreadFire(burning);
            }

            if (burning.burnTimer >= burnDuration)
            {
                if (showDebugInfo)
                    Debug.Log($"FireSpread: {burning.gameObject.name} fire extinguished");
                toRemove.Add(burning.gameObject);
            }
        }

        foreach (GameObject obj in toRemove)
        {
            if (burningObjects.ContainsKey(obj))
                burningObjects.Remove(obj);
        }
    }
    
    void UpdateEmberParticles()
    {
        for (int i = activeEmbers.Count - 1; i >= 0; i--)
        {
            EmberParticle ember = activeEmbers[i];
            
            ember.lifetime += Time.deltaTime;
            
            if (ember.lifetime >= ember.maxLifetime)
            {
                ReturnEmberToPool(ember, i);
                continue;
            }

            ember.velocity.y = emberRiseSpeed;

            ember.velocity.x += (Random.value - 0.5f) * emberDrift * Time.deltaTime;
            ember.velocity.x = Mathf.Clamp(ember.velocity.x, -emberDrift * 2f, emberDrift * 2f);

            Vector2 pos = ember.gameObject.transform.position;
            pos += ember.velocity * Time.deltaTime;
            ember.gameObject.transform.position = pos;

            float lifeRatio = ember.lifetime / ember.maxLifetime;
 
            float brightness = 1f;
            if (enableEmberTwinkle)
            {
                brightness = Mathf.Sin(Time.time * 18f + ember.twinklePhase) * 0.3f + 0.7f;
            }

            float alpha = (1f - lifeRatio) * brightness;
            Color currentColor = new Color(emberColor.r, emberColor.g, emberColor.b, alpha);
            ember.renderer.color = currentColor;

            float scale = ember.baseSize * Mathf.Lerp(1f, 0.6f, lifeRatio);
            ember.gameObject.transform.localScale = Vector3.one * scale;
        }
    }
    
    void UpdateSmokeParticles()
    {
        for (int i = activeSmoke.Count - 1; i >= 0; i--)
        {
            SmokeParticle smoke = activeSmoke[i];
            
            smoke.lifetime += Time.deltaTime;
            
            if (smoke.lifetime >= smoke.maxLifetime)
            {
                ReturnSmokeToPool(smoke, i);
                continue;
            }
            
            
            smoke.velocity.y = smokeRiseSpeed;

            smoke.velocity.x = Mathf.Sin(smoke.lifetime * 2f + smoke.driftPhase) * 0.15f;

            Vector2 pos = smoke.gameObject.transform.position;
            pos += smoke.velocity * Time.deltaTime;
            smoke.gameObject.transform.position = pos;

            float lifeRatio = smoke.lifetime / smoke.maxLifetime;

            float alpha = smokeColor.a * (1f - lifeRatio);
            Color currentColor = new Color(smokeColor.r, smokeColor.g, smokeColor.b, alpha);
            smoke.renderer.color = currentColor;

            float scale = smoke.baseSize * (1f + lifeRatio * 0.6f);
            smoke.gameObject.transform.localScale = Vector3.one * scale;
        }
    }
    
    void EmitEmbers(BurningObject burning)
    {
        for (int i = 0; i < embersPerEmission; i++)
        {
            if (activeEmbers.Count >= maxEmberParticles) break;
            
            EmberParticle ember = GetEmberFromPool();
            if (ember == null) break;

            Vector2 spawnPos = (Vector2)burning.gameObject.transform.position;
            spawnPos += Random.insideUnitCircle * spawnSpread;
            spawnPos.y += burning.bounds.extents.y + spawnHeightOffset;
            
            ember.gameObject.transform.position = spawnPos;
            ember.velocity = new Vector2(
                Random.Range(-emberDrift, emberDrift),
                emberRiseSpeed * Random.Range(0.8f, 1.2f)
            );
            ember.lifetime = 0f;
            ember.maxLifetime = emberLifetime * Random.Range(0.8f, 1.3f);
            ember.baseSize = emberSize * Random.Range(0.7f, 1.4f);
            ember.twinklePhase = Random.Range(0f, Mathf.PI * 2f);
            
            ember.renderer.sprite = emberSprite;
            ember.renderer.color = emberColor;
            ember.gameObject.transform.localScale = Vector3.one * ember.baseSize;
            
            ember.gameObject.SetActive(true);
            activeEmbers.Add(ember);
        }
    }
    
    void EmitSmoke(BurningObject burning)
    {
        for (int i = 0; i < smokePerEmission; i++)
        {
            if (activeSmoke.Count >= maxSmokeParticles) break;
            
            SmokeParticle smoke = GetSmokeFromPool();
            if (smoke == null) break;

            Vector2 spawnPos = (Vector2)burning.gameObject.transform.position;
            spawnPos += Random.insideUnitCircle * spawnSpread * 0.7f;
            spawnPos.y += burning.bounds.extents.y + spawnHeightOffset;
            
            smoke.gameObject.transform.position = spawnPos;
            smoke.velocity = new Vector2(
                Random.Range(-0.1f, 0.1f),
                smokeRiseSpeed * Random.Range(0.9f, 1.1f)
            );
            smoke.lifetime = 0f;
            smoke.maxLifetime = smokeLifetime * Random.Range(0.8f, 1.2f);
            smoke.baseSize = smokeSize * Random.Range(0.7f, 1.3f);
            smoke.driftPhase = Random.Range(0f, Mathf.PI * 2f);
            
            smoke.renderer.sprite = smokeSprite;
            smoke.renderer.color = smokeColor;
            smoke.gameObject.transform.localScale = Vector3.one * smoke.baseSize;
            
            smoke.gameObject.SetActive(true);
            activeSmoke.Add(smoke);
        }
    }
    
    void TrySpreadFire(BurningObject source)
    {
        Collider2D[] nearby = Physics2D.OverlapCircleAll(
            source.gameObject.transform.position,
            fireSpreadRadius,
            flammableLayerMask
        );
        
        foreach (Collider2D col in nearby)
        {
            if (col.gameObject == source.gameObject) continue;
            if (burningObjects.ContainsKey(col.gameObject)) continue;
            if (Random.value > spreadChance) continue;
            
            if (showDebugInfo)
                Debug.Log($"FireSpread: Spreading from {source.gameObject.name} to {col.gameObject.name}");
            
            TryIgniteObject(col.gameObject, source.temperature * 0.8f);
            break;
        }
    }
    
    bool IsFlammable(GameObject obj)
    {
        return ((1 << obj.layer) & flammableLayerMask) != 0;
    }
    
    Bounds GetObjectBounds(GameObject obj)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null) return renderer.bounds;
        
        SpriteRenderer spriteRenderer = obj.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) return spriteRenderer.bounds;
        
        Collider2D collider = obj.GetComponent<Collider2D>();
        if (collider != null) return collider.bounds;
        
        return new Bounds(obj.transform.position, Vector3.one * 0.1f);
    }

    EmberParticle GetEmberFromPool()
    {
        if (emberPool.Count > 0)
            return emberPool.Dequeue();
        else if (activeEmbers.Count < maxEmberParticles)
            return AllocateEmberParticle();
        return null;
    }
    
    SmokeParticle GetSmokeFromPool()
    {
        if (smokePool.Count > 0)
            return smokePool.Dequeue();
        else if (activeSmoke.Count < maxSmokeParticles)
            return AllocateSmokeParticle();
        return null;
    }
    
    void ReturnEmberToPool(EmberParticle ember, int index)
    {
        ember.gameObject.SetActive(false);
        emberPool.Enqueue(ember);
        activeEmbers.RemoveAt(index);
    }
    
    void ReturnSmokeToPool(SmokeParticle smoke, int index)
    {
        smoke.gameObject.SetActive(false);
        smokePool.Enqueue(smoke);
        activeSmoke.RemoveAt(index);
    }
    
    EmberParticle AllocateEmberParticle()
    {
        GameObject obj = new GameObject("Ember");
        obj.transform.SetParent(transform);
        
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = emberSprite;
        sr.sortingOrder = 11;

        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        sr.material = mat;
        
        return new EmberParticle
        {
            gameObject = obj,
            renderer = sr,
            velocity = Vector2.zero,
            lifetime = 0f,
            maxLifetime = emberLifetime,
            baseSize = emberSize,
            twinklePhase = 0f
        };
    }
    
    SmokeParticle AllocateSmokeParticle()
    {
        GameObject obj = new GameObject("Smoke");
        obj.transform.SetParent(transform);
        
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = smokeSprite;
        sr.sortingOrder = 9;
        
        return new SmokeParticle
        {
            gameObject = obj,
            renderer = sr,
            velocity = Vector2.zero,
            lifetime = 0f,
            maxLifetime = smokeLifetime,
            baseSize = smokeSize,
            driftPhase = 0f
        };
    }
    

    Sprite CreateEmberSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float nx = (x - cx) / (size * 0.5f);
                float ny = (y - cy) / (size * 0.5f);
                
                float dist = Mathf.Sqrt(nx * nx + ny * ny);

                float alpha = 0f;
                if (dist < 0.6f)
                {
                    alpha = 1f;
                }
                else if (dist < 0.9f)
                {
                    alpha = (0.9f - dist) / 0.3f;
                }
                
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size * 2);
    }
    
    Sprite CreateSmokeSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float nx = (x - cx) / (size * 0.5f);
                float ny = (y - cy) / (size * 0.5f);
                
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = Mathf.Max(0f, (1f - dist) * 0.65f);

                float noise = Mathf.PerlinNoise(x * 0.4f, y * 0.4f);
                alpha *= Mathf.Lerp(0.6f, 1f, noise);
                
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
            }
        }
        
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
    
    void OnDrawGizmosSelected()
    {
        if (!showFireSpreadRadius) return;
        
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        
        foreach (var burning in burningObjects.Values)
        {
            if (burning.gameObject != null && burning.isFullyIgnited)
            {
                Gizmos.DrawWireSphere(burning.gameObject.transform.position, fireSpreadRadius);
            }
        }
    }

    public bool IsBurning(GameObject obj)
    {
        return burningObjects.ContainsKey(obj);
    }

    public void ExtinguishImmediately(GameObject obj)
    {
        if (burningObjects.ContainsKey(obj))
        {
            if (showDebugInfo)
                Debug.Log($"FireSpread: Manually extinguishing {obj.name}");
            burningObjects.Remove(obj);
        }
    }
}