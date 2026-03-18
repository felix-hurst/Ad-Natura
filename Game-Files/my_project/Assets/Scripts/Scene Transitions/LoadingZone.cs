using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingZone : MonoBehaviour
{
    private string currentScene;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentScene = SceneManager.GetActiveScene().name;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player") == true)
        {
            if (currentScene == "Newlevel1")
                SceneManager.LoadScene("Newlevel2"); // Chapter 1 Level 2
            else if (currentScene == "Newlevel2")
                SceneManager.LoadScene("Ch_1_to_2_Interlude");
            else if (currentScene == "Newlevel3")
                SceneManager.LoadScene("Newlevel4"); // Chapter 2 Level 2
            else if (currentScene == "Newlevel4")
                SceneManager.LoadScene("Ch_2_to_3_Interlude");
            else if (currentScene == "Newlevel5")
                SceneManager.LoadScene("Newlevel6"); // Chapter 3 Level 2
            else if (currentScene == "Newlevel6")
                SceneManager.LoadScene("Epilogue");
            else
                Debug.LogError("Error: Current scene does not have a next scene configured.");
        }
    }
}
