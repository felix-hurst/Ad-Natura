using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using Unity.VisualScripting;

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

    [Header("Hover (Bob Up and Down) Behaviour")]
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float bobAmplitude = 0.1f;

    private bool hasBeenRead = false;
    private bool playerInRange = false;
    private float yPosition;
    private float currentTime;

    void Start()
    {
        if (interactPrompt != null)
            interactPrompt.SetActive(false);
        yPosition = transform.position.y;
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

        // Bob up and down
        currentTime = Time.fixedTime;
        transform.position = new Vector3(transform.position.x, yPosition + bobAmplitude * Mathf.Sin(bobSpeed * currentTime), transform.position.z);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}