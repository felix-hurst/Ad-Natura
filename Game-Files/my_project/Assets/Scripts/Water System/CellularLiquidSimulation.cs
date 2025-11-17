using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Cellular automata-based liquid simulation like Noita
/// Water flows through a grid and reacts to solid obstacles
/// </summary>
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
    
    [Header("Visual Settings")]
    [SerializeField] private Color waterColor = new Color(0.2f, 0.5f, 1f, 0.7f);
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
    
    // Grid data
    private float[,] water;           // Current water amounts
    private float[,] newWater;        // Next frame water amounts
    private bool[,] solid;            // Solid cells (walls, objects)
    private bool[,] settled;          // Is water settled (optimization)
    
    // Rendering
    private Texture2D waterTexture;
    private SpriteRenderer waterRenderer;
    private GameObject waterVisualObject;
    
    // Performance tracking
    private HashSet<Vector2Int> activeCells = new HashSet<Vector2Int>();
    private Queue<Vector2Int> cellsToCheck = new Queue<Vector2Int>();
    private float solidUpdateTimer = 0f;
    
    // Displacement tracking
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
        
        // Mark solid cells based on colliders in scene
        UpdateSolidCells();
        
        Debug.Log($"Liquid simulation initialized: {gridWidth}x{gridHeight} cells ({gridWidth * gridHeight} total)");
    }
    
    void InitializeRendering()
    {
        // Create texture for water visualization
        waterTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBA32, false);
        waterTexture.filterMode = FilterMode.Point; // Pixel-perfect - NO BLUR
        waterTexture.wrapMode = TextureWrapMode.Clamp;
        
        // Create sprite from texture
        Sprite waterSprite = Sprite.Create(
            waterTexture,
            new Rect(0, 0, gridWidth, gridHeight),
            new Vector2(0f, 0f), // Pivot at bottom-left
            pixelsPerUnit
        );
        
        // Create visual object
        waterVisualObject = new GameObject("WaterVisualization");
        waterVisualObject.transform.SetParent(transform);
        waterVisualObject.transform.position = new Vector3(gridOrigin.x, gridOrigin.y, 0);
        
        waterRenderer = waterVisualObject.AddComponent<SpriteRenderer>();
        waterRenderer.sprite = waterSprite;
        waterRenderer.sortingOrder = 5;
        
        // CRITICAL: Ensure sprite texture also uses Point filtering
        if (waterRenderer.sprite != null && waterRenderer.sprite.texture != null)
        {
            waterRenderer.sprite.texture.filterMode = FilterMode.Point;
        }
        
        if (waterMaterial != null)
        {
            waterRenderer.material = waterMaterial;
        }
        
        // Initial render
        UpdateWaterTexture();
    }
    
    void Update()
    {
        if (!enableSimulation) return;
        
        // Periodically update solid cells if dynamic update is enabled
        if (dynamicSolidUpdate)
        {
            solidUpdateTimer += Time.deltaTime;
            if (solidUpdateTimer >= solidUpdateInterval)
            {
                solidUpdateTimer = 0f;
                UpdateSolidCells();
            }
        }
        
        // Update water displacement for moving objects
        if (enableDisplacement)
        {
            displacementUpdateTimer += Time.deltaTime;
            if (displacementUpdateTimer >= displacementUpdateInterval)
            {
                displacementUpdateTimer = 0f;
                UpdateDisplacement();
            }
        }
        
        // Run multiple simulation steps per frame for smoother flow
        for (int i = 0; i < simulationStepsPerFrame; i++)
        {
            SimulationStep();
        }
        
        // Update visuals
        UpdateWaterTexture();
    }
    
    void SimulationStep()
    {
        // Copy current water to newWater
        System.Array.Copy(water, newWater, water.Length);
        
        // Process active cells (cells with water or near water)
        HashSet<Vector2Int> nextActiveCells = new HashSet<Vector2Int>();
        
        // If no active cells, scan for any water
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
            
            // Mark this cell and neighbors as active for next frame
            nextActiveCells.Add(cell);
            
            // Water flows down first
            if (y > 0 && !solid[x, y - 1])
            {
                float below = water[x, y - 1];
                if (below < maxWaterPerCell)
                {
                    float flow = Mathf.Min(currentWater * waterFlowSpeed, maxWaterPerCell - below);
                    flow = Mathf.Max(flow, 0f);
                    
                    newWater[x, y] -= flow;
                    newWater[x, y - 1] += flow;
                    currentWater -= flow;
                    
                    nextActiveCells.Add(new Vector2Int(x, y - 1));
                    settled[x, y] = false;
                    settled[x, y - 1] = false;
                }
            }
            
            // If still has water, spread horizontally
            if (currentWater > minWaterTransfer)
            {
                // Check if water can spread left or right
                bool canSpreadLeft = x > 0 && !solid[x - 1, y];
                bool canSpreadRight = x < gridWidth - 1 && !solid[x + 1, y];
                
                if (canSpreadLeft || canSpreadRight)
                {
                    float leftWater = canSpreadLeft ? water[x - 1, y] : maxWaterPerCell;
                    float rightWater = canSpreadRight ? water[x + 1, y] : maxWaterPerCell;
                    
                    // Spread to lower side first
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
        }
        
        // Swap buffers
        float[,] temp = water;
        water = newWater;
        newWater = temp;
        
        activeCells = nextActiveCells;
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
                    // Debug: show solid cells (optional)
                    // pixels[index] = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                    pixels[index] = Color.clear;
                }
                else
                {
                    float amount = water[x, y];
                    if (amount > minWaterTransfer)
                    {
                        // Water color with alpha based on amount
                        float alpha = Mathf.Clamp01(amount / maxWaterPerCell) * waterColor.a;
                        pixels[index] = new Color(waterColor.r, waterColor.g, waterColor.b, alpha);
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
        
        // Ensure filter mode stays Point (no blur) - Unity sometimes resets this
        waterTexture.filterMode = FilterMode.Point;
    }
    
    /// <summary>
    /// Call this if water appears blurry - forces pixel-perfect rendering
    /// </summary>
    [ContextMenu("Fix Blurry Water")]
    public void FixBlurryWater()
    {
        if (waterTexture != null)
        {
            waterTexture.filterMode = FilterMode.Point;
            waterTexture.anisoLevel = 0;
            Debug.Log("✅ Fixed water texture filtering - set to Point (pixel-perfect)");
        }
        
        if (waterRenderer != null && waterRenderer.sprite != null && waterRenderer.sprite.texture != null)
        {
            waterRenderer.sprite.texture.filterMode = FilterMode.Point;
            waterRenderer.sprite.texture.anisoLevel = 0;
            Debug.Log("✅ Fixed sprite texture filtering - set to Point (pixel-perfect)");
        }
        
        Debug.Log("Water should now be crisp and pixel-perfect!");
    }
    
    /// <summary>
    /// Updates which cells are solid based on physics colliders
    /// </summary>
    public void UpdateSolidCells()
    {
        // Track which cells changed from solid to empty
        bool[,] oldSolid = new bool[gridWidth, gridHeight];
        System.Array.Copy(solid, oldSolid, solid.Length);
        
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector2 worldPos = GridToWorld(x, y);
                Collider2D hit = Physics2D.OverlapCircle(worldPos, physicsCheckRadius, solidLayer);
                solid[x, y] = (hit != null);
                
                // If this cell was solid but is now empty, activate water in it
                if (oldSolid[x, y] && !solid[x, y])
                {
                    // If there's trapped water here, mark it as active so it flows out
                    if (water[x, y] > 0f)
                    {
                        activeCells.Add(new Vector2Int(x, y));
                        settled[x, y] = false;
                    }
                }
            }
        }
        
        Debug.Log("Updated solid cells from physics colliders");
    }
    
    /// <summary>
    /// Removes all water cells with very little water (cleanup stuck/dark water)
    /// </summary>
    [ContextMenu("Clean Up Stuck Water")]
    public void CleanUpStuckWater()
    {
        int cleanedCells = 0;
        
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                // Remove water below minimum threshold
                if (water[x, y] > 0f && water[x, y] < minWaterTransfer * 2f)
                {
                    water[x, y] = 0f;
                    cleanedCells++;
                }
            }
        }
        
        Debug.Log($"Cleaned up {cleanedCells} stuck water cells");
    }
    
    /// <summary>
    /// Spawns water at a world position
    /// </summary>
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
    
    /// <summary>
    /// Spawns water in a region (for cuts)
    /// </summary>
    public void SpawnWaterInRegion(List<Vector2> worldVertices, float totalAmount)
    {
        if (worldVertices == null || worldVertices.Count < 3) return;
        
        // Get bounding box
        Vector2 min = worldVertices[0];
        Vector2 max = worldVertices[0];
        
        foreach (Vector2 v in worldVertices)
        {
            min.x = Mathf.Min(min.x, v.x);
            min.y = Mathf.Min(min.y, v.y);
            max.x = Mathf.Max(max.x, v.x);
            max.y = Mathf.Max(max.y, v.y);
        }
        
        // Convert to grid coordinates
        Vector2Int gridMin = WorldToGrid(min);
        Vector2Int gridMax = WorldToGrid(max);
        
        // Count valid cells in region
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
        
        // Distribute water across cells
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
        
        Debug.Log($"Spawned water in region: {validCells} cells, {waterPerCell:F3} per cell");
    }
    
    /// <summary>
    /// Removes water at a world position (for displacement)
    /// </summary>
    public void RemoveWater(Vector2 worldPosition, float amount)
    {
        Vector2Int gridPos = WorldToGrid(worldPosition);
        if (IsValidCell(gridPos.x, gridPos.y))
        {
            water[gridPos.x, gridPos.y] = Mathf.Max(0f, water[gridPos.x, gridPos.y] - amount);
            activeCells.Add(gridPos);
        }
    }
    
    /// <summary>
    /// Removes all water
    /// </summary>
    public void ClearAllWater()
    {
        System.Array.Clear(water, 0, water.Length);
        System.Array.Clear(newWater, 0, newWater.Length);
        System.Array.Clear(settled, 0, settled.Length);
        activeCells.Clear();
        UpdateWaterTexture();
        Debug.Log("All water cleared");
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
    
    // Public getters
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
    
    /// <summary>
    /// Gets the amount of water in a specific cell
    /// </summary>
    public float GetWater(int x, int y)
    {
        if (IsValidCell(x, y))
        {
            return water[x, y];
        }
        return 0f;
    }
    
    /// <summary>
    /// Sets the amount of water in a specific cell (careful - use SpawnWater/RemoveWater instead usually)
    /// </summary>
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
    
    /// <summary>
    /// Converts world position to grid coordinates (public for external use)
    /// </summary>
    public Vector2Int WorldToGrid(Vector2 worldPos)
    {
        Vector2 localPos = worldPos - gridOrigin;
        int x = Mathf.FloorToInt(localPos.x / cellSize);
        int y = Mathf.FloorToInt(localPos.y / cellSize);
        return new Vector2Int(x, y);
    }
    
    /// <summary>
    /// Converts grid coordinates to world position (public for external use)
    /// </summary>
    public Vector2 GridToWorld(int x, int y)
    {
        return gridOrigin + new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
    }
    
    /// <summary>
    /// Checks if grid coordinates are valid (public for external use)
    /// </summary>
    public bool IsValidCell(int x, int y)
    {
        return x >= 0 && x < gridWidth && y >= 0 && y < gridHeight;
    }
    
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        
        // Draw grid bounds
        Gizmos.color = Color.cyan;
        Vector2 gridSize = new Vector2(gridWidth * cellSize, gridHeight * cellSize);
        Gizmos.DrawWireCube(gridOrigin + gridSize * 0.5f, gridSize);
    }
    
    // ==================== WATER DISPLACEMENT SYSTEM ====================
    
    /// <summary>
    /// Updates water displacement for all moving rigidbodies
    /// </summary>
void UpdateDisplacement()
{
    // Find all rigidbodies in the scene
    Rigidbody2D[] allRigidbodies = FindObjectsOfType<Rigidbody2D>();
    
    int rigidbodiesToCheck = 0;
    int rigidbodiesTooSlow = 0;
    int rigidbodiesTooDisplaced = 0;
    
    // Update tracked rigidbodies
    Dictionary<Rigidbody2D, Vector2> currentRigidbodies = new Dictionary<Rigidbody2D, Vector2>();
    
    foreach (Rigidbody2D rb in allRigidbodies)
    {
        rigidbodiesToCheck++;
        
        // Skip if not on displacement layer
        int layerMask = 1 << rb.gameObject.layer;
        if ((layerMask & displacementLayers) == 0)
        {
            continue;
        }
        
        // Skip static rigidbodies
        if (rb.bodyType == RigidbodyType2D.Static)
        {
            continue;
        }
        
        Vector2 currentPos = rb.position;
        Vector2 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;
        
        // Check if moving fast enough
        if (speed < minDisplacementVelocity)
        {
            rigidbodiesTooSlow++;
            continue;
        }
        
        // Get previous position
        Vector2 previousPos = currentPos;
        if (trackedRigidbodies.ContainsKey(rb))
        {
            previousPos = trackedRigidbodies[rb];
        }
        
        // Displace water if object is in water
        bool displaced = DisplaceWaterForRigidbody(rb, currentPos, previousPos, velocity, speed);
        
        if (displaced)
        {
            rigidbodiesTooDisplaced++;
        }
        
        // Track this rigidbody
        currentRigidbodies[rb] = currentPos;
    }
    
    // Debug info (only log if something happened)
    if (rigidbodiesTooDisplaced > 0)
    {
        Debug.Log($"[Displacement] Checked {rigidbodiesToCheck} RBs, {rigidbodiesTooDisplaced} displaced water, {rigidbodiesTooSlow} too slow");
    }
    
    // Update tracking dictionary
    trackedRigidbodies = currentRigidbodies;
}

    
bool DisplaceWaterForRigidbody(Rigidbody2D rb, Vector2 currentPos, Vector2 previousPos, Vector2 velocity, float speed)
{
    Collider2D col = rb.GetComponent<Collider2D>();
    if (col == null) return false;
    
    Bounds bounds = col.bounds;
    
    // Calculate displacement amount based on object size
    float objectArea = bounds.size.x * bounds.size.y;
    float baseDisplacement = objectArea * displacementStrength;
    
    // Velocity multiplier (faster = more splash)
    float velocityMultiplier = 1f + (speed / 10f) * pushForce;
    velocityMultiplier = Mathf.Clamp(velocityMultiplier, 1f, 5f);
    
    float totalDisplacement = baseDisplacement * velocityMultiplier;
    
    // Find all water cells that overlap with this object
    List<Vector2Int> overlappingCells = new List<Vector2Int>();
    float totalWaterInCells = 0f;
    
    Vector2 min = bounds.min;
    Vector2 max = bounds.max;
    
    // Sample points within the object's bounds
    // INCREASED sampling for better detection
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
            
            // Check if this point is actually inside the collider
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
    
    if (overlappingCells.Count == 0)
    {
        return false; // No water to displace
    }
    
    Debug.Log($"[Displacement] {rb.name} displacing {overlappingCells.Count} cells, velocity: {velocity.magnitude:F2}, speed mult: {velocityMultiplier:F2}");
    
    // Remove water from overlapping cells (displacement)
    float waterPerCell = totalWaterInCells / overlappingCells.Count;
    float waterToDisplace = 0f;
    
    foreach (Vector2Int cell in overlappingCells)
    {
        // Remove MORE water for better effect (85% instead of 70%)
        float waterToRemove = Mathf.Min(waterPerCell * 0.85f, water[cell.x, cell.y]);
        water[cell.x, cell.y] -= waterToRemove;
        waterToDisplace += waterToRemove;
        
        if (water[cell.x, cell.y] < minWaterTransfer)
        {
            water[cell.x, cell.y] = 0f;
        }
    }
    
    // Push water to surrounding cells (upward and sideways)
    if (waterToDisplace > minWaterTransfer)
    {
        PushWaterAround(overlappingCells, waterToDisplace, velocity);
    }
    
    return true;
}

/// <summary>
/// Pushes displaced water to surrounding cells
/// </summary>
void PushWaterAround(List<Vector2Int> sourceCells, float waterAmount, Vector2 velocity)
{
    // Calculate push direction (upward + velocity direction)
    // INCREASED upward bias for more dramatic splash
    Vector2 pushDirection = Vector2.up * 3f + velocity.normalized;
    pushDirection.Normalize();
    
    // Find cells to push water into
    HashSet<Vector2Int> targetCells = new HashSet<Vector2Int>();
    
    foreach (Vector2Int sourceCell in sourceCells)
    {
        // Add cells above (water splashes up) - INCREASED range
        for (int dy = 1; dy <= 5; dy++) // Push up to 5 cells up (was 3)
        {
            Vector2Int above = new Vector2Int(sourceCell.x, sourceCell.y + dy);
            if (IsValidCell(above.x, above.y) && !solid[above.x, above.y])
            {
                targetCells.Add(above);
            }
        }
        
        // Push sideways based on velocity direction - INCREASED range
        int sidewaysDir = velocity.x > 0.1f ? 1 : (velocity.x < -0.1f ? -1 : 0);
        if (sidewaysDir != 0)
        {
            for (int dx = 0; dx <= 3; dx++) // Was 2
            {
                for (int dy = 0; dy <= 2; dy++) // Was 1
                {
                    Vector2Int side = new Vector2Int(
                        sourceCell.x + sidewaysDir * dx, 
                        sourceCell.y + dy
                    );
                    if (IsValidCell(side.x, side.y) && !solid[side.x, side.y])
                    {
                        targetCells.Add(side);
                    }
                }
            }
        }
        
        // Also push in opposite direction for splash effect
        if (sidewaysDir != 0)
        {
            for (int dx = 0; dx <= 2; dx++)
            {
                Vector2Int opposite = new Vector2Int(
                    sourceCell.x - sidewaysDir * dx,
                    sourceCell.y
                );
                if (IsValidCell(opposite.x, opposite.y) && !solid[opposite.x, opposite.y])
                {
                    targetCells.Add(opposite);
                }
            }
        }
    }
    
    if (targetCells.Count == 0) return;
    
    Debug.Log($"[Displacement] Pushing {waterAmount:F2} water to {targetCells.Count} cells");
    
    // Distribute water to target cells
    float waterPerTarget = waterAmount / targetCells.Count;
    
    foreach (Vector2Int targetCell in targetCells)
    {
        water[targetCell.x, targetCell.y] = Mathf.Min(
            water[targetCell.x, targetCell.y] + waterPerTarget, 
            maxWaterPerCell
        );
        
        // Mark as active for simulation
        activeCells.Add(targetCell);
        settled[targetCell.x, targetCell.y] = false;
    }
}

    
    // ==================== END WATER DISPLACEMENT SYSTEM ====================
}