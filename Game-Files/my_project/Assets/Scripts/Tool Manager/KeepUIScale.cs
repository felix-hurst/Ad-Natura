using UnityEngine;

public class KeepUIScale : MonoBehaviour
{
    private Vector3 initialScale;

    void Start()
    {
        initialScale = transform.localScale;
    }

    void Update()
    {
        if (transform.parent != null)
        {
            float parentX = transform.parent.localScale.x;

            transform.localScale = new Vector3(
                Mathf.Abs(initialScale.x) * (parentX > 0 ? 1 : -1),
                initialScale.y,
                initialScale.z
            );
        }
    }
}