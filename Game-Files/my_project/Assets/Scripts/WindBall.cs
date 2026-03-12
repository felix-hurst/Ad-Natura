using UnityEngine;
using System.Collections.Generic;

public class WindBall : MonoBehaviour
{
    [Header("Lifetime")]
    [SerializeField] private float maxLifetime = 10f;

    [Header("Collider")]
    [SerializeField] private float ballRadius = 0.3f;

    [Header("Impact Settings")]
    [Range(0f, 1f)]
    [SerializeField] private float impactIntensity = 0.8f;
    [SerializeField] private bool scaleWithVelocity = true;
    [SerializeField] private float maxVelocityForImpact = 15f;

    [Header("Cutting & Destruction")]
    [Tooltip("Master switch — uncheck to disable all cutting and structural collapse on hit.")]
    [SerializeField] private bool enableDestruction = true;
    [SerializeField] private bool enableIncidentCut = false;
    [SerializeField] private float cutRaycastDistance = 10f;
    [SerializeField] private bool explosionAffectsParent = false;

    [Header("Exclusions")]
    [SerializeField] private LayerMask excludedLayers;
    [SerializeField] private List<string> excludedTags = new List<string> { "Player", "Debris", "Fragment" };

    [Header("Leaf/Debris Sweep")]
    [SerializeField] private bool enableLeafSweep = true;
    [SerializeField] private float leafSweepRadius = 5.5f;
    [SerializeField] private float leafSweepForce = 5f;
    [SerializeField] private float leafSweepInterval = 0.12f;

    [Header("Proximity Push")]
    [SerializeField] private bool enableProximityPush = true;
    [SerializeField] private float pushAuraRadius = 2f;
    [SerializeField] private float pushForce = 4f;
    [SerializeField] private float pushInterval = 0.1f;
    [SerializeField] private float pushMaxMass = 2f;

    [Header("Structural Collapse Settings")]
    [SerializeField] private float weaknessDelay = 1.5f;
    [SerializeField] private int minRayCount = 3;
    [SerializeField] private int maxRayCount = 6;
    [SerializeField] private float explosionRayDistance = 4f;
    [SerializeField] private float minAngle = 0f;
    [SerializeField] private float maxAngle = 360f;
    [SerializeField] private bool showExplosionRays = true;
    [SerializeField] private float rayVisualizationDuration = 0.4f;
    [SerializeField] private Color explosionRayColor = new Color(0.7f, 0.7f, 0.7f);
    [SerializeField] private bool showFractureWarning = true;
    [SerializeField] private float warningDuration = 0.4f;
    [SerializeField] private Color warningColor = new Color(0.6f, 0.6f, 0.6f);

    [Header("Flight")]
    [Tooltip("Keep at 0 for straight wind travel. Tiny values like 0.03 add barely-perceptible droop.")]
    [SerializeField] private float gravityScale = 0f;
    [Tooltip("Air resistance — ball slows naturally over distance like wind dissipating.")]
    [SerializeField] private float linearDrag = 0.4f;

    [Header("Wind Wisps")]
    [Tooltip("Number of orbiting wisp trails. 2-3 gives the natural multi-curl look.")]
    [Range(1, 4)]
    [SerializeField] private int wispCount = 2;

    [Tooltip("Starting orbit radius when first fired. Expands outward over wispExpandTime seconds.")]
    [SerializeField] private float wispOrbitRadius = 0.25f;

    [Tooltip("Maximum orbit radius the wisps grow to as the ball travels.")]
    [SerializeField] private float wispOrbitRadiusMax = 1.2f;

    [Tooltip("Seconds to expand from wispOrbitRadius to wispOrbitRadiusMax.")]
    [SerializeField] private float wispExpandTime = 1.5f;

    [Tooltip("Orbit speed in rotations per second. Higher = tighter loops carved into the trail.")]
    [SerializeField] private float wispOrbitSpeed = 1.8f;

    [Tooltip("Each wisp pivot also pulses its orbit radius at this frequency (Hz), making the trail thickness vary organically.")]
    [SerializeField] private float wispPulseFrequency = 1.1f;

    [Tooltip("How much the orbit radius pulses (0 = constant circle, 0.5 = radius halves and doubles).")]
    [Range(0f, 0.8f)]
    [SerializeField] private float wispPulseAmount = 0.28f;

    [Tooltip("How long each wisp trail lingers. Longer = bigger looping S-shapes visible.")]
    [SerializeField] private float wispTrailTime = 0.75f;

    [Tooltip("Max width of the wisp at its thickest point.")]
    [SerializeField] private float wispWidthMax = 0.042f;

    [Tooltip("Opacity at the thickest point of the wisp.")]
    [Range(0f, 1f)]
    [SerializeField] private float wispAlpha = 0.6f;

    [Tooltip("A secondary smaller, faster wisp for the fine curling tips seen in the reference.")]
    [SerializeField] private bool enableTipWisp = true;
    [SerializeField] private float tipWispOrbitRadius = 0.18f;
    [SerializeField] private float tipWispOrbitRadiusMax = 0.6f;
    [SerializeField] private float tipWispOrbitSpeed = 4.5f;
    [SerializeField] private float tipWispTrailTime = 0.38f;
    [SerializeField] private float tipWispWidthMax = 0.016f;

    [SerializeField] private int wispSortingOrder = 11;

    [Header("Debug Visualization")]
    [SerializeField] private bool showDebugInfo = false;
    [SerializeField] private bool showDebugVisual = true;
    [SerializeField] private Color debugBallColor = new Color(0.4f, 0.8f, 1f, 0.5f);
    [SerializeField] private bool showPushRadius = false;
    [SerializeField] private Color debugPushColor = new Color(0.4f, 0.8f, 1f, 0.1f);

    private float lifetime = 0f;
    private bool hasImpacted = false;
    private Rigidbody2D rb;
    private Vector2 preImpactVelocity;

    private float leafSweepTimer = 0f;
    private float pushTimer = 0f;

    private SpriteRenderer debugRenderer;
    private SpriteRenderer debugPushRenderer;

    private struct WispTrail
    {
        public Transform pivot;
        public TrailRenderer trail;
        public float orbitRadiusMin;
        public float orbitRadiusMax;
        public float orbitSpeed;
        public float pulseFreq;
        public float pulseAmt;
        public float phaseOffset;
        public Vector2 smoothedPos;
        public Vector2 smoothVelocity;
    }
    private WispTrail[] wisps;
    private Material wispMaterial;

    void Start()
    {
        SetupCollider();

        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = gravityScale;
        rb.linearDamping = linearDrag;

        SetupDebugVisual();
        SetupWisps();

        preImpactVelocity = Vector2.zero;
    }

    void SetupCollider()
    {
        CircleCollider2D col = GetComponent<CircleCollider2D>();
        if (col == null) col = gameObject.AddComponent<CircleCollider2D>();
        col.radius = ballRadius;
    }

    void SetupWisps()
    {
        wispMaterial = CreateWispMaterial();

        int tipCount = enableTipWisp ? wispCount : 0;
        wisps = new WispTrail[wispCount + tipCount];

        for (int i = 0; i < wispCount; i++)
        {
            float phase = (Mathf.PI * 2f / wispCount) * i;
            wisps[i] = CreateWisp(
                $"Wisp_{i}",
                wispOrbitRadius, wispOrbitRadiusMax,
                wispOrbitSpeed,
                wispPulseFrequency, wispPulseAmount,
                phase,
                wispTrailTime, wispWidthMax, wispAlpha
            );
        }

        for (int i = 0; i < tipCount; i++)
        {
            float phase = (Mathf.PI * 2f / wispCount) * i + Mathf.PI / wispCount;
            wisps[wispCount + i] = CreateWisp(
                $"TipWisp_{i}",
                tipWispOrbitRadius, tipWispOrbitRadiusMax,
                tipWispOrbitSpeed,
                wispPulseFrequency * 1.7f, wispPulseAmount * 0.5f,
                phase,
                tipWispTrailTime, tipWispWidthMax, wispAlpha * 0.7f
            );
        }
    }

    WispTrail CreateWisp(string name, float orbitRMin, float orbitRMax, float orbitSpd,
                         float pulseFreq, float pulseAmt,
                         float phase, float trailTime, float widthMax, float alpha)
    {
        GameObject pivotGO = new GameObject(name);
        pivotGO.transform.SetParent(transform, false);
        Transform pivot = pivotGO.transform;

        TrailRenderer tr = pivotGO.AddComponent<TrailRenderer>();
        tr.time = trailTime;
        tr.minVertexDistance = 0.003f;
        tr.autodestruct = false;
        tr.emitting = true;
        tr.alignment = LineAlignment.View;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.sortingOrder = wispSortingOrder;
        tr.material = wispMaterial;

        AnimationCurve wc = new AnimationCurve();
        wc.AddKey(new Keyframe(0f, 0f, 0f, widthMax * 8f));
        wc.AddKey(new Keyframe(0.08f, widthMax, 0f, 0f));
        wc.AddKey(new Keyframe(0.45f, widthMax * 0.75f, 0f, 0f));
        wc.AddKey(new Keyframe(0.78f, widthMax * 0.3f, 0f, 0f));
        wc.AddKey(new Keyframe(1f, 0f, -widthMax * 2f, 0f));
        tr.widthCurve = wc;

        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f,            0f),
                new GradientAlphaKey(alpha * 0.4f,  0.05f),
                new GradientAlphaKey(alpha * 0.6f,  0.15f),
                new GradientAlphaKey(alpha * 0.35f, 0.38f),
                new GradientAlphaKey(alpha * 0.06f, 0.80f),
                new GradientAlphaKey(0f,            0.95f),
                new GradientAlphaKey(0f,            1f),
            }
        );
        tr.colorGradient = grad;

        return new WispTrail
        {
            pivot = pivot,
            trail = tr,
            orbitRadiusMin = orbitRMin,
            orbitRadiusMax = orbitRMax,
            orbitSpeed = orbitSpd,
            pulseFreq = pulseFreq,
            pulseAmt = pulseAmt,
            phaseOffset = phase,
            smoothedPos = (Vector2)pivot.position,
            smoothVelocity = Vector2.zero
        };
    }

    Material CreateWispMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.color = Color.white;
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3001;
        return mat;
    }

    void FixedUpdate()
    {
        if (hasImpacted || rb == null) return;
        preImpactVelocity = rb.linearVelocity;

        if (enableProximityPush)
        {
            pushTimer += Time.fixedDeltaTime;
            if (pushTimer >= pushInterval)
            {
                pushTimer = 0f;
                ApplyProximityPush();
            }
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

        if (hasImpacted) return;

        if (wisps != null)
        {
            Vector2 ballPos = transform.position;

            float expandT = Mathf.Clamp01(lifetime / wispExpandTime);
            float eased = 1f - Mathf.Pow(1f - expandT, 2.5f);

            for (int i = 0; i < wisps.Length; i++)
            {
                ref WispTrail w = ref wisps[i];
                if (w.pivot == null) continue;

                float currentRadius = Mathf.Lerp(w.orbitRadiusMin, w.orbitRadiusMax, eased);

                float pulse = 1f + Mathf.Sin(lifetime * w.pulseFreq * Mathf.PI * 2f + w.phaseOffset) * w.pulseAmt;
                float radius = currentRadius * pulse;

                float angle = lifetime * w.orbitSpeed * Mathf.PI * 2f + w.phaseOffset;
                Vector2 target = ballPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

                w.smoothedPos = Vector2.SmoothDamp(
                    w.smoothedPos, target,
                    ref w.smoothVelocity,
                    smoothTime: 0.03f,
                    maxSpeed: 40f,
                    deltaTime: Time.deltaTime
                );

                w.pivot.position = new Vector3(w.smoothedPos.x, w.smoothedPos.y, transform.position.z);
            }
        }

        if (enableLeafSweep)
        {
            leafSweepTimer += Time.deltaTime;
            if (leafSweepTimer >= leafSweepInterval)
            {
                leafSweepTimer = 0f;
                BurstLeafSystem.BlastAll(transform.position, leafSweepRadius, -leafSweepForce, 0f);
            }
        }
    }

    void SetupDebugVisual()
    {
        SpriteRenderer existingSr = GetComponent<SpriteRenderer>();
        if (existingSr != null) existingSr.enabled = false;
        if (!showDebugVisual) return;

        Sprite circle = CreateCircleSprite(64);

        GameObject ballVisual = new GameObject("DebugBallVisual");
        ballVisual.transform.SetParent(transform, false);
        debugRenderer = ballVisual.AddComponent<SpriteRenderer>();
        debugRenderer.sprite = circle;
        debugRenderer.color = debugBallColor;
        debugRenderer.sortingOrder = 10;
        float d = ballRadius * 2f;
        ballVisual.transform.localScale = new Vector3(d, d, 1f);

        if (showPushRadius && enableProximityPush)
        {
            GameObject pushVisual = new GameObject("DebugPushVisual");
            pushVisual.transform.SetParent(transform, false);
            debugPushRenderer = pushVisual.AddComponent<SpriteRenderer>();
            debugPushRenderer.sprite = circle;
            debugPushRenderer.color = debugPushColor;
            debugPushRenderer.sortingOrder = 9;
            float pd = pushAuraRadius * 2f;
            pushVisual.transform.localScale = new Vector3(pd, pd, 1f);
        }
    }

    Sprite CreateCircleSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f, r = size * 0.5f;
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float alpha = Mathf.Clamp01((r - dist) / (r * 0.1f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    void ApplyProximityPush()
    {
        Collider2D[] nearby = Physics2D.OverlapCircleAll(transform.position, pushAuraRadius);
        foreach (Collider2D col in nearby)
        {
            if (col.gameObject == gameObject) continue;
            if (ShouldExcludeObject(col.gameObject)) continue;
            Rigidbody2D otherRb = col.GetComponent<Rigidbody2D>();
            if (otherRb == null || otherRb.mass > pushMaxMass) continue;
            Vector2 pushDir = ((Vector2)col.transform.position - (Vector2)transform.position).normalized;
            float distance = Vector2.Distance(transform.position, col.transform.position);
            float falloff = 1f - Mathf.Clamp01(distance / pushAuraRadius);
            otherRb.AddForce(pushDir * pushForce * falloff, ForceMode2D.Force);
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasImpacted) return;

        if (IsBurstLeaf(collision.gameObject)) return;

        if (ShouldExcludeObject(collision.gameObject)) return;

        Vector2 impactPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : (Vector2)transform.position;
        Vector2 impactVelocity = preImpactVelocity;
        Vector2 surfaceNormal = collision.contacts.Length > 0 ? collision.contacts[0].normal : Vector2.up;
        GameObject hitObject = collision.gameObject;

        if (showDebugInfo)
            Debug.Log($"[WindBall] Hit {hitObject.name} at {impactPoint}, vel={impactVelocity.magnitude:F1}");

        if (wisps != null)
        {
            foreach (var w in wisps)
            {
                if (w.trail == null) continue;
                w.trail.emitting = false;
                if (w.pivot != null)
                {
                    w.pivot.SetParent(null);
                    float maxTrailTime = Mathf.Max(wispTrailTime, tipWispTrailTime);
                    Destroy(w.pivot.gameObject, maxTrailTime + 0.1f);
                }
            }
        }

        if (enableDestruction)
        {
            GameObject targetForExplosion = hitObject;
            if (enableIncidentCut)
                targetForExplosion = PerformIncidentCut(hitObject, impactPoint, impactVelocity, surfaceNormal);

            if (targetForExplosion != null && StructuralCollapseManager.Instance != null)
            {
                StructuralCollapseManager.Instance.ScheduleDelayedExplosion(
                    targetForExplosion, impactPoint, weaknessDelay,
                    minRayCount, maxRayCount, explosionRayDistance,
                    minAngle, maxAngle, showExplosionRays, rayVisualizationDuration, explosionRayColor,
                    showFractureWarning, warningDuration, warningColor
                );
            }
        }

        hasImpacted = true;
        Destroy(gameObject);
    }

    bool IsBurstLeaf(GameObject obj)
    {
        if (obj.CompareTag("BurstLeaf")) return true;

        Transform t = obj.transform;
        while (t != null)
        {
            if (t.GetComponent<BurstLeafSystem>() != null) return true;
            t = t.parent;
        }

        return false;
    }

    GameObject PerformIncidentCut(GameObject hitObject, Vector2 impactPoint, Vector2 impactVelocity, Vector2 surfaceNormal)
    {
        RaycastReceiver receiver = hitObject.GetComponent<RaycastReceiver>();
        if (receiver == null) return hitObject;

        Vector2 incidentDirection = impactVelocity.normalized;
        if (incidentDirection.magnitude < 0.1f) incidentDirection = -surfaceNormal;

        Vector2 entryPoint = impactPoint;
        Vector2 exitPoint = Vector2.zero;
        bool foundExit = false;

        Collider2D hitCollider = hitObject.GetComponent<Collider2D>();
        if (hitCollider != null)
        {
            Vector2 rayOrigin = impactPoint - incidentDirection * 0.5f;
            RaycastHit2D[] hits = Physics2D.RaycastAll(rayOrigin, incidentDirection, cutRaycastDistance);

            int hitCount = 0;
            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider == hitCollider)
                {
                    hitCount++;
                    if (hitCount == 1) entryPoint = hit.point;
                    else if (hitCount == 2) { exitPoint = hit.point; foundExit = true; break; }
                }
            }

            if (!foundExit)
            {
                Bounds bounds = hitCollider.bounds;
                Vector2 farPoint = rayOrigin + incidentDirection * (cutRaycastDistance + bounds.size.magnitude);
                RaycastHit2D[] reverseHits = Physics2D.RaycastAll(farPoint, -incidentDirection, cutRaycastDistance + bounds.size.magnitude);
                foreach (RaycastHit2D hit in reverseHits)
                {
                    if (hit.collider == hitCollider && Vector2.Distance(hit.point, entryPoint) > 0.1f)
                    {
                        exitPoint = hit.point;
                        foundExit = true;
                        break;
                    }
                }
            }
        }

        if (!foundExit) return hitObject;
        if (showDebugInfo) Debug.DrawLine(entryPoint, exitPoint, Color.white, 5f);

        GameObject explosionTarget = null;
        RaycastReceiver.OnLargePieceSpawned callback = null;

        if (!explosionAffectsParent)
        {
            callback = (GameObject piece) => { explosionTarget = piece; };
            receiver.LargePieceSpawned += callback;
        }

        receiver.ExecuteCutDirect(entryPoint, exitPoint, null);
        if (callback != null) receiver.LargePieceSpawned -= callback;

        if (explosionAffectsParent) return hitObject;
        return explosionTarget != null ? explosionTarget : hitObject;
    }

    bool ShouldExcludeObject(GameObject obj)
    {
        if (((1 << obj.layer) & excludedLayers) != 0) return true;
        foreach (string tag in excludedTags)
            if (obj.CompareTag(tag)) return true;
        if (obj.name.Contains("Debris") || obj.name.Contains("Fragment")) return true;
        return false;
    }
}