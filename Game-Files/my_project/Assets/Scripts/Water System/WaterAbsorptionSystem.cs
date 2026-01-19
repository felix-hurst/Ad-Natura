using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(CellularLiquidSimulation))]
public class WaterAbsorptionSystem : MonoBehaviour
{
    [Header("Absorption Settings")]
    [Tooltip("How often to check for absorption (seconds)")]
    [SerializeField] private float absorptionCheckInterval = 0.1f;
    
    [Tooltip("Enable/disable absorption globally")]
    [SerializeField] private bool enableAbsorption = true;
    
    [Header("Material Absorption Rates")]
    [Tooltip("Water cells absorbed per second for each tag")]
    [SerializeField] private List<MaterialAbsorption> materialAbsorptions = new List<MaterialAbsorption>()
    {
        new MaterialAbsorption("Sponge", 5.0f),
        new MaterialAbsorption("Cloth", 3.0f),
        new MaterialAbsorption("Wood", 1.0f),
        new MaterialAbsorption("Paper", 4.0f),
        new MaterialAbsorption("Dirt", 2.0f),
        new MaterialAbsorption("Sand", 1.5f),
    };
    
    [Header("Absorption Effects")]
    [Tooltip("Visual effect when water is absorbed")]
    [SerializeField] private bool showAbsorptionParticles = true;
    [SerializeField] private Color absorptionParticleColor = new Color(0.3f, 0.6f, 1f, 0.5f);
    
    [Header("Saturation")]
    [Tooltip("Enable saturation - objects stop absorbing when full")]
    [SerializeField] private bool enableSaturation = true;
    [Tooltip("Maximum water an object can absorb per unit area")]
    [SerializeField] private float saturationCapacity = 50f;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
    [SerializeField] private bool showAbsorptionGizmos = false;
    [SerializeField] private bool showDebugLines = true;
    [SerializeField] private bool showCellChecks = false;
    [SerializeField] private bool showAbsorptionRays = true;
    [SerializeField] private float debugLineDuration = 0.2f;
    
    private CellularLiquidSimulation liquidSim;
    private float absorptionTimer = 0f;

    private Dictionary<GameObject, float> objectSaturation = new Dictionary<GameObject, float>();

    private Dictionary<string, float> absorptionRateCache = new Dictionary<string, float>();
    
    [System.Serializable]
    public class MaterialAbsorption
    {
        public string tag;
        public float absorptionRate;
        
        public MaterialAbsorption(string tag, float rate)
        {
            this.tag = tag;
            this.absorptionRate = rate;
        }
    }
    
    void Start()
    {
        liquidSim = GetComponent<CellularLiquidSimulation>();
        
        if (liquidSim == null)
        {
            Debug.LogError("WaterAbsorptionSystem requires CellularLiquidSimulation on the same GameObject!");
            enabled = false;
            return;
        }

        BuildAbsorptionCache();
        
        Debug.Log($"Water Absorption System initialized with {materialAbsorptions.Count} material types");
    }
    
    void BuildAbsorptionCache()
    {
        absorptionRateCache.Clear();
        foreach (var mat in materialAbsorptions)
        {
            if (!string.IsNullOrEmpty(mat.tag))
            {
                absorptionRateCache[mat.tag] = mat.absorptionRate;
            }
        }
    }
    
    void Update()
    {
        if (!enableAbsorption) return;
        
        absorptionTimer += Time.deltaTime;
        
        if (absorptionTimer >= absorptionCheckInterval)
        {
            absorptionTimer = 0f;
            ProcessAbsorption();
        }
    }
    
    void ProcessAbsorption()
    {
        Collider2D[] allColliders = FindObjectsOfType<Collider2D>();
        
        int totalAbsorbed = 0;
        int objectsChecked = 0;
        int objectsWithAbsorption = 0;
        int objectsInWater = 0;
        
        foreach (Collider2D col in allColliders)
        {
            float absorptionRate = GetAbsorptionRate(col.gameObject);
            
            if (absorptionRate <= 0f)
            {
                if (showDebugInfo)
                {
                    Vector2Int gridPos = liquidSim.WorldToGrid(col.bounds.center);
                    if (liquidSim.IsValidCell(gridPos.x, gridPos.y) && liquidSim.GetWater(gridPos.x, gridPos.y) > 0f)
                    {
                        Debug.Log($"{col.gameObject.name} with tag '{col.tag}' is in water but has NO absorption rate defined!");
                    }
                }
                continue;
            }
            
            objectsChecked++;

            if (showDebugLines)
            {
                Bounds b = col.bounds;
                Debug.DrawLine(new Vector2(b.min.x, b.min.y), new Vector2(b.max.x, b.min.y), Color.yellow, debugLineDuration);
                Debug.DrawLine(new Vector2(b.max.x, b.min.y), new Vector2(b.max.x, b.max.y), Color.yellow, debugLineDuration);
                Debug.DrawLine(new Vector2(b.max.x, b.max.y), new Vector2(b.min.x, b.max.y), Color.yellow, debugLineDuration);
                Debug.DrawLine(new Vector2(b.min.x, b.max.y), new Vector2(b.min.x, b.min.y), Color.yellow, debugLineDuration);
            }

            if (enableSaturation && IsSaturated(col.gameObject))
            {
                if (showDebugInfo)
                    Debug.Log($"{col.gameObject.name} is saturated, skipping absorption");

                if (showDebugLines)
                {
                    Vector2 center = col.bounds.center;
                    float size = 0.2f;
                    Debug.DrawLine(center + new Vector2(-size, -size), center + new Vector2(size, size), Color.red, debugLineDuration);
                    Debug.DrawLine(center + new Vector2(-size, size), center + new Vector2(size, -size), Color.red, debugLineDuration);
                }
                continue;
            }
            int absorbed = AbsorbWaterFromCollider(col, absorptionRate);
            totalAbsorbed += absorbed;
            
            if (absorbed > 0)
            {
                objectsWithAbsorption++;
                objectsInWater++;
                
                if (showDebugInfo)
                {
                    Debug.Log($"{col.gameObject.name} ({col.tag}) absorbed {absorbed} water cells @ {absorptionRate} cells/s");
                }

                if (showDebugLines)
                {
                    Vector2 center = (Vector2)col.bounds.center + new Vector2(0, col.bounds.extents.y + 0.3f);
                    Debug.DrawLine(center + new Vector2(-0.1f, 0), center + new Vector2(-0.05f, -0.1f), Color.green, debugLineDuration);
                    Debug.DrawLine(center + new Vector2(-0.05f, -0.1f), center + new Vector2(0.1f, 0.1f), Color.green, debugLineDuration);
                }
            }
            else
            {
                if (showDebugInfo)
                {
                    Vector2Int gridPos = liquidSim.WorldToGrid(col.bounds.center);
                    if (liquidSim.IsValidCell(gridPos.x, gridPos.y))
                    {
                        float waterAtCenter = liquidSim.GetWater(gridPos.x, gridPos.y);
                        if (waterAtCenter > 0f)
                        {
                            Debug.Log($"🔍 {col.gameObject.name} ({col.tag}) has absorption but absorbed 0 cells (water at center: {waterAtCenter:F2})");
                        }
                    }
                }
            }
        }
        
        if (showDebugInfo)
        {
            if (totalAbsorbed > 0)
            {
                Debug.Log($"[Absorption] {totalAbsorbed} cells absorbed across {objectsWithAbsorption}/{objectsChecked} objects");
            }
            else if (objectsChecked > 0)
            {
                Debug.Log($"[Absorption] No water absorbed. {objectsChecked} objects check with absorption capability");
            }
        }
    }
    
    int AbsorbWaterFromCollider(Collider2D collider, float absorptionRate)
    {
        Bounds bounds = collider.bounds;

        if (showDebugLines)
        {
            Debug.DrawLine(new Vector2(bounds.min.x, bounds.min.y), new Vector2(bounds.max.x, bounds.min.y), Color.yellow, debugLineDuration);
            Debug.DrawLine(new Vector2(bounds.max.x, bounds.min.y), new Vector2(bounds.max.x, bounds.max.y), Color.yellow, debugLineDuration);
            Debug.DrawLine(new Vector2(bounds.max.x, bounds.max.y), new Vector2(bounds.min.x, bounds.max.y), Color.yellow, debugLineDuration);
            Debug.DrawLine(new Vector2(bounds.min.x, bounds.max.y), new Vector2(bounds.min.x, bounds.min.y), Color.yellow, debugLineDuration);
        }

        float waterToAbsorb = absorptionRate * absorptionCheckInterval;

        if (showDebugInfo)
        {
            Vector2Int centerGrid = liquidSim.WorldToGrid(bounds.center);
            float waterAtCenter = liquidSim.IsValidCell(centerGrid.x, centerGrid.y) ? liquidSim.GetWater(centerGrid.x, centerGrid.y) : 0f;
            Debug.Log($"Checking {collider.gameObject.name} - Bounds: {bounds.min} to {bounds.max}, Water at center: {waterAtCenter:F3}, Rate: {absorptionRate} cells/s");
        }

        List<Vector2Int> waterCells = FindWaterCellsInBounds(bounds, collider);
        
        if (waterCells.Count == 0)
        {
            if (showDebugInfo)
            {
                Debug.Log($"No water cells found for {collider.gameObject.name}");
            }
            return 0;
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"Found {waterCells.Count} water cells overlapping {collider.gameObject.name}");
        }

        if (showDebugLines)
        {
            Vector2 center = bounds.center;
            foreach (Vector2Int cell in waterCells)
            {
                Vector2 cellWorldPos = liquidSim.GridToWorld(cell.x, cell.y);
                Debug.DrawLine(center, cellWorldPos, Color.cyan, debugLineDuration);
            }
        }

        int cellsAbsorbed = 0;
        float totalAbsorbed = 0f;
        float waterPerCell = waterToAbsorb / waterCells.Count;
        
        foreach (Vector2Int cell in waterCells)
        {
            float waterInCell = liquidSim.GetWater(cell.x, cell.y);
            
            if (waterInCell > 0f)
            {
                float absorbAmount = Mathf.Min(waterPerCell, waterInCell);

                if (enableSaturation)
                {
                    float currentSaturation = GetSaturation(collider.gameObject);
                    float remainingCapacity = GetMaxSaturation(collider.gameObject) - currentSaturation;
                    absorbAmount = Mathf.Min(absorbAmount, remainingCapacity);
                    
                    if (showDebugInfo && absorbAmount < waterPerCell)
                    {
                        Debug.Log($"Saturation limiting absorption: {currentSaturation:F1}/{GetMaxSaturation(collider.gameObject):F1}");
                    }
                }
                
                if (absorbAmount > 0f)
                {
                    liquidSim.SetWater(cell.x, cell.y, waterInCell - absorbAmount);
                    AddSaturation(collider.gameObject, absorbAmount);
                    totalAbsorbed += absorbAmount;
                    cellsAbsorbed++;

                    if (showAbsorptionRays)
                    {
                        Vector2 worldPos = liquidSim.GridToWorld(cell.x, cell.y);
                        Debug.DrawLine(worldPos, bounds.center, Color.green, debugLineDuration);
                        Debug.DrawLine(worldPos + Vector2.left * 0.05f, worldPos + Vector2.right * 0.05f, Color.green, debugLineDuration);
                        Debug.DrawLine(worldPos + Vector2.up * 0.05f, worldPos + Vector2.down * 0.05f, Color.green, debugLineDuration);
                    }

                    if (showAbsorptionParticles)
                    {
                        Vector2 worldPos = liquidSim.GridToWorld(cell.x, cell.y);
                        ShowAbsorptionEffect(worldPos);
                    }
                }
            }
        }
        
        if (showDebugInfo && cellsAbsorbed > 0)
        {
            Debug.Log($"Absorbed {totalAbsorbed:F2} water from {cellsAbsorbed} cells");
        }

        if (showDebugLines && cellsAbsorbed > 0)
        {
            Debug.DrawRay(bounds.center, Vector2.up * 0.3f, Color.magenta, debugLineDuration);
        }
        
        return cellsAbsorbed;
    }
    
    List<Vector2Int> FindWaterCellsInBounds(Bounds bounds, Collider2D collider)
    {
        List<Vector2Int> waterCells = new List<Vector2Int>();

        Vector2Int minGrid = liquidSim.WorldToGrid(bounds.min);
        Vector2Int maxGrid = liquidSim.WorldToGrid(bounds.max);

        minGrid.x -= 2;
        minGrid.y -= 2;
        maxGrid.x += 2;
        maxGrid.y += 2;

        if (showDebugLines && showCellChecks)
        {
            Vector2 gridMin = liquidSim.GridToWorld(minGrid.x, minGrid.y);
            Vector2 gridMax = liquidSim.GridToWorld(maxGrid.x, maxGrid.y);
            Debug.DrawLine(gridMin, new Vector2(gridMax.x, gridMin.y), Color.gray, debugLineDuration);
            Debug.DrawLine(new Vector2(gridMax.x, gridMin.y), gridMax, Color.gray, debugLineDuration);
            Debug.DrawLine(gridMax, new Vector2(gridMin.x, gridMax.y), Color.gray, debugLineDuration);
            Debug.DrawLine(new Vector2(gridMin.x, gridMax.y), gridMin, Color.gray, debugLineDuration);
        }

        for (int x = minGrid.x; x <= maxGrid.x; x++)
        {
            for (int y = minGrid.y; y <= maxGrid.y; y++)
            {
                if (!liquidSim.IsValidCell(x, y))
                    continue;

                float waterAmount = liquidSim.GetWater(x, y);
                if (waterAmount <= 0f)
                    continue;

                Vector2 worldPos = liquidSim.GridToWorld(x, y);

                Vector2 nearestPoint = collider.ClosestPoint(worldPos);
                float distance = Vector2.Distance(worldPos, nearestPoint);

                float absorptionRange = liquidSim.GridToWorld(1, 0).x - liquidSim.GridToWorld(0, 0).x;
                bool isNearSurface = distance <= absorptionRange * 1.5f;

                if (showDebugLines && showCellChecks)
                {
                    Color checkColor = isNearSurface ? Color.blue : Color.red;
                    Debug.DrawLine(worldPos + Vector2.left * 0.02f, worldPos + Vector2.right * 0.02f, checkColor, debugLineDuration);
                    Debug.DrawLine(worldPos + Vector2.up * 0.02f, worldPos + Vector2.down * 0.02f, checkColor, debugLineDuration);
                }
                
                if (isNearSurface)
                {
                    waterCells.Add(new Vector2Int(x, y));

                    if (showDebugLines)
                    {
                        Debug.DrawLine(worldPos + new Vector2(-0.03f, -0.03f), worldPos + new Vector2(0.03f, 0.03f), Color.cyan, debugLineDuration);
                        Debug.DrawLine(worldPos + new Vector2(-0.03f, 0.03f), worldPos + new Vector2(0.03f, -0.03f), Color.cyan, debugLineDuration);
                        Debug.DrawLine(worldPos, nearestPoint, Color.magenta, debugLineDuration);
                    }
                }
            }
        }
        
        return waterCells;
    }
    
    float GetAbsorptionRate(GameObject obj)
    {
        if (absorptionRateCache.ContainsKey(obj.tag))
        {
            return absorptionRateCache[obj.tag];
        }
        return 0f;
    }
    
    float GetSaturation(GameObject obj)
    {
        if (objectSaturation.ContainsKey(obj))
        {
            return objectSaturation[obj];
        }
        return 0f;
    }
    
    void AddSaturation(GameObject obj, float amount)
    {
        if (!objectSaturation.ContainsKey(obj))
        {
            objectSaturation[obj] = 0f;
        }
        objectSaturation[obj] += amount;
    }
    
    float GetMaxSaturation(GameObject obj)
    {
        Collider2D col = obj.GetComponent<Collider2D>();
        if (col != null)
        {
            float area = col.bounds.size.x * col.bounds.size.y;
            return area * saturationCapacity;
        }
        return saturationCapacity;
    }
    
    bool IsSaturated(GameObject obj)
    {
        return GetSaturation(obj) >= GetMaxSaturation(obj);
    }

    public void ResetSaturation(GameObject obj)
    {
        if (objectSaturation.ContainsKey(obj))
        {
            objectSaturation.Remove(obj);
        }
    }

    [ContextMenu("Reset All Saturation")]
    public void ResetAllSaturation()
    {
        objectSaturation.Clear();
        Debug.Log("Reset all object saturation levels");
    }

    [ContextMenu("List All Objects and Tags")]
    public void ListAllObjectsAndTags()
    {
        Collider2D[] allColliders = FindObjectsOfType<Collider2D>();
        
        Debug.Log("=== ALL OBJECTS IN SCENE ===");
        
        int withAbsorption = 0;
        int withoutAbsorption = 0;
        
        foreach (Collider2D col in allColliders)
        {
            float rate = GetAbsorptionRate(col.gameObject);
            
            if (rate > 0f)
            {
                withAbsorption++;
            }
            else
            {
                withoutAbsorption++;
            }
        }

    }

    public void AddMaterialAbsorption(string tag, float rate)
    {
        for (int i = 0; i < materialAbsorptions.Count; i++)
        {
            if (materialAbsorptions[i].tag == tag)
            {
                materialAbsorptions[i].absorptionRate = rate;
                BuildAbsorptionCache();
                return;
            }
        }

        materialAbsorptions.Add(new MaterialAbsorption(tag, rate));
        BuildAbsorptionCache();
    }

    public void RemoveMaterialAbsorption(string tag)
    {
        materialAbsorptions.RemoveAll(m => m.tag == tag);
        BuildAbsorptionCache();
    }

    public string GetSaturationInfo(GameObject obj)
    {
        float current = GetSaturation(obj);
        float max = GetMaxSaturation(obj);
        float percentage = (current / max) * 100f;
        return $"{current:F1}/{max:F1} ({percentage:F0}%)";
    }
    
    void ShowAbsorptionEffect(Vector2 position)
    {
        if (showDebugLines)
        {
            Debug.DrawLine(position, position + Vector2.up * 0.15f, absorptionParticleColor, debugLineDuration * 2f);
            Debug.DrawLine(position + Vector2.up * 0.15f, position + new Vector2(-0.03f, 0.12f), absorptionParticleColor, debugLineDuration * 2f);
            Debug.DrawLine(position + Vector2.up * 0.15f, position + new Vector2(0.03f, 0.12f), absorptionParticleColor, debugLineDuration * 2f);
        }
    }
    
    void OnDrawGizmos()
    {
        if (!showAbsorptionGizmos || !Application.isPlaying)
            return;

        foreach (var kvp in objectSaturation)
        {
            if (kvp.Key == null) continue;
            
            Collider2D col = kvp.Key.GetComponent<Collider2D>();
            if (col == null) continue;
            
            float saturationPercent = kvp.Value / GetMaxSaturation(kvp.Key);

            Gizmos.color = Color.Lerp(
                new Color(0.3f, 0.6f, 1f, 0.3f),
                new Color(0.1f, 0.2f, 0.4f, 0.6f),
                saturationPercent
            );
            
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);

            Vector3 barPos = col.bounds.center + Vector3.up * (col.bounds.size.y * 0.5f + 0.2f);
            float barWidth = col.bounds.size.x;
            float filledWidth = barWidth * saturationPercent;
            
            Gizmos.color = Color.gray;
            Gizmos.DrawLine(barPos - Vector3.right * barWidth * 0.5f, barPos + Vector3.right * barWidth * 0.5f);
            
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(barPos - Vector3.right * barWidth * 0.5f, barPos - Vector3.right * barWidth * 0.5f + Vector3.right * filledWidth);
        }
    }
    
    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying)
            return;
        
        GUILayout.BeginArea(new Rect(10, 200, 300, 400));
        GUILayout.Label("=== Water Absorption Debug ===");
        GUILayout.Label($"Absorption: {(enableAbsorption ? "ON" : "OFF")}");
        GUILayout.Label($"Saturated Objects: {objectSaturation.Count}");
        
        GUILayout.Space(10);
        GUILayout.Label("Material Rates:");
        foreach (var mat in materialAbsorptions)
        {
            GUILayout.Label($"  {mat.tag}: {mat.absorptionRate:F1} cells/s");
        }
        
        if (objectSaturation.Count > 0)
        {
            GUILayout.Space(10);
            GUILayout.Label("Object Saturation:");
            foreach (var kvp in objectSaturation)
            {
                if (kvp.Key != null)
                {
                    GUILayout.Label($"  {kvp.Key.name}: {GetSaturationInfo(kvp.Key)}");
                }
            }
        }
        
        GUILayout.EndArea();
    }
}