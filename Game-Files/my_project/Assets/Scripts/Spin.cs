using UnityEngine;

public class Spin : MonoBehaviour
{
    [SerializeField] private float spinSpeed = 40f;

    void Update()
    {
        // Spin forever
        transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);
    }
}
