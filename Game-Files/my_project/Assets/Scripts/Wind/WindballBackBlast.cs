using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Backblast VFX for WindBall — attach alongside WindBall.
/// Provides a continuous wind-wake trail during flight and a full
/// ejecta/smoke/light burst on impact, ported from IncendiaryImpactSystem.
///
/// WindBall must call:
///   backblast.NotifyFlight(velocity)  — each FixedUpdate while in flight
///   backblast.NotifyImpact(point, velocity, normal) — on collision
/// </summary>
public class WindBallBackblast : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  FLIGHT WAKE
    // -------------------------------------------------------------------------
    [Header("Flight Wake Trail")]
    [Tooltip("Emit dust/ejecta particles continuously during flight")]
    [SerializeField] private bool enableFlightWake = true;
    [Tooltip("How often to emit a wake burst (seconds)")]
    [SerializeField] private float wakeEmitInterval = 0.06f;
    [Tooltip("Number of wake ejecta particles per burst")]
    [SerializeField] private int wakeParticlesPerBurst = 4;
    [Tooltip("How far behind the ball the wake spawns (world units)")]
    [SerializeField] private float wakeSpawnOffset = 0.25f;
    [Tooltip("Scatter radius around the spawn point")]
    [Range(0f, 0.4f)]
    [SerializeField] private float wakeSpawnScatter = 0.15f;
    [Tooltip("Wake particle speed (fraction of ball speed)")]
    [Range(0.1f, 1f)]
    [SerializeField] private float wakeVelocityFraction = 0.35f;
    [Tooltip("Random velocity spread on wake particles")]
    [Range(0f, 1f)]
    [SerializeField] private float wakeVelocityRandomness = 0.5f;
    [Tooltip("Wake particle size")]
    [SerializeField] private float wakeParticleSize = 0.07f;
    [Range(0f, 1f)]
    [SerializeField] private float wakeSizeVariation = 0.6f;
    [Tooltip("Wake ejecta particle lifetime (seconds)")]
    [SerializeField] private float wakeParticleLifetime = 0.5f;
    [Tooltip("Wake dust colour")]
    [SerializeField] private Color wakeColor = new Color(0.7f, 0.85f, 1f, 0.6f);

    [Header("Flight Smoke Wisps")]
    [Tooltip("Emit small anime-style smoke wisps during flight")]
    [SerializeField] private bool enableFlightSmoke = true;
    [Tooltip("How often to emit a wisp (seconds)")]
    [SerializeField] private float flightSmokeInterval = 0.15f;
    [Tooltip("Number of smoke puffs per wisp burst")]
    [SerializeField] private int flightSmokePuffsPerBurst = 2;
    [SerializeField] private float flightSmokeSize = 0.22f;
    [SerializeField] private float flightSmokeLifetime = 1.2f;
    [SerializeField] private float flightSmokeRiseSpeed = 0.4f;
    [Range(0.8f, 2.5f)]
    [SerializeField] private float flightSmokeBillowing = 1.2f;
    [SerializeField] private Color flightSmokeColor = new Color(0.8f, 0.9f, 1f, 0.45f);

    // -------------------------------------------------------------------------
    //  IMPACT EJECTA PLUME
    // -------------------------------------------------------------------------
    [Header("Impact — Ejecta Plume")]
    [SerializeField] private bool enableImpactPlume = true;
    [SerializeField] private int ejectaParticleCount = 50;
    [Tooltip("Cone half-angle around reflected direction (degrees)")]
    [SerializeField] private float ejectaConeAngle = 50f;
    [SerializeField] private float ejectaConeSpread = 20f;
    [Range(0.1f, 1.5f)]
    [SerializeField] private float ejectaVelocityFraction = 0.5f;
    [Range(0f, 1f)]
    [SerializeField] private float ejectaVelocityRandomness = 0.4f;
    [SerializeField] private float ejectaParticleSize = 0.09f;
    [Range(0f, 1f)]
    [SerializeField] private float ejectaSizeVariation = 0.7f;
    [SerializeField] private float ejectaLifetime = 1.8f;
    [SerializeField] private Color ejectaColor = new Color(0.6f, 0.85f, 1f, 0.75f);
    [SerializeField] private float ejectaGravity = -4f;
    [Range(0.85f, 0.99f)]
    [SerializeField] private float ejectaAirResistance = 0.95f;
    [Range(0.01f, 0.3f)]
    [SerializeField] private float ejectaSpawnOffset = 0.08f;
    [Range(0f, 0.2f)]
    [SerializeField] private float ejectaSpawnScatter = 0.1f;

    // -------------------------------------------------------------------------
    //  IMPACT SMOKE BLAST
    // -------------------------------------------------------------------------
    [Header("Impact — Smoke Blast")]
    [SerializeField] private bool enableImpactSmoke = true;
    [SerializeField] private int impactSmokeCount = 7;
    [Range(0.3f, 1f)]
    [SerializeField] private float impactSmokeVelocityFraction = 0.55f;
    [SerializeField] private float impactSmokeLifetime = 3.5f;
    [SerializeField] private float impactSmokeSize = 0.32f;
    [SerializeField] private float impactSmokeRiseSpeed = 0.5f;
    [Range(0.8f, 2.5f)]
    [SerializeField] private float impactSmokeBillowing = 1.35f;
    [SerializeField] private Color impactSmokeColor = new Color(0.75f, 0.88f, 1f, 0.65f);

    // -------------------------------------------------------------------------
    //  IMPACT LIGHT BURST
    // -------------------------------------------------------------------------
    [Header("Impact — Light Burst")]
    [SerializeField] private bool enableImpactLightBurst = true;
    [SerializeField] private float impactLightIntensity = 4f;
    [SerializeField] private float impactLightRadius = 3.5f;
    [SerializeField] private float impactLightDuration = 0.25f;
    [SerializeField] private Color impactLightColor = new Color(0.7f, 0.9f, 1f);

    // -------------------------------------------------------------------------
    //  SHARED SMOKE STYLE
    // -------------------------------------------------------------------------
    [Header("Smoke Style (Shared)")]
    [Range(2, 8)]
    [SerializeField] private int spheresPerPuff = 4;
    [Range(0.5f, 3f)]
    [SerializeField] private float animeRotationSpeed = 1.1f;
    [SerializeField] private bool spawnInClusters = true;

    // -------------------------------------------------------------------------
    //  PERFORMANCE
    // -------------------------------------------------------------------------
    [Header("Performance")]
    [SerializeField] private int maxEjectaParticles = 120;
    [SerializeField] private int maxSmokeParticles = 80;

    // =========================================================================
    //  PRIVATE STATE
    // =========================================================================

    // --- Ejecta ---
    private class EjectaParticle
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Vector2 velocity;
        public float lifetime, maxLifetime, initialSize;
        public Color baseColor;
    }

    // --- Anime Smoke ---
    private class SmokePuff
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Vector2 velocity, initialVelocity;
        public float lifetime, maxLifetime, initialSize;
        public Color baseColor;
        public float rotationAngle, rotationSpeed, expansionRate, customRiseSpeed;
        public bool isImpactSmoke;
    }

    // --- Impact Light ---
    private class ImpactLight
    {
        public GameObject go;
        public UnityEngine.Rendering.Universal.Light2D light2D;
        public float lifetime, maxLifetime, peakIntensity;
    }

    private List<EjectaParticle>  activeEjecta = new List<EjectaParticle>();
    private List<SmokePuff>       activeSmoke  = new List<SmokePuff>();
    private List<ImpactLight>     activeLights = new List<ImpactLight>();

    private Queue<EjectaParticle> ejectaPool = new Queue<EjectaParticle>();
    private Queue<SmokePuff>      smokePool  = new Queue<SmokePuff>();

    private List<Sprite> smokeVariants = new List<Sprite>();
    private const int SmokeVariantCount = 6;

    private Sprite ejectaSprite;
    private Sprite wakeSprite;

    private float wakeTimer       = 0f;
    private float flightSmokeTimer = 0f;

    // =========================================================================
    //  UNITY LIFECYCLE
    // =========================================================================

    void Awake()
    {
        ejectaSprite = CreateSoftCircleSprite(10, new Color(0.7f, 0.9f, 1f, 1f));
        wakeSprite   = CreateSoftCircleSprite(8,  new Color(0.8f, 0.95f, 1f, 1f));

        PreGenerateSmokeVariants();

        // Warm the pools
        for (int i = 0; i < 15; i++)
        {
            var e = AllocateEjecta();  e.go.SetActive(false); ejectaPool.Enqueue(e);
            var s = AllocateSmoke();   s.go.SetActive(false); smokePool.Enqueue(s);
        }
    }

    void Update()
    {
        UpdateEjecta();
        UpdateSmoke();
        UpdateLights();
    }

    void OnDestroy()
    {
        foreach (var l in activeLights)  if (l.go) Destroy(l.go);
        foreach (var e in activeEjecta)  if (e.go) Destroy(e.go);
        foreach (var s in activeSmoke)   if (s.go) Destroy(s.go);
        while (ejectaPool.Count > 0) { var e = ejectaPool.Dequeue(); if (e.go) Destroy(e.go); }
        while (smokePool.Count  > 0) { var s = smokePool.Dequeue();  if (s.go) Destroy(s.go); }
        foreach (var sp in smokeVariants) if (sp && sp.texture) Destroy(sp.texture);
        if (ejectaSprite && ejectaSprite.texture) Destroy(ejectaSprite.texture);
        if (wakeSprite   && wakeSprite.texture)   Destroy(wakeSprite.texture);
    }

    // =========================================================================
    //  PUBLIC API  (called by WindBall)
    // =========================================================================

    /// <summary>Call every FixedUpdate while the ball is in flight.</summary>
    public void NotifyFlight(Vector2 velocity)
    {
        if (velocity.magnitude < 0.5f) return;

        wakeTimer        += Time.fixedDeltaTime;
        flightSmokeTimer += Time.fixedDeltaTime;

        if (enableFlightWake && wakeTimer >= wakeEmitInterval)
        {
            wakeTimer = 0f;
            EmitFlightWake(transform.position, velocity);
        }

        if (enableFlightSmoke && flightSmokeTimer >= flightSmokeInterval)
        {
            flightSmokeTimer = 0f;
            EmitFlightSmoke(transform.position, velocity);
        }
    }

    /// <summary>Call once when the ball collides with something.</summary>
    public void NotifyImpact(Vector2 impactPoint, Vector2 impactVelocity, Vector2 surfaceNormal)
    {
        float speed     = impactVelocity.magnitude;
        float intensity = Mathf.Clamp01(speed / 15f); // normalise against a reasonable max

        if (enableImpactLightBurst)
            SpawnImpactLight(impactPoint, intensity);

        if (enableImpactPlume)
            CreateEjectaPlume(impactPoint, impactVelocity, surfaceNormal, speed);

        if (enableImpactSmoke)
            CreateImpactSmoke(impactPoint, impactVelocity, surfaceNormal, speed);
    }

    // =========================================================================
    //  FLIGHT WAKE EMISSION
    // =========================================================================

    void EmitFlightWake(Vector2 ballPos, Vector2 velocity)
    {
        Vector2 backward = -velocity.normalized;
        Vector2 spawnBase = ballPos + backward * wakeSpawnOffset;

        for (int i = 0; i < wakeParticlesPerBurst; i++)
        {
            if (activeEjecta.Count >= maxEjectaParticles) break;

            Vector2 pos = spawnBase + Random.insideUnitCircle * wakeSpawnScatter;

            // Spray mostly backwards with a spread cone
            float spreadAngle = Random.Range(-35f, 35f) * Mathf.Deg2Rad;
            float backAngle   = Mathf.Atan2(backward.y, backward.x);
            Vector2 dir = new Vector2(
                Mathf.Cos(backAngle + spreadAngle),
                Mathf.Sin(backAngle + spreadAngle)
            );

            float speed = velocity.magnitude * wakeVelocityFraction
                        * Random.Range(1f - wakeVelocityRandomness, 1f + wakeVelocityRandomness);
            Vector2 vel = dir * speed;

            float size = wakeParticleSize * Random.Range(1f - wakeSizeVariation, 1f + wakeSizeVariation);

            EmitEjecta(pos, vel, size, wakeParticleLifetime, wakeColor, isWake: true);
        }
    }

    void EmitFlightSmoke(Vector2 ballPos, Vector2 velocity)
    {
        Vector2 backward  = -velocity.normalized;
        Vector2 spawnBase = ballPos + backward * (wakeSpawnOffset * 0.8f);

        for (int i = 0; i < flightSmokePuffsPerBurst; i++)
        {
            if (activeSmoke.Count >= maxSmokeParticles) break;

            Vector2 pos = spawnBase + Random.insideUnitCircle * wakeSpawnScatter;

            float angle = Mathf.Atan2(backward.y, backward.x)
                        + Random.Range(-25f, 25f) * Mathf.Deg2Rad;
            Vector2 vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
                        * velocity.magnitude * 0.2f;

            EmitSmoke(pos, vel, flightSmokeSize, flightSmokeLifetime,
                      flightSmokeColor, flightSmokeRiseSpeed, flightSmokeBillowing,
                      isImpactSmoke: false);
        }
    }

    // =========================================================================
    //  IMPACT EJECTA PLUME  (ported from IncendiaryImpactSystem)
    // =========================================================================

    void CreateEjectaPlume(Vector2 position, Vector2 projectileVelocity,
                           Vector2 surfaceNormal, float impactSpeed)
    {
        Vector2 incomingDir   = projectileVelocity.normalized;
        Vector2 reflectedDir  = Vector2.Reflect(incomingDir, surfaceNormal);
        Vector2 primaryDir    = (reflectedDir + Vector2.up * 0.2f).normalized;
        float   primaryAngle  = Mathf.Atan2(primaryDir.y, primaryDir.x);
        float   ejectaSpeed   = impactSpeed * ejectaVelocityFraction;

        for (int i = 0; i < ejectaParticleCount; i++)
        {
            if (activeEjecta.Count >= maxEjectaParticles) break;

            float angleOffset = (Random.Range(-ejectaConeSpread, ejectaConeSpread)
                              + Mathf.Cos(Random.Range(0f, 360f) * Mathf.Deg2Rad)
                              * Random.Range(0f, ejectaConeAngle)) * Mathf.Deg2Rad;
            float finalAngle  = primaryAngle + angleOffset;

            Vector2 dir = new Vector2(Mathf.Cos(finalAngle), Mathf.Sin(finalAngle));
            if (Vector2.Dot(dir, surfaceNormal) < 0f)
                dir = Vector2.Reflect(dir, surfaceNormal);

            float velMult = Random.Range(1f - ejectaVelocityRandomness, 1f + ejectaVelocityRandomness);
            Vector2 vel = dir * ejectaSpeed * velMult;

            float spawnDist = Random.Range(ejectaSpawnOffset * 0.5f, ejectaSpawnOffset);
            Vector2 pos = position + surfaceNormal * spawnDist
                        + Random.insideUnitCircle * ejectaSpawnScatter;

            float size = ejectaParticleSize * Random.Range(1f - ejectaSizeVariation, 1f + ejectaSizeVariation);

            EmitEjecta(pos, vel, size, ejectaLifetime, ejectaColor, isWake: false);
        }
    }

    // =========================================================================
    //  IMPACT SMOKE BLAST  (ported from IncendiaryImpactSystem)
    // =========================================================================

    void CreateImpactSmoke(Vector2 position, Vector2 projectileVelocity,
                           Vector2 surfaceNormal, float impactSpeed)
    {
        Vector2 reflectedDir = Vector2.Reflect(projectileVelocity.normalized, surfaceNormal);
        Vector2 primaryDir   = (reflectedDir + Vector2.up * 0.3f).normalized;
        float   primaryAngle = Mathf.Atan2(primaryDir.y, primaryDir.x);
        float   smokeSpeed   = impactSpeed * impactSmokeVelocityFraction;

        int clusters    = spawnInClusters ? Mathf.CeilToInt(impactSmokeCount / 3f) : impactSmokeCount;
        int puffsPerCluster = spawnInClusters ? 3 : 1;

        for (int c = 0; c < clusters; c++)
        {
            float clusterAngle = primaryAngle + Random.Range(-40f, 40f) * Mathf.Deg2Rad;
            Vector2 clusterDir = new Vector2(Mathf.Cos(clusterAngle), Mathf.Sin(clusterAngle));
            if (Vector2.Dot(clusterDir, surfaceNormal) < 0f)
                clusterDir = Vector2.Reflect(clusterDir, surfaceNormal);

            for (int p = 0; p < puffsPerCluster; p++)
            {
                if (activeSmoke.Count >= maxSmokeParticles) break;

                float   velVar = Random.Range(0.7f, 1.3f);
                Vector2 vel    = clusterDir * smokeSpeed * velVar;
                Vector2 pos    = position + surfaceNormal * Random.Range(0.05f, 0.15f)
                               + Random.insideUnitCircle * 0.2f;

                float size     = impactSmokeSize * Random.Range(0.9f, 1.2f);
                float lifetime = impactSmokeLifetime * Random.Range(0.9f, 1.1f);

                EmitSmoke(pos, vel, size, lifetime, impactSmokeColor,
                          impactSmokeRiseSpeed, impactSmokeBillowing, isImpactSmoke: true);
            }
        }
    }

    // =========================================================================
    //  IMPACT LIGHT BURST  (ported from IncendiaryImpactSystem)
    // =========================================================================

    void SpawnImpactLight(Vector2 position, float intensity)
    {
        GameObject obj  = new GameObject("WindBallImpactLight");
        obj.transform.SetParent(transform);
        obj.transform.position = position;

        var light2D = obj.AddComponent<UnityEngine.Rendering.Universal.Light2D>();
        light2D.lightType              = UnityEngine.Rendering.Universal.Light2D.LightType.Point;
        light2D.intensity              = impactLightIntensity * intensity;
        light2D.pointLightOuterRadius  = impactLightRadius * Mathf.Lerp(0.7f, 1.3f, intensity);
        light2D.color                  = impactLightColor;
        light2D.falloffIntensity       = 0.7f;

        activeLights.Add(new ImpactLight
        {
            go            = obj,
            light2D       = light2D,
            lifetime      = 0f,
            maxLifetime   = impactLightDuration,
            peakIntensity = impactLightIntensity * intensity
        });
    }

    // =========================================================================
    //  EMIT HELPERS
    // =========================================================================

    void EmitEjecta(Vector2 pos, Vector2 vel, float size, float lifetime, Color color, bool isWake)
    {
        EjectaParticle e = ejectaPool.Count > 0 ? ejectaPool.Dequeue() : AllocateEjecta();

        e.go.transform.position  = pos;
        e.velocity    = vel;
        e.lifetime    = 0f;
        e.maxLifetime = lifetime;
        e.initialSize = size;
        e.baseColor   = color;
        e.sr.sprite   = isWake ? wakeSprite : ejectaSprite;
        e.sr.color    = color;
        e.go.transform.localScale = Vector3.one * size;
        e.go.SetActive(true);
        activeEjecta.Add(e);
    }

    void EmitSmoke(Vector2 pos, Vector2 vel, float size, float lifetime,
                   Color color, float riseSpeed, float billowing, bool isImpactSmoke)
    {
        SmokePuff s = smokePool.Count > 0 ? smokePool.Dequeue() : AllocateSmoke();

        s.go.transform.position = pos;
        s.velocity       = vel;
        s.initialVelocity = vel;
        s.lifetime       = 0f;
        s.maxLifetime    = lifetime;
        s.initialSize    = size;
        s.baseColor      = color;
        s.rotationAngle  = Random.Range(0f, 360f);
        s.rotationSpeed  = Random.Range(-45f, 45f) * animeRotationSpeed;
        s.expansionRate  = billowing;
        s.customRiseSpeed = riseSpeed;
        s.isImpactSmoke  = isImpactSmoke;

        int idx = Random.Range(0, smokeVariants.Count);
        s.sr.sprite = smokeVariants[idx];
        s.sr.color  = color;
        s.go.transform.localScale = Vector3.one;
        s.go.transform.rotation   = Quaternion.Euler(0f, 0f, s.rotationAngle);
        s.go.SetActive(true);
        activeSmoke.Add(s);
    }

    // =========================================================================
    //  UPDATE LOOPS
    // =========================================================================

    void UpdateEjecta()
    {
        for (int i = activeEjecta.Count - 1; i >= 0; i--)
        {
            EjectaParticle e = activeEjecta[i];
            e.lifetime += Time.deltaTime;

            if (e.lifetime >= e.maxLifetime)
            {
                e.go.SetActive(false);
                ejectaPool.Enqueue(e);
                activeEjecta.RemoveAt(i);
                continue;
            }

            e.velocity.y += ejectaGravity * Time.deltaTime;
            e.velocity   *= ejectaAirResistance;

            Vector2 pos  = e.go.transform.position;
            pos          += e.velocity * Time.deltaTime;
            e.go.transform.position = pos;

            // Spin in the direction of travel
            float spin = e.velocity.magnitude * 60f;
            e.go.transform.Rotate(0f, 0f, spin * Time.deltaTime);

            // Fade + scale
            float t     = e.lifetime / e.maxLifetime;
            float alpha = e.baseColor.a * (1f - Mathf.Pow(t, 1.5f));
            float scale = e.initialSize * Mathf.Lerp(1f, 0.6f, t);
            e.sr.color                = new Color(e.baseColor.r, e.baseColor.g, e.baseColor.b, alpha);
            e.go.transform.localScale = Vector3.one * scale;
        }
    }

    void UpdateSmoke()
    {
        for (int i = activeSmoke.Count - 1; i >= 0; i--)
        {
            SmokePuff s = activeSmoke[i];
            s.lifetime += Time.deltaTime;

            if (s.lifetime >= s.maxLifetime)
            {
                s.go.SetActive(false);
                smokePool.Enqueue(s);
                activeSmoke.RemoveAt(i);
                continue;
            }

            float t = s.lifetime / s.maxLifetime;

            // Velocity bleed / rise  (same staged logic as IncendiaryImpactSystem)
            bool hasMomentum = s.initialVelocity.magnitude > 2f;
            if (hasMomentum)
            {
                if (t < 0.3f)
                {
                    float p = t / 0.3f;
                    s.velocity   = Vector2.Lerp(s.initialVelocity, s.initialVelocity * 0.15f, p);
                    s.velocity  *= 0.93f;
                    s.velocity.y += s.customRiseSpeed * 0.4f * p;
                }
                else if (t < 0.6f)
                {
                    float p = (t - 0.3f) / 0.3f;
                    s.velocity.x *= Mathf.Lerp(0.93f, 0.85f, p);
                    s.velocity.y  = Mathf.Lerp(s.velocity.y, s.customRiseSpeed * 0.9f, p * 0.5f);
                }
                else
                {
                    s.velocity.x *= 0.92f;
                    s.velocity.y  = s.customRiseSpeed * 0.9f;
                }
            }
            else
            {
                s.velocity.x *= 0.94f;
                s.velocity.y  = s.customRiseSpeed * Mathf.Lerp(0.85f, 1f, t * 0.4f);
            }

            Vector2 pos = s.go.transform.position;
            pos        += s.velocity * Time.deltaTime;
            s.go.transform.position = pos;

            s.rotationAngle += s.rotationSpeed * Time.deltaTime;
            s.go.transform.rotation = Quaternion.Euler(0f, 0f, s.rotationAngle);

            // Expand
            float expandCurve = t < 0.4f
                ? Mathf.Lerp(1f, 1.6f, t / 0.4f)
                : Mathf.Lerp(1.6f, 2.2f, (t - 0.4f) / 0.6f);
            s.go.transform.localScale = Vector3.one * (s.initialSize * expandCurve * s.expansionRate);

            // Fade
            float alpha = s.baseColor.a;
            if (t > 0.7f) alpha *= 1f - ((t - 0.7f) / 0.3f);
            s.sr.color = new Color(s.baseColor.r, s.baseColor.g, s.baseColor.b, alpha);
        }
    }

    void UpdateLights()
    {
        for (int i = activeLights.Count - 1; i >= 0; i--)
        {
            ImpactLight l = activeLights[i];
            l.lifetime += Time.deltaTime;

            if (l.lifetime >= l.maxLifetime)
            {
                Destroy(l.go);
                activeLights.RemoveAt(i);
                continue;
            }

            float t = l.lifetime / l.maxLifetime;
            float curve = t < 0.1f
                ? t / 0.1f
                : Mathf.Pow(1f - ((t - 0.1f) / 0.9f), 2.5f);

            l.light2D.intensity = l.peakIntensity * curve;
            float pulse = 1f + Mathf.Sin(l.lifetime * 30f) * 0.1f;
            l.light2D.pointLightOuterRadius = impactLightRadius * curve * pulse;
        }
    }

    // =========================================================================
    //  ALLOCATION
    // =========================================================================

    EjectaParticle AllocateEjecta()
    {
        GameObject obj = new GameObject("WBEjecta");
        obj.transform.SetParent(transform);
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite       = ejectaSprite;
        sr.color        = ejectaColor;
        sr.sortingOrder = 8;
        return new EjectaParticle { go = obj, sr = sr };
    }

    SmokePuff AllocateSmoke()
    {
        GameObject obj = new GameObject("WBSmoke");
        obj.transform.SetParent(transform);
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 7;
        return new SmokePuff { go = obj, sr = sr };
    }

    // =========================================================================
    //  TEXTURE / SPRITE GENERATION
    // =========================================================================

    void PreGenerateSmokeVariants()
    {
        for (int v = 0; v < SmokeVariantCount; v++)
        {
            var offsets = new List<Vector2>();
            var sizes   = new List<float>();

            offsets.Add(Random.insideUnitCircle * 0.08f);
            sizes.Add(Random.Range(0.9f, 1.1f));

            int extra = spheresPerPuff - 1;
            for (int i = 0; i < extra; i++)
            {
                float a = (360f / extra) * i + Random.Range(-40f, 40f);
                float d = Random.Range(0.25f, 0.65f);
                offsets.Add(new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * d);
                sizes.Add(Random.Range(0.5f, 0.95f));
            }
            // wispy extras
            for (int i = 0; i < Random.Range(1, 3); i++)
            {
                offsets.Add(Random.insideUnitCircle * Random.Range(0.6f, 0.9f));
                sizes.Add(Random.Range(0.3f, 0.5f));
            }

            Texture2D tex = BuildSmokeTex(offsets, sizes);
            smokeVariants.Add(Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f));
        }
    }

    Texture2D BuildSmokeTex(List<Vector2> offsets, List<float> sizes)
    {
        int res = 64;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;

        float c = (res - 1) * 0.5f;
        for (int x = 0; x < res; x++)
        for (int y = 0; y < res; y++)
        {
            float nx = (x - c) / (res * 0.5f);
            float ny = (y - c) / (res * 0.5f);
            float density = 0f;

            for (int i = 0; i < offsets.Count; i++)
            {
                float dx   = nx - offsets[i].x;
                float dy   = ny - offsets[i].y;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float d    = Mathf.Max(0f, 1f - dist / (sizes[i] * 1.1f));
                density   += Mathf.Pow(d, 0.8f);
            }
            density = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(density));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, density));
        }
        tex.Apply();
        return tex;
    }

    Sprite CreateSoftCircleSprite(int res, Color tint)
    {
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;

        float c = (res - 1) * 0.5f;
        for (int x = 0; x < res; x++)
        for (int y = 0; y < res; y++)
        {
            float nx   = (x - c) / (res * 0.5f);
            float ny   = (y - c) / (res * 0.5f);
            float dist = Mathf.Sqrt(nx * nx + ny * ny);
            float a    = Mathf.Clamp01(1.1f - dist * 1.1f);
            tex.SetPixel(x, y, new Color(tint.r, tint.g, tint.b, a * tint.a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
    }
}