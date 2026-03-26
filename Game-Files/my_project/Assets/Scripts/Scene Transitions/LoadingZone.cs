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

    private void OnTriggerEnter2D(Collider2D collider)
    {
        // Note: Chapter 1 Level 2 (1-2) and Chapter 3 Level 2 (3-2) use different conditions
        // for proceeding to the next stage, and they do not have loading zones
        if (collider.CompareTag("Player") == true)
        {
            // 1-1 to 1-2
            if (currentScene == "Newlevel1")
                SceneManager.LoadScene("Newlevel2");
            // 2-1 to 2-2
            else if (currentScene == "Newlevel3")
                SceneManager.LoadScene("Newlevel4");
            // 2-2 to Interlude / Intro to Ch. 3
            else if (currentScene == "Newlevel4")
                SceneManager.LoadScene("Ch_2_to_3_Interlude");
            // 3-1 to 3-2
            else if (currentScene == "Newlevel5")
                SceneManager.LoadScene("Newlevel6");
            else
                Debug.LogError("Error: Current scene does not have a next scene configured.");
        }
    }
}
