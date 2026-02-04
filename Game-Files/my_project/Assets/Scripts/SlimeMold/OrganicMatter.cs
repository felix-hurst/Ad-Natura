using Unity.VisualScripting.ReorderableList.Element_Adder_Menu;
using UnityEngine;
using UnityEngine.Events;

public class OrganicMatter : MonoBehaviour
{
    /*
     * decompositionRate (float) - how fast it breaks down (i.e. # seconds until fully destroyed)
     * maxHealth / currentHealth (float) - health until destroyed
     * TakeDecompositionDamage(float amount) method - reduces health
     * UnityEvent onDecomposed - fires when health hits 0, then destroy the object
     * Green gizmo in editor so you can see which objects have it
     * To test: Add to any object, call TakeDecompositionDamage(50) repeatedly in Play mode.
     */

    [SerializeField] private float decompositionRate = 50f;
    [SerializeField] private float maxHealth = 500f;
    private float currentHealth;
    public UnityEvent onDecomposed;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentHealth = maxHealth;
    }

    // Update is called once per frame
    void Update()
    {
        if (currentHealth <= 0)
        {
            // Starts the decomposition process
            onDecomposed.Invoke();
        }
    }

    [ContextMenu("Decomp Damage")]
    void DebugTakeDecompositionDamage()
    {
        TakeDecompositionDamage(50);
    }

    public void TakeDecompositionDamage(float amount)
    {
        currentHealth -= amount;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y, transform.position.z), 0.5f);
    }
}
