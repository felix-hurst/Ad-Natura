using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class Prologue : MonoBehaviour
{
    [Header("Text Object References")]
    [SerializeField] private TextMeshProUGUI text1;
    [SerializeField] private TextMeshProUGUI text2;
    [SerializeField] private TextMeshProUGUI text3;
    private int progress = 0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        text2.alpha = 0f;
        text3.alpha = 0f;
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
            SceneManager.LoadScene("Newlevel1");
        }
    }
}
