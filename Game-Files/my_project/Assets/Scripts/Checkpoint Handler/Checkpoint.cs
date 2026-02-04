using UnityEngine;
using System.Collections;

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
    [SerializeField] private float fadeOutDuration = 2f;
    [SerializeField] private float cooldownDuration = 1f;
    
    private SpriteRenderer spriteRenderer;
    private bool isActivated = false;
    private bool isOnCooldown = false;

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
        if (other.CompareTag("Player") && !isActivated && !isOnCooldown)
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

        StartCoroutine(FadeOutAndReset());
    }

    private IEnumerator FadeOutAndReset()
    {
        yield return new WaitForSeconds(fadeOutDuration * 0.3f);

        float elapsedTime = 0f;
        float fadeDuration = fadeOutDuration * 0.7f;
        
        while (elapsedTime < fadeDuration && spriteRenderer != null)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / fadeDuration;
            spriteRenderer.color = Color.Lerp(activatedColor, inactiveColor, t);
            yield return null;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = inactiveColor;
        }

        isActivated = false;

        isOnCooldown = true;
        yield return new WaitForSeconds(cooldownDuration);
        isOnCooldown = false;
    }
}