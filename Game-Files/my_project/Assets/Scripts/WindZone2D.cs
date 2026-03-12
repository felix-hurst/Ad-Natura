using UnityEngine;

public class WindZone2D : MonoBehaviour
{
    [Header("Zone Shape")]
    [SerializeField] private ZoneShape shape = ZoneShape.Circle;

    public enum ZoneShape
    {
        Circle,
        Rectangle
    }

    [Header("Circle Settings")]
    [SerializeField] private float radius = 5f;

    [Header("Rectangle Settings")]
    [SerializeField] private Vector2 size = new Vector2(10f, 10f);

    [Header("Wind Properties")]
    [SerializeField] private Vector2 windDirection = Vector2.right;
    [SerializeField] private float windStrength = 5f;

    [Header("Wind Behavior")]
    [SerializeField] private WindBehavior behavior = WindBehavior.Constant;

    public enum WindBehavior
    {
        Constant,
        Vortex,
        Radial,
        Suction,
        Turbulent
    }

    [Header("Vortex Settings")]
    [SerializeField] private float vortexRotationSpeed = 2f;
    [SerializeField] private bool clockwise = true;

    [Header("Falloff")]
    [Tooltip("Wind strength decreases near edges")]
    [SerializeField] private bool useFalloff = true;
    [SerializeField] private AnimationCurve falloffCurve = AnimationCurve.Linear(0, 1, 1, 0);

    [Header("Turbulence")]
    [SerializeField] private bool addTurbulence = false;
    [Range(0f, 1f)]
    [SerializeField] private float turbulenceAmount = 0.3f;
    [SerializeField] private float turbulenceFrequency = 2f;

    [Header("Visual Feedback")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private Color gizmoColor = new Color(0.5f, 1f, 1f, 0.3f);
    [SerializeField] private GameObject particleEffectPrefab;

    private GameObject particleEffect;
    private float turbulenceTimer = 0f;

    void Start()
    {
        if (particleEffectPrefab != null)
        {
            particleEffect = Instantiate(particleEffectPrefab, transform.position, Quaternion.identity, transform);
        }
    }

    void Update()
    {
        turbulenceTimer += Time.deltaTime * turbulenceFrequency;
    }

    public bool IsPositionInZone(Vector2 position)
    {
        Vector2 localPos = transform.InverseTransformPoint(position);

        switch (shape)
        {
            case ZoneShape.Circle:
                return localPos.magnitude <= radius;

            case ZoneShape.Rectangle:
                return Mathf.Abs(localPos.x) <= size.x * 0.5f &&
                       Mathf.Abs(localPos.y) <= size.y * 0.5f;

            default:
                return false;
        }
    }

    public Vector2 GetWindVelocity(Vector2 position)
    {
        if (!IsPositionInZone(position))
        {
            return Vector2.zero;
        }

        Vector2 baseWind = CalculateWindByBehavior(position);

        if (useFalloff)
        {
            float distance = GetNormalizedDistance(position);
            float falloff = falloffCurve.Evaluate(distance);
            baseWind *= falloff;
        }

        if (addTurbulence)
        {
            Vector2 turbulence = GetTurbulence(position);
            baseWind += turbulence * turbulenceAmount;
        }

        return baseWind;
    }

    public Vector2 GetWindVelocity()
    {
        return GetWindVelocity(transform.position);
    }

    Vector2 CalculateWindByBehavior(Vector2 position)
    {
        Vector2 localPos = transform.InverseTransformPoint(position);

        switch (behavior)
        {
            case WindBehavior.Constant:
                return transform.TransformDirection(windDirection.normalized) * windStrength;

            case WindBehavior.Vortex:
                return CalculateVortexWind(localPos);

            case WindBehavior.Radial:
                Vector2 radialDir = (position - (Vector2)transform.position).normalized;
                return radialDir * windStrength;

            case WindBehavior.Suction:
                Vector2 suctionDir = ((Vector2)transform.position - position).normalized;
                return suctionDir * windStrength;

            case WindBehavior.Turbulent:
                return CalculateTurbulentWind(position);

            default:
                return windDirection.normalized * windStrength;
        }
    }

    Vector2 CalculateVortexWind(Vector2 localPos)
    {
        float distance = localPos.magnitude;
        if (distance < 0.01f) return Vector2.zero;

        Vector2 tangent = new Vector2(-localPos.y, localPos.x).normalized;

        if (!clockwise)
        {
            tangent = -tangent;
        }

        float speedMultiplier = Mathf.Min(distance / (radius * 0.5f), 1f);

        Vector2 inwardPull = -localPos.normalized * windStrength * 0.3f;
        Vector2 tangentialWind = tangent * windStrength * vortexRotationSpeed * speedMultiplier;

        return transform.TransformDirection(tangentialWind + inwardPull);
    }

    Vector2 CalculateTurbulentWind(Vector2 position)
    {
        float noiseX = Mathf.PerlinNoise(position.x * 0.5f + turbulenceTimer, position.y * 0.5f);
        float noiseY = Mathf.PerlinNoise(position.x * 0.5f, position.y * 0.5f + turbulenceTimer);

        Vector2 noiseDir = new Vector2(noiseX - 0.5f, noiseY - 0.5f).normalized;

        return noiseDir * windStrength;
    }

    Vector2 GetTurbulence(Vector2 position)
    {
        float noiseX = Mathf.PerlinNoise(position.x + turbulenceTimer, position.y) - 0.5f;
        float noiseY = Mathf.PerlinNoise(position.x, position.y + turbulenceTimer) - 0.5f;

        return new Vector2(noiseX, noiseY) * windStrength;
    }

    float GetNormalizedDistance(Vector2 position)
    {
        Vector2 localPos = transform.InverseTransformPoint(position);

        switch (shape)
        {
            case ZoneShape.Circle:
                return localPos.magnitude / radius;

            case ZoneShape.Rectangle:
                float distX = Mathf.Abs(localPos.x) / (size.x * 0.5f);
                float distY = Mathf.Abs(localPos.y) / (size.y * 0.5f);
                return Mathf.Max(distX, distY);

            default:
                return 0f;
        }
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;

        Gizmos.color = gizmoColor;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        switch (shape)
        {
            case ZoneShape.Circle:
                DrawCircleGizmo();
                break;

            case ZoneShape.Rectangle:
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0));
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, gizmoColor.a * 0.3f);
                Gizmos.DrawCube(Vector3.zero, new Vector3(size.x, size.y, 0));
                break;
        }

        Gizmos.matrix = oldMatrix;

        DrawWindArrows();
    }

    void DrawCircleGizmo()
    {
        int segments = 32;
        Vector3 prevPoint = Vector3.right * radius;

        for (int i = 1; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 point = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
            Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
        }

        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, gizmoColor.a * 0.3f);
        Gizmos.DrawSphere(Vector3.zero, radius);
    }

    void DrawWindArrows()
    {
        Gizmos.color = Color.yellow;

        for (int i = 0; i < 5; i++)
        {
            Vector2 samplePos = GetRandomPointInZone();
            Vector2 wind = GetWindVelocity(samplePos);

            if (wind.magnitude > 0.1f)
            {
                Vector3 start = samplePos;
                Vector3 end = start + (Vector3)wind.normalized * 0.5f;

                Gizmos.DrawLine(start, end);

                Vector3 dir = (end - start).normalized;
                Vector3 right = new Vector3(-dir.y, dir.x, 0);

                Gizmos.DrawLine(end, end - dir * 0.2f + right * 0.1f);
                Gizmos.DrawLine(end, end - dir * 0.2f - right * 0.1f);
            }
        }
    }

    Vector2 GetRandomPointInZone()
    {
        switch (shape)
        {
            case ZoneShape.Circle:
                Vector2 randomDir = Random.insideUnitCircle * radius;
                return (Vector2)transform.position + (Vector2)transform.TransformDirection(randomDir);

            case ZoneShape.Rectangle:
                Vector2 randomPoint = new Vector2(
                    Random.Range(-size.x * 0.5f, size.x * 0.5f),
                    Random.Range(-size.y * 0.5f, size.y * 0.5f)
                );
                return (Vector2)transform.position + (Vector2)transform.TransformDirection(randomPoint);

            default:
                return transform.position;
        }
    }
}