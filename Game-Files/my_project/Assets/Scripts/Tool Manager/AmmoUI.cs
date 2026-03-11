using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

public class AmmoUI : MonoBehaviour
{
    [Header("Text")]
    [SerializeField] private TextMeshProUGUI toolNameText;
    [SerializeField] private TextMeshProUGUI ammoCountText;

    [Header("Bar")]
    [SerializeField] private GameObject bar1;
    [SerializeField] private GameObject bar2;
    [SerializeField] private GameObject bar3;
    [SerializeField] private GameObject bar4;
    [SerializeField] private GameObject bar5;

    [Header("Player")]
    [SerializeField] private GameObject player;
    private PlayerController playerCon;

    private readonly string[] toolNames =
    {
        "Water Shooter",
        "Impact Round",
        "Wind Fan"
    };

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        playerCon = player.GetComponent<PlayerController>();
    }

    // Update is called once per frame
    // Potential future optimization: Call the following code block
    // only when switching tools or using a tool
    void Update()
    {
        // Check current tool of the player
        // Check ammo of the current tool
        // Display information
        
        int currentTool = playerCon.GetCurrentTool();
        var ammoCount = currentTool switch
                {
                    0 => playerCon.GetWaterAmmo(), // Water shooter tool
                    1 => playerCon.GetIncendiaryAmmo(), // Impact round tool
                    2 => playerCon.GetWindAmmo(), // Wind fan tool
                    _ => 0,
                };
        
        string toolName;
        if (currentTool >= 0)
            toolName = toolNames[currentTool];
        else
            toolName = "-";

        // Display tool name
        toolNameText.text = "Tool: " + toolName;

        // Display ammo count
        if (ammoCount > 0 && currentTool >= 0 || currentTool < 0)
            ammoCountText.text = "Ammo:";
        else
            ammoCountText.text = "Press R to Reload";

        switch (ammoCount)
        {
            case 0:
                bar1.SetActive(false);
                bar2.SetActive(false);
                bar3.SetActive(false);
                bar4.SetActive(false);
                bar5.SetActive(false);
                break;
            case 1:
                bar1.SetActive(true);
                bar2.SetActive(false);
                bar3.SetActive(false);
                bar4.SetActive(false);
                bar5.SetActive(false);
                break;
            case 2:
                bar1.SetActive(true);
                bar2.SetActive(true);
                bar3.SetActive(false);
                bar4.SetActive(false);
                bar5.SetActive(false);
                break;
            case 3:
                bar1.SetActive(true);
                bar2.SetActive(true);
                bar3.SetActive(true);
                bar4.SetActive(false);
                bar5.SetActive(false);
                break;
            case 4:
                bar1.SetActive(true);
                bar2.SetActive(true);
                bar3.SetActive(true);
                bar4.SetActive(true);
                bar5.SetActive(false);
                break;
            case 5:
                bar1.SetActive(true);
                bar2.SetActive(true);
                bar3.SetActive(true);
                bar4.SetActive(true);
                bar5.SetActive(true);
                break;
            default:
                bar1.SetActive(false);
                bar2.SetActive(false);
                bar3.SetActive(false);
                bar4.SetActive(false);
                bar5.SetActive(false);
                break;
        }
    }
}
