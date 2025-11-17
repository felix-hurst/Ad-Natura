using UnityEngine;

/// <summary>
/// Automatically updates the water simulation's solid cells when objects move
/// Attach this to your CellularLiquidSimulation GameObject
/// </summary>
public class AutoUpdateSolidCells : MonoBehaviour
{
    [Header("Update Settings")]
    [Tooltip("How often to check for moved objects (seconds)")]
    [SerializeField] private float updateInterval = 0.5f;
    
    [Tooltip("Only update when objects actually move")]
    [SerializeField] private bool onlyUpdateOnChange = true;
    
    [Tooltip("Automatically clean up stuck water when updating solid cells")]
    [SerializeField] private bool cleanStuckWater = true;
    
    [Tooltip("Layer mask of objects that can block water")]
    [SerializeField] private LayerMask solidLayers = ~0;
    
    [Header("Manual Update")]
    [Tooltip("Press this key to force update solid cells")]
    [SerializeField] private KeyCode manualUpdateKey = KeyCode.U;
    
    private CellularLiquidSimulation liquidSim;
    private float updateTimer;
    private int lastColliderCount;
    
    void Start()
    {
        liquidSim = GetComponent<CellularLiquidSimulation>();
        if (liquidSim == null)
        {
            Debug.LogError("AutoUpdateSolidCells needs to be on the same GameObject as CellularLiquidSimulation!");
            enabled = false;
        }
        
        // Initial count
        lastColliderCount = FindObjectsOfType<Collider2D>().Length;
    }
    
    void Update()
    {
        // Manual update with key
        if (Input.GetKeyDown(manualUpdateKey))
        {
            ForceUpdate();
        }
        
        // Automatic periodic update
        updateTimer += Time.deltaTime;
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            
            if (onlyUpdateOnChange)
            {
                // Only update if collider count changed (something was added/destroyed)
                int currentCount = FindObjectsOfType<Collider2D>().Length;
                if (currentCount != lastColliderCount)
                {
                    ForceUpdate();
                    lastColliderCount = currentCount;
                    Debug.Log($"Detected collider change ({lastColliderCount} → {currentCount}), updating solid cells");
                }
            }
            else
            {
                // Always update (less efficient but catches moved objects)
                ForceUpdate();
            }
        }
    }
    
    /// <summary>
    /// Forces an immediate update of solid cells
    /// </summary>
    [ContextMenu("Force Update Solid Cells")]
    public void ForceUpdate()
    {
        if (liquidSim != null)
        {
            liquidSim.UpdateSolidCells();
            
            if (cleanStuckWater)
            {
                liquidSim.SendMessage("CleanUpStuckWater", SendMessageOptions.DontRequireReceiver);
            }
            
            Debug.Log($"Updated solid cells at {Time.time:F2}s");
        }
    }
}