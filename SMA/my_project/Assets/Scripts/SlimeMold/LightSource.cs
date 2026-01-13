using UnityEngine;

// Marks GameObject as light source that attracts slime mold
public class LightSource : MonoBehaviour
{
    [Range(0f, 1f)]
    public float attractionStrength = 1.0f;

    [Range(1f, 100f)]
    public float attractionRadius = 20f;

    public bool isActive = true;
    public bool showGizmo = true;
    public Color gizmoColor = new Color(1.0f, 0.9f, 0.2f, 0.3f);

    public Vector2 GetPosition()
    {
        return transform.position;
    }

    public float GetAttractionAt(Vector2 position)
    {
        if (!isActive) return 0f;

        float distance = Vector2.Distance(position, GetPosition());
        if (distance > attractionRadius) return 0f;

        float normalizedDistance = distance / attractionRadius;
        float falloff = 1f - normalizedDistance;

        return attractionStrength * falloff;
    }

    private void OnDrawGizmos()
    {
        if (!showGizmo) return;

        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(transform.position, attractionRadius);
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, gizmoColor.a * 2f);
        Gizmos.DrawSphere(transform.position, 0.5f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1.0f, 1.0f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, attractionRadius);
        Gizmos.DrawSphere(transform.position, 1.0f);
    }
}
