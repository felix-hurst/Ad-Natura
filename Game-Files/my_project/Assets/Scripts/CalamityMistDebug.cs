using UnityEngine;

/// <summary>
/// Attach this to the same GameObject as CalamityMistFluid to get
/// detailed debug output about what the fluid simulation is doing.
///
/// It reads back the velocity and density textures every N frames
/// and logs min/max/average values so you can see if the simulation
/// is actually running and producing non-zero data.
/// </summary>
[RequireComponent(typeof(CalamityMistFluid))]
public class CalamityMistDebug : MonoBehaviour
{
    [Header("Readback Settings")]
    [Tooltip("How many frames between texture readbacks (readback is slow, don't do every frame)")]
    public int readbackInterval = 30;

    [Tooltip("Log velocity field stats")]
    public bool logVelocity = true;

    [Tooltip("Log density field stats")]
    public bool logDensity = true;

    [Tooltip("Show an on-screen debug overlay")]
    public bool showOverlay = true;

    [Tooltip("Draw gizmo showing quad bounds and emitter position")]
    public bool showGizmos = true;

    // ── Internal state ────────────────────────────────────────────────────
    private CalamityMistFluid _fluid;
    private int               _frameCount;

    // Latest stats
    private float _velMax, _velAvg;
    private float _densMax, _densAvg;
    private bool  _shadersFound;
    private bool  _rtCreated;
    private string _lastError = "";

    // Readback textures (reused)
    private Texture2D _readbackTex;

    void Awake()
    {
        _fluid = GetComponent<CalamityMistFluid>();
    }

    void Start()
    {
        // Check shaders immediately
        var simShader  = Shader.Find("Hidden/CalamityFluid");
        var dispShader = Shader.Find("Hidden/CalamityFluidDisplay");

        _shadersFound = simShader != null && dispShader != null;

        if (simShader == null)
            Debug.LogError("[MistDebug] Hidden/CalamityFluid shader NOT FOUND. " +
                           "Go to Edit > Project Settings > Graphics > Always Included Shaders " +
                           "and add both CalamityFluid and CalamityFluidDisplay shaders.");
        else
            Debug.Log("[MistDebug] Hidden/CalamityFluid shader FOUND.");

        if (dispShader == null)
            Debug.LogError("[MistDebug] Hidden/CalamityFluidDisplay shader NOT FOUND.");
        else
            Debug.Log("[MistDebug] Hidden/CalamityFluidDisplay shader FOUND.");

        // Check MeshRenderer
        var mr = GetComponent<MeshRenderer>();
        if (mr == null)
        {
            Debug.LogError("[MistDebug] No MeshRenderer on fluid object!");
        }
        else
        {
            Debug.Log($"[MistDebug] MeshRenderer present. " +
                      $"Enabled={mr.enabled} " +
                      $"Material={(mr.material == null ? "NULL" : mr.material.shader.name)} " +
                      $"SortingLayer={mr.sortingLayerName} " +
                      $"SortingOrder={mr.sortingOrder}");
        }

        // Check MeshFilter
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.mesh == null)
            Debug.LogError("[MistDebug] MeshFilter has no mesh — quad was not built!");
        else
            Debug.Log($"[MistDebug] Quad mesh has {mf.mesh.vertexCount} vertices, " +
                      $"bounds={mf.mesh.bounds}");

        _readbackTex = new Texture2D(1, 1, TextureFormat.RGBAFloat, false);

        Debug.Log($"[MistDebug] Fluid settings: " +
                  $"resolution={_fluid.resolution} " +
                  $"emitterStrength={_fluid.emitterStrength} " +
                  $"densityStrength={_fluid.densityStrength} " +
                  $"vorticityStrength={_fluid.vorticityStrength} " +
                  $"densityDissipation={_fluid.densityDissipation} " +
                  $"velocityDissipation={_fluid.velocityDissipation} " +
                  $"emitterY={_fluid.emitterY} " +
                  $"emitterRadius={_fluid.emitterRadius}");

        Debug.Log($"[MistDebug] Quad dimensions: " +
                  $"quadWidthMult={_fluid.quadWidthMultiplier} " +
                  $"quadHeightMult={_fluid.quadHeightMultiplier} " +
                  $"quadYOffset={_fluid.quadYOffset} " +
                  $"rockWidth={_fluid.rockWidth} " +
                  $"rockHeight={_fluid.rockHeight}");
    }

    void Update()
    {
        _frameCount++;
        if (_frameCount % readbackInterval != 0) return;

        // Use reflection to access private RenderTextures on CalamityMistFluid
        var type = typeof(CalamityMistFluid);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        var velField  = type.GetField("_velocityA", flags);
        var densField = type.GetField("_densityA",  flags);
        var simField  = type.GetField("_simMat",    flags);
        var dispField = type.GetField("_dispMat",   flags);

        if (velField == null)
        {
            Debug.LogWarning("[MistDebug] Could not find _velocityA field via reflection. " +
                             "Field names may have changed.");
            return;
        }

        var velRT  = velField.GetValue(_fluid)  as RenderTexture;
        var densRT = densField.GetValue(_fluid) as RenderTexture;
        var simMat = simField.GetValue(_fluid)  as Material;
        var dispMat= dispField.GetValue(_fluid) as Material;

        _rtCreated = velRT != null && velRT.IsCreated();

        if (!_rtCreated)
        {
            Debug.LogWarning("[MistDebug] Velocity RenderTexture is null or not created. " +
                             "Initialise() may not have been called.");
            return;
        }

        if (simMat == null)
        {
            Debug.LogError("[MistDebug] _simMat is null — shader was not found or material creation failed.");
            return;
        }

        if (dispMat == null)
        {
            Debug.LogError("[MistDebug] _dispMat is null — display shader not found.");
            return;
        }

        // Read velocity texture
        if (logVelocity && velRT != null)
        {
            ReadRTStats(velRT, "Velocity", ref _velMax, ref _velAvg);
        }

        // Read density texture
        if (logDensity && densRT != null)
        {
            ReadRTStats(densRT, "Density", ref _densMax, ref _densAvg);
        }

        // Check if display material has the density texture set
        var densTexOnDisp = dispMat.GetTexture("_DensityTex");
        if (densTexOnDisp == null)
            Debug.LogWarning("[MistDebug] _DensityTex on display material is NULL — " +
                             "the density texture is not being passed to the display shader.");
        else
            Debug.Log($"[MistDebug] Display material has _DensityTex assigned: {densTexOnDisp.name}");

        // Warn if values look wrong
        if (_velMax < 0.0001f)
            Debug.LogWarning("[MistDebug] Velocity max is near zero — " +
                             "force injection (PassAddForce) may not be running. " +
                             "Check that PassAddForce=8 index matches the shader Pass order.");

        if (_densMax < 0.0001f)
            Debug.LogWarning("[MistDebug] Density max is near zero — " +
                             "density injection (PassInjectDensity) may not be running. " +
                             "Check that PassInjectDensity=10 index matches shader Pass order.");

        if (_velMax > 0.001f && _densMax < 0.0001f)
            Debug.LogWarning("[MistDebug] Velocity is non-zero but density is zero — " +
                             "density injection pass is the problem.");

        if (_velMax < 0.0001f && _densMax > 0.001f)
            Debug.LogWarning("[MistDebug] Density is non-zero but velocity is zero — " +
                             "force injection pass is the problem, density will not move.");
    }

    void ReadRTStats(RenderTexture rt, string name, ref float maxVal, ref float avgVal)
    {
        // Resize readback texture to match RT resolution (sample a small tile for speed)
        int sampleSize = Mathf.Min(rt.width, 32);
        if (_readbackTex.width != sampleSize)
            _readbackTex.Reinitialize(sampleSize, sampleSize);

        // Blit RT to a temporary small RT then read pixels
        RenderTexture tmp = RenderTexture.GetTemporary(sampleSize, sampleSize, 0, RenderTextureFormat.ARGBFloat);
        Graphics.Blit(rt, tmp);

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = tmp;
        _readbackTex.ReadPixels(new Rect(0, 0, sampleSize, sampleSize), 0, 0);
        _readbackTex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(tmp);

        Color[] pixels = _readbackTex.GetPixels();
        float max = 0f, sum = 0f;
        foreach (var p in pixels)
        {
            float mag = Mathf.Sqrt(p.r * p.r + p.g * p.g); // XY for velocity, R for density
            if (mag > max) max = mag;
            sum += mag;
        }

        maxVal = max;
        avgVal = sum / pixels.Length;

        Debug.Log($"[MistDebug] {name}: max={max:F6}  avg={avgVal:F6}  " +
                  $"(sampled {sampleSize}x{sampleSize} from {rt.width}x{rt.height})");
    }

    void OnGUI()
    {
        if (!showOverlay) return;

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize  = 12,
            alignment = TextAnchor.UpperLeft
        };
        style.normal.textColor = Color.white;

        string status = _shadersFound ? "<color=green>FOUND</color>" : "<color=red>MISSING</color>";
        string rtStatus = _rtCreated  ? "<color=green>CREATED</color>" : "<color=red>NOT CREATED</color>";
        string velStatus  = _velMax  > 0.0001f ? $"<color=green>{_velMax:F5}</color>"  : $"<color=red>{_velMax:F5}</color>";
        string densStatus = _densMax > 0.0001f ? $"<color=green>{_densMax:F5}</color>" : $"<color=red>{_densMax:F5}</color>";

        string label =
            $"=== Calamity Mist Debug ===\n" +
            $"Object: {gameObject.name}\n" +
            $"Shaders: {(_shadersFound ? "FOUND" : "MISSING")}\n" +
            $"RenderTextures: {(_rtCreated ? "CREATED" : "NOT CREATED")}\n" +
            $"Velocity Max: {_velMax:F6} {(_velMax > 0.0001f ? "[OK]" : "[ZERO - PROBLEM]")}\n" +
            $"Density Max:  {_densMax:F6} {(_densMax > 0.0001f ? "[OK]" : "[ZERO - PROBLEM]")}\n" +
            $"Settings:\n" +
            $"  emitterStrength={_fluid.emitterStrength}\n" +
            $"  densityStrength={_fluid.densityStrength}\n" +
            $"  dissipDens={_fluid.densityDissipation}\n" +
            $"  dissipVel={_fluid.velocityDissipation}\n" +
            $"  vorticity={_fluid.vorticityStrength}\n" +
            $"  emitterY={_fluid.emitterY}  radius={_fluid.emitterRadius}";

        // Position near bottom-left, offset per instance
        int instanceOffset = transform.GetSiblingIndex() * 220;
        GUI.Box(new Rect(10, 10 + instanceOffset, 340, 240), label);
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;

        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        // Draw quad bounds in cyan
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireMesh(mf.sharedMesh, transform.position, transform.rotation, transform.lossyScale);

        // Draw emitter position in red
        if (_fluid != null)
        {
            var mesh = mf.sharedMesh;
            if (mesh.vertices.Length >= 4)
            {
                Vector3 quadMin = transform.TransformPoint(mesh.vertices[0]);
                Vector3 quadMax = transform.TransformPoint(mesh.vertices[3]);
                float quadH = quadMax.y - quadMin.y;
                float quadW = quadMax.x - quadMin.x;

                Vector3 emitterWorld = new Vector3(
                    quadMin.x + quadW * 0.5f,
                    quadMin.y + quadH * _fluid.emitterY,
                    0f
                );

                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(emitterWorld, _fluid.rockWidth * _fluid.emitterRadius);
                Gizmos.DrawLine(emitterWorld + Vector3.left  * _fluid.rockWidth,
                                emitterWorld + Vector3.right * _fluid.rockWidth);
            }
        }
    }

    void OnDestroy()
    {
        if (_readbackTex != null) Destroy(_readbackTex);
    }
}