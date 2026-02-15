using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class Journal : MonoBehaviour
{
    public static Journal Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject journalPanel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI pageTextLeft;
    [SerializeField] private TextMeshProUGUI pageTextRight;
    [SerializeField] private TextMeshProUGUI pageNumberText;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button backButton;

    [Header("Pagination")]
    [SerializeField] private int wordsPerPage = 60;

    private List<string> pages = new List<string>();
    private int currentPage = 0;
    private string journalTitle = "";
    private bool isOpen = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        journalPanel.SetActive(false);

        nextButton.onClick.AddListener(NextPage);
        previousButton.onClick.AddListener(PreviousPage);
        backButton.onClick.AddListener(CloseJournal);
    }

    void Update()
    {
        if (isOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CloseJournal();
        }
    }

    public void OpenJournal(string title, string fullText)
    {
        journalTitle = title;
        pages = SplitIntoPages(fullText, wordsPerPage);
        currentPage = 0;

        journalPanel.SetActive(true);
        isOpen = true;

        Time.timeScale = 0f;

        RefreshPage();
    }

    public void CloseJournal()
    {
        journalPanel.SetActive(false);
        isOpen = false;
        Time.timeScale = 1f;
    }

    private void NextPage()
    {
        if (currentPage + 2 < pages.Count)
        {
            currentPage += 2;
            RefreshPage();
        }
    }

    private void PreviousPage()
    {
        if (currentPage - 2 >= 0)
        {
            currentPage -= 2;
            RefreshPage();
        }
    }

private void RefreshPage()
{
    titleText.text = journalTitle;
    pageTextLeft.text = pages.Count > currentPage ? pages[currentPage] : "";
    pageTextRight.text = pages.Count > currentPage + 1 ? pages[currentPage + 1] : "";

    int leftPageNum = currentPage + 1;
    int rightPageNum = currentPage + 2;
    pageNumberText.text = $"{currentPage}";

    previousButton.interactable = currentPage > 1;
    nextButton.interactable = currentPage + 2 < pages.Count;
}

    private List<string> SplitIntoPages(string fullText, int maxWords)
    {
        List<string> result = new List<string>();

        string[] words = fullText.Split(new char[] { ' ', '\n', '\r' },
            System.StringSplitOptions.RemoveEmptyEntries);

        int index = 0;
        while (index < words.Length)
        {
            List<string> pageWords = new List<string>();
            for (int i = 0; i < maxWords && index < words.Length; i++, index++)
            {
                pageWords.Add(words[index]);
            }
            result.Add(string.Join(" ", pageWords));
        }

        if (result.Count == 0)
            result.Add("");

        return result;
    }

    public bool IsOpen() => isOpen;
}
