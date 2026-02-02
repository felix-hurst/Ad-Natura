using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class CheckpointUI : MonoBehaviour
{
    public static CheckpointUI Instance { get; private set; }

    [SerializeField] private GameObject popupPanel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Image iconImage;
    [SerializeField] private float displayDuration = 4f;
    [SerializeField] private float fadeSpeed = 2f;

    private CanvasGroup canvasGroup;
    private Coroutine hideCoroutine;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        canvasGroup = popupPanel.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = popupPanel.AddComponent<CanvasGroup>();
        }
        
        popupPanel.SetActive(false);
    }

    public void ShowCheckpointMessage(string title, string message)
    {
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
        }

        titleText.text = title;
        messageText.text = message;
        popupPanel.SetActive(true);
        canvasGroup.alpha = 1f;

        hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(displayDuration);

        while (canvasGroup.alpha > 0)
        {
            canvasGroup.alpha -= Time.deltaTime * fadeSpeed;
            yield return null;
        }

        popupPanel.SetActive(false);
    }
}
