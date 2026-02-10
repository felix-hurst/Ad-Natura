using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Marks an object as organic matter that can be decomposed by slime mold.
/// Attach to wood, plants, or other organic objects.
/// </summary>
public class OrganicMatter : MonoBehaviour
{
    [Header("Health")]
    [Tooltip("Maximum health before fully decomposed")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Slime Overlay")]
    [Tooltip("Enable slime texture overlay as object decomposes")]
    [SerializeField] private bool enableSlimeOverlay = true;
    [Tooltip("Color of the slime overlay")]
    [SerializeField] private Color slimeColor = new Color(0.9f, 0.85f, 0.2f, 1f);
    [Tooltip("How much damage before overlay starts showing (0-1)")]
    [Range(0f, 0.5f)]
    [SerializeField] private float overlayStartThreshold = 0.1f;
    [Tooltip("Sorting order offset for overlay (relative to main sprite)")]
    [SerializeField] private int overlaySortingOffset = 1;

    [Header("Decomposition Effects")]
    [Tooltip("Try to trigger DebrisSpawner component when decomposed")]
    [SerializeField] private bool triggerDebrisSpawner = true;
    [Tooltip("Delay before destroying object (allows debris/effects to spawn)")]
    [SerializeField] private float destroyDelay = 0.1f;

    [Header("Events")]
    [Tooltip("Called when fully decomposed (health reaches 0)")]
    public UnityEvent onDecomposed;

    [Header("Debug")]
    [SerializeField] private bool showHealthBar = true;

    private float currentHealth;
    private bool isDecomposed;
    private SpriteRenderer overlayRenderer;
    private SpriteRenderer mainRenderer;
    private float damagePercent;

    void Start()
    {
        currentHealth = maxHealth;

        if (enableSlimeOverlay)
        {
            CreateSlimeOverlay();
        }
    }

    void CreateSlimeOverlay()
    {
        mainRenderer = GetComponent<SpriteRenderer>();
        if (mainRenderer == null) return;

        // Create overlay child object
        GameObject overlayObj = new GameObject("SlimeOverlay");
        overlayObj.transform.SetParent(transform, false);
        overlayObj.transform.localPosition = Vector3.zero;
        overlayObj.transform.localRotation = Quaternion.identity;
        overlayObj.transform.localScale = Vector3.one;

        // Add sprite renderer with same sprite
        overlayRenderer = overlayObj.AddComponent<SpriteRenderer>();
        overlayRenderer.sprite = mainRenderer.sprite;
        overlayRenderer.sortingLayerID = mainRenderer.sortingLayerID;
        overlayRenderer.sortingOrder = mainRenderer.sortingOrder + overlaySortingOffset;

        // Start fully transparent
        Color c = slimeColor;
        c.a = 0f;
        overlayRenderer.color = c;
    }

    public void TakeDecompositionDamage(float amount)
    {
        if (isDecomposed) return;

        currentHealth -= amount;
        damagePercent = 1f - (currentHealth / maxHealth);

        // Update slime overlay
        UpdateSlimeOverlay();

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            isDecomposed = true;

            // Try to trigger debris spawner
            if (triggerDebrisSpawner)
            {
                TriggerDebris();
            }

            onDecomposed?.Invoke();

            // Delay destroy so debris/effects can spawn
            Destroy(gameObject, destroyDelay);
        }
    }

    void UpdateSlimeOverlay()
    {
        if (overlayRenderer == null) return;

        // Calculate overlay alpha based on damage
        // Starts showing after overlayStartThreshold damage
        float overlayAlpha = 0f;
        if (damagePercent > overlayStartThreshold)
        {
            // Remap from threshold->1 to 0->1
            overlayAlpha = Mathf.InverseLerp(overlayStartThreshold, 1f, damagePercent);
            // Cap at ~0.85 so the wood is still somewhat visible
            overlayAlpha = Mathf.Min(overlayAlpha, 0.85f);
        }

        Color c = slimeColor;
        c.a = overlayAlpha;
        overlayRenderer.color = c;
    }

    public float GetHealthPercent() => currentHealth / maxHealth;

    void TriggerDebris()
    {
        // Try to find DebrisSpawner and trigger decomposition debris
        var debrisSpawner = GetComponent<DebrisSpawner>();
        if (debrisSpawner != null)
        {
            debrisSpawner.SpawnDecompositionDebris();
        }
    }

    [ContextMenu("Debug: Take 50 Damage")]
    void DebugTakeDecompositionDamage()
    {
        TakeDecompositionDamage(50);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.3f);

        if (showHealthBar && Application.isPlaying)
        {
            float healthPercent = GetHealthPercent();
            Gizmos.color = Color.Lerp(Color.red, Color.green, healthPercent);
            Gizmos.DrawCube(transform.position + Vector3.up * 0.5f, new Vector3(healthPercent, 0.1f, 0.1f));
        }
    }
}
