using UnityEngine;

public class LightFlicker : MonoBehaviour
{
    [SerializeField] private bool useFixedInterval = true;
    [SerializeField] private float fixedFlickerInterval = 3f;
    [Tooltip("Increase when the scene runs at high fps and vice versa.")]
    [SerializeField] private float randomFlickerInterval = 500f;
    [SerializeField] private GameObject lightSource;
    private float timer = 0f;
    private float randomNumber;
    private SpriteRenderer spriteRenderer;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        if (useFixedInterval)
        {
            // Fixed interval system
            timer += Time.deltaTime;
            if (timer > fixedFlickerInterval)
            {
                FlipState();
                timer = 0f;
            }
        }
        else
        {
            // Random chance each frame that it hides/unhides itself
            randomNumber = Random.Range(0.0f, randomFlickerInterval);
            if (randomNumber <= 1.0f)
            {
                FlipState();
            }
        }
    }

    private void FlipState()
    {
        spriteRenderer.enabled = !spriteRenderer.enabled;
        lightSource.SetActive(!lightSource.activeSelf);
    }
}
