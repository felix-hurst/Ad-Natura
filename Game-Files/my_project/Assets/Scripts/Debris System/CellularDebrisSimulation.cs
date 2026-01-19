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
    [SerializeField] private float debrisFallSpeed = 1.0f;
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
    [Tooltip("How much moving objects displace debris")]
    [Range(0f, 5f)]
    [SerializeField] private float displacementStrength = 2.0f;
    
    [Header("Visual Settings")]
    [SerializeField] private Material renderMaterial;
    [SerializeField] private int pixelsPerUnit = 10;
    
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
    private Texture2D debrisTexture;
    private SpriteRenderer debrisRenderer;
    private GameObject debrisVisualObject;

    private HashSet<Vector2Int> activeCells = new HashSet<Vector2Int>();
    private float solidUpdateTimer = 0f;
    
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

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                debrisMaterial[x, y] = "Default";
                velocity[x, y] = Vector2.zero;
            }
        }
        UpdateSolidCells();
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
        for (int i = 0; i < simulationStepsPerFrame; i++)
        {
            SimulationStep();
        }
        UpdateDebrisAge();
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

            Vector2 vel = velocity[x, y];

            vel.y -= 9.8f * Time.deltaTime * debrisFallSpeed;

            vel *= (1f - (1f - inertia) * Time.deltaTime * 3f);
            
            bool moved = false;

            if (y > 0 && !solid[x, y - 1])
            {
                float below = newDebris[x, y - 1];
                if (below < maxDebrisPerCell * 0.9f)
                {
                    float moveAmount = currentDebris * debrisFallSpeed;
                    moveAmount = Mathf.Min(moveAmount, maxDebrisPerCell - below);
                    
                    newDebris[x, y] -= moveAmount;
                    newDebris[x, y - 1] += moveAmount;
                    
                    debrisMaterial[x, y - 1] = debrisMaterial[x, y];
                    newVelocity[x, y - 1] = new Vector2(vel.x, vel.y);
                    
                    nextActiveCells.Add(new Vector2Int(x, y - 1));
                    settled[x, y - 1] = false;
                    moved = true;
                    
                    if (newDebris[x, y] > minDebrisTransfer)
                    {
                        nextActiveCells.Add(cell);
                    }
                }
            }

            if (!moved && y > 0)
            {
                bool canSlideLeft = x > 0 && !solid[x - 1, y - 1] && newDebris[x - 1, y - 1] < maxDebrisPerCell * 0.5f;
                bool canSlideRight = x < gridWidth - 1 && !solid[x + 1, y - 1] && newDebris[x + 1, y - 1] < maxDebrisPerCell * 0.5f;
                int dir = vel.x < -0.1f ? -1 : (vel.x > 0.1f ? 1 : (Random.value > 0.5f ? 1 : -1));
                
                if (dir < 0 && canSlideLeft)
                {
                    float slideAmount = currentDebris * slideCoefficient;
                    newDebris[x, y] -= slideAmount;
                    newDebris[x - 1, y - 1] += slideAmount;
                    
                    debrisMaterial[x - 1, y - 1] = debrisMaterial[x, y];
                    newVelocity[x - 1, y - 1] = new Vector2(-slideCoefficient * 3f, vel.y);
                    
                    nextActiveCells.Add(new Vector2Int(x - 1, y - 1));
                    settled[x - 1, y - 1] = false;
                    moved = true;
                }
                else if (dir > 0 && canSlideRight)
                {
                    float slideAmount = currentDebris * slideCoefficient;
                    newDebris[x, y] -= slideAmount;
                    newDebris[x + 1, y - 1] += slideAmount;
                    
                    debrisMaterial[x + 1, y - 1] = debrisMaterial[x, y];
                    newVelocity[x + 1, y - 1] = new Vector2(slideCoefficient * 3f, vel.y);
                    
                    nextActiveCells.Add(new Vector2Int(x + 1, y - 1));
                    settled[x + 1, y - 1] = false;
                    moved = true;
                }
                else if (dir > 0 && canSlideLeft)
                {
                    float slideAmount = currentDebris * slideCoefficient;
                    newDebris[x, y] -= slideAmount;
                    newDebris[x - 1, y - 1] += slideAmount;
                    
                    debrisMaterial[x - 1, y - 1] = debrisMaterial[x, y];
                    newVelocity[x - 1, y - 1] = new Vector2(-slideCoefficient * 3f, vel.y);
                    
                    nextActiveCells.Add(new Vector2Int(x - 1, y - 1));
                    settled[x - 1, y - 1] = false;
                    moved = true;
                }
                else if (dir < 0 && canSlideRight)
                {
                    float slideAmount = currentDebris * slideCoefficient;
                    newDebris[x, y] -= slideAmount;
                    newDebris[x + 1, y - 1] += slideAmount;
                    
                    debrisMaterial[x + 1, y - 1] = debrisMaterial[x, y];
                    newVelocity[x + 1, y - 1] = new Vector2(slideCoefficient * 3f, vel.y);
                    
                    nextActiveCells.Add(new Vector2Int(x + 1, y - 1));
                    settled[x + 1, y - 1] = false;
                    moved = true;
                }
            }
            
            if (!moved)
            {
                newVelocity[x, y] = Vector2.zero;
                settled[x, y] = true;
            }
            else if (newDebris[x, y] > minDebrisTransfer)
            {
                nextActiveCells.Add(cell);
            }
        }

        float[,] tempDebris = debris;
        debris = newDebris;
        newDebris = tempDebris;
        
        Vector2[,] tempVel = velocity;
        velocity = newVelocity;
        newVelocity = tempVel;
        
        activeCells = nextActiveCells;
    }
    
    void UpdateDebrisAge()
    {
        if (debrisLifetime <= 0f) return;
        
        int cleanedCells = 0;
        
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
                        cleanedCells++;
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
                    float amount = debris[x, y];
                    if (amount > minDebrisTransfer)
                    {
                        string material = debrisMaterial[x, y];
                        Color baseColor = materialColors.ContainsKey(material) 
                            ? materialColors[material] 
                            : materialColors["Default"];

                        float alpha = 1.0f;
                        if (debrisLifetime > 0f && debrisAge[x, y] > debrisLifetime * 0.7f)
                        {
                            float fadeAmount = (debrisAge[x, y] - debrisLifetime * 0.7f) / (debrisLifetime * 0.3f);
                            alpha *= (1f - fadeAmount);
                        }
                        
                        pixels[index] = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
                    }
                    else
                    {
                        pixels[index] = Color.clear;
                    }
                }
            }
        }
        
        debrisTexture.SetPixels(pixels);
        debrisTexture.Apply();
        debrisTexture.filterMode = FilterMode.Point;
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
        int totalChecked = 0;
        int outOfBounds = 0;
        int solidCells = 0;
        int notInPolygon = 0;
        int skippedSourceObject = 0;
        
        for (int x = gridMin.x; x <= gridMax.x; x++)
        {
            for (int y = gridMin.y; y <= gridMax.y; y++)
            {
                totalChecked++;
                
                if (!IsValidCell(x, y))
                {
                    outOfBounds++;
                    continue;
                }

                Vector2 worldPos = GridToWorld(x, y);
                Collider2D hit = Physics2D.OverlapCircle(worldPos, physicsCheckRadius, solidLayer);
                
                bool isSolid = false;
                if (hit != null)
                {
                    if (sourceObject != null && hit.gameObject == sourceObject)
                    {
                        skippedSourceObject++;
                        isSolid = false;
                    }
                    else
                    {
                        isSolid = true;
                        solidCells++;
                    }
                }
                
                if (isSolid)
                {
                    continue;
                }
                
                if (IsPointInPolygon(worldPos, worldVertices))
                {
                    validCells++;
                }
                else
                {
                    notInPolygon++;
                }
            }
        }
        
        if (validCells == 0)
        {
            return;
        }
        
        float debrisPerCell = totalAmount / validCells;
        int spawnedCells = 0;
        
        
        for (int x = gridMin.x; x <= gridMax.x; x++)
        {
            for (int y = gridMin.y; y <= gridMax.y; y++)
            {
                if (!IsValidCell(x, y))
                {
                    continue;
                }

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
                
                if (isSolid)
                {
                    continue;
                }
                
                if (IsPointInPolygon(worldPos, worldVertices))
                {
                    debris[x, y] = Mathf.Min(debris[x, y] + debrisPerCell, maxDebrisPerCell);
                    debrisMaterial[x, y] = material;
                    debrisAge[x, y] = 0f;
                    velocity[x, y] = new Vector2(Random.Range(-2f, 2f), Random.Range(-1f, 1f));
                    
                    activeCells.Add(new Vector2Int(x, y));
                    settled[x, y] = false;
                    spawnedCells++;
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
        
        activeCells.Clear();
        UpdateDebrisTexture();
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