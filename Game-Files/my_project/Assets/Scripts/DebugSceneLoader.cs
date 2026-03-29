using UnityEngine;
using UnityEngine.SceneManagement;

public class DebugSceneLoader : MonoBehaviour
{
    public void LoadPrologue()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Prologue");
    }

    public void LoadCh1to2Interlude()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Ch_1_to_2_Interlude");
    }

    public void LoadCh2to3Interlude()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Ch_2_to_3_Interlude");
    }

    public void LoadEpilogue()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Epilogue");
    }

    public void LoadLvl1()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Newlevel1");
    }

    public void LoadLvl2()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Newlevel2");
    }

    public void LoadLvl3()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Newlevel3");
    }

    public void LoadLvl4()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Newlevel4");
    }

    public void LoadLvl5()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Newlevel5");
    }

    public void LoadLvl6()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Newlevel6");
    }
}
