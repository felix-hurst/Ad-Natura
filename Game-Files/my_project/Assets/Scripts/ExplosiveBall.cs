using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ExplosiveBall : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private float ballRadius = 0.3f;
    [SerializeField] private Color ballColor = Color.red;
    
    [Header("Lifetime")]
    [SerializeField] private float maxLifetime = 10f;
    
    [Header("Explosion Settings")]
    [SerializeField] private int minRayCount = 4;
    [SerializeField] private int maxRayCount = 9;
    [SerializeField] private float explosionRayDistance = 5f;
    [SerializeField] private float minAngle = 0f;
    [SerializeField] private float maxAngle = 360f;
    
    [Header("Structural Weakness Settings")]
    [SerializeField] private bool enableStructuralWeakness = true;
    [SerializeField] private float weaknessDelay = 2f;
    [SerializeField] private int minFractureCount = 3;
    [SerializeField] private int maxFractureCount = 7;
    [SerializeField] private float fractureRayDistance = 10f;
    [SerializeField] private bool showFractureWarning = true;
    [SerializeField] private float warningDuration = 0.5f;
    
    [Header("Exclusions")]
    [SerializeField] private LayerMask excludedLayers;
    [SerializeField] private List<string> excludedTags = new List<string> { "Player", "Debris", "Fragment" };
    
    [Header("Visual Feedback")]
    [SerializeField] private bool showExplosionRays = true;
    [SerializeField] private float rayVisualizationDuration = 0.5f;
    [SerializeField] private Color explosionRayColor = Color.yellow;
    [SerializeField] private Color fractureRayColor = Color.red;
    [SerializeField] private Color warningColor = Color.red;
    
    private float lifetime = 0f;
    private bool hasExploded = false;
    private List<GameObject> weaknessTargets = new List<GameObject>();
    
    void Start()
    {
        CreateVisual();
    }
    
    void Update()
    {
        lifetime += Time.deltaTime;
        
        if (lifetime >= maxLifetime)
        {
            Destroy(gameObject);
        }
    }
    
    void CreateVisual()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            sr = gameObject.AddComponent<SpriteRenderer>();
            
            Texture2D texture = CreateCircleTexture(64);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 32f / ballRadius);
            
            sr.sprite = sprite;
            sr.color = ballColor;
            sr.sortingOrder = 10;
        }
        
        transform.localScale = Vector3.one * (ballRadius * 2f);
        
        CircleCollider2D collider = GetComponent<CircleCollider2D>();
        if (collider == null)
        {
            collider = gameObject.AddComponent<CircleCollider2D>();
        }
        
        collider.radius = 0.5f;
    }
    
    Texture2D CreateCircleTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];
        
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                pixels[y * size + x] = distance <= radius ? Color.white : Color.clear;
            }
        }
        
        texture.SetPixels(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Bilinear;
        
        return texture;
    }
    
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasExploded) return;
        
        if (ShouldExcludeObject(collision.gameObject))
        {
            return;
        }

        GameObject hitObject = collision.gameObject;

        StructuralCollapseManager.Instance.ScheduleDelayedExplosion(
            hitObject,
            transform.position,
            weaknessDelay,
            minRayCount,
            maxRayCount,
            explosionRayDistance,
            minAngle,
            maxAngle,
            showExplosionRays,
            rayVisualizationDuration,
            explosionRayColor,
            showFractureWarning,
            warningDuration,
            warningColor);
        
        hasExploded = true;
        Destroy(gameObject);
    }
    
    bool ShouldExcludeObject(GameObject obj)
    {
        if (((1 << obj.layer) & excludedLayers) != 0)
        {
            return true;
        }
        
        foreach (string tag in excludedTags)
        {
            if (obj.CompareTag(tag))
            {
                return true;
            }
        }
        
        if (obj.name.Contains("Debris") || obj.name.Contains("Fragment"))
        {
            return true;
        }
        
        return false;
    }


    Bounds GetObjectBounds(GameObject obj)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            return renderer.bounds;
        }
        
        renderer = obj.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            return renderer.bounds;

        }
        
        Collider2D collider = obj.GetComponent<Collider2D>();
        if (collider != null)
        {
            return collider.bounds;
        }
        
        return new Bounds(obj.transform.position, Vector3.one);
    }
}