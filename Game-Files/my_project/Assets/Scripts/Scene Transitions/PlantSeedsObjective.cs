using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlantSeedsObjective : MonoBehaviour
{
    [SerializeField] private GameObject trackerUI;
    [SerializeField] private GameObject clearUI;
    [SerializeField] private TextMeshProUGUI trackerText;
    [SerializeField] private int numberOfSeedsToPlant = 5;
    [SerializeField] private float timeForSproutsToGrow = 3f;
    [SerializeField] private float timeBetweenSprouts = 1f;
    private int plantedSeeds;
    private List<GameObject> sprouts = new List<GameObject>();
    private bool waitingForSprouts = false;
    private float timer = 0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        clearUI.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        // Handle the wait for the first sprout
        // And between subsequent sprouts
        if (waitingForSprouts)
        {
            timer += Time.deltaTime;
            for (int i = 0; i < numberOfSeedsToPlant; i++)
            {
                if (timer >= timeForSproutsToGrow + i*timeBetweenSprouts)
                {
                    clearUI.SetActive(false);
                    sprouts[i].SetActive(true);
                }
            }

            if (timer >= timeForSproutsToGrow + numberOfSeedsToPlant * timeBetweenSprouts + 3f)
            {
                SceneManager.LoadScene("Epilogue");
            }

            return;
        }

        // Update trackerUI display
        trackerText.text = "Seeds planted: " + plantedSeeds + " / " + numberOfSeedsToPlant;

        // If all seeds planted, congratulate player and proceed to epilogue
        if (plantedSeeds >= numberOfSeedsToPlant)
        {
            // Hide progress tracker (trackerUI), show congratulatory message (clearUI)
            trackerUI.SetActive(false);
            clearUI.SetActive(true);

            // Start timer to wait a few seconds and then make sprouts appear
            waitingForSprouts = true;
        }
    }

    public void Increment(GameObject sprout)
    {
        plantedSeeds++;
        sprouts.Add(sprout);
    }
}
