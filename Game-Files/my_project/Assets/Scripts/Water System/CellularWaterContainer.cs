using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Integrates cellular water simulation with your cutting system
/// Spawns water into the simulation grid when objects are cut
/// </summary>
[RequireComponent(typeof(RaycastReceiver))]
public class CellularWaterContainer : MonoBehaviour
{
    [Header("Water Content")]
    [SerializeField] private bool containsWater = true;
    [Tooltip("Amount of water per unit area")]
    [SerializeField] private float waterDensity = 5f; // Water cells per square unit
    
    [Header("Spawn Settings")]
    [Tooltip("Spread water over multiple frames for better performance")]
    [SerializeField] private bool gradualSpawn = true;
    [SerializeField] private int framesForFullSpawn = 10;
    
    [Header("Pressure Settings")]
    [Tooltip("Does water spray out under pressure?")]
    [SerializeField] private bool hasPressure = false;
    [Tooltip("Extra water spawned above the cut (pressure effect)")]
    [SerializeField] private float pressureMultiplier = 1.5f;
    
    private CellularLiquidSimulation liquidSim;
    private Queue<WaterSpawnRequest> pendingSpawns = new Queue<WaterSpawnRequest>();
    
    private struct WaterSpawnRequest
    {
        public List<Vector2> vertices;
        public float totalAmount;
        public int framesRemaining;
    }
    
    void Start()
    {
        liquidSim = FindObjectOfType<CellularLiquidSimulation>();
        
        if (liquidSim == null)
        {
            Debug.LogError("CellularLiquidSimulation not found! Please add one to the scene.");
            enabled = false;
        }
    }
    
    void Update()
    {
        // Process pending water spawns gradually
        if (pendingSpawns.Count > 0)
        {
            WaterSpawnRequest request = pendingSpawns.Dequeue();
            
            if (request.framesRemaining > 1)
            {
                // Spawn partial amount this frame
                float amountThisFrame = request.totalAmount / request.framesRemaining;
                liquidSim.SpawnWaterInRegion(request.vertices, amountThisFrame);
                
                // Re-queue with remaining amount
                request.totalAmount -= amountThisFrame;
                request.framesRemaining--;
                pendingSpawns.Enqueue(request);
            }
            else
            {
                // Spawn remaining water
                liquidSim.SpawnWaterInRegion(request.vertices, request.totalAmount);
            }
        }
    }
    
    /// <summary>
    /// Called when the object is cut - spawns water into the cellular simulation
    /// </summary>
    public void OnObjectCut(Vector2 entryPoint, Vector2 exitPoint, List<Vector2> cutOffShape, float cutOffArea)
    {
        if (!containsWater || liquidSim == null || cutOffShape == null)
            return;
        
        // Calculate total water to spawn based on cut area and density
        float totalWater = cutOffArea * waterDensity;
        
        // Apply pressure multiplier if enabled
        if (hasPressure)
        {
            totalWater *= pressureMultiplier;
        }
        
        Debug.Log($"Spawning cellular water from cut: area={cutOffArea:F2}, density={waterDensity}, total={totalWater:F1} cells");
        
        if (gradualSpawn)
        {
            // Queue for gradual spawning
            WaterSpawnRequest request = new WaterSpawnRequest
            {
                vertices = new List<Vector2>(cutOffShape),
                totalAmount = totalWater,
                framesRemaining = framesForFullSpawn
            };
            pendingSpawns.Enqueue(request);
        }
        else
        {
            // Spawn all water immediately
            liquidSim.SpawnWaterInRegion(cutOffShape, totalWater);
        }
    }
    
    /// <summary>
    /// Spawns water continuously at a point (for leaks)
    /// </summary>
    public void SpawnContinuousWater(Vector2 position, float amountPerSecond)
    {
        if (!containsWater || liquidSim == null) return;
        
        float amount = amountPerSecond * Time.deltaTime;
        liquidSim.SpawnWater(position, amount);
    }
}