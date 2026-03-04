using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class CalamitySpawnTrigger : MonoBehaviour
{
    [Tooltip("The spawner to activate")]
    [SerializeField] private CalamityObjectSpawner spawner;

    [Tooltip("Tag of the object that triggers spawning (e.g. 'Player')")]
    [SerializeField] private string triggerTag = "Player";

    [Tooltip("Only trigger once, then disable")]
    [SerializeField] private bool oneShot = true;

    [Tooltip("Optional delay before spawning starts")]
    [SerializeField] private float triggerDelay = 0f;

    [Tooltip("Specific zone index to spawn in (-1 = all zones)")]
    [SerializeField] private int targetZoneIndex = -1;

    private bool hasTriggered = false;

    void Start()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (hasTriggered && oneShot) return;
        if (!other.CompareTag(triggerTag)) return;
        if (spawner == null) return;

        hasTriggered = true;

        if (triggerDelay > 0)
        {
            StartCoroutine(DelayedSpawn());
        }
        else
        {
            ExecuteSpawn();
        }
    }

    System.Collections.IEnumerator DelayedSpawn()
    {
        yield return new WaitForSeconds(triggerDelay);
        ExecuteSpawn();
    }

    void ExecuteSpawn()
    {
        if (targetZoneIndex >= 0)
        {
            spawner.SpawnInZone(targetZoneIndex);
        }
        else
        {
            spawner.TriggerSpawn();
        }
    }
}

public class CalamityCutProfileRegistrar : MonoBehaviour
{
    [Header("Calamity Cut Profile")]
    [Tooltip("How smooth the cut edges are (0 = jagged crystalline, 1 = smooth organic)")]
    [Range(0f, 1f)] public float softness = 0.2f;

    [Tooltip("How much extra material is cut away (0 = clean, 1 = messy)")]
    [Range(0f, 1f)] public float strength = 0.4f;

    void Start()
    {
        CutProfileManager manager = FindObjectOfType<CutProfileManager>();
        if (manager == null)
        {
            Debug.LogWarning("[CalamityCutProfile] No CutProfileManager found in scene. " +
                           "Calamity objects will use the default cut profile.");
            return;
        }

        Debug.Log($"[CalamityCutProfile] Calamity cut profile registered: " +
                  $"softness={softness}, strength={strength}. " +
                  $"Add to your cut_profiles.json for persistence.");
    }
}