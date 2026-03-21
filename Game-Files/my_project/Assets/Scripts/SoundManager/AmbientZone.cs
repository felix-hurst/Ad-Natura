using UnityEngine;

/// <summary>
/// Place this on any GameObject with a 2D trigger collider.
/// When the player enters, the ambient sound transitions to this zone's clip.
/// When the player exits (and no other zone is active), ambient fades out.
/// </summary>
public class AmbientZone : MonoBehaviour
{
    [Header("Sound")]
    [Tooltip("Filename of the clip in Assets/Resources/Sounds/ (no extension). E.g. 'Birds' or 'BeachWaves'")]
    [SerializeField] private string clipName;

    [Header("Zone")]
    [Tooltip("Tag of the object that triggers this zone.")]
    [SerializeField] private string playerTag = "Player";

    [Header("Debug")]
    [SerializeField] private Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.25f);

    // Track how many players are inside (supports edge cases with multiple colliders)
    private int overlapCount = 0;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        overlapCount++;
        AmbientZoneManager.Instance?.PlayAmbient(clipName);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        overlapCount = Mathf.Max(0, overlapCount - 1);

        if (overlapCount == 0)
        {
            AmbientZoneManager.Instance?.StopAmbient();
        }
    }

    // Draw a coloured box in the editor so you can see zones at a glance
    private void OnDrawGizmos()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return;

        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);

        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);

#if UNITY_EDITOR
        UnityEditor.Handles.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
        UnityEditor.Handles.Label(col.bounds.center, $"♪ {clipName}");
#endif
    }
}