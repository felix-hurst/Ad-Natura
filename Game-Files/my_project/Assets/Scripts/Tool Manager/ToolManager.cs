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
    [SerializeField] private CanvasGroup hudGroup; // Add a CanvasGroup for fading

    [Header("Visibility Settings")]
    [SerializeField] private float displayDuration = 2.0f;
    private float visibilityTimer;
    [SerializeField] private float activeScale = 1.2f;

    private int currentToolIndex = -1; // -1 means nothing is selected

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
        if (keyboard.digit1Key.wasPressedThisFrame) { SelectTool(0); ShowHUD(); }
        if (keyboard.digit2Key.wasPressedThisFrame) { SelectTool(1); ShowHUD(); }
        if (keyboard.digit3Key.wasPressedThisFrame) { SelectTool(2); ShowHUD(); }

        // Handle the auto-hide timer
        if (visibilityTimer > 0)
        {
            visibilityTimer -= Time.deltaTime;
            if (visibilityTimer <= 0 && hudGroup != null)
            {
                // Start fading or just hide
                hudGroup.alpha = 0;
            }
        }
    }

    private void ShowHUD()
    {
        visibilityTimer = displayDuration;
        if (hudGroup != null) hudGroup.alpha = 1;
    }

    public void DeselectAll()
    {
        currentToolIndex = -1;
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].sprite = inactiveFrame;
            slots[i].rectTransform.localScale = Vector3.one;
        }
    }

    public void SelectTool(int index)
    {
        // If we click the tool that is already active, unselect it
        if (currentToolIndex == index)
        {
            currentToolIndex = -1; // -1 means no tool
        }
        else
        {
            currentToolIndex = index;
        }

        // Update the UI visual frames
        for (int i = 0; i < slots.Length; i++)
        {
            if (i == currentToolIndex)
            {
                slots[i].sprite = activeFrame;
                slots[i].transform.localScale = Vector3.one * activeScale;
            }
            else
            {
                slots[i].sprite = inactiveFrame;
                slots[i].transform.localScale = Vector3.one;
            }
        }

        // Tell the PlayerController what happened
        if (player != null)
        {
            player.SwitchTool(currentToolIndex);
        }
    }
}