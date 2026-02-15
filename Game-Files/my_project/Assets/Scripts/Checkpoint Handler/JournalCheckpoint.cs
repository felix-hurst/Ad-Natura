using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class JournalCheckpoint : MonoBehaviour
{
    [Header("Journal Entry")]
    [SerializeField] private string journalTitle = "Journal Entry";
    [TextArea(4, 10)]
    [SerializeField] private string journalFullText = "Write your full journal entry here.";

    [Header("Interaction")]
    [SerializeField] private float interactRange = 2f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private string checkpointID = "Journal_1";
    [SerializeField] private bool oneTimeOnly = false;

    [Header("Prompt UI")]
    [SerializeField] private GameObject interactPrompt;

    private bool hasBeenRead = false;
    private bool playerInRange = false;

    void Start()
    {
        if (interactPrompt != null)
            interactPrompt.SetActive(false);
    }

    void Update()
    {
        Collider2D player = Physics2D.OverlapCircle(transform.position, interactRange, playerLayer);
        playerInRange = player != null;

        if (interactPrompt != null)
            interactPrompt.SetActive(playerInRange && !(oneTimeOnly && hasBeenRead));

        if (playerInRange && Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (oneTimeOnly && hasBeenRead) return;
            if (Journal.Instance != null && !Journal.Instance.IsOpen())
            {
                Journal.Instance.OpenJournal(journalTitle, journalFullText);
                hasBeenRead = true;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}