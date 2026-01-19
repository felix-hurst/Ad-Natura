using UnityEngine;
using System.Collections.Generic;

public class CellularLiquidSimulation : MonoBehaviour
{
    [Header("Grid Settings")]
    [SerializeField] private int gridWidth = 200;
    [SerializeField] private int gridHeight = 150;
    [SerializeField] private float cellSize = 0.1f;
    [SerializeField] private Vector2 gridOrigin = new Vector2(-10f, -7.5f);
    
    [Header("Simulation Settings")]
    [SerializeField] private int simulationStepsPerFrame = 4;
    [SerializeField] private bool enableSimulation = true;
    
    [Header("Water Properties")]
    [SerializeField] private float maxWaterPerCell = 1.0f;
    [SerializeField] private float minWaterTransfer = 0.01f;
    [SerializeField] private float waterFlowSpeed = 0.7f;
    [SerializeField] private float waterSpreadRate = 0.5f;
    
    [Header("Advanced Simulation")]
    [Tooltip("Enable pressure-based water (water pushes up when compressed)")]
    [SerializeField] private bool enablePressure = true;
    [Tooltip("How much extra water a cell can hold under pressure")]
    [SerializeField] private float maxCompression = 0.5f;
    [Tooltip("How strongly pressure pushes water up")]
    [SerializeField] private float pressureStrength = 0.25f;
    [Tooltip("Enable diagonal water flow")]
    [SerializeField] private bool enableDiagonalFlow = true;
    [Tooltip("Speed of diagonal flow (relative to vertical)")]
    [Range(0f, 1f)]
    [SerializeField] private float diagonalFlowRate = 0.3f;
    
    [Header("Visual Settings - Colors")]
    [SerializeField] private Color waterColorDeep = new Color(0.08f, 0.22f, 0.55f, 0.95f);
    [SerializeField] private Color waterColorMid = new Color(0.18f, 0.4f, 0.8f, 0.9f);
    [SerializeField] private Color waterColorShallow = new Color(0.3f, 0.55f, 0.9f, 0.85f);
    [SerializeField] private Color surfaceHighlightColor = new Color(0.6f, 0.8f, 1f, 0.9f);
    
    [Header("Visual Settings - Shading")]
    [SerializeField] private bool enableDepthShading = true;
    [SerializeField] private bool enableSurfaceHighlight = true;
    [Tooltip("How many cells deep before reaching darkest color")]
    [SerializeField] private int maxShadingDepth = 8;
    [SerializeField] private Material waterMaterial;
    [Tooltip("Should match 1/cellSize for pixel-perfect rendering. If cellSize=0.1, this should be 10")]
    [SerializeField] private int pixelsPerUnit = 10;
    
    [Header("Physics Interaction")]
    [SerializeField] private LayerMask solidLayer;
    [SerializeField] private float physicsCheckRadius = 0.05f;
    [Tooltip("Automatically update solid cells when objects move")]
    [SerializeField] private bool dynamicSolidUpdate = true;
    [Tooltip("How often to check for moved objects (seconds). 0 = every frame")]
    [SerializeField] private float solidUpdateInterval = 0.5f;
    
    [Header("Water Displacement")]
    [Tooltip("Enable automatic water displacement for moving rigidbodies")]
    [SerializeField] private bool enableDisplacement = true;
    [Tooltip("Objects on these layers will displace water")]
    [SerializeField] private LayerMask displacementLayers = ~0;
    [Tooltip("How much water to displace per unit of object volume")]
    [Range(0.1f, 5f)]
    [SerializeField] private float displacementStrength = 1.0f;
    [Tooltip("How forcefully to push water (higher = more splash)")]
    [Range(0.1f, 5f)]
    [SerializeField] private float pushForce = 1.5f;
    [Tooltip("Minimum velocity to cause displacement")]
    [SerializeField] private float minDisplacementVelocity = 0.5f;
    [Tooltip("How often to update displacement (seconds)")]
    [SerializeField] private float displacementUpdateInterval = 0.05f;

    private float[,] water;
    private float[,] newWater;
    private bool[,] solid;
    private bool[,] settled;
    private int[,] waterDepth;
    private bool[,] isSurfaceCell;
    private Texture2D waterTexture;
    private SpriteRenderer waterRenderer;
    private GameObject waterVisualObject;
    private HashSet<Vector2Int> activeCells = new HashSet<Vector2Int>();
    private Queue<Vector2Int> cellsToCheck = new Queue<Vector2Int>();
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
        water = new float[gridWidth, gridHeight];
        newWater = new float[gridWidth, gridHeight];
        solid = new bool[gridWidth, gridHeight];
        settled = new bool[gridWidth, gridHeight];
        waterDepth = new int[gridWidth, gridHeight];
        isSurfaceCell = new bool[gridWidth, gridHeight];

        UpdateSolidCells();
        
        Debug.Log($"Liquid simulation initialized: {gridWidth}x{gridHeight} cells ({gridWidth * gridHeight} total)");
    }
    
    void InitializeRendering()
    {
        waterTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBA32, false);
        waterTexture.filterMode = FilterMode.Point; 
        waterTexture.wrapMode = TextureWrapMode.Clamp;

        Sprite waterSprite = Sprite.Create(
            waterTexture,
            new Rect(0, 0, gridWidth, gridHeight),
            new Vector2(0f, 0f), 
            pixelsPerUnit
        );

        waterVisualObject = new GameObject("WaterVisualization");
        waterVisualObject.transform.SetParent(transform);
        waterVisualObject.transform.position = new Vector3(gridOrigin.x, gridOrigin.y, 0);
        
        waterRenderer = waterVisualObject.AddComponent<SpriteRenderer>();
        waterRenderer.sprite = waterSprite;
        waterRenderer.sortingOrder = 5;

        if (waterRenderer.sprite != null && waterRenderer.sprite.texture != null)
        {
            waterRenderer.sprite.texture.filterMode = FilterMode.Point;
        }
        
        if (waterMaterial != null)
        {
            waterRenderer.material = waterMaterial;
        }

        UpdateWaterTexture();
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

        if (enableDepthShading || enableSurfaceHighlight)
        {
            CalculateWaterDepthAndSurface();
        }

        UpdateWaterTexture();
    }
    
    void SimulationStep()
    {
        System.Array.Copy(water, newWater, water.Length);

        HashSet<Vector2Int> nextActiveCells = new HashSet<Vector2Int>();

        if (activeCells.Count == 0)
        {
            FindAllWaterCells();
        }
        
        foreach (Vector2Int cell in activeCells)
        {
            int x = cell.x;
            int y = cell.y;
            
            if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight) continue;
            if (solid[x, y]) continue;
            
            float currentWater = water[x, y];
            if (currentWater < minWaterTransfer) continue;

            nextActiveCells.Add(cell);

            float pressure = 0f;
            if (enablePressure)
            {
                pressure = CalculatePressure(x, y);
            }

            float effectiveMaxWater = maxWaterPerCell + (pressure * maxCompression);

            if (y > 0 && !solid[x, y - 1])
            {
                float below = water[x, y - 1];
                float belowPressure = enablePressure ? CalculatePressure(x, y - 1) : 0f;
                float belowEffectiveMax = maxWaterPerCell + (belowPressure * maxCompression);
                
                if (below < belowEffectiveMax)
                {
                    float flow = Mathf.Min(currentWater * waterFlowSpeed, belowEffectiveMax - below);
                    flow = Mathf.Max(flow, 0f);
                    
                    newWater[x, y] -= flow;
                    newWater[x, y - 1] += flow;
                    currentWater -= flow;
                    
                    nextActiveCells.Add(new Vector2Int(x, y - 1));
                    settled[x, y] = false;
                    settled[x, y - 1] = false;
                }
            }

            if (enableDiagonalFlow && currentWater > minWaterTransfer && y > 0)
            {
                bool blockedBelow = solid[x, y - 1] || water[x, y - 1] >= maxWaterPerCell * 0.95f;
                
                if (blockedBelow)
                {
                    bool canFlowDiagLeft = x > 0 && !solid[x - 1, y - 1] && !solid[x - 1, y];
                    bool canFlowDiagRight = x < gridWidth - 1 && !solid[x + 1, y - 1] && !solid[x + 1, y];
                    
                    float flowAmount = currentWater * diagonalFlowRate;
                    
                    if (canFlowDiagLeft && canFlowDiagRight)
                    {
                        float leftWater = water[x - 1, y - 1];
                        float rightWater = water[x + 1, y - 1];
                        float halfFlow = flowAmount * 0.5f;
                        
                        if (leftWater < maxWaterPerCell)
                        {
                            float flowLeft = Mathf.Min(halfFlow, maxWaterPerCell - leftWater);
                            newWater[x, y] -= flowLeft;
                            newWater[x - 1, y - 1] += flowLeft;
                            currentWater -= flowLeft;
                            nextActiveCells.Add(new Vector2Int(x - 1, y - 1));
                            settled[x - 1, y - 1] = false;
                        }
                        
                        if (rightWater < maxWaterPerCell)
                        {
                            float flowRight = Mathf.Min(halfFlow, maxWaterPerCell - rightWater);
                            newWater[x, y] -= flowRight;
                            newWater[x + 1, y - 1] += flowRight;
                            currentWater -= flowRight;
                            nextActiveCells.Add(new Vector2Int(x + 1, y - 1));
                            settled[x + 1, y - 1] = false;
                        }
                    }
                    else if (canFlowDiagLeft)
                    {
                        float leftWater = water[x - 1, y - 1];
                        if (leftWater < maxWaterPerCell)
                        {
                            float flowLeft = Mathf.Min(flowAmount, maxWaterPerCell - leftWater);
                            newWater[x, y] -= flowLeft;
                            newWater[x - 1, y - 1] += flowLeft;
                            currentWater -= flowLeft;
                            nextActiveCells.Add(new Vector2Int(x - 1, y - 1));
                            settled[x - 1, y - 1] = false;
                        }
                    }
                    else if (canFlowDiagRight)
                    {
                        float rightWater = water[x + 1, y - 1];
                        if (rightWater < maxWaterPerCell)
                        {
                            float flowRight = Mathf.Min(flowAmount, maxWaterPerCell - rightWater);
                            newWater[x, y] -= flowRight;
                            newWater[x + 1, y - 1] += flowRight;
                            currentWater -= flowRight;
                            nextActiveCells.Add(new Vector2Int(x + 1, y - 1));
                            settled[x + 1, y - 1] = false;
                        }
                    }
                }
            }

            if (currentWater > minWaterTransfer)
            {
                bool canSpreadLeft = x > 0 && !solid[x - 1, y];
                bool canSpreadRight = x < gridWidth - 1 && !solid[x + 1, y];
                
                if (canSpreadLeft || canSpreadRight)
                {
                    float leftWater = canSpreadLeft ? water[x - 1, y] : maxWaterPerCell;
                    float rightWater = canSpreadRight ? water[x + 1, y] : maxWaterPerCell;

                    if (canSpreadLeft && leftWater < currentWater)
                    {
                        float flow = (currentWater - leftWater) * waterSpreadRate * 0.5f;
                        flow = Mathf.Min(flow, maxWaterPerCell - leftWater);
                        flow = Mathf.Max(flow, 0f);
                        
                        if (flow > minWaterTransfer)
                        {
                            newWater[x, y] -= flow;
                            newWater[x - 1, y] += flow;
                            nextActiveCells.Add(new Vector2Int(x - 1, y));
                            settled[x, y] = false;
                            settled[x - 1, y] = false;
                        }
                    }
                    
                    if (canSpreadRight && rightWater < currentWater)
                    {
                        float flow = (currentWater - rightWater) * waterSpreadRate * 0.5f;
                        flow = Mathf.Min(flow, maxWaterPerCell - rightWater);
                        flow = Mathf.Max(flow, 0f);
                        
                        if (flow > minWaterTransfer)
                        {
                            newWater[x, y] -= flow;
                            newWater[x + 1, y] += flow;
                            nextActiveCells.Add(new Vector2Int(x + 1, y));
                            settled[x, y] = false;
                            settled[x + 1, y] = false;
                        }
                    }
                }
            }

            if (enablePressure && pressure > 0.2f && currentWater > minWaterTransfer)
            {
                if (y < gridHeight - 1 && !solid[x, y + 1])
                {
                    float above = water[x, y + 1];
                    float abovePressure = CalculatePressure(x, y + 1);

                    if (pressure > abovePressure + 0.3f && above < maxWaterPerCell)
                    {
                        float pushUp = (pressure - abovePressure) * pressureStrength * currentWater;
                        pushUp = Mathf.Min(pushUp, maxWaterPerCell - above, currentWater * 0.25f);
                        
                        if (pushUp > minWaterTransfer)
                        {
                            newWater[x, y] -= pushUp;
                            newWater[x, y + 1] += pushUp;
                            nextActiveCells.Add(new Vector2Int(x, y + 1));
                            settled[x, y] = false;
                            settled[x, y + 1] = false;
                        }
                    }
                }
            }
        }

        float[,] temp = water;
        water = newWater;
        newWater = temp;
        
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
            
            float waterAbove = water[x, checkY];
            if (waterAbove < minWaterTransfer) break;
            
            pressure += waterAbove;
            checkY++;
        }
        
        return pressure;
    }

    void CalculateWaterDepthAndSurface()
    {
        System.Array.Clear(waterDepth, 0, waterDepth.Length);
        System.Array.Clear(isSurfaceCell, 0, isSurfaceCell.Length);

        for (int x = 0; x < gridWidth; x++)
        {
            int currentDepth = 0;
            bool inWater = false;

            for (int y = gridHeight - 1; y >= 0; y--)
            {
                if (solid[x, y])
                {
                    currentDepth = 0;
                    inWater = false;
                    continue;
                }
                
                float waterAmount = water[x, y];
                bool hasWater = waterAmount > minWaterTransfer;
                
                if (hasWater)
                {
                    if (!inWater)
                    {
                        isSurfaceCell[x, y] = true;
                        currentDepth = 0;
                    }
                    else
                    {
                        currentDepth++;
                    }
                    
                    waterDepth[x, y] = currentDepth;
                    inWater = true;
                }
                else
                {
                    currentDepth = 0;
                    inWater = false;
                }
            }
        }

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (water[x, y] <= minWaterTransfer) continue;

                bool exposedAbove = (y >= gridHeight - 1) || 
                                   (water[x, y + 1] <= minWaterTransfer && !solid[x, y + 1]);
                bool exposedLeft = (x <= 0) || 
                                  (water[x - 1, y] <= minWaterTransfer && !solid[x - 1, y]);
                bool exposedRight = (x >= gridWidth - 1) || 
                                   (water[x + 1, y] <= minWaterTransfer && !solid[x + 1, y]);
                
                if (exposedAbove || exposedLeft || exposedRight)
                {
                    isSurfaceCell[x, y] = true;
                }
            }
        }
    }
    
    void FindAllWaterCells()
    {
        activeCells.Clear();
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (water[x, y] > minWaterTransfer)
                {
                    activeCells.Add(new Vector2Int(x, y));
                }
            }
        }
    }
    
    void UpdateWaterTexture()
    {
        Color[] pixels = new Color[gridWidth * gridHeight];
        
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                int index = y * gridWidth + x;
                
                if (solid[x, y])
                {
                    pixels[index] = Color.clear;
                }
                else
                {
                    float amount = water[x, y];
                    if (amount > minWaterTransfer)
                    {
                        pixels[index] = GetWaterColor(x, y, amount);
                    }
                    else
                    {
                        pixels[index] = Color.clear;
                    }
                }
            }
        }
        
        waterTexture.SetPixels(pixels);
        waterTexture.Apply();

        waterTexture.filterMode = FilterMode.Point;
    }

    Color GetWaterColor(int x, int y, float amount)
    {
        Color baseColor;

        if (enableDepthShading)
        {
            int depth = waterDepth[x, y];
            float depthRatio = Mathf.Clamp01((float)depth / maxShadingDepth);

            if (depthRatio < 0.4f)
            {
                float t = depthRatio / 0.4f;
                baseColor = Color.Lerp(waterColorShallow, waterColorMid, t);
            }
            else
            {
                float t = (depthRatio - 0.4f) / 0.6f;
                baseColor = Color.Lerp(waterColorMid, waterColorDeep, t);
            }
        }
        else
        {
            baseColor = waterColorMid;
        }

        if (enableSurfaceHighlight && isSurfaceCell[x, y])
        {
            bool isTopSurface = (y >= gridHeight - 1) || 
                               (water[x, y + 1] <= minWaterTransfer && !solid[x, y + 1]);
            
            if (isTopSurface)
            {
                baseColor = Color.Lerp(baseColor, surfaceHighlightColor, 0.5f);
            }
            else
            {
                baseColor = Color.Lerp(baseColor, surfaceHighlightColor, 0.2f);
            }
        }
        float alphaRatio = Mathf.Clamp01(amount / maxWaterPerCell);
        float minAlpha = 0.2f;
        float alpha = Mathf.Lerp(minAlpha, 1f, alphaRatio) * baseColor.a;
        
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
                    if (water[x, y] > 0f)
                    {
                        activeCells.Add(new Vector2Int(x, y));
                        settled[x, y] = false;
                    }
                }
            }
        }
    }

    [ContextMenu("Clean Up Stuck Water")]
    public void CleanUpStuckWater()
    {
        int cleanedCells = 0;
        
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (water[x, y] > 0f && water[x, y] < minWaterTransfer * 2f)
                {
                    water[x, y] = 0f;
                    cleanedCells++;
                }
            }
        }
        
        Debug.Log($"Cleaned up {cleanedCells} stuck water cells");
    }

    public void SpawnWater(Vector2 worldPosition, float amount)
    {
        Vector2Int gridPos = WorldToGrid(worldPosition);
        if (IsValidCell(gridPos.x, gridPos.y) && !solid[gridPos.x, gridPos.y])
        {
            water[gridPos.x, gridPos.y] = Mathf.Min(water[gridPos.x, gridPos.y] + amount, maxWaterPerCell);
            activeCells.Add(gridPos);
            settled[gridPos.x, gridPos.y] = false;
        }
    }

    public void SpawnWaterInRegion(List<Vector2> worldVertices, float totalAmount)
    {
        if (worldVertices == null || worldVertices.Count < 3) return;
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
                if (IsValidCell(x, y) && !solid[x, y])
                {
                    Vector2 worldPos = GridToWorld(x, y);
                    if (IsPointInPolygon(worldPos, worldVertices))
                    {
                        validCells++;
                    }
                }
            }
        }
        
        if (validCells == 0) return;
        float waterPerCell = totalAmount / validCells;
        
        for (int x = gridMin.x; x <= gridMax.x; x++)
        {
            for (int y = gridMin.y; y <= gridMax.y; y++)
            {
                if (IsValidCell(x, y) && !solid[x, y])
                {
                    Vector2 worldPos = GridToWorld(x, y);
                    if (IsPointInPolygon(worldPos, worldVertices))
                    {
                        water[x, y] = Mathf.Min(water[x, y] + waterPerCell, maxWaterPerCell);
                        activeCells.Add(new Vector2Int(x, y));
                        settled[x, y] = false;
                    }
                }
            }
        }

    }

    public void RemoveWater(Vector2 worldPosition, float amount)
    {
        Vector2Int gridPos = WorldToGrid(worldPosition);
        if (IsValidCell(gridPos.x, gridPos.y))
        {
            water[gridPos.x, gridPos.y] = Mathf.Max(0f, water[gridPos.x, gridPos.y] - amount);
            activeCells.Add(gridPos);
        }
    }

    public void ClearAllWater()
    {
        System.Array.Clear(water, 0, water.Length);
        System.Array.Clear(newWater, 0, newWater.Length);
        System.Array.Clear(settled, 0, settled.Length);
        activeCells.Clear();
        UpdateWaterTexture();
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
    public int TotalWaterCells
    {
        get
        {
            int count = 0;
            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    if (water[x, y] > minWaterTransfer) count++;
                }
            }
            return count;
        }
    }

    public float GetWater(int x, int y)
    {
        if (IsValidCell(x, y))
        {
            return water[x, y];
        }
        return 0f;
    }

    public void SetWater(int x, int y, float amount)
    {
        if (IsValidCell(x, y) && !solid[x, y])
        {
            water[x, y] = Mathf.Clamp(amount, 0f, maxWaterPerCell);
            if (amount > 0f)
            {
                activeCells.Add(new Vector2Int(x, y));
                settled[x, y] = false;
            }
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
    
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = Color.cyan;
        Vector2 gridSize = new Vector2(gridWidth * cellSize, gridHeight * cellSize);
        Gizmos.DrawWireCube(gridOrigin + gridSize * 0.5f, gridSize);
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
            
            DisplaceWaterForRigidbody(rb, currentPos, previousPos, velocity, speed);
            currentRigidbodies[rb] = currentPos;
        }
        
        trackedRigidbodies = currentRigidbodies;
    }
    
    bool DisplaceWaterForRigidbody(Rigidbody2D rb, Vector2 currentPos, Vector2 previousPos, Vector2 velocity, float speed)
    {
        Collider2D col = rb.GetComponent<Collider2D>();
        if (col == null) return false;
        
        Bounds bounds = col.bounds;
        float objectArea = bounds.size.x * bounds.size.y;
        float baseDisplacement = objectArea * displacementStrength;
        
        float velocityMultiplier = 1f + (speed / 10f) * pushForce;
        velocityMultiplier = Mathf.Clamp(velocityMultiplier, 1f, 5f);
        
        List<Vector2Int> overlappingCells = new List<Vector2Int>();
        float totalWaterInCells = 0f;
        
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
                        float waterHere = water[gridPos.x, gridPos.y];
                        if (waterHere > minWaterTransfer)
                        {
                            if (!overlappingCells.Contains(gridPos))
                            {
                                overlappingCells.Add(gridPos);
                                totalWaterInCells += waterHere;
                            }
                        }
                    }
                }
            }
        }
        
        if (overlappingCells.Count == 0) return false;
        
        float waterPerCell = totalWaterInCells / overlappingCells.Count;
        float waterToDisplace = 0f;
        
        foreach (Vector2Int cell in overlappingCells)
        {
            float waterToRemove = Mathf.Min(waterPerCell * 0.85f, water[cell.x, cell.y]);
            water[cell.x, cell.y] -= waterToRemove;
            waterToDisplace += waterToRemove;
            
            if (water[cell.x, cell.y] < minWaterTransfer)
            {
                water[cell.x, cell.y] = 0f;
            }
        }
        
        if (waterToDisplace > minWaterTransfer)
        {
            PushWaterAround(overlappingCells, waterToDisplace, velocity);
        }
        
        return true;
    }
    
    void PushWaterAround(List<Vector2Int> sourceCells, float waterAmount, Vector2 velocity)
    {
        HashSet<Vector2Int> targetCells = new HashSet<Vector2Int>();
        
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
        
        float waterPerTarget = waterAmount / targetCells.Count;
        
        foreach (Vector2Int targetCell in targetCells)
        {
            water[targetCell.x, targetCell.y] = Mathf.Min(
                water[targetCell.x, targetCell.y] + waterPerTarget, 
                maxWaterPerCell
            );
            
            activeCells.Add(targetCell);
            settled[targetCell.x, targetCell.y] = false;
        }
    }
}