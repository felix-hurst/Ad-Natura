using UnityEngine;

public class LightFlicker : MonoBehaviour
{
    [Tooltip("Increase when the scene runs at high fps and vice versa.")]
    [SerializeField] private float flickerInterval = 500f;
    [SerializeField] private GameObject lightSource;
    private float randomNumber;
    private SpriteRenderer spriteRenderer;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        // Random chance each frame that it hides/unhides itself
        randomNumber = Random.Range(0.0f, flickerInterval);
        if (randomNumber <= 1.0f)
        {
            spriteRenderer.enabled = !spriteRenderer.enabled;
            lightSource.SetActive(!lightSource.activeSelf);
        }
    }
}
