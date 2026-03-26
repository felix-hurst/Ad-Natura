using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class CalamityFlower : MonoBehaviour
{
    [SerializeField] private GameObject popupMessage;
    [SerializeField] private GameObject keyItemShine;
    [SerializeField] private Sprite spriteWithoutShadow;
    private SpriteRenderer spriteRenderer;
    private bool rising = false;
    private bool waitingOnPlayer = false;
    private Keyboard keyboard;

    void Start()
    {
        popupMessage.SetActive(false);
        keyItemShine.SetActive(false);
        spriteRenderer = GetComponent<SpriteRenderer>();
        keyboard = Keyboard.current;
    }

    void Update()
    {
        // Rise into the air
        if (rising)
        {
            transform.position = new Vector3(transform.position.x, transform.position.y + 2f * Time.deltaTime, 0f);
        }
        // Show congratulatory message
        if (transform.position.y >= 3f && !waitingOnPlayer)
        {
            rising = false;
            popupMessage.SetActive(true);
            keyItemShine.SetActive(true);
            waitingOnPlayer = true;
        }
        // Continue to next level
        if (waitingOnPlayer && keyboard.enterKey.wasPressedThisFrame)
        {
            SceneManager.LoadScene("Ch_1_to_2_Interlude");
        }
    }

    private void OnTriggerEnter2D(Collider2D collider)
    {
        if (collider.CompareTag("Player") == true && !rising && !waitingOnPlayer)
        {
            // Set to foreground so it renders above other objects
            spriteRenderer.sortingLayerName = "Foreground";

            // Rise into the air (in Update() function)
            rising = true;

            // Swap sprite to one without shadow
            spriteRenderer.sprite = spriteWithoutShadow;
        }
    }
}
