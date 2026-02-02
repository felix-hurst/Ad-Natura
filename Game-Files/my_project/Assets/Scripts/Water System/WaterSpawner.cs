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
    
    [Header("Spawn Duration")]
    [Tooltip("How long to spawn water for (in seconds). Set 0 for infinite spawning")]
    [SerializeField] private float spawnDuration = 0f;
    
    [Tooltip("Should spawning automatically restart after duration ends?")]
    [SerializeField] private bool loopSpawning = false;
    
    [Tooltip("Delay before restarting spawn loop (seconds)")]
    [SerializeField] private float loopDelay = 1f;
    
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
    private float durationTimer;
    private float loopDelayTimer;
    private bool isSpawning;
    private bool isInLoopDelay;

    public bool IsSpawning => isSpawning;
    public float RemainingDuration => spawnDuration > 0 ? Mathf.Max(0, spawnDuration - durationTimer) : -1f;
    public float SpawnProgress => spawnDuration > 0 ? Mathf.Clamp01(durationTimer / spawnDuration) : 0f;
    
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
        if (isInLoopDelay)
        {
            loopDelayTimer += Time.deltaTime;
            if (loopDelayTimer >= loopDelay)
            {
                isInLoopDelay = false;
                loopDelayTimer = 0f;
                StartSpawning();
            }
            return;
        }

        if (isSpawning && continuousSpawning)
        {
            if (spawnDuration > 0)
            {
                durationTimer += Time.deltaTime;

                if (durationTimer >= spawnDuration)
                {
                    if (loopSpawning)
                    {
                        Debug.Log($"WaterSpawner: Spawn duration completed. Waiting {loopDelay}s before restart...");
                        isSpawning = false;
                        isInLoopDelay = true;
                        loopDelayTimer = 0f;
                        durationTimer = 0f;
                    }
                    else
                    {
                        Debug.Log("WaterSpawner: Spawn duration completed. Stopping.");
                        StopSpawning();
                    }
                    return;
                }
            }

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

            case SpawnPattern.Circle:
                SpawnCircle(centerCell);
                break;
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


    [ContextMenu("Start Spawning")]
    public void StartSpawning()
    {
        if (!continuousSpawning)
        {
            Debug.LogWarning("WaterSpawner: Continuous spawning is disabled. Enable it in the inspector.");
            return;
        }
        
        isSpawning = true;
        isInLoopDelay = false;
        spawnTimer = 0f;
        durationTimer = 0f;
        loopDelayTimer = 0f;
        
        if (spawnDuration > 0)
        {
            Debug.Log($"WaterSpawner: Started spawning at {GetSpawnPosition()} for {spawnDuration} seconds");
        }
        else
        {
            Debug.Log($"WaterSpawner: Started infinite spawning at {GetSpawnPosition()}");
        }
    }

    public void StartSpawning(float customDuration)
    {
        float originalDuration = spawnDuration;
        spawnDuration = customDuration;
        StartSpawning();
        spawnDuration = originalDuration; 
    }

    [ContextMenu("Stop Spawning")]
    public void StopSpawning()
    {
        isSpawning = false;
        isInLoopDelay = false;
        durationTimer = 0f;
        loopDelayTimer = 0f;
        Debug.Log("WaterSpawner: Stopped spawning");
    }

    [ContextMenu("Toggle Spawning")]
    public void ToggleSpawning()
    {
        if (isSpawning)
            StopSpawning();
        else
            StartSpawning();
    }

    public void ExtendDuration(float additionalSeconds)
    {
        if (spawnDuration > 0 && isSpawning)
        {
            spawnDuration += additionalSeconds;
            Debug.Log($"WaterSpawner: Extended duration by {additionalSeconds}s. New total: {spawnDuration}s");
        }
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

            if (spawnDuration > 0)
            {
                float progress = durationTimer / spawnDuration;
                Gizmos.color = Color.Lerp(Color.green, Color.red, progress);
                Gizmos.DrawWireSphere(spawnPos + Vector2.up * 0.3f, 0.1f * (1f - progress));
            }
        }

        if (Application.isPlaying && isInLoopDelay)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(spawnPos, 0.15f);
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