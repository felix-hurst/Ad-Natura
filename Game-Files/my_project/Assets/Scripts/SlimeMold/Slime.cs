using UnityEngine;

public class Slime : MonoBehaviour
{
    [Header("Compute Shader (Required)")]
    public ComputeShader shader;

    [Header("Simulation")]
    [Tooltip("Texture width. Higher = sharper visuals, more GPU work")]
    [SerializeField] private int width = 128;
    [Tooltip("Texture height. Higher = sharper visuals, more GPU work")]
    [SerializeField] private int height = 96;
    [Tooltip("Number of slime agents. More = denser networks")]
    [SerializeField] private int numAgents = 1024;
    [Tooltip("Base movement speed when attracted to water")]
    [SerializeField] private float moveSpeed = 6f;

    [Header("Trail")]
    [Tooltip("How fast trails blur/spread. Lower = sharper trails")]
    [SerializeField, Range(0f, 1f)] private float diffuseSpeed = 0.25f;
    [Tooltip("How fast trails fade. Lower = longer-lasting networks")]
    [SerializeField, Range(0f, 0.5f)] private float evaporateSpeed = 0.04f;
    [Tooltip("How much agents follow existing trails vs water. Higher = more vein formation")]
    [SerializeField, Range(0f, 2f)] private float trailWeight = 0.8f;

    [Header("Movement")]
    [Tooltip("Speed when no water nearby. Keep low for player control")]
    [SerializeField, Range(0f, 2f)] private float idleSpeed = 0.3f;
    [Tooltip("Speed multiplier when attracted to water")]
    [SerializeField, Range(1f, 5f)] private float attractedSpeedMultiplier = 2.5f;

    [Header("Sensors")]
    [Tooltip("How far ahead agents look for trails/water")]
    [SerializeField, Range(2f, 30f)] private float sensorLength = 5f;
    [Tooltip("Angle between sensors (degrees). Lower = tighter turns")]
    [SerializeField, Range(15f, 45f)] private float sensorAngle = 25f;
    [Tooltip("How fast agents turn. Lower = smoother, organic movement")]
    [SerializeField, Range(1f, 10f)] private float turnSpeed = 4f;

    [Header("Spawn")]
    [Tooltip("Spawn X position (0-1 normalized)")]
    [SerializeField, Range(0f, 1f)] private float spawnX = 0.5f;
    [Tooltip("Spawn Y position (0-1 normalized)")]
    [SerializeField, Range(0f, 1f)] private float spawnY = 0.5f;
    [Tooltip("Spawn radius (0-1 normalized)")]
    [SerializeField, Range(0.01f, 0.5f)] private float spawnRadius = 0.1f;

    [Header("Bounds")]
    [Tooltip("Optional: derive bounds from this object's collider/sprite")]
    [SerializeField] private GameObject boundingObject;
    [SerializeField] private Rect worldBounds = new Rect(-10f, -7.5f, 20f, 15f);

    [Header("Pulse (Cytoplasmic Streaming)")]
    [Tooltip("How strongly trails brighten and dim. 0 = no pulse, 0.3 = subtle breathing, 0.6 = dramatic")]
    [SerializeField, Range(0f, 0.8f)] private float pulseAmplitude = 0.2f;
    [Tooltip("Pulse frequency when far from water (Hz). Low = slow, meditative breathing")]
    [SerializeField, Range(0.1f, 2f)] private float pulseMinFreq = 0.3f;
    [Tooltip("Pulse frequency when near water (Hz). Higher = excited, rapid streaming")]
    [SerializeField, Range(0.1f, 6f)] private float pulseMaxFreq = 1.5f;
    [Tooltip("Wave spatial scale. Controls how fast the pulse phase shifts across the organism. Higher = tighter ripples")]
    [SerializeField, Range(0f, 1f)] private float pulseWaveScale = 0f;

    [Header("Behavioral Drift")]
    [Tooltip("How much agents differ from each other. 0 = all identical, 1 = wide personality variation")]
    [SerializeField, Range(0f, 1f)] private float driftStrength = 0.5f;
    [Tooltip("How fast agent personalities shift over time. Higher = more restless colony")]
    [SerializeField, Range(0f, 1f)] private float driftSpeed = 0.15f;

    [Header("Entropic Decay")]
    [Tooltip("Spatial scale of decay noise. Lower = larger patches of persistence, higher = fine grain")]
    [SerializeField, Range(0.005f, 0.2f)] private float entropyScale = 0.05f;
    [Tooltip("How fast the noise pattern drifts over time")]
    [SerializeField, Range(0f, 1f)] private float entropySpeed = 0.15f;
    [Tooltip("How much evaporation varies. 0 = uniform (original), 1 = some pixels barely evaporate at all")]
    [SerializeField, Range(0f, 1f)] private float entropyStrength = 0.4f;

    [Header("Hazard (Calamity)")]
    [Tooltip("How strongly agents steer away from hazards. Higher = wider avoidance zone")]
    [SerializeField, Range(0f, 20f)] private float hazardAvoidanceStrength = 8f;
    [Tooltip("Color the dying agent's trail turns before fading away")]
    [SerializeField] private Color deathTint = new Color(0.7f, 0.55f, 0.1f, 1f);
    [Tooltip("How fast hazard zones corrode existing trails. Higher = faster dissolution")]
    [SerializeField, Range(0f, 15f)] private float hazardCorrosionRate = 6f;
    [Tooltip("Trail deposit reduction in hazard zone (0=full trails, 1=no trails)")]
    [SerializeField, Range(0f, 1f)] private float hazardTrailDampen = 0.85f;

    [Header("Water Consumption")]
    [Tooltip("How much water the organism absorbs per second at each occupied cell")]
    [SerializeField, Range(0f, 5f)] private float consumptionRate = 0.15f;
    [Tooltip("Minimum trail density needed to consume water at a position")]
    [SerializeField, Range(0f, 0.5f)] private float consumptionThreshold = 0.05f;
    [Tooltip("How brightly the organism glows when feeding")]
    [SerializeField, Range(0f, 1f)] private float glowIntensity = 0.4f;
    [Tooltip("How fast the glow fades after feeding stops")]
    [SerializeField, Range(0f, 5f)] private float glowDecayRate = 1.5f;

    [Header("Microphone Reactivity")]
    [Tooltip("Optional: drag a MicrophoneInput component here. Leave empty to disable mic")]
    [SerializeField] private MicrophoneInput micInput;
    [Tooltip("Master mix: how strongly mic affects simulation. 0 = mic ignored entirely")]
    [SerializeField, Range(0f, 1f)] private float micInfluence = 0.6f;
    [Tooltip("Loud sound -> faster trail evaporation (agitation)")]
    [SerializeField, Range(0f, 5f)] private float micAmpEvaporateGain = 1.8f;
    [Tooltip("Loud sound -> faster turning (agitation)")]
    [SerializeField, Range(0f, 4f)] private float micAmpTurnGain = 1.2f;
    [Tooltip("Low pitch -> trail diffusion boost (network softens)")]
    [SerializeField, Range(0f, 3f)] private float micLowFreqDiffuseGain = 1.5f;
    [Tooltip("High pitch -> faster turning (nervous contraction)")]
    [SerializeField, Range(0f, 3f)] private float micHighFreqTurnGain = 0.6f;
    [Tooltip("High pitch -> shorter sensor reach (myopic, contracted)")]
    [SerializeField, Range(0f, 0.7f)] private float micHighFreqSensorShrink = 0.25f;
    [Tooltip("Quiet presence -> pulse frequency lift (organism notices you)")]
    [SerializeField, Range(0f, 2f)] private float micQuietPulseGain = 1.0f;
    [Tooltip("Sudden sound -> temporary trail weight burst (memory of the sound event)")]
    [SerializeField, Range(0f, 5f)] private float micOnsetTrailBurst = 1.5f;

    [Header("Consent/Resistance")]
    [Tooltip("Amplitude above which stress accumulates")]
    [SerializeField, Range(0f, 1f)] private float stressThreshold = 0.45f;
    [Tooltip("How fast stress fills when overstimulated")]
    [SerializeField, Range(0f, 2f)] private float stressAccumulationRate = 0.35f;
    [Tooltip("How fast stress drains in silence")]
    [SerializeField, Range(0f, 1f)] private float stressRecoveryRate = 0.06f;
    [Tooltip("Stress level that triggers full withdrawal")]
    [SerializeField, Range(0f, 1f)] private float refractoryThreshold = 0.75f;
    [Tooltip("How long withdrawal lasts once triggered (seconds)")]
    [SerializeField, Range(1f, 30f)] private float refractoryDuration = 12f;

    [Header("Visual")]
    [Tooltip("Slime color. Golden yellow is classic Physarum")]
    [SerializeField] private Color slimeColor = new Color(1f, 0.9f, 0.2f, 1f);
    [SerializeField] private int sortingOrder = 5;

    private RenderTexture trailMapA;
    private RenderTexture trailMapB;
    private bool useBufferA = true;

    private Texture2D cpuSampleTexture;
    private Texture2D externalHazardMap;
    private Texture2D defaultHazardMap;
    private bool hasExternalHazard;

    private RenderTexture defaultAttractionMap;
    private ComputeBuffer agentsBuffer;

    // Water consumption state
    private CellularLiquidSimulation liquidSim;
    private float metabolicGlow;
    private float consumptionTimer;
    private Color[] cachedTrailPixels;

    // Consent/Resistance state
    private float stressLevel;
    private float refractoryTimer;
    private bool inRefractory;
    private float smoothRefractory;
    private Texture2D displayTexture;
    private Sprite displaySprite;
    private SpriteRenderer targetRenderer;
    private GameObject slimeQuad;

    private int kernelUpdate;
    private int kernelPostprocess;

    private RenderTexture externalWaterMap;
    private float externalWaterStrength;
    private bool hasExternalAttraction;

    public struct Agent
    {
        public Vector2 position;
        public float angle;
        public Vector4 type;
    }

    public float GetSpawnX() => spawnX;
    public float GetSpawnY() => spawnY;

    void Start()
    {
        shader = Resources.Load<ComputeShader>("Slime");
        Debug.LogError($"shader after Resources.Load: {(shader == null ? "NULL" : shader.name)}");

        if (shader == null)
        {
            Debug.LogError("Slime: Compute shader not found in Resources!");
            enabled = false;
            return;
        }


        // Each instance needs its own shader copy so multiple slime molds
        // don't overwrite each other's texture bindingsshader = Instantiate(shader);

        if (boundingObject != null)
            CalculateBoundsFromObject();

        numAgents = Mathf.Max(16, (numAgents / 16) * 16);
        width = Mathf.Max(8, (width / 8) * 8);
        height = Mathf.Max(8, (height / 8) * 8);

        kernelUpdate = shader.FindKernel("Update");
        kernelPostprocess = shader.FindKernel("Postprocess");

        trailMapA = CreateTrailTexture();
        trailMapB = CreateTrailTexture();

        InitializeAgents();

        defaultAttractionMap = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
        defaultAttractionMap.enableRandomWrite = true;
        defaultAttractionMap.Create();
        ClearTexture(defaultAttractionMap);

        defaultHazardMap = new Texture2D(width, height, TextureFormat.RFloat, false);
        var clearPixels = new Color[width * height];
        defaultHazardMap.SetPixels(clearPixels);
        defaultHazardMap.Apply();

        liquidSim = FindAnyObjectByType<CellularLiquidSimulation>();

        SetupDisplay();
    }

    RenderTexture CreateTrailTexture()
    {
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat);
        rt.enableRandomWrite = true;
        rt.filterMode = FilterMode.Point;
        rt.Create();
        return rt;
    }

    void ClearTexture(RenderTexture rt)
    {
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = null;
    }

    void InitializeAgents()
    {
        var agents = new Agent[numAgents];
        float centerX = width * spawnX;
        float centerY = height * spawnY;
        float radius = Mathf.Min(width, height) * spawnRadius;

        for (int i = 0; i < numAgents; i++)
        {
            float angle = Random.Range(0, 2 * Mathf.PI);
            float dist = Random.Range(0f, 1f) * Random.Range(0f, 1f) * radius;
            agents[i].position = new Vector2(
                Mathf.Clamp(centerX + Mathf.Cos(angle) * dist, 0, width - 1),
                Mathf.Clamp(centerY + Mathf.Sin(angle) * dist, 0, height - 1)
            );
            agents[i].angle = Random.Range(0, 2 * Mathf.PI);
            agents[i].type = Vector4.one;
        }

        agentsBuffer = new ComputeBuffer(numAgents, sizeof(float) * 7);
        agentsBuffer.SetData(agents);
    }

    void CalculateBoundsFromObject()
    {
        var col = boundingObject.GetComponent<Collider2D>();
        if (col != null)
        {
            worldBounds = new Rect(col.bounds.min.x, col.bounds.min.y, col.bounds.size.x, col.bounds.size.y);
            return;
        }
        var sr = boundingObject.GetComponent<SpriteRenderer>();
        if (sr != null)
            worldBounds = new Rect(sr.bounds.min.x, sr.bounds.min.y, sr.bounds.size.x, sr.bounds.size.y);
    }

    void SetupDisplay()
    {
        displayTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        displayTexture.filterMode = FilterMode.Point;

        // Separate texture purely for CPU sampling — never touched by sprite system
        cachedTrailPixels = new Color[width * height];
        cpuSampleTexture = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
        cpuSampleTexture.filterMode = FilterMode.Point;

        slimeQuad = new GameObject("SlimeDisplay");
        targetRenderer = slimeQuad.AddComponent<SpriteRenderer>();
        targetRenderer.sortingOrder = sortingOrder;

        displaySprite = Sprite.Create(displayTexture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        targetRenderer.sprite = displaySprite;

        float scaleX = worldBounds.width / (width / 100f);
        float scaleY = worldBounds.height / (height / 100f);
        targetRenderer.transform.localScale = new Vector3(scaleX, scaleY, 1f);
        targetRenderer.transform.position = new Vector3(worldBounds.center.x, worldBounds.center.y, -1f);
    }

    void FixedUpdate()
    {
        if (trailMapA == null || agentsBuffer == null) return;

        // --- Local copies of parameters for mic modulation ---
        // Mic shifts conditions each frame without mutating Inspector values.
        float effEvaporateSpeed = evaporateSpeed;
        float effDiffuseSpeed = diffuseSpeed;
        float effTurnSpeed = turnSpeed;
        float effSensorLength = sensorLength;
        float effTrailWeight = trailWeight;
        float effPulseMinFreq = pulseMinFreq;
        float effPulseAmplitude = pulseAmplitude;
        float effDriftStrength = driftStrength;
        float currentStress = 0f;
        float currentRefractory = 0f;

        // --- Consent/Resistance: stress + refractory (runs even if mic drops out) ---
        // Recovery ticks independently so the organism doesn't freeze mid-withdrawal
        // if the mic disconnects or micInfluence is set to 0.
        float micAmp = 0f, micCentroid = 0f, micOnset = 0f;
        bool micAvailable = micInput != null && micInput.IsActive && micInfluence > 0f;

        if (micAvailable)
        {
            micAmp = micInput.Amplitude;
            micCentroid = micInput.SpectralCentroid;
            micOnset = micInput.Onset;
        }

        if (inRefractory)
        {
            // Loud input during withdrawal pauses recovery — the organism
            // cannot heal while still being assaulted. Silence is required.
            bool stillLoud = micAvailable && micAmp > stressThreshold;
            if (!stillLoud)
                refractoryTimer -= Time.fixedDeltaTime;

            if (refractoryTimer <= 0f)
            {
                inRefractory = false;
                stressLevel = 0.3f; // fragile after withdrawal, not fully reset
            }
        }
        else if (micAvailable)
        {
            if (micAmp > stressThreshold)
                stressLevel += (micAmp - stressThreshold) * stressAccumulationRate * Time.fixedDeltaTime;
            else
                stressLevel -= stressRecoveryRate * Time.fixedDeltaTime;

            stressLevel = Mathf.Clamp01(stressLevel);

            if (stressLevel >= refractoryThreshold)
            {
                inRefractory = true;
                refractoryTimer = refractoryDuration;
            }
        }

        currentStress = stressLevel;
        float targetRefractory = inRefractory ? Mathf.Clamp01(refractoryTimer / refractoryDuration) : 0f;
        smoothRefractory = Mathf.Lerp(smoothRefractory, targetRefractory, 1.5f * Time.fixedDeltaTime);
        currentRefractory = smoothRefractory;

        // --- Microphone modulation ---
        if (micAvailable)
        {
            // Responsiveness fades as stress builds — the organism gradually
            // stops listening before full withdrawal. Not a switch, a retreat.
            float responsiveness;
            if (inRefractory)
                responsiveness = (1f - currentRefractory) * 0.1f;
            else
                responsiveness = 1f - stressLevel * 0.7f;
            float mix = micInfluence * responsiveness;

            // Quiet presence: pulse frequency lifts gently (organism notices you)
            float quietness = 1f - micAmp;
            effPulseMinFreq *= 1f + quietness * micQuietPulseGain * mix * 0.3f;

            // Loud sound: evaporation increases (agitation, dissolution)
            effEvaporateSpeed *= 1f + micAmp * micAmpEvaporateGain * mix;

            // Turn speed: combine amplitude (agitation) + pitch (nervousness) additively
            float turnBoost = micAmp * micAmpTurnGain + micCentroid * micHighFreqTurnGain;
            effTurnSpeed *= 1f + turnBoost * mix;

            // Low frequency: diffusion boost (network softens, expands)
            float lowFreq = 1f - micCentroid;
            effDiffuseSpeed = Mathf.Clamp01(effDiffuseSpeed + lowFreq * micLowFreqDiffuseGain * mix * 0.1f);

            // High frequency: sensor shrink (nervous, contracted)
            // Floor at 50% of base length — too-short sensors cause agents to
            // lock onto nearby trails and spiral into dense clusters
            float shrink = micCentroid * micHighFreqSensorShrink * mix;
            effSensorLength *= Mathf.Max(1f - shrink, 0.5f);

            // Onset: temporary trail weight burst (memory of the sound event)
            effTrailWeight += micOnset * micOnsetTrailBurst * mix;
        }

        var readBuffer = useBufferA ? trailMapA : trailMapB;
        var writeBuffer = useBufferA ? trailMapB : trailMapA;

        shader.SetTexture(kernelUpdate, "TrailMap", readBuffer);
        shader.SetInt("width", width);
        shader.SetInt("height", height);
        shader.SetInt("numAgents", numAgents);
        shader.SetFloat("moveSpeed", moveSpeed);
        shader.SetFloat("deltaTime", Time.fixedDeltaTime);
        shader.SetFloat("gameTime", Time.time);
        shader.SetFloat("sensorLength", effSensorLength);
        shader.SetFloat("sensorAngleSpacing", sensorAngle * Mathf.Deg2Rad);
        shader.SetFloat("turnSpeed", effTurnSpeed);
        shader.SetFloat("trailWeight", effTrailWeight);
        shader.SetFloat("idleSpeed", idleSpeed);
        shader.SetFloat("attractedSpeedMultiplier", attractedSpeedMultiplier);
        shader.SetFloat("driftStrength", effDriftStrength);
        shader.SetFloat("driftSpeed", driftSpeed);
        shader.SetFloat("micStress", currentStress);
        shader.SetFloat("refractoryState", currentRefractory);
        shader.SetVector("slimeColor", new Vector4(slimeColor.r, slimeColor.g, slimeColor.b, slimeColor.a));

        var waterMap = hasExternalAttraction && externalWaterMap != null ? externalWaterMap : defaultAttractionMap;
        var waterStrength = hasExternalAttraction ? externalWaterStrength : 0f;
        shader.SetTexture(kernelUpdate, "WaterAttractionMap", waterMap);
        shader.SetFloat("waterAttractionStrength", waterStrength);
        // Hazard map + death visual parameters
        var hazMap = hasExternalHazard && externalHazardMap != null ? externalHazardMap : defaultHazardMap;
        shader.SetTexture(kernelUpdate, "HazardMap", hazMap);
        shader.SetFloat("spawnCenterX", width * spawnX);
        shader.SetFloat("spawnCenterY", height * spawnY);
        shader.SetFloat("spawnRadius", Mathf.Min(width, height) * spawnRadius);
        shader.SetFloat("hazardAvoidanceStrength", hazardAvoidanceStrength);
        shader.SetVector("deathTint", new Vector4(deathTint.r, deathTint.g, deathTint.b, deathTint.a));

        shader.SetFloat("hazardCorrosionRate", hazardCorrosionRate);
        shader.SetFloat("hazardTrailDampen", hazardTrailDampen);

        shader.SetBuffer(kernelUpdate, "agents", agentsBuffer);
        shader.Dispatch(kernelUpdate, numAgents / 16, 1, 1);

        shader.SetFloat("evaporateSpeed", effEvaporateSpeed);
        shader.SetFloat("diffuseSpeed", effDiffuseSpeed);

        // Pulse parameters
        shader.SetFloat("pulseAmplitude", effPulseAmplitude);
        shader.SetFloat("pulseMinFreq", effPulseMinFreq);
        shader.SetFloat("pulseMaxFreq", pulseMaxFreq);
        shader.SetFloat("pulseWaveScale", pulseWaveScale);

        // Entropic Decay parameters
        shader.SetFloat("entropyScale", entropyScale);
        shader.SetFloat("entropySpeed", entropySpeed);
        shader.SetFloat("entropyStrength", entropyStrength);

        // Metabolic glow — organism brightens when consuming water
        shader.SetFloat("metabolicGlow", metabolicGlow * glowIntensity);

        shader.SetTexture(kernelPostprocess, "TrailMap", readBuffer);
        shader.SetTexture(kernelPostprocess, "TrailMapProcessed", writeBuffer);
        shader.SetTexture(kernelPostprocess, "WaterAttractionMap", waterMap);
        shader.SetTexture(kernelPostprocess, "HazardMap", hazMap);
        shader.Dispatch(kernelPostprocess, width / 8, height / 8, 1);

        useBufferA = !useBufferA;

        // Blit to a plain non-compute RT first — ReadPixels from enableRandomWrite
        // textures silently fails on many platforms
        RenderTexture blitTarget = RenderTexture.GetTemporary(
            width, height, 0, RenderTextureFormat.ARGBFloat);

        Graphics.Blit(writeBuffer, blitTarget);

        RenderTexture.active = blitTarget;
        displayTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        cpuSampleTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        RenderTexture.active = null;

        RenderTexture.ReleaseTemporary(blitTarget);
        displayTexture.Apply();
        cpuSampleTexture.Apply(false);

        ConsumeWater();
    }

    void ConsumeWater()
    {
        if (liquidSim == null || consumptionRate <= 0f) return;

        // Decay glow toward 0 every frame
        metabolicGlow = Mathf.MoveTowards(metabolicGlow, 0f, glowDecayRate * Time.fixedDeltaTime);

        // Only sample every ~0.2s to avoid per-frame cost
        consumptionTimer -= Time.fixedDeltaTime;
        if (consumptionTimer > 0f) return;
        consumptionTimer = 0.2f;

        float totalConsumed = 0f;
        int sampleStep = Mathf.Max(2, Mathf.Min(width, height) / 16);

        // Bulk read — reuse reference, GC handles the old array
        cachedTrailPixels = cpuSampleTexture.GetPixels();

        for (int y = 0; y < height; y += sampleStep)
        {
            for (int x = 0; x < width; x += sampleStep)
            {
                Color trail = cachedTrailPixels[y * width + x];
                float density = trail.r + trail.g + trail.b;
                if (density < consumptionThreshold) continue;

                float worldX = worldBounds.x + (float)x / width * worldBounds.width;
                float worldY = worldBounds.y + (float)y / height * worldBounds.height;
                Vector2 worldPos = new Vector2(worldX, worldY);

                // Sample a small cluster of liquid cells — the slime pixel
                // covers multiple liquid grid cells due to resolution mismatch
                Vector2Int gridPos = liquidSim.WorldToGrid(worldPos);
                for (int gy = -1; gy <= 1; gy++)
                {
                    for (int gx = -1; gx <= 1; gx++)
                    {
                        float w = liquidSim.GetWater(gridPos.x + gx, gridPos.y + gy);
                        if (w < 0.01f) continue;
                        float amount = Mathf.Min(w, consumptionRate * 0.2f);
                        liquidSim.RemoveWater(worldPos + new Vector2(gx * 0.1f, gy * 0.1f), amount);
                        totalConsumed += amount;
                    }
                }
            }
        }

        // Feed the glow — organism brightens when metabolizing
        if (totalConsumed > 0f)
            metabolicGlow = Mathf.Min(1f, metabolicGlow + totalConsumed * 2f);
    }

    void OnDestroy()
    {
        trailMapA?.Release();
        trailMapB?.Release();
        defaultAttractionMap?.Release();
        if (defaultHazardMap != null) Destroy(defaultHazardMap);
        agentsBuffer?.Release();
        if (shader != null) Destroy(shader);
        if (displayTexture != null) Destroy(displayTexture);
        if (cpuSampleTexture != null) Destroy(cpuSampleTexture);
        if (displaySprite != null) Destroy(displaySprite);
        if (slimeQuad != null) Destroy(slimeQuad);
    }

    public void SetAttractionMaps(RenderTexture waterMap, float waterStrength)
    {
        externalWaterMap = waterMap;
        externalWaterStrength = waterStrength;
        hasExternalAttraction = true;
    }

    public void SetHazardMap(Texture2D hazardMap)
    {
        externalHazardMap = hazardMap;
        hasExternalHazard = true;
    }

    public Rect GetWorldBounds() => worldBounds;

    public void SetWorldBounds(Rect bounds)
    {
        worldBounds = bounds;
        if (targetRenderer != null)
        {
            float scaleX = worldBounds.width / (width / 100f);
            float scaleY = worldBounds.height / (height / 100f);
            targetRenderer.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            targetRenderer.transform.position = new Vector3(worldBounds.center.x, worldBounds.center.y, 0f);
        }
    }

    public Vector2Int GetSimulationResolution() => new Vector2Int(width, height);

    /// <summary>
    /// Get the current trail texture for sampling slime density.
    /// Used by SlimeDecomposer to determine decomposition damage.
    /// </summary>
    public Texture2D GetTrailTexture() => cpuSampleTexture;

    void OnDrawGizmos()
    {
        Rect bounds = worldBounds;
        if (boundingObject != null)
        {
            var col = boundingObject.GetComponent<Collider2D>();
            if (col != null)
                bounds = new Rect(col.bounds.min.x, col.bounds.min.y, col.bounds.size.x, col.bounds.size.y);
            else
            {
                var sr = boundingObject.GetComponent<SpriteRenderer>();
                if (sr != null)
                    bounds = new Rect(sr.bounds.min.x, sr.bounds.min.y, sr.bounds.size.x, sr.bounds.size.y);
            }
        }

        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
        Gizmos.DrawWireCube(bounds.center, new Vector3(bounds.width, bounds.height, 0));

        Vector2 spawnCenter = new Vector2(bounds.x + bounds.width * spawnX, bounds.y + bounds.height * spawnY);
        float radius = Mathf.Min(bounds.width, bounds.height) * spawnRadius;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Gizmos.DrawWireSphere(spawnCenter, radius);
        Gizmos.DrawSphere(spawnCenter, 0.15f);
    }
}