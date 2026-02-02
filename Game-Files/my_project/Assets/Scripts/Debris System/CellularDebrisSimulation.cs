using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Cellular automata-based debris simulation (Noita-style)
/// Debris particles fall through a grid and interact with solid obstacles
/// Similar to water simulation but for solid fragments from cuts
/// </summary>
public class CellularDebrisSimulation : MonoBehaviour
{
    [Header("Grid Settings")]
    [SerializeField] private int gridWidth = 200;
    [SerializeField] private int gridHeight = 150;
    [SerializeField] private float cellSize = 0.1f;
    [SerializeField] private Vector2 gridOrigin = new Vector2(-10f, -7.5f);
    
    [Header("Simulation Settings")]
    [SerializeField] private int simulationStepsPerFrame = 2;
    [SerializeField] private bool enableSimulation = true;
    
    [Header("Debris Properties")]
    [SerializeField] private float maxDebrisPerCell = 1.0f;
    [SerializeField] private float minDebrisTransfer = 0.01f;
    [SerializeField] private float gravityMultiplier = 1.0f; 
    [Tooltip("How much debris transfers per step (0-1, where 1 = instant transfer)")]
    [SerializeField] private float transferRate = 1.0f;
    [SerializeField] private float debrisLifetime = 5f;
    
    [Header("Powder/Granular Physics (Sand-like)")]
    [Tooltip("Debris acts like powder/sand - piles up, rolls, can be pushed")]
    [SerializeField] private bool powderPhysics = true;
    [Tooltip("How easily debris slides down slopes (0 = no slide, 1 = very slippery)")]
    [Range(0f, 1f)]
    [SerializeField] private float slideCoefficient = 0.7f;
    [Tooltip("Maximum slope angle before debris slides (degrees)")]
    [Range(0f, 90f)]
    [SerializeField] private float angleOfRepose = 35f;
    [Tooltip("Inertia - debris keeps moving in same direction")]
    [Range(0f, 1f)]
    [SerializeField] private float inertia = 0.3f;
    
    [Header("Advanced Physics (NEW - from Water Simulation)")]
    [Tooltip("Enable pressure-based debris (debris compresses and can push upward when compressed)")]
    [SerializeField] private bool enablePressure = true;
    [Tooltip("How much extra debris a cell can hold under pressure")]
    [SerializeField] private float maxCompression = 0.3f;
    [Tooltip("How strongly pressure pushes debris up")]
    [SerializeField] private float pressureStrength = 0.15f;
    [Tooltip("Enable diagonal debris flow")]
    [SerializeField] private bool enableDiagonalFlow = true;
    [Tooltip("Speed of diagonal flow (relative to vertical)")]
    [Range(0f, 1f)]
    [SerializeField] private float diagonalFlowRate = 0.4f;
    [Tooltip("Horizontal spreading rate for debris")]
    [Range(0f, 1f)]
    [SerializeField] private float debrisSpreadRate = 0.3f;
    
    [Header("Displacement")]
    [Tooltip("Enable automatic debris displacement for moving rigidbodies")]
    [SerializeField] private bool enableDisplacement = true;
    [Tooltip("Objects on these layers will displace debris")]
    [SerializeField] private LayerMask displacementLayers = ~0;
    [Tooltip("How much debris to displace per unit of object volume")]
    [Range(0.1f, 5f)]
    [SerializeField] private float displacementStrength = 2.0f;
    [Tooltip("How forcefully to push debris (higher = more scatter)")]
    [Range(0.1f, 5f)]
    [SerializeField] private float pushForce = 2.0f;
    [Tooltip("Minimum velocity to cause displacement")]
    [SerializeField] private float minDisplacementVelocity = 0.5f;
    [Tooltip("How often to update displacement (seconds)")]
    [SerializeField] private float displacementUpdateInterval = 0.05f;
    
    [Header("Visual Settings")]
    [SerializeField] private Material renderMaterial;
    [SerializeField] private int pixelsPerUnit = 10;
    [Tooltip("Enable depth-based color variation")]
    [SerializeField] private bool enableDepthShading = true;
    [Tooltip("How many cells deep before reaching darkest color")]
    [SerializeField] private int maxShadingDepth = 8;
    [Tooltip("Size multiplier for debris chunks (1 = 1 pixel, 3 = 3x3 pixels)")]
    [Range(1, 5)]
    [SerializeField] private int chunkSize = 3;
    [Tooltip("Add random variation to chunk shapes")]
    [SerializeField] private bool randomizedChunks = true;
    
    [Header("Physics Interaction")]
    [SerializeField] private LayerMask solidLayer;
    [SerializeField] private float physicsCheckRadius = 0.05f;
    [SerializeField] private bool dynamicSolidUpdate = true;
    [SerializeField] private float solidUpdateInterval = 0.5f;

    private Dictionary<string, Color> materialColors = new Dictionary<string, Color>
    {
        { "Stone", new Color(0.5f, 0.5f, 0.5f, 1f) },
        { "Metal", new Color(0.7f, 0.7f, 0.8f, 1f) },
        { "Glass", new Color(0.7f, 0.9f, 1f, 0.8f) },
        { "Wood", new Color(0.6f, 0.4f, 0.2f, 1f) },
        { "Dirt", new Color(0.4f, 0.3f, 0.2f, 1f) },
        { "Granite", new Color(0.6f, 0.5f, 0.5f, 1f) },
        { "Ice", new Color(0.7f, 0.85f, 0.95f, 0.7f) },
        { "Default", new Color(0.6f, 0.6f, 0.6f, 1f) }
    };
    
    private float[,] debris;
    private float[,] newDebris;
    private string[,] debrisMaterial;
    private float[,] debrisAge;
    private bool[,] solid;
    private bool[,] settled;
    private Vector2[,] velocity;
    private Vector2[,] newVelocity;
    private int[,] debrisDepth; 
    private Texture2D debrisTexture;
    private SpriteRenderer debrisRenderer;
    private GameObject debrisVisualObject;

    private HashSet<Vector2Int> activeCells = new HashSet<Vector2Int>();
    private float solidUpdateTimer = 0f;
    private Dictionary<Rigidbody2D, Vector2> trackedRigidbodies = new Dictionary<Rigidbody2D, Vector2>(); 
    private float displacementUpdateTimer = 0f; 
    
    void Awake()
    {
        InitializeGrid();
        InitializeRendering();
    }
    
    void InitializeGrid()
    {
        debris = new float[gridWidth, gridHeight];
        newDebris = new float[gridWidth, gridHeight];
        debrisMaterial = new string[gridWidth, gridHeight];
        debrisAge = new float[gridWidth, gridHeight];
        solid = new bool[gridWidth, gridHeight];
        settled = new bool[gridWidth, gridHeight];
        velocity = new Vector2[gridWidth, gridHeight];
        newVelocity = new Vector2[gridWidth, gridHeight];
        debrisDepth = new int[gridWidth, gridHeight]; 

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                debrisMaterial[x, y] = "Default";
                velocity[x, y] = Vector2.zero;
            }
        }
        UpdateSolidCells();
        
        Debug.Log($"Debris simulation initialized: {gridWidth}x{gridHeight} cells ({gridWidth * gridHeight} total)");
    }
    
    void InitializeRendering()
    {
        debrisTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBA32, false);
        debrisTexture.filterMode = FilterMode.Point;
        debrisTexture.wrapMode = TextureWrapMode.Clamp;
        
        Sprite debrisSprite = Sprite.Create(
            debrisTexture,
            new Rect(0, 0, gridWidth, gridHeight),
            new Vector2(0f, 0f),
            pixelsPerUnit
        );
        
        debrisVisualObject = new GameObject("DebrisVisualization");
        debrisVisualObject.transform.SetParent(transform);
        debrisVisualObject.transform.position = new Vector3(gridOrigin.x, gridOrigin.y, 0);
        
        debrisRenderer = debrisVisualObject.AddComponent<SpriteRenderer>();
        debrisRenderer.sprite = debrisSprite;
        debrisRenderer.sortingOrder = 6;
        
        if (debrisRenderer.sprite != null && debrisRenderer.sprite.texture != null)
        {
            debrisRenderer.sprite.texture.filterMode = FilterMode.Point;
        }
        
        if (renderMaterial != null)
        {
            debrisRenderer.material = renderMaterial;
        }
        
        UpdateDebrisTexture();
    }
    
    void Update()
    {
        if (!enableSimulation) return;
        
        if (dynamicSolidUpdate)
        {
            solidUpdateTimer += Time.deltaTime;
            if (solidUpdateTimer >= solidUpdateInterval)
            {
                solidUpdateTimer = 0f;
                UpdateSolidCells();
            }
        }

        if (enableDisplacement)
        {
            displacementUpdateTimer += Time.deltaTime;
            if (displacementUpdateTimer >= displacementUpdateInterval)
            {
                displacementUpdateTimer = 0f;
                UpdateDisplacement();
            }
        }
        
        for (int i = 0; i < simulationStepsPerFrame; i++)
        {
            SimulationStep();
        }
        
        UpdateDebrisAge();

        if (enableDepthShading)
        {
            CalculateDebrisDepth();
        }
        
        UpdateDebrisTexture();
    }
    
void SimulationStep()
{
    System.Array.Copy(debris, newDebris, debris.Length);
    System.Array.Copy(velocity, newVelocity, velocity.Length);
    
    HashSet<Vector2Int> nextActiveCells = new HashSet<Vector2Int>();
    
    if (activeCells.Count == 0)
    {
        FindAllDebrisCells();
    }

    foreach (Vector2Int cell in activeCells)
    {
        int x = cell.x;
        int y = cell.y;
        
        if (!IsValidCell(x, y) || solid[x, y]) continue;
        
        float currentDebris = debris[x, y];
        if (currentDebris < minDebrisTransfer) continue;

        nextActiveCells.Add(cell);

        Vector2 vel = velocity[x, y];

        vel.y -= 9.81f * gravityMultiplier * Time.deltaTime;

        vel *= (1f - (1f - inertia) * Time.deltaTime * 3f);

        float velocityBasedFall = Mathf.Abs(vel.y) * Time.deltaTime / cellSize;

        velocityBasedFall = Mathf.Clamp(velocityBasedFall, 0f, 5f);

        float pressure = 0f;
        if (enablePressure)
        {
            pressure = CalculatePressure(x, y);
        }
        
        float effectiveMaxDebris = maxDebrisPerCell + (pressure * maxCompression);

        if (y > 0 && !solid[x, y - 1])
        {
            float below = debris[x, y - 1];
            float belowPressure = enablePressure ? CalculatePressure(x, y - 1) : 0f;
            float belowEffectiveMax = maxDebrisPerCell + (belowPressure * maxCompression);
            
            if (below < belowEffectiveMax)
            {
                float velocityFactor = Mathf.Clamp01(velocityBasedFall);
                float baseTransfer = currentDebris * transferRate;
                float moveAmount = baseTransfer * (0.3f + velocityFactor * 0.7f);
                
                moveAmount = Mathf.Min(moveAmount, belowEffectiveMax - below);
                moveAmount = Mathf.Max(moveAmount, 0f);
                
                newDebris[x, y] -= moveAmount;
                newDebris[x, y - 1] += moveAmount;
                currentDebris -= moveAmount;
                
                debrisMaterial[x, y - 1] = debrisMaterial[x, y];
                newVelocity[x, y - 1] = new Vector2(vel.x, vel.y);
                
                nextActiveCells.Add(new Vector2Int(x, y - 1));
                settled[x, y - 1] = false;
                settled[x, y] = false;
            }
            else
            {
                vel.y = Mathf.Max(vel.y, 0f);
            }
        }
        else if (y == 0 || solid[x, y - 1])
        {
            vel.y = Mathf.Max(vel.y, 0f);
        }

            if (enableDiagonalFlow && currentDebris > minDebrisTransfer && y > 0)
            {
                bool blockedBelow = solid[x, y - 1] || debris[x, y - 1] >= maxDebrisPerCell * 0.95f;
                
                if (blockedBelow)
                {
                    bool canFlowDiagLeft = x > 0 && !solid[x - 1, y - 1] && !solid[x - 1, y];
                    bool canFlowDiagRight = x < gridWidth - 1 && !solid[x + 1, y - 1] && !solid[x + 1, y];
                    
                    float flowAmount = currentDebris * diagonalFlowRate;
                    
                    if (canFlowDiagLeft && canFlowDiagRight)
                    {
                        float leftDebris = debris[x - 1, y - 1];
                        float rightDebris = debris[x + 1, y - 1];
                        float halfFlow = flowAmount * 0.5f;
                        
                        if (leftDebris < maxDebrisPerCell)
                        {
                            float flowLeft = Mathf.Min(halfFlow, maxDebrisPerCell - leftDebris);
                            newDebris[x, y] -= flowLeft;
                            newDebris[x - 1, y - 1] += flowLeft;
                            currentDebris -= flowLeft;
                            debrisMaterial[x - 1, y - 1] = debrisMaterial[x, y];
                            newVelocity[x - 1, y - 1] = new Vector2(-slideCoefficient * 3f, vel.y);
                            nextActiveCells.Add(new Vector2Int(x - 1, y - 1));
                            settled[x - 1, y - 1] = false;
                        }
                        
                        if (rightDebris < maxDebrisPerCell)
                        {
                            float flowRight = Mathf.Min(halfFlow, maxDebrisPerCell - rightDebris);
                            newDebris[x, y] -= flowRight;
                            newDebris[x + 1, y - 1] += flowRight;
                            currentDebris -= flowRight;
                            debrisMaterial[x + 1, y - 1] = debrisMaterial[x, y];
                            newVelocity[x + 1, y - 1] = new Vector2(slideCoefficient * 3f, vel.y);
                            nextActiveCells.Add(new Vector2Int(x + 1, y - 1));
                            settled[x + 1, y - 1] = false;
                        }
                    }
                    else if (canFlowDiagLeft)
                    {
                        float leftDebris = debris[x - 1, y - 1];
                        if (leftDebris < maxDebrisPerCell)
                        {
                            float flowLeft = Mathf.Min(flowAmount, maxDebrisPerCell - leftDebris);
                            newDebris[x, y] -= flowLeft;
                            newDebris[x - 1, y - 1] += flowLeft;
                            currentDebris -= flowLeft;
                            debrisMaterial[x - 1, y - 1] = debrisMaterial[x, y];
                            newVelocity[x - 1, y - 1] = new Vector2(-slideCoefficient * 3f, vel.y);
                            nextActiveCells.Add(new Vector2Int(x - 1, y - 1));
                            settled[x - 1, y - 1] = false;
                        }
                    }
                    else if (canFlowDiagRight)
                    {
                        float rightDebris = debris[x + 1, y - 1];
                        if (rightDebris < maxDebrisPerCell)
                        {
                            float flowRight = Mathf.Min(flowAmount, maxDebrisPerCell - rightDebris);
                            newDebris[x, y] -= flowRight;
                            newDebris[x + 1, y - 1] += flowRight;
                            currentDebris -= flowRight;
                            debrisMaterial[x + 1, y - 1] = debrisMaterial[x, y];
                            newVelocity[x + 1, y - 1] = new Vector2(slideCoefficient * 3f, vel.y);
                            nextActiveCells.Add(new Vector2Int(x + 1, y - 1));
                            settled[x + 1, y - 1] = false;
                        }
                    }
                }
            }

            if (currentDebris > minDebrisTransfer)
            {
                bool canSpreadLeft = x > 0 && !solid[x - 1, y];
                bool canSpreadRight = x < gridWidth - 1 && !solid[x + 1, y];
                
                if (canSpreadLeft || canSpreadRight)
                {
                    float leftDebris = canSpreadLeft ? debris[x - 1, y] : maxDebrisPerCell;
                    float rightDebris = canSpreadRight ? debris[x + 1, y] : maxDebrisPerCell;

                    if (canSpreadLeft && leftDebris < currentDebris)
                    {
                        float flow = (currentDebris - leftDebris) * debrisSpreadRate * 0.5f;
                        flow = Mathf.Min(flow, maxDebrisPerCell - leftDebris);
                        flow = Mathf.Max(flow, 0f);
                        
                        if (flow > minDebrisTransfer)
                        {
                            newDebris[x, y] -= flow;
                            newDebris[x - 1, y] += flow;
                            debrisMaterial[x - 1, y] = debrisMaterial[x, y];
                            newVelocity[x - 1, y] = new Vector2(-1f, 0f);
                            nextActiveCells.Add(new Vector2Int(x - 1, y));
                            settled[x, y] = false;
                            settled[x - 1, y] = false;
                        }
                    }
                    
                    if (canSpreadRight && rightDebris < currentDebris)
                    {
                        float flow = (currentDebris - rightDebris) * debrisSpreadRate * 0.5f;
                        flow = Mathf.Min(flow, maxDebrisPerCell - rightDebris);
                        flow = Mathf.Max(flow, 0f);
                        
                        if (flow > minDebrisTransfer)
                        {
                            newDebris[x, y] -= flow;
                            newDebris[x + 1, y] += flow;
                            debrisMaterial[x + 1, y] = debrisMaterial[x, y];
                            newVelocity[x + 1, y] = new Vector2(1f, 0f);
                            nextActiveCells.Add(new Vector2Int(x + 1, y));
                            settled[x, y] = false;
                            settled[x + 1, y] = false;
                        }
                    }
                }
            }

            if (enablePressure && pressure > 0.2f && currentDebris > minDebrisTransfer)
            {
                if (y < gridHeight - 1 && !solid[x, y + 1])
                {
                    float above = debris[x, y + 1];
                    float abovePressure = CalculatePressure(x, y + 1);

                    if (pressure > abovePressure + 0.3f && above < maxDebrisPerCell)
                    {
                        float pushUp = (pressure - abovePressure) * pressureStrength * currentDebris;
                        pushUp = Mathf.Min(pushUp, maxDebrisPerCell - above, currentDebris * 0.25f);
                        
                        if (pushUp > minDebrisTransfer)
                        {
                            newDebris[x, y] -= pushUp;
                            newDebris[x, y + 1] += pushUp;
                            debrisMaterial[x, y + 1] = debrisMaterial[x, y];
                            newVelocity[x, y + 1] = new Vector2(0f, 2f);
                            nextActiveCells.Add(new Vector2Int(x, y + 1));
                            settled[x, y] = false;
                            settled[x, y + 1] = false;
                        }
                    }
                }
            }
            
            // Update velocity
            newVelocity[x, y] = vel;
        }

        float[,] tempDebris = debris;
        debris = newDebris;
        newDebris = tempDebris;
        
        Vector2[,] tempVel = velocity;
        velocity = newVelocity;
        newVelocity = tempVel;
        
        activeCells = nextActiveCells;
    }

    float CalculatePressure(int x, int y)
    {
        float pressure = 0f;
        int checkY = y + 1;
        int maxCheck = Mathf.Min(y + 15, gridHeight);
        
        while (checkY < maxCheck)
        {
            if (solid[x, checkY]) break;
            
            float debrisAbove = debris[x, checkY];
            if (debrisAbove < minDebrisTransfer) break;
            
            pressure += debrisAbove;
            checkY++;
        }
        
        return pressure;
    }

    void CalculateDebrisDepth()
    {
        System.Array.Clear(debrisDepth, 0, debrisDepth.Length);

        for (int x = 0; x < gridWidth; x++)
        {
            int currentDepth = 0;
            bool inDebris = false;

            for (int y = gridHeight - 1; y >= 0; y--)
            {
                if (solid[x, y])
                {
                    currentDepth = 0;
                    inDebris = false;
                    continue;
                }
                
                float debrisAmount = debris[x, y];
                bool hasDebris = debrisAmount > minDebrisTransfer;
                
                if (hasDebris)
                {
                    if (!inDebris)
                    {
                        currentDepth = 0;
                    }
                    else
                    {
                        currentDepth++;
                    }
                    
                    debrisDepth[x, y] = currentDepth;
                    inDebris = true;
                }
                else
                {
                    currentDepth = 0;
                    inDebris = false;
                }
            }
        }
    }
    
    void UpdateDebrisAge()
    {
        if (debrisLifetime <= 0f) return;
        
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (debris[x, y] > minDebrisTransfer)
                {
                    debrisAge[x, y] += Time.deltaTime;
                    if (debrisAge[x, y] >= debrisLifetime)
                    {
                        debris[x, y] = 0f;
                        debrisAge[x, y] = 0f;
                    }
                }
            }
        }
    }
    
    void FindAllDebrisCells()
    {
        activeCells.Clear();
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (debris[x, y] > minDebrisTransfer)
                {
                    activeCells.Add(new Vector2Int(x, y));
                }
            }
        }
    }
    
    void UpdateDebrisTexture()
    {
        Color[] pixels = new Color[gridWidth * gridHeight];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.clear;
        }
        
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (solid[x, y])
                {
                    continue;
                }
                else
                {
                    float amount = debris[x, y];
                    if (amount > minDebrisTransfer)
                    {
                        Color debrisColor = GetDebrisColor(x, y, amount);

                        int halfChunk = chunkSize / 2;
                        
                        for (int dx = -halfChunk; dx <= halfChunk; dx++)
                        {
                            for (int dy = -halfChunk; dy <= halfChunk; dy++)
                            {
                                int px = x + dx;
                                int py = y + dy;

                                if (px < 0 || px >= gridWidth || py < 0 || py >= gridHeight)
                                    continue;

                                if (solid[px, py])
                                    continue;

                                if (randomizedChunks && chunkSize > 1)
                                {
                                    float random = Mathf.PerlinNoise(x * 0.5f, y * 0.5f);
                                    if ((Mathf.Abs(dx) == halfChunk || Mathf.Abs(dy) == halfChunk) && random < 0.3f)
                                        continue;
                                }
                                
                                int index = py * gridWidth + px;

                                if (pixels[index].a > 0.01f)
                                {
                                    pixels[index] = Color.Lerp(pixels[index], debrisColor, 0.5f);
                                }
                                else
                                {
                                    pixels[index] = debrisColor;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        debrisTexture.SetPixels(pixels);
        debrisTexture.Apply();
        debrisTexture.filterMode = FilterMode.Point;
    }

    Color GetDebrisColor(int x, int y, float amount)
    {
        string material = debrisMaterial[x, y];
        Color baseColor = materialColors.ContainsKey(material) 
            ? materialColors[material] 
            : materialColors["Default"];

        float noiseX = Mathf.PerlinNoise(x * 0.3f, y * 0.3f);
        float noiseY = Mathf.PerlinNoise(x * 0.3f + 100f, y * 0.3f + 100f);
        float variation = (noiseX + noiseY) * 0.5f;

        float brightnessVar = Mathf.Lerp(0.85f, 1.15f, variation);
        baseColor = new Color(
            baseColor.r * brightnessVar,
            baseColor.g * brightnessVar,
            baseColor.b * brightnessVar,
            baseColor.a
        );

        if (enableDepthShading)
        {
            int depth = debrisDepth[x, y];
            float depthRatio = Mathf.Clamp01((float)depth / maxShadingDepth);

            float darkenAmount = depthRatio * 0.4f;
            baseColor = new Color(
                baseColor.r * (1f - darkenAmount),
                baseColor.g * (1f - darkenAmount),
                baseColor.b * (1f - darkenAmount),
                baseColor.a
            );
        }
        float alpha = baseColor.a;
        if (debrisLifetime > 0f && debrisAge[x, y] > debrisLifetime * 0.7f)
        {
            float fadeAmount = (debrisAge[x, y] - debrisLifetime * 0.7f) / (debrisLifetime * 0.3f);
            alpha *= (1f - fadeAmount);
        }
        float alphaRatio = Mathf.Clamp01(amount / maxDebrisPerCell);
        float minAlpha = 0.8f;
        alpha *= Mathf.Lerp(minAlpha, 1f, alphaRatio);
        
        return new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
    }
    
    public void UpdateSolidCells()
    {
        bool[,] oldSolid = new bool[gridWidth, gridHeight];
        System.Array.Copy(solid, oldSolid, solid.Length);
        
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector2 worldPos = GridToWorld(x, y);
                Collider2D hit = Physics2D.OverlapCircle(worldPos, physicsCheckRadius, solidLayer);
                solid[x, y] = (hit != null);
                
                if (oldSolid[x, y] && !solid[x, y])
                {
                    if (debris[x, y] > 0f)
                    {
                        activeCells.Add(new Vector2Int(x, y));
                        settled[x, y] = false;
                    }
                }
            }
        }
    }

    void UpdateDisplacement()
    {
        Rigidbody2D[] allRigidbodies = FindObjectsOfType<Rigidbody2D>();
        
        Dictionary<Rigidbody2D, Vector2> currentRigidbodies = new Dictionary<Rigidbody2D, Vector2>();
        
        foreach (Rigidbody2D rb in allRigidbodies)
        {
            int layerMask = 1 << rb.gameObject.layer;
            if ((layerMask & displacementLayers) == 0) continue;
            if (rb.bodyType == RigidbodyType2D.Static) continue;
            
            Vector2 currentPos = rb.position;
            Vector2 velocity = rb.linearVelocity;
            float speed = velocity.magnitude;
            
            if (speed < minDisplacementVelocity) continue;
            
            Vector2 previousPos = currentPos;
            if (trackedRigidbodies.ContainsKey(rb))
            {
                previousPos = trackedRigidbodies[rb];
            }
            
            DisplaceDebrisForRigidbody(rb, currentPos, previousPos, velocity, speed);
            currentRigidbodies[rb] = currentPos;
        }
        
        trackedRigidbodies = currentRigidbodies;
    }
    
    bool DisplaceDebrisForRigidbody(Rigidbody2D rb, Vector2 currentPos, Vector2 previousPos, Vector2 velocity, float speed)
    {
        Collider2D col = rb.GetComponent<Collider2D>();
        if (col == null) return false;
        
        Bounds bounds = col.bounds;
        float objectArea = bounds.size.x * bounds.size.y;
        float baseDisplacement = objectArea * displacementStrength;
        
        float velocityMultiplier = 1f + (speed / 10f) * pushForce;
        velocityMultiplier = Mathf.Clamp(velocityMultiplier, 1f, 5f);
        
        List<Vector2Int> overlappingCells = new List<Vector2Int>();
        float totalDebrisInCells = 0f;
        
        Vector2 min = bounds.min;
        Vector2 max = bounds.max;
        
        int samplesX = Mathf.Max(3, Mathf.CeilToInt(bounds.size.x / (cellSize * 1.5f)));
        int samplesY = Mathf.Max(3, Mathf.CeilToInt(bounds.size.y / (cellSize * 1.5f)));
        
        for (int ix = 0; ix < samplesX; ix++)
        {
            for (int iy = 0; iy < samplesY; iy++)
            {
                float t_x = ix / (float)(samplesX - 1);
                float t_y = iy / (float)(samplesY - 1);
                
                Vector2 samplePoint = new Vector2(
                    Mathf.Lerp(min.x, max.x, t_x),
                    Mathf.Lerp(min.y, max.y, t_y)
                );
                
                if (col.OverlapPoint(samplePoint))
                {
                    Vector2Int gridPos = WorldToGrid(samplePoint);
                    
                    if (IsValidCell(gridPos.x, gridPos.y) && !solid[gridPos.x, gridPos.y])
                    {
                        float debrisHere = debris[gridPos.x, gridPos.y];
                        if (debrisHere > minDebrisTransfer)
                        {
                            if (!overlappingCells.Contains(gridPos))
                            {
                                overlappingCells.Add(gridPos);
                                totalDebrisInCells += debrisHere;
                            }
                        }
                    }
                }
            }
        }
        
        if (overlappingCells.Count == 0) return false;
        
        float debrisPerCell = totalDebrisInCells / overlappingCells.Count;
        float debrisToDisplace = 0f;
        
        foreach (Vector2Int cell in overlappingCells)
        {
            float debrisToRemove = Mathf.Min(debrisPerCell * 0.85f, debris[cell.x, cell.y]);
            debris[cell.x, cell.y] -= debrisToRemove;
            debrisToDisplace += debrisToRemove;
            
            if (debris[cell.x, cell.y] < minDebrisTransfer)
            {
                debris[cell.x, cell.y] = 0f;
            }
        }
        
        if (debrisToDisplace > minDebrisTransfer)
        {
            PushDebrisAround(overlappingCells, debrisToDisplace, velocity);
        }
        
        return true;
    }
    

void PushDebrisAround(List<Vector2Int> sourceCells, float debrisAmount, Vector2 velocity)
{
    HashSet<Vector2Int> targetCells = new HashSet<Vector2Int>();

    Dictionary<string, float> materialAmounts = new Dictionary<string, float>();
    foreach (Vector2Int sourceCell in sourceCells)
    {
        string mat = debrisMaterial[sourceCell.x, sourceCell.y];
        float amt = debris[sourceCell.x, sourceCell.y];
        
        if (materialAmounts.ContainsKey(mat))
            materialAmounts[mat] += amt;
        else
            materialAmounts[mat] = amt;
    }

    string dominantMaterial = "Default";
    float maxAmount = 0f;
    foreach (var kvp in materialAmounts)
    {
        if (kvp.Value > maxAmount)
        {
            maxAmount = kvp.Value;
            dominantMaterial = kvp.Key;
        }
    }
    
    foreach (Vector2Int sourceCell in sourceCells)
    {
        for (int dy = 1; dy <= 5; dy++)
        {
            Vector2Int above = new Vector2Int(sourceCell.x, sourceCell.y + dy);
            if (IsValidCell(above.x, above.y) && !solid[above.x, above.y])
            {
                targetCells.Add(above);
            }
        }
        
        int sidewaysDir = velocity.x > 0.1f ? 1 : (velocity.x < -0.1f ? -1 : 0);
        if (sidewaysDir != 0)
        {
            for (int dx = 0; dx <= 3; dx++)
            {
                for (int dy = 0; dy <= 2; dy++)
                {
                    Vector2Int side = new Vector2Int(sourceCell.x + sidewaysDir * dx, sourceCell.y + dy);
                    if (IsValidCell(side.x, side.y) && !solid[side.x, side.y])
                    {
                        targetCells.Add(side);
                    }
                }
            }
            
            for (int dx = 0; dx <= 2; dx++)
            {
                Vector2Int opposite = new Vector2Int(sourceCell.x - sidewaysDir * dx, sourceCell.y);
                if (IsValidCell(opposite.x, opposite.y) && !solid[opposite.x, opposite.y])
                {
                    targetCells.Add(opposite);
                }
            }
        }
    }
    
    if (targetCells.Count == 0) return;
    
    float debrisPerTarget = debrisAmount / targetCells.Count;
    
    foreach (Vector2Int targetCell in targetCells)
    {
        debris[targetCell.x, targetCell.y] = Mathf.Min(
            debris[targetCell.x, targetCell.y] + debrisPerTarget, 
            maxDebrisPerCell
        );

        debrisMaterial[targetCell.x, targetCell.y] = dominantMaterial;
        
        activeCells.Add(targetCell);
        settled[targetCell.x, targetCell.y] = false;
    }
}
    public void SpawnDebris(Vector2 worldPosition, float amount, string material = "Default")
    {
        Vector2Int gridPos = WorldToGrid(worldPosition);
        
        if (!IsValidCell(gridPos.x, gridPos.y))
        {
            Debug.LogWarning($"[CellularDebris] Cannot spawn - outside grid bounds");
            return;
        }
        
        if (solid[gridPos.x, gridPos.y])
        {
            Debug.LogWarning($"[CellularDebris] Cannot spawn - cell is solid");
            return;
        }

        debris[gridPos.x, gridPos.y] = Mathf.Min(debris[gridPos.x, gridPos.y] + amount, maxDebrisPerCell);
        debrisMaterial[gridPos.x, gridPos.y] = material;
        debrisAge[gridPos.x, gridPos.y] = 0f;
        velocity[gridPos.x, gridPos.y] = new Vector2(Random.Range(-1f, 1f), -5f);
        
        activeCells.Add(gridPos);
        settled[gridPos.x, gridPos.y] = false;
    }
    
    public void SpawnDebrisInRegion(List<Vector2> worldVertices, float totalAmount, string material = "Default", GameObject sourceObject = null)
    {
        if (worldVertices == null || worldVertices.Count < 3)
        {
            return;
        }

        Vector2 min = worldVertices[0];
        Vector2 max = worldVertices[0];
        
        foreach (Vector2 v in worldVertices)
        {
            min.x = Mathf.Min(min.x, v.x);
            min.y = Mathf.Min(min.y, v.y);
            max.x = Mathf.Max(max.x, v.x);
            max.y = Mathf.Max(max.y, v.y);
        }
        
        Vector2Int gridMin = WorldToGrid(min);
        Vector2Int gridMax = WorldToGrid(max);
        
        int validCells = 0;
        
        for (int x = gridMin.x; x <= gridMax.x; x++)
        {
            for (int y = gridMin.y; y <= gridMax.y; y++)
            {
                if (!IsValidCell(x, y)) continue;

                Vector2 worldPos = GridToWorld(x, y);
                Collider2D hit = Physics2D.OverlapCircle(worldPos, physicsCheckRadius, solidLayer);
                
                bool isSolid = false;
                if (hit != null)
                {
                    if (sourceObject != null && hit.gameObject == sourceObject)
                    {
                        isSolid = false;
                    }
                    else
                    {
                        isSolid = true;
                    }
                }
                
                if (!isSolid && IsPointInPolygon(worldPos, worldVertices))
                {
                    validCells++;
                }
            }
        }
        
        if (validCells == 0) return;
        
        float debrisPerCell = totalAmount / validCells;
        
        for (int x = gridMin.x; x <= gridMax.x; x++)
        {
            for (int y = gridMin.y; y <= gridMax.y; y++)
            {
                if (!IsValidCell(x, y)) continue;

                Vector2 worldPos = GridToWorld(x, y);
                Collider2D hit = Physics2D.OverlapCircle(worldPos, physicsCheckRadius, solidLayer);
                
                bool isSolid = false;
                if (hit != null)
                {
                    if (sourceObject != null && hit.gameObject == sourceObject)
                    {
                        isSolid = false;
                    }
                    else
                    {
                        isSolid = true;
                    }
                }
                
                if (!isSolid && IsPointInPolygon(worldPos, worldVertices))
                {
                    debris[x, y] = Mathf.Min(debris[x, y] + debrisPerCell, maxDebrisPerCell);
                    debrisMaterial[x, y] = material;
                    debrisAge[x, y] = 0f;
                    velocity[x, y] = new Vector2(Random.Range(-2f, 2f), Random.Range(-1f, 1f));
                    
                    activeCells.Add(new Vector2Int(x, y));
                    settled[x, y] = false;
                }
            }
        }
    }
    
    public void ClearAllDebris()
    {
        System.Array.Clear(debris, 0, debris.Length);
        System.Array.Clear(newDebris, 0, newDebris.Length);
        System.Array.Clear(debrisAge, 0, debrisAge.Length);
        System.Array.Clear(settled, 0, settled.Length);
        System.Array.Clear(velocity, 0, velocity.Length);
        System.Array.Clear(newVelocity, 0, newVelocity.Length);
        System.Array.Clear(debrisDepth, 0, debrisDepth.Length);
        
        activeCells.Clear();
        UpdateDebrisTexture();
    }
    
    [ContextMenu("Clean Up Stuck Debris")]
    public void CleanUpStuckDebris()
    {
        int cleanedCells = 0;
        
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (debris[x, y] > 0f && debris[x, y] < minDebrisTransfer * 2f)
                {
                    debris[x, y] = 0f;
                    cleanedCells++;
                }
            }
        }
        
        Debug.Log($"Cleaned up {cleanedCells} stuck debris cells");
    }
    
    bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        int n = polygon.Count;
        
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / 
                (polygon[j].y - polygon[i].y) + polygon[i].x))
            {
                inside = !inside;
            }
        }
        
        return inside;
    }

    public int ActiveCellCount => activeCells.Count;
    public int TotalDebrisCells
    {
        get
        {
            int count = 0;
            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    if (debris[x, y] > minDebrisTransfer) count++;
                }
            }
            return count;
        }
    }
    
    public Vector2Int WorldToGrid(Vector2 worldPos)
    {
        Vector2 localPos = worldPos - gridOrigin;
        int x = Mathf.FloorToInt(localPos.x / cellSize);
        int y = Mathf.FloorToInt(localPos.y / cellSize);
        return new Vector2Int(x, y);
    }
    
    public Vector2 GridToWorld(int x, int y)
    {
        return gridOrigin + new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
    }
    
    public bool IsValidCell(int x, int y)
    {
        return x >= 0 && x < gridWidth && y >= 0 && y < gridHeight;
    }
    
    public void SetMaterialColor(string materialName, Color color)
    {
        materialColors[materialName] = color;
    }
    
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        
        Gizmos.color = Color.yellow;
        Vector2 gridSize = new Vector2(gridWidth * cellSize, gridHeight * cellSize);
        Gizmos.DrawWireCube(gridOrigin + gridSize * 0.5f, gridSize);
    }
}