using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class Cutscene : MonoBehaviour
{
    [Header("Text Object References")]
    [SerializeField] private TextMeshProUGUI text1;
    [SerializeField] private TextMeshProUGUI text2;
    [SerializeField] private TextMeshProUGUI text3;
    private int progress = 0;
    private string currentScene;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        text2.alpha = 0f;
        text3.alpha = 0f;
        currentScene = SceneManager.GetActiveScene().name;
    }

    public void Advance()
    {
        if (progress == 0)
        {
            progress = 1;
            text1.alpha = 0f;
            text2.alpha = 1f;
        }
        else if (progress == 1)
        {
            progress = 2;
            text2.alpha = 0f;
            text3.alpha = 1f;
        }
        else
        {
            if (currentScene == "Prologue")
                SceneManager.LoadScene("Newlevel1"); // Chapter 1 Level 1
            else if (currentScene == "Ch_1_to_2_Interlude")
                SceneManager.LoadScene("Newlevel3"); // Chapter 2 Level 1
            else if (currentScene == "Ch_2_to_3_Interlude")
                SceneManager.LoadScene("Newlevel5"); // Chapter 3 Level 1
            else if (currentScene == "Epilogue")
                Application.Quit(); // End game
            else
                Debug.LogError("Error: Current scene does not have a next scene configured.");
        }
    }
}
