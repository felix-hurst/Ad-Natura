using UnityEngine;

/// <summary>
/// Marks GameObject as water source that attracts slime mold.
/// Attach to any object where the player places water.
/// </summary>
public class WaterSource : MonoBehaviour
{
    public enum FalloffType { Linear, Quadratic, Smooth }

    [Header("Attraction")]
    [Tooltip("How strongly this source attracts slime (0-1)")]
    [Range(0f, 1f)]
    public float attractionStrength = 0.8f;

    [Tooltip("Radius of attraction effect in world units")]
    [Range(1f, 50f)]
    public float attractionRadius = 8f;

    [Tooltip("Falloff curve: Linear (sharp), Quadratic (natural), Smooth (gradual)")]
    public FalloffType falloffType = FalloffType.Quadratic;

    [Tooltip("Is this water source currently active?")]
    public bool isActive = true;

    public Vector2 GetPosition() => transform.position;

    public float GetAttractionAt(Vector2 position)
    {
        if (!isActive) return 0f;

        float distance = Vector2.Distance(position, GetPosition());
        if (distance > attractionRadius) return 0f;

        float t = distance / attractionRadius;
        float falloff;

        switch (falloffType)
        {
            case FalloffType.Quadratic: falloff = 1f - (t * t); break;
            case FalloffType.Smooth: falloff = 1f - (t * t * (3f - 2f * t)); break;
            default: falloff = 1f - t; break;
        }

        return attractionStrength * falloff;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = isActive ? new Color(0.2f, 0.6f, 1.0f, 0.4f) : new Color(0.5f, 0.5f, 0.5f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, attractionRadius);
        Gizmos.DrawSphere(transform.position, 0.2f);
    }
}
