using UnityEngine;

/// <summary>
/// Spawns water continuously into the cellular simulation
/// Like Noita's water spouts, fountains, etc.
/// </summary>
public class CellularWaterSource : MonoBehaviour
{
    [Header("Source Settings")]
    [SerializeField] private bool isActive = true;
    [SerializeField] private float waterPerSecond = 10f; // Water cells per second
    
    [Header("Spawn Pattern")]
    [SerializeField] private SourceType sourceType = SourceType.Point;
    [SerializeField] private float spawnRadius = 0.2f; // For area sources
    [SerializeField] private Vector2 spawnDirection = Vector2.down; // For directional sources
    
    public enum SourceType
    {
        Point,        // Single point (tap, hole)
        Area,         // Random in radius (fountain)
        Directional   // Along a direction (water jet)
    }
    
    [Header("Variation")]
    [SerializeField] private bool varyFlow = false;
    [SerializeField] private float flowCycleTime = 2f;
    [SerializeField] private AnimationCurve flowCurve = AnimationCurve.Linear(0, 0.5f, 1, 1.5f);
    
    private CellularLiquidSimulation liquidSim;
    private float flowTimer = 0f;
    
    void Start()
    {
        liquidSim = FindObjectOfType<CellularLiquidSimulation>();
        
        if (liquidSim == null)
        {
            Debug.LogError("CellularLiquidSimulation not found! WaterSource needs it in the scene.");
            enabled = false;
        }
    }
    
    void Update()
    {
        if (!isActive || liquidSim == null) return;
        
        // Update flow cycle
        if (varyFlow)
        {
            flowTimer += Time.deltaTime;
            if (flowTimer >= flowCycleTime)
            {
                flowTimer = 0f;
            }
        }
        
        // Calculate flow rate
        float flowMultiplier = varyFlow ? flowCurve.Evaluate(flowTimer / flowCycleTime) : 1f;
        float waterThisFrame = waterPerSecond * flowMultiplier * Time.deltaTime;
        
        // Spawn water based on type
        SpawnWater(waterThisFrame);
    }
    
    void SpawnWater(float amount)
    {
        Vector2 spawnPos = transform.position;
        
        switch (sourceType)
        {
            case SourceType.Point:
                // Spawn at exact position
                liquidSim.SpawnWater(spawnPos, amount);
                break;
            
            case SourceType.Area:
                // Spread across multiple cells in radius
                int cellsToSpawn = Mathf.CeilToInt(amount);
                float amountPerCell = amount / cellsToSpawn;
                
                for (int i = 0; i < cellsToSpawn; i++)
                {
                    Vector2 randomOffset = Random.insideUnitCircle * spawnRadius;
                    liquidSim.SpawnWater(spawnPos + randomOffset, amountPerCell);
                }
                break;
            
            case SourceType.Directional:
                // Spawn along direction
                Vector2 offset = spawnDirection.normalized * spawnRadius;
                liquidSim.SpawnWater(spawnPos + offset, amount);
                break;
        }
    }
    
    public void SetActive(bool active)
    {
        isActive = active;
    }
    
    public void Toggle()
    {
        isActive = !isActive;
    }
    
    public void SetFlowRate(float rate)
    {
        waterPerSecond = Mathf.Max(0f, rate);
    }
    
    void OnDrawGizmos()
    {
        Gizmos.color = isActive ? new Color(0.2f, 0.5f, 1f, 0.5f) : Color.gray;
        
        switch (sourceType)
        {
            case SourceType.Point:
                Gizmos.DrawWireSphere(transform.position, 0.1f);
                break;
            
            case SourceType.Area:
                Gizmos.DrawWireSphere(transform.position, spawnRadius);
                break;
            
            case SourceType.Directional:
                Gizmos.DrawWireSphere(transform.position, 0.1f);
                Vector2 dir = spawnDirection.normalized;
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, (Vector2)transform.position + dir * spawnRadius);
                break;
        }
    }
}