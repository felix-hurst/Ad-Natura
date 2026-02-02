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
    [Tooltip("How much agents follow existing trails vs water. Lower = more water-responsive")]
    [SerializeField, Range(0f, 1f)] private float trailWeight = 0.35f;

    [Header("Movement")]
    [Tooltip("Speed when no water nearby. Keep low for player control")]
    [SerializeField, Range(0f, 2f)] private float idleSpeed = 0.3f;
    [Tooltip("Speed multiplier when attracted to water")]
    [SerializeField, Range(1f, 5f)] private float attractedSpeedMultiplier = 2.5f;

    [Header("Sensors")]
    [Tooltip("How far ahead agents look for trails/water")]
    [SerializeField, Range(2f, 15f)] private float sensorLength = 5f;
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

    [Header("Visual")]
    [Tooltip("Slime color. Golden yellow is classic Physarum")]
    [SerializeField] private Color slimeColor = new Color(1f, 0.9f, 0.2f, 1f);
    [SerializeField] private int sortingOrder = 5;

    private RenderTexture trailMapA;
    private RenderTexture trailMapB;
    private bool useBufferA = true;

    private RenderTexture defaultAttractionMap;
    private ComputeBuffer agentsBuffer;
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

    void Start()
    {
        if (shader == null)
        {
            Debug.LogError("Slime: Compute shader not assigned!");
            enabled = false;
            return;
        }

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

        defaultAttractionMap = new RenderTexture(width, height, 0);
        defaultAttractionMap.enableRandomWrite = true;
        defaultAttractionMap.Create();
        ClearTexture(defaultAttractionMap);

        SetupDisplay();
    }

    RenderTexture CreateTrailTexture()
    {
        var rt = new RenderTexture(width, height, 0);
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

        slimeQuad = new GameObject("SlimeDisplay");
        targetRenderer = slimeQuad.AddComponent<SpriteRenderer>();
        targetRenderer.sortingOrder = sortingOrder;

        displaySprite = Sprite.Create(displayTexture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        targetRenderer.sprite = displaySprite;

        float scaleX = worldBounds.width / (width / 100f);
        float scaleY = worldBounds.height / (height / 100f);
        targetRenderer.transform.localScale = new Vector3(scaleX, scaleY, 1f);
        targetRenderer.transform.position = new Vector3(worldBounds.center.x, worldBounds.center.y, 0f);
    }

    void FixedUpdate()
    {
        if (trailMapA == null || agentsBuffer == null) return;

        var readBuffer = useBufferA ? trailMapA : trailMapB;
        var writeBuffer = useBufferA ? trailMapB : trailMapA;

        shader.SetTexture(kernelUpdate, "TrailMap", readBuffer);
        shader.SetInt("width", width);
        shader.SetInt("height", height);
        shader.SetInt("numAgents", numAgents);
        shader.SetFloat("moveSpeed", moveSpeed);
        shader.SetFloat("deltaTime", Time.fixedDeltaTime);
        shader.SetFloat("gameTime", Time.time);
        shader.SetFloat("sensorLength", sensorLength);
        shader.SetFloat("sensorAngleSpacing", sensorAngle * Mathf.Deg2Rad);
        shader.SetFloat("turnSpeed", turnSpeed);
        shader.SetFloat("trailWeight", trailWeight);
        shader.SetFloat("idleSpeed", idleSpeed);
        shader.SetFloat("attractedSpeedMultiplier", attractedSpeedMultiplier);
        shader.SetVector("slimeColor", new Vector4(slimeColor.r, slimeColor.g, slimeColor.b, slimeColor.a));

        var waterMap = hasExternalAttraction && externalWaterMap != null ? externalWaterMap : defaultAttractionMap;
        var waterStrength = hasExternalAttraction ? externalWaterStrength : 0f;
        shader.SetTexture(kernelUpdate, "WaterAttractionMap", waterMap);
        shader.SetFloat("waterAttractionStrength", waterStrength);

        shader.SetBuffer(kernelUpdate, "agents", agentsBuffer);
        shader.Dispatch(kernelUpdate, numAgents / 16, 1, 1);

        shader.SetFloat("evaporateSpeed", evaporateSpeed);
        shader.SetFloat("diffuseSpeed", diffuseSpeed);
        shader.SetTexture(kernelPostprocess, "TrailMap", readBuffer);
        shader.SetTexture(kernelPostprocess, "TrailMapProcessed", writeBuffer);
        shader.Dispatch(kernelPostprocess, width / 8, height / 8, 1);

        useBufferA = !useBufferA;

        RenderTexture.active = writeBuffer;
        displayTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        displayTexture.Apply();
        RenderTexture.active = null;
    }

    void OnDestroy()
    {
        trailMapA?.Release();
        trailMapB?.Release();
        defaultAttractionMap?.Release();
        agentsBuffer?.Release();
        if (displayTexture != null) Destroy(displayTexture);
        if (displaySprite != null) Destroy(displaySprite);
        if (slimeQuad != null) Destroy(slimeQuad);
    }

    public void SetAttractionMaps(RenderTexture waterMap, float waterStrength)
    {
        externalWaterMap = waterMap;
        externalWaterStrength = waterStrength;
        hasExternalAttraction = true;
    }

    public Rect GetWorldBounds() => worldBounds;

    public void SetWorldBounds(Rect bounds)
    {
        worldBounds = bounds;
        // Update display transform if already initialized
        if (targetRenderer != null)
        {
            float scaleX = worldBounds.width / (width / 100f);
            float scaleY = worldBounds.height / (height / 100f);
            targetRenderer.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            targetRenderer.transform.position = new Vector3(worldBounds.center.x, worldBounds.center.y, 0f);
        }
    }

    public Vector2Int GetSimulationResolution() => new Vector2Int(width, height);

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

        // World bounds
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
        Gizmos.DrawWireCube(bounds.center, new Vector3(bounds.width, bounds.height, 0));

        // Spawn area
        Vector2 spawnCenter = new Vector2(bounds.x + bounds.width * spawnX, bounds.y + bounds.height * spawnY);
        float radius = Mathf.Min(bounds.width, bounds.height) * spawnRadius;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Gizmos.DrawWireSphere(spawnCenter, radius);
        Gizmos.DrawSphere(spawnCenter, 0.15f);
    }
}
