using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class WindAffected : MonoBehaviour
{
    [Header("Wind Response")]
    [SerializeField] private bool affectedByWind = true;
    [Tooltip("Multiplier for wind force (0 = immune, 1 = normal, >1 = more affected)")]
    [Range(0f, 5f)]
    [SerializeField] private float windMultiplier = 1f;

    [Header("Physical Properties")]
    [Tooltip("How aerodynamic the object is (0.5 = streamlined, 2.0 = flat surface)")]
    [Range(0.1f, 3f)]
    [SerializeField] private float initDragCoefficient = 1f;

    [Tooltip("Override automatic surface area calculation")]
    [SerializeField] private bool overrideSurfaceArea = false;
    [SerializeField] private float customSurfaceArea = 1f;

    [Header("Mass-Based Scaling")]
    [Tooltip("Lighter objects are affected more by wind")]
    [SerializeField] private bool initScaleWithMass = true;
    [SerializeField] private float initMassScalingFactor = 1f;
    [Tooltip("Reference mass - objects lighter than this are affected more")]
    [SerializeField] private float initReferenceMass = 1f;

    [Header("Rotation from Wind")]
    [Tooltip("Apply torque from wind (makes objects tumble)")]
    [SerializeField] private bool initApplyWindTorque = true;
    [Range(0f, 5f)]
    [SerializeField] private float initTorqueMultiplier = 1f;

    [Header("Terminal Velocity")]
    [Tooltip("Limit maximum wind-induced velocity")]
    [SerializeField] private bool initLimitVelocity = true;
    [SerializeField] private float initMaxWindVelocity = 10f;

    public bool ScaleWithMass { get => initScaleWithMass; set => initScaleWithMass = value; }
    public float MassScalingFactor { get => initMassScalingFactor; set => initMassScalingFactor = value; }
    public float ReferenceMass { get => initReferenceMass; set => initReferenceMass = value; }
    public bool ApplyWindTorque { get => initApplyWindTorque; set => initApplyWindTorque = value; }
    public float TorqueMultiplier { get => initTorqueMultiplier; set => initTorqueMultiplier = value; }
    public bool LimitVelocity { get => initLimitVelocity; set => initLimitVelocity = value; }
    public float MaxWindVelocity { get => initMaxWindVelocity; set => initMaxWindVelocity = value; }
    public float DragCoefficient { get => initDragCoefficient; set => initDragCoefficient = value; }

    private Rigidbody2D rb;
    private Collider2D col;
    private float calculatedSurfaceArea;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        if (col != null)
        {
            Bounds bounds = col.bounds;
            calculatedSurfaceArea = Mathf.Max(bounds.size.x, bounds.size.y);
        }
        else
        {
            calculatedSurfaceArea = 1f;
        }
    }

    void FixedUpdate()
    {
        if (!affectedByWind || rb == null) return;

        if (initApplyWindTorque)
        {
            ApplyWindTorquePhysics();
        }

        if (initLimitVelocity)
        {
            ClampVelocity();
        }
    }

    void ApplyWindTorquePhysics()
    {
        if (RealisticWindManager.Instance == null) return;

        Vector2 windVelocity = RealisticWindManager.Instance.GetWindAtPosition(transform.position);
        Vector2 windDirection = windVelocity.normalized;

        Vector2 objectUp = transform.up;
        float crossProduct = Vector2.Dot(Vector2.Perpendicular(objectUp), windDirection);

        float windStrength = windVelocity.magnitude;
        float torque = crossProduct * windStrength * initTorqueMultiplier * GetSurfaceArea();

        rb.AddTorque(torque, ForceMode2D.Force);
    }

    void ClampVelocity()
    {
        if (rb.linearVelocity.magnitude > initMaxWindVelocity)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * initMaxWindVelocity;
        }
    }

    public bool IsAffectedByWind()
    {
        return affectedByWind;
    }

    public float GetDragCoefficient()
    {
        return initDragCoefficient;
    }

    public float GetSurfaceArea()
    {
        return overrideSurfaceArea ? customSurfaceArea : calculatedSurfaceArea;
    }

    public float GetWindMultiplier()
    {
        float finalMultiplier = windMultiplier;

        if (initScaleWithMass && rb != null)
        {
            float massRatio = initReferenceMass / Mathf.Max(rb.mass, 0.1f);
            finalMultiplier *= Mathf.Pow(massRatio, initMassScalingFactor);
        }

        return finalMultiplier;
    }

    public void SetAffectedByWind(bool affected)
    {
        affectedByWind = affected;
    }

    public void SetWindMultiplier(float multiplier)
    {
        windMultiplier = Mathf.Max(0f, multiplier);
    }
}