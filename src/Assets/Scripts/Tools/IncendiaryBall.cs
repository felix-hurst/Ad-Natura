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

    [Header("Leaf Blast on Impact")]
    [Tooltip("Blow nearby leaves away on impact")]
    [SerializeField] private bool enableLeafBlastOnImpact = true;
    [SerializeField] private float impactLeafBlastRadius = 4f;
    [SerializeField] private float impactLeafBlastForce = 12f;
    [SerializeField] private float impactLeafBlastUpwardBias = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    private float lifetime = 0f;

    // Guards against OnCollisionEnter2D triggering the impact sequence
    // more than once if multiple contacts are resolved in the same frame.
    private bool hasImpacted = false;
    private IncendiaryImpactSystem incendiarySystem;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;

    // Velocity is cached in FixedUpdate rather than read from the collision
    // event because collision.relativeVelocity can be affected by the physics
    // solver's post-contact resolution, which distorts the true incoming speed.
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

        // Spawn muzzle smoke pointing opposite to the launch direction so it
        // looks like a trail puff from the barrel rather than from the ball itself.
        if (incendiarySystem != null && rb != null)
        {
            Vector2 smokeDirection = rb.linearVelocity.magnitude > 0.1f
                ? -rb.linearVelocity.normalized
                : Vector2.up;
            incendiarySystem.TriggerMuzzleSmoke(transform.position, smokeDirection);
        }

        preImpactVelocity = Vector2.zero;
    }

    void FixedUpdate()
    {
        // Snapshot velocity every physics step so the most recent pre-contact
        // value is available when OnCollisionEnter2D fires.
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
    }

    void CreateVisual()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

            Texture2D texture = CreateIncendiaryTexture(64);
            // Pixels-per-unit is derived from ballRadius so the sprite fills the
            // intended world-space size regardless of texture resolution.
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

        // Radius stays at 0.5 in local space — world-space size is controlled
        // by localScale, keeping the collider perfectly in sync with the visual.
        collider.radius = 0.5f;
    }

    Texture2D CreateIncendiaryTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];

        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        // Three-zone gradient from near-white core to transparent edge simulates
        // a molten/burning appearance without needing a particle system.
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
                        // Outer 30% fades to transparent so the ball blends
                        // softly into the scene rather than having a hard edge.
                        float t = (normalizedDist - 0.7f) / 0.3f;
                        pixelColor = Color.Lerp(edgeColor, new Color(edgeColor.r, edgeColor.g, edgeColor.b, 0f), t);
                    }

                    // Perlin noise adds subtle brightness variation that makes the
                    // surface look like it's flickering or burning unevenly.
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

        Vector2 impactPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : (Vector2)transform.position;
        Vector2 impactVelocity = preImpactVelocity;
        Vector2 surfaceNormal = collision.contacts.Length > 0 ? collision.contacts[0].normal : Vector2.up;

        bool isExcluded = ShouldExcludeObject(collision.gameObject);
        if (isExcluded)
        {
            // Still trigger the visual/audio impact for excluded objects (e.g.
            // player surfaces) so the collision doesn't feel silent or invisible,
            // even though no structural damage is applied.
            if (incendiarySystem != null)
                CreateIncendiaryImpact(impactPoint, preImpactVelocity, surfaceNormal);

            if (showImpactEffect)
                ShowImpactEffect(impactPoint);

            if (enableLeafBlastOnImpact)
                BurstLeafSystem.BlastAll(impactPoint, impactLeafBlastRadius, impactLeafBlastForce, impactLeafBlastUpwardBias);

            hasImpacted = true;
            Destroy(gameObject);
            return;
        }

        int hitLayer = collision.gameObject.layer;
        if (hitLayer == LayerMask.NameToLayer("Wood") || hitLayer == LayerMask.NameToLayer("Decompose") || hitLayer == LayerMask.NameToLayer("CutPiece"))
        {
            SoundManager.Instance?.Play("RifleHitWood");
            Debug.Log("here");
        }
        else
        {
            Debug.Log($"nohere {collision.gameObject.layer}");
        }

        GameObject hitObject = collision.gameObject;

        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Hit object: {hitObject.name} (ID: {hitObject.GetInstanceID()})");
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Impact point: {impactPoint}, Velocity: {impactVelocity.magnitude:F2}");

        // If incident cutting is enabled, the explosion should target the freshly
        // severed piece rather than the whole object, so that only the cut portion
        // is structurally weakened.
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

        if (enableLeafBlastOnImpact)
        {
            BurstLeafSystem.BlastAll(impactPoint, impactLeafBlastRadius, impactLeafBlastForce, impactLeafBlastUpwardBias);
        }

        hasImpacted = true;
        Debug.Log($"[Ball {gameObject.GetInstanceID()}] Impact complete - destroying ball");
        Debug.Log($">>> INCENDIARY BALL {gameObject.GetInstanceID()} COLLISION END <<<\n");
        Destroy(gameObject);
    }

    // Casts a ray through the hit object along the ball's travel direction to
    // find entry and exit points, then slices the object so the explosion can
    // target only the detached piece. Returns the cut piece if one was spawned,
    // or the original object if the cut failed.
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
            // Fall back to surface-normal direction if velocity is negligible
            // (e.g. a slow roll into a wall), so the cut is still well-defined.
            incidentDirection = -surfaceNormal;
        }

        Vector2 entryPoint = impactPoint;
        Vector2 exitPoint = Vector2.zero;
        bool foundExit = false;

        Collider2D hitCollider = hitObject.GetComponent<Collider2D>();
        if (hitCollider != null)
        {
            // Start slightly behind the impact point to avoid the ray originating
            // inside the collider and missing the entry surface altogether.
            Vector2 rayOrigin = impactPoint - incidentDirection * 0.5f;
            RaycastHit2D[] hits = Physics2D.RaycastAll(rayOrigin, incidentDirection, cutRaycastDistance);

            Debug.Log($"[Ball {gameObject.GetInstanceID()}] Raycasting for entry/exit points - found {hits.Length} hits");

            // The first hit of the target collider is the entry face,
            // the second is the exit face — together they define the cut line.
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
                // A reverse raycast from behind the object finds the exit face when
                // the forward ray exits through a face that was never entered (e.g.
                // a concave shape or a very thin geometry).
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

        // Subscribe to the LargePieceSpawned event before calling ExecuteCutDirect
        // so the callback fires synchronously during the cut and we can capture
        // which piece was created. Unsubscribe immediately after to avoid leaks.
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
                // The cut may have produced only small fragments below the area
                // threshold, in which case no large piece event fires. Fall back
                // to the parent so the explosion still has a valid target.
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
            // A slow-moving ball produces a gentler effect; a fast-moving one
            // produces a more dramatic burst, making the weapon feel responsive.
            float velocityMagnitude = velocity.magnitude;
            float velocityIntensity = Mathf.Clamp01(velocityMagnitude / maxVelocityForImpact);
            intensity *= velocityIntensity;
            intensity = Mathf.Clamp01(intensity);
        }

        // Minimum floor prevents the impact effect from being so weak it's
        // invisible, which would make the collision feel like it didn't register.
        intensity = Mathf.Max(intensity, 0.3f);

        incendiarySystem.TriggerIncendiaryImpactWithNormal(impactPoint, velocity, surfaceNormal, intensity);

        if (showDebugInfo)
        {
            Debug.Log($"IncendiaryBall: Created thermite impact with intensity {intensity:F2}, velocity {velocity.magnitude:F1} m/s");
        }
    }

    // Brief ring drawn at the impact point gives the player immediate visual
    // confirmation that the collision was detected, before the delayed explosion fires.
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

        // Name-based check catches dynamically spawned debris that may lack a tag.
        if (obj.name.Contains("Debris") || obj.name.Contains("Fragment"))
        {
            return true;
        }

        return false;
    }
}