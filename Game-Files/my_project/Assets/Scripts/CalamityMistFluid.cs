using UnityEngine;

/// <summary>
/// Stable Fluids (Navier-Stokes) GPU simulation.
/// Wind is a constant velocity bias applied every frame across the whole field,
/// fading near the ground so mist rises before being blown sideways.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class CalamityMistFluid : MonoBehaviour
{
    // ── Simulation ────────────────────────────────────────────────────────
    [Header("Simulation")]
    public int resolution = 128;

    [Range(0f, 0.0005f)]
    public float viscosity = 0.0001f;

    [Range(0f, 0.005f)]
    public float diffusion = 0.0002f;

    [Range(0.8f, 0.999f)]
    [Tooltip("Higher = density lingers longer and travels further. " +
             "steady-state density = densityStrength / (1 - this). " +
             "0.988 gives ~40% further travel than 0.978.")]
    public float densityDissipation  = 0.993f;

    [Range(0.8f, 0.99f)]
    [Tooltip("Higher = velocity persists longer. Helps wind carry density further.")]
    public float velocityDissipation = 0.97f;

    [Range(0f, 30f)]
    public float vorticityStrength = 18f;

    [Range(1, 40)]
    public int pressureIterations = 25;

    // ── Wind ──────────────────────────────────────────────────────────────
    [Header("Wind")]
    [Tooltip("Horizontal wind speed in UV units per frame. " +
             "Positive = left to right. 0.04 is a gentle breeze, 0.10 is strong.")]
    [Range(-0.15f, 0.15f)]
    public float windX = 0.02f;

    [Tooltip("Vertical wind component. Small positive gives a slight uplift.")]
    [Range(-0.05f, 0.05f)]
    public float windY = 0.005f;

    [Tooltip("Animate wind strength with a sine wave for gusting feel.")]
    public bool animateWind = true;

    [Range(0f, 1f)]
    [Tooltip("How much the wind speed varies (0 = steady, 1 = fully gusting).")]
    public float windGustAmount = 0.4f;

    [Range(0.1f, 2f)]
    [Tooltip("Speed of wind gust cycle in Hz.")]
    public float windGustFrequency = 0.3f;

    // ── Emitter ───────────────────────────────────────────────────────────
    [Header("Emitter")]
    public float rockWidth  = 5.5f;
    public float rockHeight = 4f;

    [Range(0f, 0.5f)]
    public float emitterRadius = 0.20f;

    [Range(0f, 0.05f)]
    public float emitterStrength = 0.025f;

    [Range(0f, 0.12f)]
    public float densityStrength = 0.045f;

    public float emitterY = 0.12f;

    public float quadWidthMultiplier  = 4.5f;   // wider so wind can carry density far
    public float quadHeightMultiplier = 2.2f;   // taller for more vertical travel
    public float quadYOffset          = 0.0f;

    // ── Obstacle collision ────────────────────────────────────────────────
    [Header("Obstacle Collision")]
    [Tooltip("Layers that act as solid obstacles. Set to Nothing to disable. " +
             "Do NOT include the layer your calamity rocks are on.")]
    public LayerMask obstacleLayerMask = 0;

    [Range(1, 10)]
    public int obstacleUpdateInterval = 2;

    // ── Visual ────────────────────────────────────────────────────────────
    [Header("Visual")]
    public Color mistColor    = new Color(0.04f, 0.02f, 0.06f, 1f);
    public Color mistColorAlt = new Color(0.06f, 0.03f, 0.08f, 1f);

    [Range(0f, 1f)]
    public float mistOpacity = 0.72f;

    public int    sortingOrder = 6;
    public string sortingLayer = "Default";

    [Header("Debug")]
    public bool verboseDebug       = false;
    public bool autoDisableVerbose = true;

    // ── Private state ─────────────────────────────────────────────────────
    private RenderTexture _velocityA, _velocityB;
    private RenderTexture _densityA,  _densityB;
    private RenderTexture _pressureA, _pressureB;
    private RenderTexture _vorticity;
    private RenderTexture _divergence;
    private RenderTexture _obstacleRT;

    private Material _simMat;
    private Material _dispMat;

    private Camera   _obstacleCamera;
    private Shader   _obstacleMaskShader;
    private int      _obstacleFrameCounter;

    private Texture2D _readbackTex;
    private bool      _didVerboseFrame;

    private static readonly int
        VelocityTex  = Shader.PropertyToID("_VelocityTex"),
        DensityTex   = Shader.PropertyToID("_DensityTex"),
        PressureTex  = Shader.PropertyToID("_PressureTex"),
        VorticityTex = Shader.PropertyToID("_VorticityTex"),
        DivergenceTex= Shader.PropertyToID("_DivergenceTex"),
        ObstacleTex  = Shader.PropertyToID("_ObstacleTex"),
        WindVelocity = Shader.PropertyToID("_WindVelocity"),
        Viscosity    = Shader.PropertyToID("_Viscosity"),
        Diffusion    = Shader.PropertyToID("_Diffusion"),
        DtProp       = Shader.PropertyToID("_Dt"),
        TexelSize    = Shader.PropertyToID("_TexelSize"),
        DissipDens   = Shader.PropertyToID("_DensityDissipation"),
        DissipVel    = Shader.PropertyToID("_VelocityDissipation"),
        Vorticity    = Shader.PropertyToID("_VorticityStrength"),
        EmitterPos   = Shader.PropertyToID("_EmitterPos"),
        EmitterRad   = Shader.PropertyToID("_EmitterRadius"),
        EmitterStr   = Shader.PropertyToID("_EmitterStrength"),
        DensityStr   = Shader.PropertyToID("_DensityStrength"),
        MistCol      = Shader.PropertyToID("_MistColor"),
        MistColAlt   = Shader.PropertyToID("_MistColorAlt"),
        MistOpa      = Shader.PropertyToID("_MistOpacity");

    private const int
        PassAdvectVelocity   = 0,
        PassDiffuse          = 1,
        PassDivergence       = 2,
        PassPressure         = 3,
        PassSubtractGradient = 4,
        PassVorticity        = 5,
        PassVorticityForce   = 6,
        PassAdvectDensity    = 7,
        PassAddForce         = 8,
        PassDisplay          = 9,
        PassInjectDensity    = 10;

    // ─────────────────────────────────────────────────────────────────────
    // Public initialisation
    // ─────────────────────────────────────────────────────────────────────

    public void Initialise(float width, float height, Color color, Color colorAlt,
                           float opacity, int sortOrder, string sortLayer,
                           float quadWidthMult   = 4.5f,
                           float quadHeightMult  = 2.2f,
                           float quadYOff        = 0.0f,
                           float emitY           = 0.12f,
                           float emitStrength    = 0.025f,
                           float densStrength    = 0.045f,
                           float vorticity       = 18f)
    {
        rockWidth            = width;
        rockHeight           = height;
        mistColor            = color;
        mistColorAlt         = colorAlt;
        mistOpacity          = opacity;
        this.sortingOrder    = sortOrder;
        this.sortingLayer    = sortLayer;
        quadWidthMultiplier  = quadWidthMult;
        quadHeightMultiplier = quadHeightMult;
        quadYOffset          = quadYOff;
        emitterY             = emitY;
        emitterStrength      = emitStrength;
        densityStrength      = densStrength;
        vorticityStrength    = vorticity;

        _readbackTex = new Texture2D(16, 16, TextureFormat.RGBAFloat, false);

        CreateRenderTextures();
        CreateMaterials();

        if (_dispMat != null)
        {
            BuildQuad();
            ClearTextures();
            SetupObstacleCamera();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Update
    // ─────────────────────────────────────────────────────────────────────

    void Update()
    {
        if (_simMat == null || _dispMat == null) return;

        _obstacleFrameCounter++;
        if (_obstacleFrameCounter >= obstacleUpdateInterval)
        {
            _obstacleFrameCounter = 0;
            RefreshObstacleMask();
        }

        bool log = verboseDebug && !_didVerboseFrame;

        float dt    = Mathf.Min(Time.deltaTime, 0.033f);
        float texel = 1f / resolution;

        // Compute current wind — optionally animated with a gust sine wave
        float gustMult = 1f;
        if (animateWind)
            gustMult = 1f + windGustAmount * Mathf.Sin(Time.time * windGustFrequency * Mathf.PI * 2f);

        Vector4 wind = new Vector4(windX * gustMult, windY * gustMult, 0f, 0f);

        _simMat.SetFloat(DtProp,      dt);
        _simMat.SetFloat(TexelSize,   texel);
        _simMat.SetFloat(Viscosity,   viscosity);
        _simMat.SetFloat(Diffusion,   diffusion);
        _simMat.SetFloat(DissipDens,  densityDissipation);
        _simMat.SetFloat(DissipVel,   velocityDissipation);
        _simMat.SetFloat(Vorticity,   vorticityStrength);
        _simMat.SetFloat(EmitterRad,  emitterRadius);
        _simMat.SetFloat(EmitterStr,  emitterStrength);
        _simMat.SetFloat(DensityStr,  densityStrength);
        _simMat.SetVector(WindVelocity, wind);
        _simMat.SetTexture(ObstacleTex, _obstacleRT);

        Vector4 emitUV = new Vector4(0.5f, emitterY, 0f, 0f);
        _simMat.SetVector(EmitterPos, emitUV);

        // 1 — inject velocity + wind
        _simMat.SetTexture(VelocityTex, _velocityA);
        Blit(_velocityA, _velocityB, PassAddForce);
        Swap(ref _velocityA, ref _velocityB);
        if (log) LogRT("AddForce", _velocityA, true);

        // 2 — advect velocity
        _simMat.SetTexture(VelocityTex, _velocityA);
        Blit(_velocityA, _velocityB, PassAdvectVelocity);
        Swap(ref _velocityA, ref _velocityB);
        if (log) LogRT("AdvectVelocity", _velocityA, true);

        // 3 — diffuse velocity
        for (int i = 0; i < pressureIterations; i++)
        {
            _simMat.SetTexture(VelocityTex, _velocityA);
            Blit(_velocityA, _velocityB, PassDiffuse);
            Swap(ref _velocityA, ref _velocityB);
        }
        if (log) LogRT("Diffuse", _velocityA, true);

        // 4 — divergence
        _simMat.SetTexture(VelocityTex, _velocityA);
        Blit(_velocityA, _divergence, PassDivergence);

        // 5 — pressure solve
        RenderTexture.active = _pressureA;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = _pressureB;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = null;

        _simMat.SetTexture(DivergenceTex, _divergence);
        for (int i = 0; i < pressureIterations; i++)
        {
            Blit(_pressureA, _pressureB, PassPressure);
            Swap(ref _pressureA, ref _pressureB);
        }
        if (log) LogRT("PressureSolve", _pressureA, false);

        // 6 — subtract gradient
        _simMat.SetTexture(VelocityTex, _velocityA);
        _simMat.SetTexture(PressureTex, _pressureA);
        Blit(_velocityA, _velocityB, PassSubtractGradient);
        Swap(ref _velocityA, ref _velocityB);
        if (log) LogRT("SubtractGradient", _velocityA, true);

        // 7 — vorticity
        _simMat.SetTexture(VelocityTex, _velocityA);
        Blit(_velocityA, _vorticity, PassVorticity);

        // 8 — vorticity confinement
        _simMat.SetTexture(VelocityTex,  _velocityA);
        _simMat.SetTexture(VorticityTex, _vorticity);
        Blit(_velocityA, _velocityB, PassVorticityForce);
        Swap(ref _velocityA, ref _velocityB);

        // 9 — advect density
        _simMat.SetTexture(VelocityTex, _velocityA);
        _simMat.SetTexture(DensityTex,  _densityA);
        Blit(_densityA, _densityB, PassAdvectDensity);
        Swap(ref _densityA, ref _densityB);

        // 10 — inject density
        _simMat.SetTexture(DensityTex, _densityA);
        _simMat.SetVector(EmitterPos,  emitUV);
        Blit(_densityA, _densityB, PassInjectDensity);
        Swap(ref _densityA, ref _densityB);
        if (log) LogRT("InjectDensity", _densityA, false);

        // 11 — update display
        _dispMat.SetTexture(DensityTex, _densityA);
        _dispMat.SetColor(MistCol,      mistColor);
        _dispMat.SetColor(MistColAlt,   mistColorAlt);
        _dispMat.SetFloat(MistOpa,      mistOpacity);

        if (log && autoDisableVerbose)
        {
            _didVerboseFrame = true;
            Debug.Log($"[Fluid:{name}] Verbose frame done.");
        }
    }

    void OnDestroy()
    {
        ReleaseTextures();
        if (_simMat      != null) Destroy(_simMat);
        if (_dispMat     != null) Destroy(_dispMat);
        if (_readbackTex != null) Destroy(_readbackTex);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Obstacle camera
    // ─────────────────────────────────────────────────────────────────────

    void SetupObstacleCamera()
    {
        _obstacleMaskShader = Shader.Find("Hidden/CalamityObstacleMask");
        if (_obstacleMaskShader == null)
        {
            Debug.LogWarning("[CalamityMistFluid] Hidden/CalamityObstacleMask not found. Obstacle collision disabled.");
            return;
        }

        GameObject camObj = new GameObject("_MistObstacleCam");
        camObj.transform.SetParent(transform);
        camObj.transform.localPosition = Vector3.zero;
        camObj.transform.localRotation = Quaternion.identity;

        _obstacleCamera = camObj.AddComponent<Camera>();
        _obstacleCamera.orthographic    = true;
        _obstacleCamera.cullingMask     = obstacleLayerMask;
        _obstacleCamera.clearFlags      = CameraClearFlags.SolidColor;
        _obstacleCamera.backgroundColor = Color.black;
        _obstacleCamera.targetTexture   = _obstacleRT;
        _obstacleCamera.nearClipPlane   = -10f;
        _obstacleCamera.farClipPlane    = 10f;
        _obstacleCamera.depth           = -99;
        _obstacleCamera.enabled         = false;

        AudioListener al = camObj.GetComponent<AudioListener>();
        if (al != null) Destroy(al);

        PositionObstacleCamera();
    }

    void PositionObstacleCamera()
    {
        if (_obstacleCamera == null) return;

        float quadW   = rockWidth  * quadWidthMultiplier;
        float quadH   = rockHeight * quadHeightMultiplier;
        float quadYOff= rockHeight * quadYOffset;

        _obstacleCamera.transform.position = new Vector3(
            transform.position.x,
            transform.position.y + quadYOff + quadH * 0.5f,
            -5f
        );
        _obstacleCamera.projectionMatrix = Matrix4x4.Ortho(
            -quadW * 0.5f, quadW * 0.5f,
            -quadH * 0.5f, quadH * 0.5f,
            -10f, 10f
        );
    }

    void RefreshObstacleMask()
    {
        if (_obstacleCamera == null || _obstacleMaskShader == null) return;
        PositionObstacleCamera();
        _obstacleCamera.targetTexture = _obstacleRT;
        _obstacleCamera.RenderWithShader(_obstacleMaskShader, "RenderType");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Setup
    // ─────────────────────────────────────────────────────────────────────

    void BuildQuad()
    {
        float w    = rockWidth  * quadWidthMultiplier;
        float h    = rockHeight * quadHeightMultiplier;
        float yOff = rockHeight * quadYOffset;

        Mesh mesh = new Mesh { name = "MistQuad" };
        mesh.vertices = new Vector3[]
        {
            new Vector3(-w * 0.5f, yOff,     0f),
            new Vector3( w * 0.5f, yOff,     0f),
            new Vector3(-w * 0.5f, yOff + h, 0f),
            new Vector3( w * 0.5f, yOff + h, 0f),
        };
        mesh.uv        = new Vector2[] { new Vector2(0,0), new Vector2(1,0),
                                         new Vector2(0,1), new Vector2(1,1) };
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;

        MeshRenderer mr     = GetComponent<MeshRenderer>();
        mr.sortingLayerName = sortingLayer;
        mr.sortingOrder     = sortingOrder;
        mr.material         = _dispMat;
    }

    void CreateRenderTextures()
    {
        _velocityA  = MakeRT();
        _velocityB  = MakeRT();
        _densityA   = MakeRT();
        _densityB   = MakeRT();
        _pressureA  = MakeRT();
        _pressureB  = MakeRT();
        _vorticity  = MakeRT();
        _divergence = MakeRT();

_obstacleRT = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGB32)
{
    filterMode = FilterMode.Point,  // was Bilinear
    wrapMode   = TextureWrapMode.Clamp,
    name       = "CalamityObstacleRT"
};
        _obstacleRT.Create();
    }

RenderTexture MakeRT()
{
    var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat)
    {
        filterMode = FilterMode.Point,  // was Bilinear — Point = hard pixel edges
        wrapMode   = TextureWrapMode.Clamp,
        name       = "CalamityMistRT"
    };
    rt.Create();
    return rt;
}

    void ClearTextures()
    {
        var rts = new[]
        {
            _velocityA, _velocityB,
            _densityA,  _densityB,
            _pressureA, _pressureB,
            _vorticity, _divergence
        };
        foreach (var rt in rts)
        {
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
        }
        RenderTexture.active = _obstacleRT;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = null;
    }

void CreateMaterials()
{
    Shader simShader = Shader.Find("Hidden/CalamityFluid");
    if (simShader == null)
    {
        Debug.LogError("[CalamityMistFluid] Hidden/CalamityFluid NOT FOUND.");
        enabled = false;
        return;
    }

    if (simShader.passCount < 11)
        Debug.LogError($"[CalamityMistFluid] Shader has {simShader.passCount} passes, need 11.");

    Shader dispShader = Shader.Find("Hidden/CalamityFluidDisplay");
    if (dispShader == null)
    {
        Debug.LogError("[CalamityMistFluid] Hidden/CalamityFluidDisplay NOT FOUND.");
        enabled = false;
        return;
    }

    // THIS LINE WAS MISSING — _simMat was never created so the whole pipeline was null
    _simMat  = new Material(simShader)  { hideFlags = HideFlags.HideAndDontSave };

    _dispMat = new Material(dispShader) { hideFlags = HideFlags.HideAndDontSave };
    _dispMat.SetColor("_MistColor",    mistColor);
    _dispMat.SetColor("_MistColorAlt", mistColorAlt);
    _dispMat.SetFloat("_MistOpacity",  mistOpacity);
    _dispMat.SetFloat("_PixelCount",   resolution); // use resolution as pixel count
}

    void ReleaseTextures()
    {
        var rts = new[]
        {
            _velocityA, _velocityB,
            _densityA,  _densityB,
            _pressureA, _pressureB,
            _vorticity, _divergence,
            _obstacleRT
        };
        foreach (var rt in rts)
            if (rt != null) rt.Release();
    }

    void Blit(RenderTexture src, RenderTexture dst, int pass)
        => Graphics.Blit(src, dst, _simMat, pass);

    static void Swap(ref RenderTexture a, ref RenderTexture b)
        => (a, b) = (b, a);

    void LogRT(string label, RenderTexture rt, bool isVelocity)
    {
        if (rt == null || !rt.IsCreated()) return;
        int s = 16;
        RenderTexture tmp = RenderTexture.GetTemporary(s, s, 0, RenderTextureFormat.ARGBFloat);
        Graphics.Blit(rt, tmp);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = tmp;
        _readbackTex.ReadPixels(new Rect(0, 0, s, s), 0, 0);
        _readbackTex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(tmp);
        Color[] px = _readbackTex.GetPixels();
        float maxMag = 0f, sumMag = 0f, peakR = 0f, peakG = 0f;
        foreach (var p in px)
        {
            float mag = isVelocity ? Mathf.Sqrt(p.r * p.r + p.g * p.g) : Mathf.Abs(p.r);
            if (mag > maxMag) { maxMag = mag; peakR = p.r; peakG = p.g; }
            sumMag += mag;
        }
        string extra = isVelocity ? $"peak=({peakR:F4},{peakG:F4})" : $"peakR={peakR:F5}";
        Debug.Log($"[Fluid:{name}] {label}: max={maxMag:F5} avg={sumMag/px.Length:F5} {extra}");
    }
}
