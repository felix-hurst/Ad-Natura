using UnityEngine;
using UnityEngine.InputSystem;

public class ChestInteract : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite openChestSprite;

    private SpriteRenderer spriteRenderer;
    private bool isPlayerNearby = false;
    private bool isOpen = false;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        if (isPlayerNearby && !isOpen && Keyboard.current.eKey.wasPressedThisFrame)
        {
            OpenChest();
        }
    }

    void OpenChest()
    {
        isOpen = true;
        spriteRenderer.sprite = openChestSprite;
        Debug.Log("Chest opened!");
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player")) { isPlayerNearby = true; }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player")) { isPlayerNearby = false; }
    }
}