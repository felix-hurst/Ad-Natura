using UnityEngine;
using System.Collections.Generic;

public class IncendiaryBall : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private float ballRadius = 0.3f;
    [SerializeField] private Color ballColor = new Color(1f, 0.6f, 0.2f, 0.95f);
    
    [Header("Lifetime")]
    [SerializeField] private float maxLifetime = 10f;
    
    [Header("Impact Settings")]
    [Tooltip("Impact intensity (0-1). Higher = more dramatic effect")]
    [Range(0f, 1f)]
    [SerializeField] private float impactIntensity = 0.8f;
    [Tooltip("Multiply intensity by impact velocity")]
    [SerializeField] private bool scaleWithVelocity = true;
    [Tooltip("Maximum velocity for intensity scaling")]
    [SerializeField] private float maxVelocityForImpact = 15f;
    
    [Header("Cutting Settings")]
    [Tooltip("Enable cutting based on incident angle on impact")]
    [SerializeField] private bool enableIncidentCut = true;
    [Tooltip("Distance to raycast through object for cut")]
    [SerializeField] private float cutRaycastDistance = 10f;
    [Tooltip("If enabled, explosion affects parent object. If disabled, only cut pieces are affected")]
    [SerializeField] private bool explosionAffectsParent = false;
    
    [Header("Exclusions")]
    [SerializeField] private LayerMask excludedLayers;
    [SerializeField] private List<string> excludedTags = new List<string> { "Player", "Debris", "Fragment" };
    
    [Header("Visual Feedback")]
    [SerializeField] private bool showImpactEffect = true;
    [SerializeField] private Color impactColor = new Color(1f, 0.5f, 0f, 1f);
    [SerializeField] private float impactDuration = 0.3f;
    
    [Header("Incendiary Trail Settings")]
    [SerializeField] private bool enableIncendiaryTrail = true;
    [SerializeField] private float trailSpawnInterval = 0.08f;
    [Range(1, 5)]
    [SerializeField] private int trailParticlesPerSpawn = 2;
    [SerializeField] private float minTrailVelocity = 0.5f;
    [Range(0f, 0.5f)]
    [SerializeField] private float trailIntensity = 0.15f;
    [SerializeField] private bool scaleTrailWithVelocity = true;
    [SerializeField] private float maxTrailVelocity = 10f;
    [SerializeField] private float trailBehindDistance = 0.2f;
    
    [Header("Visual Glow Effect")]
    [SerializeField] private bool enableGlowEffect = true;
    [SerializeField] private float glowFlickerSpeed = 10f;
    [Range(0f, 1f)]
    [SerializeField] private float glowVariation = 0.3f;

    [Header("Structural Collapse Settings")]
    [SerializeField] private float weaknessDelay = 1.5f;
    [SerializeField] private int minRayCount = 3;
    [SerializeField] private int maxRayCount = 6;
    [SerializeField] private float explosionRayDistance = 4f;
    [SerializeField] private float minAngle = 0f;
    [SerializeField] private float maxAngle = 360f;
    [SerializeField] private bool showExplosionRays = true;
    [SerializeField] private float rayVisualizationDuration = 0.4f;
    [SerializeField] private Color explosionRayColor = new Color(1f, 0.4f, 0.1f);
    [SerializeField] private bool showFractureWarning = true;
    [SerializeField] private float warningDuration = 0.4f;
    [SerializeField] private Color warningColor = new Color(1f, 0.3f, 0.1f);
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
    
    private float lifetime = 0f;
    private bool hasImpacted = false;
    private IncendiaryImpactSystem incendiarySystem;
    private float trailTimer = 0f;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Color baseBallColor;
    private Vector2 preImpactVelocity;
    
    void Start()
    {
        CreateVisual();
        
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogWarning("IncendiaryBall: No Rigidbody2D found! Adding one.");
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 1f;
        }
        
        incendiarySystem = FindObjectOfType<IncendiaryImpactSystem>();
        if (incendiarySystem == null && showDebugInfo)
        {
            Debug.LogWarning("IncendiaryBall: No IncendiaryImpactSystem found in scene! Impact effects will not work.");
        }
        
        trailTimer = Random.Range(0f, trailSpawnInterval * 0.5f);
        baseBallColor = ballColor;
        preImpactVelocity = Vector2.zero;
    }
    
    void FixedUpdate()
    {
        if (!hasImpacted && rb != null)
        {
            preImpactVelocity = rb.linearVelocity;
        }
    }
    
    void Update()
    {
        lifetime += Time.deltaTime;
        
        if (lifetime >= maxLifetime)
        {
            Destroy(gameObject);
            return;
        }
        
        if (enableGlowEffect && spriteRenderer != null)
        {
            UpdateGlowEffect();
        }
        
        if (enableIncendiaryTrail && !hasImpacted && incendiarySystem != null && rb != null)
        {
            trailTimer += Time.deltaTime;
            float currentVelocity = rb.linearVelocity.magnitude;
            
            if (currentVelocity >= minTrailVelocity && trailTimer >= trailSpawnInterval)
            {
                trailTimer = 0f;
                SpawnIncendiaryTrail(currentVelocity);
            }
        }
    }
    
    void UpdateGlowEffect()
    {
        float flicker = Mathf.PerlinNoise(Time.time * glowFlickerSpeed, 0f);
        flicker = Mathf.Lerp(1f - glowVariation, 1f, flicker);
        
        Color glowColor = baseBallColor * flicker;
        glowColor.a = baseBallColor.a;
        
        spriteRenderer.color = glowColor;
    }
    
    void SpawnIncendiaryTrail(float velocity)
    {
        Vector2 currentPosition = transform.position;
        
        float actualTrailIntensity = trailIntensity;
        if (scaleTrailWithVelocity)
        {
            float velocityRatio = Mathf.Clamp01(velocity / maxTrailVelocity);
            actualTrailIntensity *= Mathf.Lerp(0.3f, 1f, velocityRatio);
        }
        
        Vector2 ballVelocity = rb.linearVelocity;
        Vector2 velocityDirection = ballVelocity.normalized;
        Vector2 trailOffset = -velocityDirection * trailBehindDistance;
        Vector2 spawnPosition = currentPosition + trailOffset;
        
        int particleCount = trailParticlesPerSpawn;
        if (scaleTrailWithVelocity)
        {
            float velocityRatio = Mathf.Clamp01(velocity / maxTrailVelocity);
            particleCount = Mathf.RoundToInt(trailParticlesPerSpawn * Mathf.Lerp(0.5f, 1f, velocityRatio));
            particleCount = Mathf.Max(1, particleCount);
        }
        
        for (int i = 0; i < particleCount; i++)
        {
            Vector2 randomOffset = Random.insideUnitCircle * 0.1f;
            Vector2 particleSpawnPos = spawnPosition + randomOffset;
            Vector2 particleVelocity = ballVelocity * Random.Range(0.3f, 0.6f);
            Vector2 perpendicular = new Vector2(-velocityDirection.y, velocityDirection.x);
            particleVelocity += perpendicular * Random.Range(-1f, 1f);
            
            if (incendiarySystem != null)
            {
                incendiarySystem.TriggerIncendiaryImpact(particleSpawnPos, particleVelocity, actualTrailIntensity);
            }
        }
    }
    
    void CreateVisual()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            
            Texture2D texture = CreateIncendiaryTexture(64);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 32f / ballRadius);
            
            spriteRenderer.sprite = sprite;
            spriteRenderer.color = ballColor;
            spriteRenderer.sortingOrder = 10;
        }
        
        transform.localScale = Vector3.one * (ballRadius * 2f);
        
        CircleCollider2D collider = GetComponent<CircleCollider2D>();
        if (collider == null)
        {
            collider = gameObject.AddComponent<CircleCollider2D>();
        }
        
        collider.radius = 0.5f;
    }
    
    Texture2D CreateIncendiaryTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];
        
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        Color coreColor = new Color(1f, 1f, 0.9f, 1f);
        Color midColor = new Color(1f, 0.9f, 0.5f, 1f);
        Color edgeColor = new Color(1f, 0.6f, 0.2f, 0.9f);
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                
                if (distance <= radius)
                {
                    float normalizedDist = distance / radius;
                    
                    Color pixelColor;
                    if (normalizedDist < 0.3f)
                    {
                        float t = normalizedDist / 0.3f;
                        pixelColor = Color.Lerp(coreColor, midColor, t);
                    }
                    else if (normalizedDist < 0.7f)
                    {
                        float t = (normalizedDist - 0.3f) / 0.4f;
                        pixelColor = Color.Lerp(midColor, edgeColor, t);
                    }
                    else
                    {
                        float t = (normalizedDist - 0.7f) / 0.3f;
                        pixelColor = Color.Lerp(edgeColor, new Color(edgeColor.r, edgeColor.g, edgeColor.b, 0f), t);
                    }
                    
                    float noise = Mathf.PerlinNoise(x * 0.1f, y * 0.1f);
                    pixelColor *= Mathf.Lerp(0.9f, 1.1f, noise);
                    
                    pixels[y * size + x] = pixelColor;
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
    
    Debug.Log($"\n>>> INCENDIARY BALL {gameObject.GetInstanceID()} COLLISION START <<<");
    
    if (ShouldExcludeObject(collision.gameObject))
    {
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Object {collision.gameObject.name} is excluded - ignoring");
        return;
    }
    
    Vector2 impactPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : (Vector2)transform.position;
    Vector2 impactVelocity = preImpactVelocity;
    Vector2 surfaceNormal = collision.contacts.Length > 0 ? collision.contacts[0].normal : Vector2.up;
    
    GameObject hitObject = collision.gameObject;
    
    Debug.Log($"[Ball {gameObject.GetInstanceID()}] Hit object: {hitObject.name} (ID: {hitObject.GetInstanceID()})");
    Debug.Log($"[Ball {gameObject.GetInstanceID()}] Impact point: {impactPoint}, Velocity: {impactVelocity.magnitude:F2}");

    GameObject targetForExplosion = hitObject;
    if (enableIncidentCut)
    {
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Incident cut enabled - performing cut...");
        targetForExplosion = PerformIncidentCut(hitObject, impactPoint, impactVelocity, surfaceNormal);
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Cut complete - explosion target is: {targetForExplosion.name} (ID: {targetForExplosion.GetInstanceID()})");
    }

    if (targetForExplosion != null && StructuralCollapseManager.Instance != null)
    {
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Scheduling explosion on {targetForExplosion.name} with {weaknessDelay}s delay");
        StructuralCollapseManager.Instance.ScheduleDelayedExplosion(
            targetForExplosion,
            impactPoint,
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
            warningColor
        );
    }
    else if (StructuralCollapseManager.Instance == null)
    {
        Debug.LogWarning($"[Ball {gameObject.GetInstanceID()}] StructuralCollapseManager not found!");
    }
    
    if (incendiarySystem != null)
    {
        CreateIncendiaryImpact(impactPoint, impactVelocity, surfaceNormal);
    }
    
    if (showImpactEffect)
    {
        ShowImpactEffect(impactPoint);
    }
    
    hasImpacted = true;
    Debug.Log($"[Ball {gameObject.GetInstanceID()}] Impact complete - destroying ball");
    Debug.Log($">>> INCENDIARY BALL {gameObject.GetInstanceID()} COLLISION END <<<\n");
    Destroy(gameObject);
}
    
GameObject PerformIncidentCut(GameObject hitObject, Vector2 impactPoint, Vector2 impactVelocity, Vector2 surfaceNormal)
{
    Debug.Log($"=== PerformIncidentCut START for ball {gameObject.GetInstanceID()} hitting {hitObject.name} (ID: {hitObject.GetInstanceID()}) ===");
    
    RaycastReceiver receiver = hitObject.GetComponent<RaycastReceiver>();
    if (receiver == null)
    {
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Hit object {hitObject.name} has no RaycastReceiver, skipping cut");
        return hitObject;
    }
    
    Debug.Log($"[Ball {gameObject.GetInstanceID()}] Found RaycastReceiver on {hitObject.name}");

    Vector2 incidentDirection = impactVelocity.normalized;
    if (incidentDirection.magnitude < 0.1f)
    {
        incidentDirection = -surfaceNormal;
    }

    Vector2 entryPoint = impactPoint;
    Vector2 exitPoint = Vector2.zero;
    bool foundExit = false;
    
    Collider2D hitCollider = hitObject.GetComponent<Collider2D>();
    if (hitCollider != null)
    {
        Vector2 rayOrigin = impactPoint - incidentDirection * 0.5f;
        RaycastHit2D[] hits = Physics2D.RaycastAll(rayOrigin, incidentDirection, cutRaycastDistance);
        
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Raycasting for entry/exit points - found {hits.Length} hits");
        
        int hitCount = 0;
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == hitCollider)
            {
                hitCount++;
                if (hitCount == 1)
                {
                    entryPoint = hit.point;
                    Debug.Log($"[Ball {gameObject.GetInstanceID()}] Found entry point: {entryPoint}");
                }
                else if (hitCount == 2)
                {
                    exitPoint = hit.point;
                    foundExit = true;
                    Debug.Log($"[Ball {gameObject.GetInstanceID()}] Found exit point: {exitPoint}");
                    break;
                }
            }
        }
        
        if (!foundExit)
        {
            Debug.Log($"[Ball {gameObject.GetInstanceID()}] No exit found in forward raycast, trying reverse...");
            Bounds bounds = hitCollider.bounds;
            Vector2 farPoint = rayOrigin + incidentDirection * (cutRaycastDistance + bounds.size.magnitude);
            RaycastHit2D[] reverseHits = Physics2D.RaycastAll(farPoint, -incidentDirection, cutRaycastDistance + bounds.size.magnitude);
            
            Debug.Log($"[Ball {gameObject.GetInstanceID()}] Reverse raycast found {reverseHits.Length} hits");
            
            foreach (RaycastHit2D hit in reverseHits)
            {
                if (hit.collider == hitCollider)
                {
                    if (Vector2.Distance(hit.point, entryPoint) > 0.1f)
                    {
                        exitPoint = hit.point;
                        foundExit = true;
                        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Found exit point via reverse raycast: {exitPoint}");
                        break;
                    }
                }
            }
        }
    }
    
    if (!foundExit)
    {
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Could not find exit point for cut - ABORTING");
        return hitObject;
    }
    
    Debug.Log($"[Ball {gameObject.GetInstanceID()}] Cutting from {entryPoint} to {exitPoint}");
    Debug.DrawLine(entryPoint, exitPoint, Color.yellow, 5f);
    
    GameObject explosionTarget = null;

    RaycastReceiver.OnLargePieceSpawned callback = null;
    
    if (!explosionAffectsParent)
    {
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Setting up callback - explosion will affect CUT PIECE");
        
        callback = (GameObject piece) =>
        {
            explosionTarget = piece;
            Debug.Log($"*** CALLBACK TRIGGERED *** Ball {gameObject.GetInstanceID()} - Cut piece spawned: {piece.name} (ID: {piece.GetInstanceID()})");
        };
        
        receiver.LargePieceSpawned += callback;
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Subscribed to LargePieceSpawned event on {hitObject.name}");
    }
    else
    {
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Explosion will affect PARENT object");
    }

    Debug.Log($"[Ball {gameObject.GetInstanceID()}] Calling ExecuteCutDirect on {hitObject.name}...");
    receiver.ExecuteCutDirect(entryPoint, exitPoint, null);
    Debug.Log($"[Ball {gameObject.GetInstanceID()}] ExecuteCutDirect completed");

    if (callback != null)
    {
        receiver.LargePieceSpawned -= callback;
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Unsubscribed from LargePieceSpawned event on {hitObject.name}");
    }

    if (explosionAffectsParent)
    {
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] RESULT: Explosion will affect parent object: {hitObject.name} (ID: {hitObject.GetInstanceID()})");
        Debug.Log($"=== PerformIncidentCut END ===\n");
        return hitObject;
    }
    else
    {
        if (explosionTarget != null)
        {
            Debug.Log($"[Ball {gameObject.GetInstanceID()}] RESULT: Explosion will affect cut piece: {explosionTarget.name} (ID: {explosionTarget.GetInstanceID()})");
            Debug.Log($"=== PerformIncidentCut END ===\n");
            return explosionTarget;
        }
        else
        {
            Debug.LogWarning($"[Ball {gameObject.GetInstanceID()}] WARNING: Cut piece was not captured, defaulting to parent {hitObject.name}");
            Debug.Log($"=== PerformIncidentCut END ===\n");
            return hitObject;
        }
    }
}
    
    void CreateIncendiaryImpact(Vector2 impactPoint, Vector2 velocity, Vector2 surfaceNormal)
    {
        if (incendiarySystem == null)
        {
            Debug.LogWarning("IncendiaryBall: IncendiaryImpactSystem not found!");
            return;
        }
        
        float intensity = impactIntensity;
        
        if (scaleWithVelocity)
        {
            float velocityMagnitude = velocity.magnitude;
            float velocityIntensity = Mathf.Clamp01(velocityMagnitude / maxVelocityForImpact);
            intensity *= velocityIntensity;
            intensity = Mathf.Clamp01(intensity);
        }
        
        intensity = Mathf.Max(intensity, 0.3f);
        
        incendiarySystem.TriggerIncendiaryImpactWithNormal(impactPoint, velocity, surfaceNormal, intensity);
        
        if (showDebugInfo)
        {
            Debug.Log($"IncendiaryBall: Created thermite impact with intensity {intensity:F2}, velocity {velocity.magnitude:F1} m/s");
        }
    }
    
    void ShowImpactEffect(Vector2 impactPoint)
    {
        GameObject impactVis = new GameObject("IncendiaryBallImpact");
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
        
        float impactRadius = 0.5f;
        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * 360f * Mathf.Deg2Rad;
            Vector3 point = impactPoint + new Vector2(
                Mathf.Cos(angle) * impactRadius,
                Mathf.Sin(angle) * impactRadius
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