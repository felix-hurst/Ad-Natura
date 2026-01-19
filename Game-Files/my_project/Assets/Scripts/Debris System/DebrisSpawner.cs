

using UnityEngine;
using System.Collections.Generic;

public class DebrisSpawner : MonoBehaviour
{
    [Header("Debris Settings")]
    [Tooltip("Cellular debris system")]
    [SerializeField] private bool useCellularDebris = true;
    
    private SpriteRenderer spriteRenderer;
    private string parentMaterialTag;
    private CellularDebrisSimulation cellularDebris;
    
    void Awake()
    {
        Debug.Log($"[DebrisSpawner] Awake on {gameObject.name}");
    }
    
    void Start()
    {
        Debug.Log($"[DebrisSpawner] Start on {gameObject.name}");
        
        spriteRenderer = GetComponent<SpriteRenderer>();
        parentMaterialTag = gameObject.tag;

        if (useCellularDebris)
        {
            cellularDebris = FindObjectOfType<CellularDebrisSimulation>();
            if (cellularDebris == null)
            {
                Debug.LogError("[DebrisSpawner] CELLULAR DEBRIS ENABLED BUT CellularDebrisSimulation NOT FOUND!");
            }
            else
            {
                Debug.Log($"[DebrisSpawner] Found CellularDebrisSimulation on {cellularDebris.gameObject.name}");
            }
        }
    }
    
    public void SpawnDebris(List<Vector2> cutOffShape, float totalArea, string materialTag = null)
    {
 
        if (cutOffShape == null || cutOffShape.Count < 3)
        {
            Debug.LogWarning("❌ [DebrisSpawner] Invalid cut-off shape!");
            return;
        }
        
        string debrisMaterialTag = string.IsNullOrEmpty(materialTag) ? parentMaterialTag : materialTag;

        if (useCellularDebris)
        {

            if (cellularDebris != null)
            {
                
                cellularDebris.SpawnDebrisInRegion(cutOffShape, totalArea, debrisMaterialTag, gameObject);
                return;
            }
        }
    }
}