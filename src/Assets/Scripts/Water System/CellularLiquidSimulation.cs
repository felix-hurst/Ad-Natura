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

    [Header("Water Sounds")]
    [SerializeField] private float flowSoundThreshold = 50f;
    [SerializeField] private float flowSoundMaxCells = 500f;
    [SerializeField] private AudioClip flowingClip;
    private AudioSource flowSource;

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

    [Header("Performance Optimization")]
    [Tooltip("Only update texture regions that changed (massive performance boost)")]
    [SerializeField] private bool useDirtyRectOptimization = true;
    [Tooltip("Expand dirty region by this many cells to avoid artifacts")]
    [SerializeField] private int dirtyRectPadding = 2;
    [Tooltip("Use cached rigidbody tracking for displacement (massive performance boost)")]
    [SerializeField] private bool useCachedDisplacementRigidbodies = true;
    [Tooltip("How often to refresh displacement rigidbody cache (seconds). 0 = only on enable/registration.")]
    [SerializeField] private float displacementCacheRefreshInterval = 2.0f;
    [Tooltip("Batch texture updates - only update every N frames (0 = every frame, 1 = every other frame)")]
    [Range(0, 5)]
    [SerializeField] private int textureUpdateFrameSkip = 0;
    [Tooltip("Pre-allocate pixel arrays to avoid GC allocations")]
    [SerializeField] private bool usePixelArrayPool = true;

    // Double-buffered water grids — newWater receives writes during a step while water is read,
    // preventing cells updated earlier in the same step from influencing cells updated later.
    private float[,] water;
    private float[,] newWater;

    private bool[,] solid;
    private bool[,] settled;

    // Per-cell depth and surface flags, recalculated each frame for visual shading only.
    private int[,] waterDepth;
    private bool[,] isSurfaceCell;

    private Texture2D waterTexture;
    private SpriteRenderer waterRenderer;
    private GameObject waterVisualObject;

    // Active cell set limits simulation work to only cells that actually contain water,
    // avoiding the cost of iterating the entire grid every step.
    private HashSet<Vector2Int> activeCells = new HashSet<Vector2Int>();
    private Queue<Vector2Int> cellsToCheck = new Queue<Vector2Int>();

    private float solidUpdateTimer = 0f;

    // Tracks rigidbody positions from the previous displacement tick so we can compute movement delta.
    private Dictionary<Rigidbody2D, Vector2> trackedRigidbodies = new Dictionary<Rigidbody2D, Vector2>();
    private float displacementUpdateTimer = 0f;

    // Pre-filtered set of rigidbodies eligible for displacement, so we avoid FindObjectsOfType every tick.
    private HashSet<Rigidbody2D> cachedDisplacementRigidbodies = new HashSet<Rigidbody2D>();
    private float displacementCacheRefreshTimer = 0f;

    // Dirty rect tracks the minimal bounding box of changed cells so only that region is re-uploaded to the GPU.
    private int dirtyMinX = int.MaxValue;
    private int dirtyMinY = int.MaxValue;
    private int dirtyMaxX = int.MinValue;
    private int dirtyMaxY = int.MinValue;
    private bool hasVisualChanges = false;

    private int textureUpdateFrameCounter = 0;

    // Pooled pixel buffer avoids a heap allocation every time we upload to the texture.
    private Color[] pixelArrayPool;
    private List<Vector2Int> activeCellsList;

    // Pre-baked color gradient for each depth level so color lookups are a simple array index
    // rather than multiple Lerp calls per pixel per frame.
    private Color[] depthColorCache;
    private const int DEPTH_CACHE_SIZE = 32;

    void Awake()
    {
        InitializeGrid();
        InitializeOptimizations();
        InitializeRendering();

        // Audio source is configured here rather than in the Inspector so the component
        // is guaranteed to exist and be ready before the first Update.
        flowSource = gameObject.AddComponent<AudioSource>();
        flowSource.clip = flowingClip;
        flowSource.loop = true;
        flowSource.playOnAwake = false;
        flowSource.volume = 0f;
        flowSource.Play();

        // Populate the displacement cache immediately so the first Update doesn't pay
        // the cost of a FindObjectsOfType scan.
        if (useCachedDisplacementRigidbodies && enableDisplacement)
        {
            RefreshDisplacementCache();
        }
    }

    void InitializeOptimizations()
    {
        if (usePixelArrayPool)
        {
            pixelArrayPool = new Color[gridWidth * gridHeight];
        }

        // Pre-sized list avoids mid-frame resizing when the active cell count is typical.
        activeCellsList = new List<Vector2Int>(gridWidth * gridHeight / 4);

        // Bake the depth-to-color gradient once at startup. Recalculating per pixel per frame
        // would be wasteful since the color ramp never changes at runtime.
        depthColorCache = new Color[DEPTH_CACHE_SIZE];
        for (int i = 0; i < DEPTH_CACHE_SIZE; i++)
        {
            float depthRatio = Mathf.Clamp01((float)i / maxShadingDepth);

            if (depthRatio < 0.4f)
            {
                float t = depthRatio / 0.4f;
                depthColorCache[i] = Color.Lerp(waterColorShallow, waterColorMid, t);
            }
            else
            {
                float t = (depthRatio - 0.4f) / 0.6f;
                depthColorCache[i] = Color.Lerp(waterColorMid, waterColorDeep, t);
            }
        }
    }

    void InitializeGrid()
    {
        water = new float[gridWidth, gridHeight];
        newWater = new float[gridWidth, gridHeight];
        solid = new bool[gridWidth, gridHeight];
        settled = new bool[gridWidth, gridHeight];
        waterDepth = new int[gridWidth, gridHeight];
        isSurfaceCell = new bool[gridWidth, gridHeight];

        // Build the initial solid map from whatever physics colliders are already in the scene.
        UpdateSolidCells();

        Debug.Log($"Liquid simulation initialized: {gridWidth}x{gridHeight} cells ({gridWidth * gridHeight} total)");
    }

    void InitializeRendering()
    {
        // Point filtering keeps pixel edges sharp; bilinear would blur the blocky water look.
        waterTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBA32, false);
        waterTexture.filterMode = FilterMode.Point;
        waterTexture.wrapMode = TextureWrapMode.Clamp;

        // Pivot at (0,0) so the sprite's bottom-left corner aligns with gridOrigin,
        // making WorldToGrid / GridToWorld math straightforward.
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

        // Sorting order 5 places water above most background elements but below UI or foreground objects.
        waterRenderer.sortingOrder = 5;

        if (waterRenderer.sprite != null && waterRenderer.sprite.texture != null)
        {
            waterRenderer.sprite.texture.filterMode = FilterMode.Point;
        }

        if (waterMaterial != null)
        {
            waterRenderer.material = waterMaterial;
        }

        UpdateWaterTextureOptimized();
    }

    void Update()
    {
        if (!enableSimulation) return;

        // Re-scan physics colliders periodically so walls or platforms that move
        // at runtime are reflected in the solid grid without doing it every frame.
        if (dynamicSolidUpdate)
        {
            solidUpdateTimer += Time.deltaTime;
            if (solidUpdateTimer >= solidUpdateInterval)
            {
                solidUpdateTimer = 0f;
                UpdateSolidCells();
            }
        }

        splashSoundCooldown -= Time.deltaTime;

        if (enableDisplacement)
        {
            displacementUpdateTimer += Time.deltaTime;
            if (displacementUpdateTimer >= displacementUpdateInterval)
            {
                displacementUpdateTimer = 0f;
                UpdateDisplacement();
            }

            // Refresh the rigidbody cache on a slow interval so newly spawned or destroyed
            // objects are picked up without scanning the scene every displacement tick.
            if (useCachedDisplacementRigidbodies && displacementCacheRefreshInterval > 0f)
            {
                displacementCacheRefreshTimer += Time.deltaTime;
                if (displacementCacheRefreshTimer >= displacementCacheRefreshInterval)
                {
                    displacementCacheRefreshTimer = 0f;
                    RefreshDisplacementCache();
                }
            }
        }

        // Clear the dirty rect before simulation so any cells touched this frame
        // are correctly captured for the texture upload below.
        ResetDirtyRect();

        // Running multiple steps per frame lets the simulation converge faster
        // (e.g. water reaches the bottom of a tall column in fewer real-time frames)
        // without increasing the fixed-timestep cost.
        for (int i = 0; i < simulationStepsPerFrame; i++)
        {
            SimulationStep();
        }

        if (enableDepthShading || enableSurfaceHighlight)
        {
            CalculateWaterDepthAndSurface();
        }

        // Frame-skip batching lets us defer texture uploads on less busy frames,
        // reducing GPU bandwidth without visibly affecting smoothness at typical rates.
        textureUpdateFrameCounter++;
        bool shouldUpdateTexture = textureUpdateFrameCounter > textureUpdateFrameSkip;

        if (shouldUpdateTexture)
        {
            textureUpdateFrameCounter = 0;

            if (hasVisualChanges || !useDirtyRectOptimization)
            {
                UpdateWaterTextureOptimized();
            }
        }

        // Fade ambient flow sound in and out based on how many cells are actively moving,
        // so the audio reflects the intensity of the water without hard cuts.
        float targetVolume = 0f;
        if (activeCells.Count > flowSoundThreshold)
            targetVolume = Mathf.Clamp01((activeCells.Count - flowSoundThreshold) / flowSoundMaxCells);

        flowSource.volume = Mathf.MoveTowards(flowSource.volume, targetVolume, Time.deltaTime * 2f);
    }

    void SimulationStep()
    {
        // Copy current water into newWater so all reads during this step see a consistent
        // snapshot and writes don't immediately affect neighbouring cell calculations.
        System.Array.Copy(water, newWater, water.Length);

        HashSet<Vector2Int> nextActiveCells = new HashSet<Vector2Int>();

        // Safety net: if the active set is empty (e.g. on first frame), scan the whole
        // grid once to find any water that was placed before the simulation started.
        if (activeCells.Count == 0)
        {
            FindAllWaterCells();
        }

        activeCellsList.Clear();
        activeCellsList.AddRange(activeCells);

        int cellCount = activeCellsList.Count;
        for (int i = 0; i < cellCount; i++)
        {
            Vector2Int cell = activeCellsList[i];
            int x = cell.x;
            int y = cell.y;

            if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight) continue;
            if (solid[x, y]) continue;

            float currentWater = water[x, y];
            if (currentWater < minWaterTransfer) continue;

            // Keep this cell active next step so it keeps flowing if it still has water.
            nextActiveCells.Add(cell);

            float pressure = 0f;
            if (enablePressure)
            {
                pressure = CalculatePressure(x, y);
            }

            // Cells under pressure can hold more than maxWaterPerCell, simulating
            // the compression at the base of a deep column of water.
            float effectiveMaxWater = maxWaterPerCell + (pressure * maxCompression);

            // --- Gravity: flow downward first, as it's the highest-priority direction ---
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

                    MarkCellDirty(x, y);
                    MarkCellDirty(x, y - 1);
                }
            }

            // --- Diagonal flow: lets water "slide" around corners when the cell directly
            //     below is blocked, preventing unrealistic stacking at ledge edges ---
            if (enableDiagonalFlow && currentWater > minWaterTransfer && y > 0)
            {
                bool blockedBelow = solid[x, y - 1] || water[x, y - 1] >= maxWaterPerCell * 0.95f;

                if (blockedBelow)
                {
                    // Both the diagonal target and the same-row neighbour must be clear
                    // so water doesn't clip through thin walls.
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
                            MarkCellDirty(x, y);
                            MarkCellDirty(x - 1, y - 1);
                        }

                        if (rightWater < maxWaterPerCell)
                        {
                            float flowRight = Mathf.Min(halfFlow, maxWaterPerCell - rightWater);
                            newWater[x, y] -= flowRight;
                            newWater[x + 1, y - 1] += flowRight;
                            currentWater -= flowRight;
                            nextActiveCells.Add(new Vector2Int(x + 1, y - 1));
                            settled[x + 1, y - 1] = false;
                            MarkCellDirty(x, y);
                            MarkCellDirty(x + 1, y - 1);
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
                            MarkCellDirty(x, y);
                            MarkCellDirty(x - 1, y - 1);
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
                            MarkCellDirty(x, y);
                            MarkCellDirty(x + 1, y - 1);
                        }
                    }
                }
            }

            // --- Horizontal spread: equalise water levels with left/right neighbours ---
            if (currentWater > minWaterTransfer)
            {
                bool canSpreadLeft = x > 0 && !solid[x - 1, y];
                bool canSpreadRight = x < gridWidth - 1 && !solid[x + 1, y];

                if (canSpreadLeft || canSpreadRight)
                {
                    float leftWater = canSpreadLeft ? water[x - 1, y] : maxWaterPerCell;
                    float rightWater = canSpreadRight ? water[x + 1, y] : maxWaterPerCell;

                    // Only flow toward a neighbour that has less water — this naturally
                    // produces a flat water surface over time.
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
                            MarkCellDirty(x, y);
                            MarkCellDirty(x - 1, y);
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
                            MarkCellDirty(x, y);
                            MarkCellDirty(x + 1, y);
                        }
                    }
                }
            }

            // --- Upward pressure: simulate incompressible fluid rising inside enclosed spaces ---
            // Water only pushes upward when the pressure differential is significant enough
            // to overcome gravity, avoiding spurious upward movement in open areas.
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
                            MarkCellDirty(x, y);
                            MarkCellDirty(x, y + 1);
                        }
                    }
                }
            }
        }

        // Swap buffers — newWater becomes the authoritative state for the next step.
        float[,] temp = water;
        water = newWater;
        newWater = temp;

        activeCells = nextActiveCells;
    }

    // Approximates hydrostatic pressure by summing the water in the column directly above.
    // Capped at 15 cells to keep the check O(1) in practice and avoid full-column scans.
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

    // Computes per-cell depth (distance from the water's top surface) and flags surface-exposed
    // cells. Both are used exclusively for rendering — they have no effect on the simulation.
    void CalculateWaterDepthAndSurface()
    {
        System.Array.Clear(waterDepth, 0, waterDepth.Length);
        System.Array.Clear(isSurfaceCell, 0, isSurfaceCell.Length);

        // First pass: assign depth by scanning downward from the top of each column.
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
                    // The first water cell encountered from the top is the surface.
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

        // Second pass: also mark side-exposed cells as surface so that edge highlights
        // appear on vertical water faces, not just the top.
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

    // Full grid scan to rebuild the active set from scratch.
    // Normally the active set is maintained incrementally; this is only needed when
    // water is placed externally and the set hasn't been seeded yet.
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

    void ResetDirtyRect()
    {
        dirtyMinX = int.MaxValue;
        dirtyMinY = int.MaxValue;
        dirtyMaxX = int.MinValue;
        dirtyMaxY = int.MinValue;
        hasVisualChanges = false;
    }

    // Expands the dirty rect to include cell (x, y) plus a small padding border.
    // Padding prevents visual seams at the dirty region boundary caused by blending
    // with cells just outside the updated area.
    void MarkCellDirty(int x, int y)
    {
        if (!useDirtyRectOptimization) return;

        hasVisualChanges = true;

        int minX = Mathf.Max(0, x - dirtyRectPadding);
        int minY = Mathf.Max(0, y - dirtyRectPadding);
        int maxX = Mathf.Min(gridWidth - 1, x + dirtyRectPadding);
        int maxY = Mathf.Min(gridHeight - 1, y + dirtyRectPadding);

        dirtyMinX = Mathf.Min(dirtyMinX, minX);
        dirtyMinY = Mathf.Min(dirtyMinY, minY);
        dirtyMaxX = Mathf.Max(dirtyMaxX, maxX);
        dirtyMaxY = Mathf.Max(dirtyMaxY, maxY);
    }

    void UpdateWaterTextureOptimized()
    {
        if (usePixelArrayPool && pixelArrayPool == null)
        {
            pixelArrayPool = new Color[gridWidth * gridHeight];
        }

        if (useDirtyRectOptimization && hasVisualChanges)
        {
            // Upload only the pixels inside the dirty rect rather than the full texture,
            // which is the primary performance win for large grids with localised activity.
            int width = (dirtyMaxX - dirtyMinX) + 1;
            int height = (dirtyMaxY - dirtyMinY) + 1;

            if (width <= 0 || height <= 0) return;

            // Reuse the pooled buffer when it's large enough; fall back to a fresh allocation
            // for unusually large dirty rects so we never write out of bounds.
            Color[] pixels;
            if (usePixelArrayPool && width * height <= pixelArrayPool.Length)
            {
                pixels = pixelArrayPool;
            }
            else
            {
                pixels = new Color[width * height];
            }

            int index = 0;
            for (int y = dirtyMinY; y <= dirtyMaxY; y++)
            {
                for (int x = dirtyMinX; x <= dirtyMaxX; x++)
                {
                    if (solid[x, y])
                    {
                        // Solid cells are always transparent — the terrain sprite beneath shows through.
                        pixels[index] = Color.clear;
                    }
                    else
                    {
                        float amount = water[x, y];
                        if (amount > minWaterTransfer)
                        {
                            pixels[index] = GetWaterColorOptimized(x, y, amount);
                        }
                        else
                        {
                            pixels[index] = Color.clear;
                        }
                    }
                    index++;
                }
            }

            waterTexture.SetPixels(dirtyMinX, dirtyMinY, width, height, pixels);
            waterTexture.Apply(false);
        }
        else if (!useDirtyRectOptimization)
        {
            // Full-texture fallback when dirty rects are disabled (e.g. for debugging).
            Color[] pixels = usePixelArrayPool ? pixelArrayPool : new Color[gridWidth * gridHeight];

            int index = 0;
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    if (solid[x, y])
                    {
                        pixels[index] = Color.clear;
                    }
                    else
                    {
                        float amount = water[x, y];
                        if (amount > minWaterTransfer)
                        {
                            pixels[index] = GetWaterColorOptimized(x, y, amount);
                        }
                        else
                        {
                            pixels[index] = Color.clear;
                        }
                    }
                    index++;
                }
            }

            waterTexture.SetPixels(pixels);
            waterTexture.Apply(false);
        }

        // Enforce point filtering after every Apply to guard against Unity resetting it.
        waterTexture.filterMode = FilterMode.Point;
    }

    Color GetWaterColorOptimized(int x, int y, float amount)
    {
        Color baseColor;

        if (enableDepthShading)
        {
            int depth = waterDepth[x, y];

            // Index into the pre-baked cache rather than calling Lerp. Deep cells beyond
            // the cache size are simply rendered at the darkest shade.
            if (depth < DEPTH_CACHE_SIZE)
            {
                baseColor = depthColorCache[depth];
            }
            else
            {
                baseColor = waterColorDeep;
            }
        }
        else
        {
            baseColor = waterColorMid;
        }

        if (enableSurfaceHighlight && isSurfaceCell[x, y])
        {
            // The top surface gets a stronger highlight than side-facing edges, simulating
            // light reflecting off the water plane more directly.
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

        // Cells with very little water fade toward transparent so the transition from
        // "wet" to "dry" looks natural rather than cutting off abruptly.
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

                // When a cell transitions from solid to open (e.g. a door opens),
                // reactivate any water that was frozen inside it so it can flow out.
                if (oldSolid[x, y] && !solid[x, y])
                {
                    if (water[x, y] > 0f)
                    {
                        activeCells.Add(new Vector2Int(x, y));
                        settled[x, y] = false;
                    }
                }

                if (oldSolid[x, y] != solid[x, y])
                {
                    MarkCellDirty(x, y);
                }
            }
        }
    }

    // Utility to purge sub-threshold water that can permanently stall the simulation.
    // Cells with water below minWaterTransfer are skipped each step but never fully
    // zeroed, so they accumulate and inflate the active cell count over time.
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
                    MarkCellDirty(x, y);
                }
            }
        }

        Debug.Log($"Cleaned up {cleanedCells} stuck water cells");
    }

    private float spawnSoundCooldown = 0f;
    private float splashSoundCooldown = 0f;

    public void SpawnWater(Vector2 worldPosition, float amount)
    {
        Vector2Int gridPos = WorldToGrid(worldPosition);
        if (IsValidCell(gridPos.x, gridPos.y) && !solid[gridPos.x, gridPos.y])
        {
            water[gridPos.x, gridPos.y] = Mathf.Min(water[gridPos.x, gridPos.y] + amount, maxWaterPerCell);
            activeCells.Add(gridPos);
            settled[gridPos.x, gridPos.y] = false;
            MarkCellDirty(gridPos.x, gridPos.y);
        }

        // Choose drip vs. splash sound based on the amount being spawned,
        // and throttle calls so rapid spawning doesn't spam the sound system.
        spawnSoundCooldown -= Time.deltaTime;
        if (spawnSoundCooldown <= 0f)
        {
            if (amount < 0.3f)
                SoundManager.Instance?.Play("DrippingWater", amount / 0.3f);
            else
                SoundManager.Instance?.Play("Splash", Mathf.Clamp01(amount));

            spawnSoundCooldown = 0.2f;
        }
    }

    // Distributes a total water volume evenly across all non-solid cells inside a polygon.
    // Useful for flooding a room or pre-filling an irregular container from a designer-defined region.
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

        // Count valid cells first so we can divide the total evenly without
        // iterating the region twice or allocating a list.
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
                        MarkCellDirty(x, y);
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
            MarkCellDirty(gridPos.x, gridPos.y);
        }
    }

    public void ClearAllWater()
    {
        System.Array.Clear(water, 0, water.Length);
        System.Array.Clear(newWater, 0, newWater.Length);
        System.Array.Clear(settled, 0, settled.Length);
        activeCells.Clear();

        // Mark the entire grid dirty so the cleared state is fully uploaded this frame.
        dirtyMinX = 0;
        dirtyMinY = 0;
        dirtyMaxX = gridWidth - 1;
        dirtyMaxY = gridHeight - 1;
        hasVisualChanges = true;

        UpdateWaterTextureOptimized();
    }

    // Standard ray-casting point-in-polygon test. Works for any convex or concave polygon
    // as long as it has no self-intersections.
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
            MarkCellDirty(x, y);
        }
    }

    public float CellSize => cellSize;
    public Vector2 GridOrigin => gridOrigin;

    public Vector2Int WorldToGrid(Vector2 worldPos)
    {
        Vector2 localPos = worldPos - gridOrigin;
        int x = Mathf.FloorToInt(localPos.x / cellSize);
        int y = Mathf.FloorToInt(localPos.y / cellSize);
        return new Vector2Int(x, y);
    }

    // Returns the world-space centre of a cell, which is what physics queries and
    // collision checks should target rather than the cell's corner.
    public Vector2 GridToWorld(int x, int y)
    {
        return gridOrigin + new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
    }

    public bool IsValidCell(int x, int y)
    {
        return x >= 0 && x < gridWidth && y >= 0 && y < gridHeight;
    }

    public Rect GetWorldBounds()
    {
        return new Rect(gridOrigin.x, gridOrigin.y, gridWidth * cellSize, gridHeight * cellSize);
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // Show the simulation boundary in the Scene view so designers can see
        // exactly where the liquid grid starts and ends.
        Gizmos.color = Color.cyan;
        Vector2 gridSize = new Vector2(gridWidth * cellSize, gridHeight * cellSize);
        Gizmos.DrawWireCube(gridOrigin + gridSize * 0.5f, gridSize);
    }

    // Scans the scene for all eligible rigidbodies once and caches the result.
    // This avoids calling FindObjectsOfType every displacement tick, which is expensive
    // when many objects are in the scene.
    void RefreshDisplacementCache()
    {
        cachedDisplacementRigidbodies.Clear();

        Rigidbody2D[] allRigidbodies = FindObjectsOfType<Rigidbody2D>();
        foreach (Rigidbody2D rb in allRigidbodies)
        {
            if (rb == null || rb.bodyType == RigidbodyType2D.Static) continue;

            int layerMask = 1 << rb.gameObject.layer;
            if ((layerMask & displacementLayers) != 0)
            {
                cachedDisplacementRigidbodies.Add(rb);
            }
        }

        Debug.Log($"Displacement rigidbody cache refreshed: {cachedDisplacementRigidbodies.Count} tracked");
    }

    // Allows external code (e.g. an object spawn system) to add a rigidbody to the cache
    // immediately, so it causes displacement without waiting for the next cache refresh.
    public void RegisterDisplacementRigidbody(Rigidbody2D rb)
    {
        if (!useCachedDisplacementRigidbodies) return;
        if (rb == null || rb.bodyType == RigidbodyType2D.Static) return;

        int layerMask = 1 << rb.gameObject.layer;
        if ((layerMask & displacementLayers) != 0)
        {
            cachedDisplacementRigidbodies.Add(rb);
        }
    }

    // Removes a destroyed or pooled object from both caches so it doesn't cause
    // null-reference errors during the next displacement update.
    public void UnregisterDisplacementRigidbody(Rigidbody2D rb)
    {
        if (!useCachedDisplacementRigidbodies) return;

        cachedDisplacementRigidbodies.Remove(rb);
        trackedRigidbodies.Remove(rb);
    }

    void UpdateDisplacement()
    {
        Dictionary<Rigidbody2D, Vector2> currentRigidbodies = new Dictionary<Rigidbody2D, Vector2>();

        IEnumerable<Rigidbody2D> rigidbodiestoCheck;

        if (useCachedDisplacementRigidbodies)
        {
            // Prune stale entries first so destroyed objects don't cause null checks throughout.
            cachedDisplacementRigidbodies.RemoveWhere(rb => rb == null);
            rigidbodiestoCheck = cachedDisplacementRigidbodies;
        }
        else
        {
            rigidbodiestoCheck = FindObjectsOfType<Rigidbody2D>();
        }

        foreach (Rigidbody2D rb in rigidbodiestoCheck)
        {
            if (rb == null || rb.bodyType == RigidbodyType2D.Static) continue;

            if (!useCachedDisplacementRigidbodies)
            {
                int layerMask = 1 << rb.gameObject.layer;
                if ((layerMask & displacementLayers) == 0) continue;
            }

            Vector2 currentPos = rb.position;
            Vector2 velocity = rb.linearVelocity;
            float speed = velocity.magnitude;

            // Skip objects that are barely moving — they shouldn't disturb the water surface.
            if (speed < minDisplacementVelocity) continue;

            Vector2 previousPos = currentPos;
            if (trackedRigidbodies.ContainsKey(rb))
            {
                previousPos = trackedRigidbodies[rb];
            }

            DisplaceWaterForRigidbody(rb, currentPos, previousPos, velocity, speed);
            currentRigidbodies[rb] = currentPos;
        }

        // Replace the tracked dict entirely so positions of objects that left the water
        // are not carried over to the next tick.
        trackedRigidbodies = currentRigidbodies;
    }

    bool DisplaceWaterForRigidbody(Rigidbody2D rb, Vector2 currentPos, Vector2 previousPos, Vector2 velocity, float speed)
    {
        Collider2D col = rb.GetComponent<Collider2D>();
        if (col == null) return false;

        Bounds bounds = col.bounds;
        float objectArea = bounds.size.x * bounds.size.y;

        // Scale displacement by the object's cross-sectional area so large objects
        // push more water than small ones, which feels physically intuitive.
        float baseDisplacement = objectArea * displacementStrength;

        float velocityMultiplier = 1f + (speed / 10f) * pushForce;
        velocityMultiplier = Mathf.Clamp(velocityMultiplier, 1f, 5f);

        List<Vector2Int> overlappingCells = new List<Vector2Int>();
        float totalWaterInCells = 0f;

        Vector2 min = bounds.min;
        Vector2 max = bounds.max;

        // Sample a grid of points inside the collider bounds rather than checking every
        // water cell, so the cost scales with object size rather than grid size.
        int samplesX = Mathf.Max(3, Mathf.CeilToInt(bounds.size.x / (cellSize * 1.5f)));
        int samplesY = Mathf.Max(3, Mathf.CeilToInt(bounds.size.y / (cellSize * 1.5f)));

        for (int ix = 0; ix < samplesX; ix++)
        {
            for (int iy = 0; iy < samplesY; iy++)
            {
                float tX = ix / (float)(samplesX - 1);
                float tY = iy / (float)(samplesY - 1);

                Vector2 samplePoint = new Vector2(
                    Mathf.Lerp(min.x, max.x, tX),
                    Mathf.Lerp(min.y, max.y, tY)
                );

                // Use OverlapPoint rather than a bounds check so non-rectangular
                // colliders (circles, polygons) displace accurately.
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

        // Remove water from cells the object occupies; the displaced volume will be
        // redistributed outward to simulate the object pushing water aside.
        foreach (Vector2Int cell in overlappingCells)
        {
            float waterToRemove = Mathf.Min(waterPerCell * 0.85f, water[cell.x, cell.y]);
            water[cell.x, cell.y] -= waterToRemove;
            waterToDisplace += waterToRemove;

            if (water[cell.x, cell.y] < minWaterTransfer)
            {
                water[cell.x, cell.y] = 0f;
            }

            MarkCellDirty(cell.x, cell.y);
        }

        if (waterToDisplace > minWaterTransfer)
        {
            PushWaterAround(overlappingCells, waterToDisplace, velocity);

            // Only play the splash when the impact is significant and enough time has passed,
            // to avoid sound spam from objects that move continuously through water.
            float splashVolume = Mathf.Clamp01(speed / 10f);
            if (splashVolume > 0.1f && splashSoundCooldown <= 0f)
            {
                SoundManager.Instance?.Play("Splash", splashVolume);
                splashSoundCooldown = 5f;
            }
        }

        return true;
    }

    // Redistributes displaced water into a fan of cells above and in the direction of motion.
    // This creates the characteristic "bow wave" effect when an object moves through water.
    void PushWaterAround(List<Vector2Int> sourceCells, float waterAmount, Vector2 velocity)
    {
        HashSet<Vector2Int> targetCells = new HashSet<Vector2Int>();

        foreach (Vector2Int sourceCell in sourceCells)
        {
            // Always push some water upward — gravity will settle it back naturally.
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
                // Push water forward in the direction of travel to simulate a bow wave.
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

                // Include a small wake behind the object to fill the cavity it leaves.
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
            MarkCellDirty(targetCell.x, targetCell.y);
        }
    }
}