using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class ToolManager : MonoBehaviour
{
    [Header("Frame Sprites")]
    [SerializeField] private Sprite inactiveFrame;
    [SerializeField] private Sprite activeFrame;

    [Header("Slot UI References")]
    [SerializeField] private Image[] slots;
    [SerializeField] private CanvasGroup hudGroup; // Add a CanvasGroup for fading

    [Header("Visibility Settings")]
    [SerializeField] private float displayDuration = 2.0f;
    private float visibilityTimer;

    private int currentToolIndex = -1; // -1 means nothing is selected

    void Start()
    {
        // 1. Ensure the UI starts completely invisible/unselected
        DeselectAll();

        // 2. If you added a CanvasGroup, hide it immediately
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
        if (index < 0 || index >= slots.Length) return;
        currentToolIndex = index;

        for (int i = 0; i < slots.Length; i++)
        {
            bool isSelected = (i == index);
            slots[i].sprite = isSelected ? activeFrame : inactiveFrame;
            slots[i].rectTransform.localScale = isSelected ? new Vector3(1.15f, 1.15f, 1f) : Vector3.one;
        }
    }
}