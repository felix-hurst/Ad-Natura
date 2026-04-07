using UnityEngine;

public class LightSource : MonoBehaviour
{
    public enum LightShape { Circle, Rectangle }

    [Header("Settings")]
    public LightShape shape = LightShape.Circle;
    public bool isActive = true;

    [Tooltip("How far the slime 'feels' the light and starts fleeing")]
    public float fearRadius = 5f;

    [Tooltip("For Rectangle shape: width and height")]
    public Vector2 rectSize = new Vector2(5f, 3f);

    [Header("Repulsion Logic")]
    [Tooltip("How far away the 'Ghost Point' is created. Should be larger than sensor length.")]
    public float ghostPointOffset = 10f;

    [Tooltip("How strongly agents are attracted to the shadow (0-1)")]
    [Range(0f, 50f)]
    public float repulsionStrength = 2f;

    public Vector2 GetPosition() => transform.position;

    void OnDrawGizmos()
    {
        if (!isActive) return;
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        if (shape == LightShape.Circle)
            Gizmos.DrawWireSphere(transform.position, fearRadius);
        else
            Gizmos.DrawWireCube(transform.position, new Vector3(rectSize.x, rectSize.y, 0));

        Gizmos.DrawIcon(transform.position, "Light Gizmo", true);
    }
}