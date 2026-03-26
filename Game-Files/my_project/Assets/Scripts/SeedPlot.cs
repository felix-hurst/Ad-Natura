using UnityEngine;
using UnityEngine.InputSystem;

public class SeedPlot : MonoBehaviour
{
    [Header("Manager Object")]
    [SerializeField] private PlantSeedsObjective manager;

    [Header("Interaction")]
    [SerializeField] private float interactRange = 0.5f;
    [SerializeField] private LayerMask playerLayer;

    [Header("Prompt UI")]
    [SerializeField] private GameObject interactPrompt;

    [Header("Sprout Sprite")]
    [SerializeField] private GameObject sprout;

    private SpriteRenderer spriteRenderer;
    private bool playerInRange = false;
    private bool used = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (interactPrompt != null)
            interactPrompt.SetActive(false);
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.enabled = false;
        sprout.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        if (!used)
        {
            // Wait for player to be in proximity, if so, display button prompt
            // Much like journal logs
            Collider2D player = Physics2D.OverlapCircle(transform.position, interactRange, playerLayer);
            playerInRange = player != null;

            if (interactPrompt != null)
                interactPrompt.SetActive(playerInRange);

            if (playerInRange && Keyboard.current.eKey.wasPressedThisFrame)
            {
                // Enable sprite to include planted seed
                spriteRenderer.enabled = true;

                // Update manager object to increment objective
                manager.Increment(sprout);

                // Prevent reuse of this plot
                used = true;
                interactPrompt.SetActive(false);
            }
        }
    }
}
