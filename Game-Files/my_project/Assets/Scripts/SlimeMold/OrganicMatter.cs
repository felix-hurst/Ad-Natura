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

    [Header("Events")]
    [Tooltip("Called when fully decomposed (health reaches 0)")]
    public UnityEvent onDecomposed;

    [Header("Debug")]
    [SerializeField] private bool showHealthBar = true;

    [Header("Structural Collapse Settings")]
    [SerializeField] private float weaknessDelay = 1.5f;
    [SerializeField] private int minRayCount = 10;
    [SerializeField] private int maxRayCount = 15;
    [SerializeField] private float explosionRayDistance = 4f;
    [SerializeField] private float minAngle = 0f;
    [SerializeField] private float maxAngle = 360f;
    [SerializeField] private bool showExplosionRays = true;
    [SerializeField] private float rayVisualizationDuration = 0.4f;
    [SerializeField] private Color explosionRayColor = new Color(1f, 0.4f, 0.1f);
    [SerializeField] private bool showFractureWarning = true;
    [SerializeField] private float warningDuration = 0.4f;
    [SerializeField] private Color warningColor = new Color(1f, 0.3f, 0.1f);

    [Header("Collapse Cleanup")]
    [Tooltip("Destroy the original parent object after all cuts are done")]
    [SerializeField] private bool destroyParentAfterCollapse = true;
    [Tooltip("Delay before destroying the parent (seconds after last cut round)")]
    [SerializeField] private float parentDestroyDelay = 0.5f;
    [Tooltip("Destroy all spawned fragment pieces after collapse")]
    [SerializeField] private bool destroyFragmentsAfterCollapse = true;
    [Tooltip("Delay before destroying fragments (seconds after parent destroy)")]
    [SerializeField] private float fragmentDestroyDelay = 4f;

    private float currentHealth;
    private bool isDecomposed;
    private SpriteRenderer overlayRenderer;
    private SpriteRenderer mainRenderer;
    private float damagePercent;

    [SerializeField] private Sprite climbableWallSprite;

    void Start()
    {
        currentHealth = maxHealth;

        if (enableSlimeOverlay)
            CreateSlimeOverlay();
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

        float previousHealth = currentHealth;
        currentHealth -= amount;
        damagePercent = 1f - (currentHealth / maxHealth);

        Debug.Log($"[Decomp Step 4 - HEALTH] '{name}': health {previousHealth:F1} → {currentHealth:F1} / {maxHealth} ({damagePercent * 100:F1}% damaged)");
        // Update slime overlay
        UpdateSlimeOverlay();

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            isDecomposed = true;
            // Split by if statement on whether this is Wood or Wall
            if (CompareTag("Wood") || CompareTag("Rock"))
            {
                if (overlayRenderer != null) { Destroy(overlayRenderer.gameObject); overlayRenderer = null; }

                var sr = GetComponent<SpriteRenderer>();
                if (sr != null) sr.enabled = false;

                if (triggerDebrisSpawner)
                    TriggerDebris(useDelay: false); // sprite is hidden, start cuts immediately

                onDecomposed?.Invoke();
                enabled = false;
            }
            else // Wall
            {
                Debug.Log($"[Decomp Step 5 - COLLAPSE] '{name}': fully decomposed (Wall). Converting to Climbable.");
                gameObject.layer = LayerMask.NameToLayer("Climbable");
                if (overlayRenderer != null)
                    overlayRenderer.color = new Color(0f, 0f, 0f, 0f);
                var sr = GetComponent<SpriteRenderer>();
                if (sr != null && climbableWallSprite != null)
                    sr.sprite = climbableWallSprite;
            }
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

    void TriggerDebris(bool useDelay = true)
    {
        if (StructuralCollapseManager.Instance == null)
        {
            Debug.LogError($"[TriggerDebris] '{name}': StructuralCollapseManager.Instance is null!");
            return;
        }

        var receiver = GetComponent<RaycastReceiver>();
        if (receiver == null)
        {
            Debug.LogError($"[TriggerDebris] '{name}': Missing RaycastReceiver — cuts will silently fail!");
            return;
        }


        float delay = useDelay ? weaknessDelay : 0f;

        bool doWarning = useDelay && showFractureWarning;

        StructuralCollapseManager.Instance.ScheduleDelayedExplosion(
            gameObject,
            new Vector2(transform.position.x, transform.position.y),
            delay,
            minRayCount,
            maxRayCount,
            explosionRayDistance,
            minAngle,
            maxAngle,
            showExplosionRays,
            rayVisualizationDuration,
            explosionRayColor,
            doWarning,
            warningDuration,
            warningColor,
            destroyParentAfterCollapse,
            parentDestroyDelay,
            destroyFragmentsAfterCollapse,
            fragmentDestroyDelay
        );
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