using UnityEngine;
using System.Collections.Generic;

public class RaycastReceiver : MonoBehaviour
{
    [Header("Highlight Settings")]
    [Tooltip("Choose which piece to highlight after the cut")]
    public HighlightMode highlightMode = HighlightMode.Default;
    
    public enum HighlightMode
    {
        Default,              // Always highlights the top piece
        ClosestToGround,      // Highlights the lower piece
        FarthestFromGround    // Highlights the upper piece
    }
    
    [Header("Large Piece Settings")]
    [Tooltip("Base ratio for large piece. Material strength modulates this. (0.4 = 40% large piece, 60% debris at strength 1.0)")]
    [Range(0.1f, 0.49f)]
    public float baseLargePieceRatio = 0.4f;
    
    [Tooltip("Mass multiplier for large cut pieces")]
    public float largePieceMassMultiplier = 0.5f;
    
    [Tooltip("Force range applied to large cut pieces")]
    public Vector2 largePieceForceRange = new Vector2(1f, 3f);
    
    private LineRenderer edgeLineRenderer;
    private SpriteRenderer spriteRenderer;
    private List<Vector2> currentHighlightedShape;
    
    // References to the other components
    private ObjectReshape objectReshape;
    private DebrisSpawner debrisSpawner;
    
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        
        // Get or add ObjectReshape component
        objectReshape = GetComponent<ObjectReshape>();
        if (objectReshape == null)
        {
            objectReshape = gameObject.AddComponent<ObjectReshape>();
        }
        
        // Get or add DebrisSpawner component
        debrisSpawner = GetComponent<DebrisSpawner>();
        if (debrisSpawner == null)
        {
            debrisSpawner = gameObject.AddComponent<DebrisSpawner>();
        }
    }
    
    public void HighlightCutEdges(Vector2 entryPoint, Vector2 exitPoint)
    {
        // Clear previous highlights
        ClearHighlight();
        
        // Get the current shape vertices (could be polygon from previous cuts)
        Vector2[] corners = GetCurrentShapeVertices();
        
        // Find which edges are intersected by the cut line
        List<Vector2> newShape1 = new List<Vector2>();
        List<Vector2> newShape2 = new List<Vector2>();
        
        // Add entry and exit points
        newShape1.Add(entryPoint);
        newShape1.Add(exitPoint);
        newShape2.Add(entryPoint);
        newShape2.Add(exitPoint);
        
        // Check each vertex of the current shape
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 corner = corners[i];
            
            // Determine which side of the cut line this corner is on
            float side = GetSideOfLine(entryPoint, exitPoint, corner);
            
            if (side > 0)
            {
                newShape1.Add(corner);
            }
            else
            {
                newShape2.Add(corner);
            }
        }
        
        // Sort vertices to form proper polygons
        newShape1 = SortVerticesClockwise(newShape1);
        newShape2 = SortVerticesClockwise(newShape2);
        
        // Determine which shape to highlight based on mode
        currentHighlightedShape = ChooseShapeToHighlight(newShape1, newShape2);
        
        // Draw the outline for the chosen shape
        DrawShapeOutline(currentHighlightedShape);
        
        Debug.Log($"Cut {gameObject.name} - Highlighting piece with {currentHighlightedShape.Count} vertices (Mode: {highlightMode})");
    }
    
    /// <summary>
    /// Gets the current shape vertices - either from polygon collider or sprite bounds
    /// </summary>
    Vector2[] GetCurrentShapeVertices()
    {
        // First, try to get vertices from the polygon collider (if object was already cut)
        PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
        if (polyCol != null && polyCol.points.Length > 0)
        {
            // Convert local points to world space
            Vector2[] worldVertices = new Vector2[polyCol.points.Length];
            for (int i = 0; i < polyCol.points.Length; i++)
            {
                worldVertices[i] = transform.TransformPoint(polyCol.points[i]);
            }
            return worldVertices;
        }
        
        // Otherwise, fall back to the rectangular bounds
        return GetWorldCorners();
    }
    
    public void ExecuteCut(Vector2 entryPoint, Vector2 exitPoint)
    {
        if (currentHighlightedShape == null || currentHighlightedShape.Count < 3)
        {
            Debug.LogWarning("No valid highlighted shape to cut!");
            return;
        }
        
        // Calculate the area of the highlighted shape (TOTAL cut-off area)
        float totalCutOffArea = ObjectReshape.CalculatePolygonArea(currentHighlightedShape);
        
        Debug.Log($"Executing cut - Total cut-off area: {totalCutOffArea:F2}");
        
        // Get the material tag for this object
        string materialTag = gameObject.tag;
        if (string.IsNullOrEmpty(materialTag) || materialTag == "Untagged")
        {
            materialTag = gameObject.name;
        }
        
        Debug.Log($"Using material tag: {materialTag}");
        
        // Get the cut profile to determine the split ratio
        CutProfile cutProfile = CutProfileExtensions.GetCutProfileForObject(gameObject);
        
        // Calculate the actual large piece ratio based on material strength
        // strength = 0: large piece gets baseLargePieceRatio (e.g., 40%)
        // strength = 1: large piece gets baseLargePieceRatio (e.g., 40%)
        // The strength affects how irregular the cut is, but the base ratio determines the split
        // We'll make materials with higher strength lean more towards debris
        
        // More aggressive formula: higher strength = less large piece
        // strength 0.0: largePieceRatio = 0.8 (80% large, 20% debris)
        // strength 0.5: largePieceRatio = 0.6 (60% large, 40% debris)
        // strength 1.0: largePieceRatio = 0.4 (40% large, 60% debris)
        float strengthInfluence = 1.0f - (cutProfile.strength * 0.4f); // Range: 1.0 to 0.6
        float largePieceRatio = baseLargePieceRatio + (strengthInfluence - 0.6f) * (0.8f - baseLargePieceRatio) / 0.4f;
        largePieceRatio = Mathf.Clamp(largePieceRatio, 0.3f, 0.8f);
        
        // Calculate areas - THESE MUST SUM TO totalCutOffArea
        float largePieceArea = totalCutOffArea * largePieceRatio;
        float debrisArea = totalCutOffArea - largePieceArea; // This ensures they sum correctly!
        
        Debug.Log($"Cut profile strength: {cutProfile.strength:F2} | Large piece ratio: {largePieceRatio:F2} | Large piece area: {largePieceArea:F2} | Debris area: {debrisArea:F2} | Sum: {(largePieceArea + debrisArea):F2}");
        
        // Use ObjectReshape to cut off the portion and get the cut shape
        List<Vector2> cutOffShape = objectReshape.CutOffPortion(entryPoint, exitPoint, currentHighlightedShape);
        
        if (cutOffShape != null && cutOffShape.Count >= 3)
        {
            // ==================== WATER INTEGRATION ====================
            // Check if this object contains water and spawn it (Noita-style cellular)
            CellularWaterContainer cellularWaterContainer = GetComponent<CellularWaterContainer>();
            if (cellularWaterContainer != null)
            {
                cellularWaterContainer.OnObjectCut(entryPoint, exitPoint, cutOffShape, totalCutOffArea);
            }
            // ===========================================================
            
            // Spawn debris fragments (only if there's meaningful debris area)
            if (debrisArea > 0.01f)
            {
                debrisSpawner.SpawnDebris(cutOffShape, debrisArea, materialTag);
            }
            
            // Spawn the larger remaining piece (only if there's meaningful large piece area)
            if (largePieceArea > 0.01f)
            {
                SpawnLargeCutPiece(cutOffShape, largePieceArea, entryPoint, exitPoint, materialTag, cutProfile);
            }
        }
        else
        {
            Debug.LogWarning("Failed to get valid cut-off shape from ObjectReshape");
        }
    }
    
    /// <summary>
    /// Spawns a larger piece representing the non-debris portion of the cut
    /// </summary>
    void SpawnLargeCutPiece(List<Vector2> cutOffShape, float targetArea, Vector2 entryPoint, Vector2 exitPoint, string materialTag, CutProfile cutProfile)
    {
        // Create a new GameObject for the large piece
        GameObject largePiece = new GameObject($"{gameObject.name}_CutPiece");
        largePiece.tag = materialTag;
        
        // Calculate centroid for positioning
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 v in cutOffShape)
        {
            centroid += v;
        }
        centroid /= cutOffShape.Count;
        largePiece.transform.position = new Vector3(centroid.x, centroid.y, transform.position.z);
        
        // Add required components
        SpriteRenderer originalSpriteRenderer = GetComponent<SpriteRenderer>();
        SpriteRenderer pieceSpriteRenderer = largePiece.AddComponent<SpriteRenderer>();
        
        // Copy sprite renderer settings
        if (originalSpriteRenderer != null)
        {
            pieceSpriteRenderer.sortingLayerName = originalSpriteRenderer.sortingLayerName;
            pieceSpriteRenderer.sortingOrder = originalSpriteRenderer.sortingOrder;
            pieceSpriteRenderer.color = originalSpriteRenderer.color;
        }
        
        // Add ObjectReshape component to handle the shape
        ObjectReshape pieceReshape = largePiece.AddComponent<ObjectReshape>();
        
        // Add PixelatedCutRenderer
        PixelatedCutRenderer piecePixelRenderer = largePiece.AddComponent<PixelatedCutRenderer>();
        
        // Convert world space vertices to local space for the new object
        List<Vector2> localVertices = new List<Vector2>();
        foreach (Vector2 worldVertex in cutOffShape)
        {
            Vector2 localVertex = (Vector2)largePiece.transform.InverseTransformPoint(worldVertex);
            localVertices.Add(localVertex);
        }
        
        // Apply the shape (this will create the mesh and collider)
        CutProfileManager profileManager = FindObjectOfType<CutProfileManager>();
        List<Vector2> irregularShape = localVertices;
        if (profileManager != null && cutProfile.strength > 0.01f)
        {
            // Convert entry/exit points to local space
            Vector2 localEntry = largePiece.transform.InverseTransformPoint(entryPoint);
            Vector2 localExit = largePiece.transform.InverseTransformPoint(exitPoint);
            irregularShape = profileManager.ApplyIrregularCut(localVertices, localEntry, localExit, cutProfile);
        }
        
        // Apply pixelation to the shape
        List<Vector2> pixelatedShape = irregularShape;
        if (piecePixelRenderer != null)
        {
            Vector2 localEntry = largePiece.transform.InverseTransformPoint(entryPoint);
            Vector2 localExit = largePiece.transform.InverseTransformPoint(exitPoint);
            pixelatedShape = piecePixelRenderer.PixelatePolygonWithCutLine(irregularShape, localEntry, localExit);
        }
        
        // Create collider
        PolygonCollider2D polyCollider = largePiece.AddComponent<PolygonCollider2D>();
        polyCollider.points = irregularShape.ToArray();
        
        // Create visual mesh
        CreateLargePieceMesh(largePiece, pixelatedShape, materialTag);
        
        // Add physics
        Rigidbody2D rb = largePiece.AddComponent<Rigidbody2D>();
        rb.mass = targetArea * largePieceMassMultiplier;
        rb.gravityScale = 1f;
        
        // Apply a small impulse away from the parent object
        Vector2 cutDirection = (exitPoint - entryPoint).normalized;
        Vector2 perpendicular = new Vector2(-cutDirection.y, cutDirection.x);
        rb.linearVelocity = perpendicular * Random.Range(largePieceForceRange.x, largePieceForceRange.y);
        rb.angularVelocity = Random.Range(-90f, 90f);
        
        // Add RaycastReceiver so it can be cut again
        RaycastReceiver pieceReceiver = largePiece.AddComponent<RaycastReceiver>();
        pieceReceiver.highlightMode = this.highlightMode;
        pieceReceiver.baseLargePieceRatio = this.baseLargePieceRatio;
        pieceReceiver.largePieceMassMultiplier = this.largePieceMassMultiplier;
        pieceReceiver.largePieceForceRange = this.largePieceForceRange;
        
        // Add DebrisSpawner for future cuts
        DebrisSpawner pieceDebrisSpawner = largePiece.AddComponent<DebrisSpawner>();
        
        // Copy CellularWaterContainer if parent has one (so cut pieces also contain water!)
        CellularWaterContainer parentCellularWaterContainer = GetComponent<CellularWaterContainer>();
        if (parentCellularWaterContainer != null)
        {
            CellularWaterContainer pieceCellularWaterContainer = largePiece.AddComponent<CellularWaterContainer>();
            // Water container will use its default settings
        }
        
        // Apply physics material based on material tag
        PhysicsMaterialManager physicsManager = FindObjectOfType<PhysicsMaterialManager>();
        if (physicsManager != null)
        {
            physicsManager.ApplyPhysicsMaterial(largePiece);
        }
        
        Debug.Log($"Spawned large cut piece with area {targetArea:F2} at position {centroid}");
    }
    
    /// <summary>
    /// Creates a visual mesh for the large cut piece
    /// </summary>
    void CreateLargePieceMesh(GameObject piece, List<Vector2> localVertices, string materialTag)
    {
        if (localVertices == null || localVertices.Count < 3)
        {
            Debug.LogError("Invalid vertices for large piece mesh creation");
            return;
        }
        
        // Create mesh child object
        GameObject meshObject = new GameObject($"{piece.name}_Mesh");
        meshObject.transform.SetParent(piece.transform);
        meshObject.transform.localPosition = Vector3.zero;
        meshObject.transform.localRotation = Quaternion.identity;
        meshObject.transform.localScale = Vector3.one;
        
        MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
        
        // Get texture
        MaterialTextureGenerator textureGenerator = FindObjectOfType<MaterialTextureGenerator>();
        Texture2D texture = null;
        
        if (textureGenerator != null && !string.IsNullOrEmpty(materialTag))
        {
            texture = textureGenerator.GetTexture(materialTag);
        }
        
        // Create material
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        
        Material material = new Material(shader);
        if (texture != null)
        {
            material.mainTexture = texture;
        }
        
        // Copy renderer settings from parent
        SpriteRenderer parentSpriteRenderer = GetComponent<SpriteRenderer>();
        if (parentSpriteRenderer != null)
        {
            meshRenderer.sortingLayerName = parentSpriteRenderer.sortingLayerName;
            meshRenderer.sortingOrder = parentSpriteRenderer.sortingOrder;
            if (texture == null)
            {
                material.color = parentSpriteRenderer.color;
            }
        }
        
        meshRenderer.material = material;
        
        // Create mesh
        Mesh mesh = CreateMeshFromPolygon(localVertices);
        if (mesh != null)
        {
            meshFilter.mesh = mesh;
            Debug.Log($"Created mesh for large piece with {localVertices.Count} vertices");
        }
        else
        {
            Debug.LogError("Failed to create mesh for large piece");
            Destroy(meshObject);
        }
    }
    
    /// <summary>
    /// Creates a mesh from polygon vertices using triangulation
    /// </summary>
    Mesh CreateMeshFromPolygon(List<Vector2> vertices)
    {
        if (vertices == null || vertices.Count < 3)
        {
            Debug.LogError("Not enough vertices to create mesh");
            return null;
        }
        
        Mesh mesh = new Mesh();
        mesh.name = "LargePieceMesh";
        
        // Convert 2D vertices to 3D
        Vector3[] vertices3D = new Vector3[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices3D[i] = new Vector3(vertices[i].x, vertices[i].y, 0);
        }
        
        // Simple fan triangulation
        List<int> triangles = new List<int>();
        for (int i = 1; i < vertices.Count - 1; i++)
        {
            triangles.Add(0);
            triangles.Add(i);
            triangles.Add(i + 1);
        }
        
        // Create UVs
        Vector2[] uvs = new Vector2[vertices.Count];
        Bounds bounds = GetLocalBoundsFromVertices(vertices);
        
        if (bounds.size.x > 0 && bounds.size.y > 0)
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                uvs[i] = new Vector2(
                    (vertices[i].x - bounds.min.x) / bounds.size.x,
                    (vertices[i].y - bounds.min.y) / bounds.size.y
                );
            }
        }
        else
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                uvs[i] = new Vector2(0.5f, 0.5f);
            }
        }
        
        mesh.vertices = vertices3D;
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    /// <summary>
    /// Gets local bounds of vertices
    /// </summary>
    Bounds GetLocalBoundsFromVertices(List<Vector2> vertices)
    {
        if (vertices.Count == 0) return new Bounds();
        
        Vector2 min = vertices[0];
        Vector2 max = vertices[0];
        
        foreach (Vector2 v in vertices)
        {
            min.x = Mathf.Min(min.x, v.x);
            min.y = Mathf.Min(min.y, v.y);
            max.x = Mathf.Max(max.x, v.x);
            max.y = Mathf.Max(max.y, v.y);
        }
        
        Vector2 center = (min + max) / 2f;
        Vector2 size = max - min;
        
        return new Bounds(center, size);
    }
    
    List<Vector2> ChooseShapeToHighlight(List<Vector2> shape1, List<Vector2> shape2)
    {
        // Calculate average Y position (height) of each shape
        float avgY1 = 0;
        foreach (Vector2 v in shape1)
        {
            avgY1 += v.y;
        }
        avgY1 /= shape1.Count;
        
        float avgY2 = 0;
        foreach (Vector2 v in shape2)
        {
            avgY2 += v.y;
        }
        avgY2 /= shape2.Count;
        
        // Choose based on highlight mode
        switch (highlightMode)
        {
            case HighlightMode.Default:
            case HighlightMode.FarthestFromGround:
                // Return the shape with higher average Y (top piece)
                return avgY1 > avgY2 ? shape1 : shape2;
                
            case HighlightMode.ClosestToGround:
                // Return the shape with lower average Y (bottom piece)
                return avgY1 < avgY2 ? shape1 : shape2;
                
            default:
                return avgY1 > avgY2 ? shape1 : shape2;
        }
    }
    
    Vector2[] GetWorldCorners()
    {
        Bounds bounds;
        
        // First check if we have an ObjectReshape component with a mesh renderer
        ObjectReshape objectReshape = GetComponent<ObjectReshape>();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        
        // Check for mesh renderer on child (from previous cuts)
        if (meshRenderer == null)
        {
            meshRenderer = GetComponentInChildren<MeshRenderer>();
        }
        
        // Check if we have a mesh from a previous cut
        if (meshRenderer != null && meshRenderer.enabled)
        {
            bounds = meshRenderer.bounds;
        }
        else if (spriteRenderer != null)
        {
            bounds = spriteRenderer.bounds;
        }
        else
        {
            // If we have a polygon collider, use that
            PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
            if (polyCol != null && polyCol.points.Length > 0)
            {
                // Get bounds from the polygon collider
                Vector2 minPoint = transform.TransformPoint(polyCol.points[0]);
                Vector2 maxPoint = minPoint;
                
                foreach (Vector2 localPoint in polyCol.points)
                {
                    Vector2 worldPoint = transform.TransformPoint(localPoint);
                    minPoint.x = Mathf.Min(minPoint.x, worldPoint.x);
                    minPoint.y = Mathf.Min(minPoint.y, worldPoint.y);
                    maxPoint.x = Mathf.Max(maxPoint.x, worldPoint.x);
                    maxPoint.y = Mathf.Max(maxPoint.y, worldPoint.y);
                }
                
                Vector2 boundsCenter = (minPoint + maxPoint) / 2f;
                Vector2 boundsSize = maxPoint - minPoint;
                bounds = new Bounds(boundsCenter, boundsSize);
            }
            else
            {
                // Fallback to transform-based bounds
                bounds = new Bounds(transform.position, Vector3.one);
            }
        }
        
        Vector2 center = bounds.center;
        Vector2 extents = bounds.extents;
        
        return new Vector2[]
        {
            new Vector2(center.x - extents.x, center.y - extents.y), // Bottom-left
            new Vector2(center.x + extents.x, center.y - extents.y), // Bottom-right
            new Vector2(center.x + extents.x, center.y + extents.y), // Top-right
            new Vector2(center.x - extents.x, center.y + extents.y)  // Top-left
        };
    }
    
    float GetSideOfLine(Vector2 lineStart, Vector2 lineEnd, Vector2 point)
    {
        // Cross product to determine which side of the line the point is on
        return (lineEnd.x - lineStart.x) * (point.y - lineStart.y) - 
               (lineEnd.y - lineStart.y) * (point.x - lineStart.x);
    }
    
    List<Vector2> SortVerticesClockwise(List<Vector2> vertices)
    {
        // Find centroid
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 v in vertices)
        {
            centroid += v;
        }
        centroid /= vertices.Count;
        
        // Sort by angle from centroid
        vertices.Sort((a, b) =>
        {
            float angleA = Mathf.Atan2(a.y - centroid.y, a.x - centroid.x);
            float angleB = Mathf.Atan2(b.y - centroid.y, b.x - centroid.x);
            return angleA.CompareTo(angleB);
        });
        
        return vertices;
    }
    
    void DrawShapeOutline(List<Vector2> vertices)
    {
        if (vertices.Count < 2) return;
        
        // Create a new LineRenderer if needed
        if (edgeLineRenderer == null)
        {
            GameObject lineObj = new GameObject($"{gameObject.name}_Outline");
            lineObj.transform.SetParent(transform);
            
            edgeLineRenderer = lineObj.AddComponent<LineRenderer>();
            edgeLineRenderer.startWidth = 0.08f;
            edgeLineRenderer.endWidth = 0.08f;
            edgeLineRenderer.material = new Material(Shader.Find("Unlit/Color"));
            edgeLineRenderer.material.color = Color.red;
            edgeLineRenderer.startColor = Color.red;
            edgeLineRenderer.endColor = Color.red;
            edgeLineRenderer.sortingOrder = 15;
            edgeLineRenderer.useWorldSpace = true;
            edgeLineRenderer.loop = true; // Make it a closed loop
        }
        
        // Set positions
        edgeLineRenderer.positionCount = vertices.Count;
        for (int i = 0; i < vertices.Count; i++)
        {
            edgeLineRenderer.SetPosition(i, new Vector3(vertices[i].x, vertices[i].y, 0));
        }
    }
    
    public void ClearHighlight()
    {
        if (edgeLineRenderer != null)
        {
            Destroy(edgeLineRenderer.gameObject);
            edgeLineRenderer = null;
        }
        
        currentHighlightedShape = null;
    }
    
    void OnDestroy()
    {
        ClearHighlight();
    }
}