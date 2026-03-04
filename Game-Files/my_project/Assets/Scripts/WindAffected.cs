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
    [SerializeField] private float _dragCoefficient = 1f;
    
    [Tooltip("Override automatic surface area calculation")]
    [SerializeField] private bool overrideSurfaceArea = false;
    [SerializeField] private float customSurfaceArea = 1f;
    
    [Header("Mass-Based Scaling")]
    [Tooltip("Lighter objects are affected more by wind")]
    [SerializeField] private bool _scaleWithMass = true;
    [SerializeField] private float _massScalingFactor = 1f;
    [Tooltip("Reference mass - objects lighter than this are affected more")]
    [SerializeField] private float _referenceMass = 1f;
    
    [Header("Rotation from Wind")]
    [Tooltip("Apply torque from wind (makes objects tumble)")]
    [SerializeField] private bool _applyWindTorque = true;
    [Range(0f, 5f)]
    [SerializeField] private float _torqueMultiplier = 1f;
    
    [Header("Terminal Velocity")]
    [Tooltip("Limit maximum wind-induced velocity")]
    [SerializeField] private bool _limitVelocity = true;
    [SerializeField] private float _maxWindVelocity = 10f;
    
    public bool scaleWithMass { get => _scaleWithMass; set => _scaleWithMass = value; }
    public float massScalingFactor { get => _massScalingFactor; set => _massScalingFactor = value; }
    public float referenceMass { get => _referenceMass; set => _referenceMass = value; }
    public bool applyWindTorque { get => _applyWindTorque; set => _applyWindTorque = value; }
    public float torqueMultiplier { get => _torqueMultiplier; set => _torqueMultiplier = value; }
    public bool limitVelocity { get => _limitVelocity; set => _limitVelocity = value; }
    public float maxWindVelocity { get => _maxWindVelocity; set => _maxWindVelocity = value; }
    public float dragCoefficient { get => _dragCoefficient; set => _dragCoefficient = value; }
    
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
        
        if (_applyWindTorque)
        {
            ApplyWindTorque();
        }
        
        if (_limitVelocity)
        {
            ClampVelocity();
        }
    }
    
    void ApplyWindTorque()
    {
        if (RealisticWindManager.Instance == null) return;
        
        Vector2 windVelocity = RealisticWindManager.Instance.GetWindAtPosition(transform.position);
        Vector2 windDirection = windVelocity.normalized;
        
        Vector2 objectUp = transform.up;
        float crossProduct = Vector2.Dot(Vector2.Perpendicular(objectUp), windDirection);
        
        float windStrength = windVelocity.magnitude;
        float torque = crossProduct * windStrength * _torqueMultiplier * GetSurfaceArea();
        
        rb.AddTorque(torque, ForceMode2D.Force);
    }
    
    void ClampVelocity()
    {
        if (rb.linearVelocity.magnitude > _maxWindVelocity)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * _maxWindVelocity;
        }
    }
    
    public bool IsAffectedByWind()
    {
        return affectedByWind;
    }
    
    public float GetDragCoefficient()
    {
        return _dragCoefficient;
    }
    
    public float GetSurfaceArea()
    {
        return overrideSurfaceArea ? customSurfaceArea : calculatedSurfaceArea;
    }
    
    public float GetWindMultiplier()
    {
        float finalMultiplier = windMultiplier;
        
        if (_scaleWithMass && rb != null)
        {
            float massRatio = _referenceMass / Mathf.Max(rb.mass, 0.1f);
            finalMultiplier *= Mathf.Pow(massRatio, _massScalingFactor);
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