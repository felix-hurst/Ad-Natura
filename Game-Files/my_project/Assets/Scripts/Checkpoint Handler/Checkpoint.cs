using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    [Header("Checkpoint Settings")]
    [SerializeField] private string checkpointTitle = "Journal Log: Shovel";
    [TextArea(3, 6)]
    [SerializeField] private string checkpointMessage = "You found a checkpoint!";
    [SerializeField] private bool saveGameOnCheckpoint = false;
    [SerializeField] private string checkpointID = "Checkpoint_1";
    
    [Header("Visual Feedback")]
    [SerializeField] private Color activatedColor = Color.green;
    [SerializeField] private Color inactiveColor = Color.white;
    
    private SpriteRenderer spriteRenderer;
    private bool isActivated = false;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.color = inactiveColor;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") && !isActivated)
        {
            ActivateCheckpoint(other.transform);
        }
    }

    private void ActivateCheckpoint(Transform player)
    {
        isActivated = true;

        if (saveGameOnCheckpoint)
        {
            SaveSystem.SaveCheckpoint(player.position, checkpointID);
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = activatedColor;
        }

        CheckpointUI.Instance?.ShowCheckpointMessage(checkpointTitle, checkpointMessage);
        
        Debug.Log($"Checkpoint {checkpointID} activated!");
    }
}