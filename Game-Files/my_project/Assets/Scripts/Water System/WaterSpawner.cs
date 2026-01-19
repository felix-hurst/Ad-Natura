using UnityEngine;
using UnityEngine.InputSystem;

public enum SpawnPattern
{
    Single,
    Line,
    Circle,
    Stream
}

public class WaterSpawner : MonoBehaviour
{
    [Header("Spawn Location")]
    [Tooltip("World position where water will spawn (uses transform position if not overridden)")]
    [SerializeField] private bool useTransformPosition = true;
    
    [Tooltip("Custom spawn position (only used if Use Transform Position is false)")]
    [SerializeField] private Vector2 customSpawnPosition = Vector2.zero;
    
    [Header("Spawn Behavior")]
    [Tooltip("Continuously spawn water like a waterfall")]
    [SerializeField] private bool continuousSpawning = false;
    
    [Tooltip("Spawn pattern type")]
    [SerializeField] private SpawnPattern spawnPattern = SpawnPattern.Circle;
    
    [Tooltip("Amount of water to spawn per interval (0.0 to 1.0, where 1.0 is a full cell)")]
    [SerializeField] [Range(0.01f, 1.0f)] private float waterAmount = 0.15f;
    
    [Tooltip("How often to spawn water when continuous (seconds). Lower = smoother stream")]
    [SerializeField] private float spawnInterval = 0.02f;
    
    [Tooltip("Radius of water spawn area (in cells)")]
    [SerializeField] [Range(1, 10)] private int spawnRadius = 1;
    
    [Header("Manual Spawn")]
    [Tooltip("Press this key to manually spawn water")]
    [SerializeField] private Key manualSpawnKey = Key.Space;
    
    [Tooltip("Enable manual spawning with key press")]
    [SerializeField] private bool allowManualSpawn = true;
    
    [Header("Auto-Start")]
    [Tooltip("Start spawning automatically when the game starts")]
    [SerializeField] private bool spawnOnStart = false;
    
    [Header("References")]
    [Tooltip("Reference to the liquid simulation (auto-finds if not set)")]
    [SerializeField] private CellularLiquidSimulation liquidSimulation;
    
    private float spawnTimer;
    private bool isSpawning;
    
    void Start()
    {
        if (liquidSimulation == null)
        {
            liquidSimulation = FindObjectOfType<CellularLiquidSimulation>();
            if (liquidSimulation == null)
            {
                Debug.LogError("WaterSpawner: Could not find CellularLiquidSimulation in scene!");
                enabled = false;
                return;
            }
        }
        
        if (spawnOnStart && continuousSpawning)
        {
            StartSpawning();
        }
        else if (spawnOnStart)
        {
            SpawnWater();
        }
    }
    
    void Update()
    {
        if (allowManualSpawn && Keyboard.current != null && Keyboard.current[manualSpawnKey].wasPressedThisFrame)
        {
            SpawnWater();
        }

        if (isSpawning && continuousSpawning)
        {
            spawnTimer += Time.deltaTime;
            if (spawnTimer >= spawnInterval)
            {
                spawnTimer = 0f;
                SpawnWater();
            }
        }
    }
    
    [ContextMenu("Spawn Water Once")]
    public void SpawnWater()
    {
        if (liquidSimulation == null)
        {
            Debug.LogWarning("WaterSpawner: No liquid simulation reference!");
            return;
        }
        
        Vector2 spawnPos = useTransformPosition ? transform.position : customSpawnPosition;
        Vector2Int centerCell = liquidSimulation.WorldToGrid(spawnPos);
        
        switch (spawnPattern)
        {
            case SpawnPattern.Single:
                SpawnSingle(centerCell);
                break;
            case SpawnPattern.Line:
                SpawnLine(centerCell);
                break;
            case SpawnPattern.Circle:
                SpawnCircle(centerCell);
                break;
            case SpawnPattern.Stream:
                SpawnStream(centerCell);
                break;
        }
    }
    
    private void SpawnSingle(Vector2Int centerCell)
    {
        if (liquidSimulation.IsValidCell(centerCell.x, centerCell.y))
        {
            float currentWater = liquidSimulation.GetWater(centerCell.x, centerCell.y);
            liquidSimulation.SetWater(centerCell.x, centerCell.y, currentWater + waterAmount);
        }
    }
    
    private void SpawnLine(Vector2Int centerCell)
    {
        for (int y = 0; y < spawnRadius; y++)
        {
            int cellY = centerCell.y + y;
            if (liquidSimulation.IsValidCell(centerCell.x, cellY))
            {
                float currentWater = liquidSimulation.GetWater(centerCell.x, cellY);
                liquidSimulation.SetWater(centerCell.x, cellY, currentWater + waterAmount);
            }
        }
    }
    
    private void SpawnCircle(Vector2Int centerCell)
    {
        for (int x = -spawnRadius; x <= spawnRadius; x++)
        {
            for (int y = -spawnRadius; y <= spawnRadius; y++)
            {
                if (Mathf.Sqrt(x * x + y * y) <= spawnRadius)
                {
                    int cellX = centerCell.x + x;
                    int cellY = centerCell.y + y;

                    if (liquidSimulation.IsValidCell(cellX, cellY))
                    {
                        float currentWater = liquidSimulation.GetWater(cellX, cellY);
                        liquidSimulation.SetWater(cellX, cellY, currentWater + waterAmount);
                    }
                }
            }
        }
    }
    
    private void SpawnStream(Vector2Int centerCell)
    {
        if (liquidSimulation.IsValidCell(centerCell.x, centerCell.y))
        {
            float currentWater = liquidSimulation.GetWater(centerCell.x, centerCell.y);
            liquidSimulation.SetWater(centerCell.x, centerCell.y, currentWater + waterAmount);
        }

        if (spawnRadius > 1)
        {
            float sideAmount = waterAmount * 0.3f;
            
            for (int x = 1; x <= Mathf.Min(2, spawnRadius - 1); x++)
            {
                if (liquidSimulation.IsValidCell(centerCell.x - x, centerCell.y))
                {
                    float currentWater = liquidSimulation.GetWater(centerCell.x - x, centerCell.y);
                    liquidSimulation.SetWater(centerCell.x - x, centerCell.y, currentWater + sideAmount / x);
                }

                if (liquidSimulation.IsValidCell(centerCell.x + x, centerCell.y))
                {
                    float currentWater = liquidSimulation.GetWater(centerCell.x + x, centerCell.y);
                    liquidSimulation.SetWater(centerCell.x + x, centerCell.y, currentWater + sideAmount / x);
                }
            }
        }
    }

    [ContextMenu("Start Spawning")]
    public void StartSpawning()
    {
        if (!continuousSpawning)
        {
            Debug.LogWarning("WaterSpawner: Continuous spawning is disabled. Enable it in the inspector.");
            return;
        }
        
        isSpawning = true;
        spawnTimer = 0f;
        Debug.Log($"WaterSpawner: Started continuous spawning at {GetSpawnPosition()}");
    }

    [ContextMenu("Stop Spawning")]
    public void StopSpawning()
    {
        isSpawning = false;
        Debug.Log("WaterSpawner: Stopped continuous spawning");
    }

    [ContextMenu("Toggle Spawning")]
    public void ToggleSpawning()
    {
        if (isSpawning)
            StopSpawning();
        else
            StartSpawning();
    }

    public Vector2 GetSpawnPosition()
    {
        return useTransformPosition ? transform.position : customSpawnPosition;
    }

    public void SetSpawnPosition(Vector2 newPosition)
    {
        customSpawnPosition = newPosition;
        useTransformPosition = false;
    }

    void OnDrawGizmos()
    {
        Vector2 spawnPos = useTransformPosition ? transform.position : customSpawnPosition;

        Gizmos.color = continuousSpawning ? Color.cyan : Color.blue;
        Gizmos.DrawWireSphere(spawnPos, 0.2f);

        float estimatedCellSize = 0.1f;
        float worldRadius = spawnRadius * estimatedCellSize;
        Gizmos.color = new Color(0, 0.5f, 1f, 0.3f);
        Gizmos.DrawWireSphere(spawnPos, worldRadius);

        if (Application.isPlaying && isSpawning)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(spawnPos, spawnPos + Vector2.down * 0.5f);
        }
    }
    
    void OnDrawGizmosSelected()
    {
        Vector2 spawnPos = useTransformPosition ? transform.position : customSpawnPosition;

        Gizmos.color = Color.cyan;

        if (Application.isPlaying && liquidSimulation != null)
        {
            Vector2Int centerCell = liquidSimulation.WorldToGrid(spawnPos);
            DrawSpawnPatternGizmo(centerCell, true);
        }
        else
        {

            Vector2Int centerCell = new Vector2Int(
                Mathf.RoundToInt(spawnPos.x / 0.1f),
                Mathf.RoundToInt(spawnPos.y / 0.1f)
            );
            DrawSpawnPatternGizmo(centerCell, false);
        }
    }
    
    private void DrawSpawnPatternGizmo(Vector2Int centerCell, bool useSimulation)
    {
        float estimatedCellSize = 0.1f;
        
        switch (spawnPattern)
        {
            case SpawnPattern.Single:
                DrawCellGizmo(centerCell, estimatedCellSize, useSimulation);
                break;
                
            case SpawnPattern.Line:
                for (int y = 0; y < spawnRadius; y++)
                {
                    DrawCellGizmo(new Vector2Int(centerCell.x, centerCell.y + y), estimatedCellSize, useSimulation);
                }
                break;
                
            case SpawnPattern.Circle:
                for (int x = -spawnRadius; x <= spawnRadius; x++)
                {
                    for (int y = -spawnRadius; y <= spawnRadius; y++)
                    {
                        if (Mathf.Sqrt(x * x + y * y) <= spawnRadius)
                        {
                            DrawCellGizmo(new Vector2Int(centerCell.x + x, centerCell.y + y), estimatedCellSize, useSimulation);
                        }
                    }
                }
                break;
                
            case SpawnPattern.Stream:
                Gizmos.color = Color.cyan;
                DrawCellGizmo(centerCell, estimatedCellSize, useSimulation);

                Gizmos.color = new Color(0, 1f, 1f, 0.5f);
                if (spawnRadius > 1)
                {
                    for (int x = 1; x <= Mathf.Min(2, spawnRadius - 1); x++)
                    {
                        DrawCellGizmo(new Vector2Int(centerCell.x - x, centerCell.y), estimatedCellSize, useSimulation);
                        DrawCellGizmo(new Vector2Int(centerCell.x + x, centerCell.y), estimatedCellSize, useSimulation);
                    }
                }
                break;
        }
    }
    
    private void DrawCellGizmo(Vector2Int cell, float cellSize, bool useSimulation)
    {
        Vector2 worldPos;
        
        if (useSimulation && liquidSimulation != null)
        {
            if (liquidSimulation.IsValidCell(cell.x, cell.y))
            {
                worldPos = liquidSimulation.GridToWorld(cell.x, cell.y);
                Gizmos.DrawWireCube(worldPos, Vector2.one * cellSize * 0.8f);
            }
        }
        else
        {
            worldPos = new Vector2(cell.x * cellSize, cell.y * cellSize);
            Gizmos.DrawWireCube(worldPos, Vector2.one * cellSize * 0.8f);
        }
    }
}