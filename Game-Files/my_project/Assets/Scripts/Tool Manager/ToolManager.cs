using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class ToolManager : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private PlayerController player;

    [Header("Frame Sprites")]
    [SerializeField] private Sprite inactiveFrame;
    [SerializeField] private Sprite activeFrame;

    [Header("Slot UI References")]
    [SerializeField] private Image[] slots;
    [SerializeField] private GameObject[] descs;
    [SerializeField] private CanvasGroup hudGroup;

    [Header("Sounds")]
[SerializeField] private string openSound = "ToolWheel";
[SerializeField] private string selectSound = "ToolWheel";


    [Header("Visibility Settings")]
    [SerializeField] private float displayDuration = 5.0f;
    private float visibilityTimer;
    [SerializeField] private float activeScale = 1.2f;

    private int currentToolIndex = -1; // -1 means nothing is selected on initialization

    void Start()
    {
        //Ensure the UI starts completely invisible/unselected
        DeselectAll();
        if (hudGroup != null) hudGroup.alpha = 0;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // When a key is pressed, trigger the tool AND show the UI
        if (keyboard.digit1Key.wasPressedThisFrame)
        {
            Debug.Log("ToolManager: Pressed 1");
            SelectTool(0);
            ShowHUD();
        }

        // 2 = Incendiary Ball (Index 1)
        if (keyboard.digit2Key.wasPressedThisFrame)
        {
            Debug.Log("ToolManager: Pressed 2");
            SelectTool(1);
            ShowHUD();
        }

        // 3 = Wind Ball (Index 2)
        if (keyboard.digit3Key.wasPressedThisFrame)
        {
            Debug.Log("ToolManager: Pressed 3");
            SelectTool(2);
            ShowHUD();
        }

        HandleHUDVisibility();
    }

    private void HandleHUDVisibility()
    {
        if (visibilityTimer > 0)
        {
            visibilityTimer -= Time.deltaTime;
            if (player.IsRunning()) visibilityTimer = 0;
            if (visibilityTimer <= 0 && hudGroup != null)
            {
                hudGroup.alpha = 0;
            }
        }
    }

    private void ShowHUD()
    {
        if (visibilityTimer <= 0)
            SoundManager.Instance.Play(openSound);

        visibilityTimer = displayDuration;
        if (hudGroup != null) hudGroup.alpha = 1;
    }

    public void DeselectAll()
    {
        currentToolIndex = -1;
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].sprite = inactiveFrame;
            descs[i].SetActive(false);
            slots[i].rectTransform.localScale = Vector3.one;
        }
    }

    public void SelectTool(int index)
    {
        // Toggle: If selecting the same tool, go back to "None" (-1)
        if (currentToolIndex == index)
            currentToolIndex = -1;
        else
            currentToolIndex = index;

        SoundManager.Instance.Play(selectSound); // <-- add this

        // Update the UI visual frames
        // Update the UI visual frames
        for (int i = 0; i < slots.Length; i++)
        {
            if (i == currentToolIndex)
            {
                slots[i].sprite = activeFrame;
                // Safety check for your empty Descs array
                if (descs != null && i < descs.Length && descs[i] != null)
                    descs[i].SetActive(true);

                slots[i].rectTransform.localScale = Vector3.one * activeScale;
            }
            else
            {
                slots[i].sprite = inactiveFrame;
                if (descs != null && i < descs.Length && descs[i] != null)
                    descs[i].SetActive(false);

                slots[i].rectTransform.localScale = Vector3.one;
            }
        }

        // Tell the PlayerController to unequip/update animations
        if (player != null)
        {
            player.SwitchTool(currentToolIndex);
        }
    }
}