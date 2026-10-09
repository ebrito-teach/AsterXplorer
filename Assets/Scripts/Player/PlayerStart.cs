using UnityEngine;

public class PlayerStart : MonoBehaviour
{
    [Range(0f, 1f)] [SerializeField] private float normalizedX = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float normalizedY = 0.12f;

    void Start()
    {
        var bounds = GameBounds.Instance;
        if (bounds == null || !bounds.IsReady)
        {
            Debug.LogWarning("PlayerStart: GameBounds no está listo.");
            return;
        }

        Vector2 p = bounds.PointInArea(normalizedX, normalizedY);
        transform.position = new Vector3(p.x, p.y, transform.position.z);

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.position = p;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }
}