using UnityEngine;

public class PlayerStart : MonoBehaviour
{
    [Range(0f, 1f)] [SerializeField] private float normalizedX = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float normalizedY = 0.12f;
    [Tooltip("Mundo amplio: margen con las paredes al sortear el punto de aparición. Tiene " +
             "que dejar sitio para la expulsión, que llega desde unas unidades más allá")]
    [SerializeField] private float edgeMargin = 4f;

    bool placedByDirector;

    void Start() { if (!placedByDirector) Place(false); }

    /// Lo coloca al principio del recorrido. En un nivel espejado sale por el lado contrario,
    /// porque la meta se ha ido al otro extremo.
    public void Place(bool mirrored)
    {
        placedByDirector = true;
        float nx = mirrored ? 1f - normalizedX : normalizedX;
        var bounds = GameBounds.Instance;
        if (bounds == null || !bounds.IsReady)
        {
            Debug.LogWarning("PlayerStart: GameBounds no está listo.");
            return;
        }

        // Mundo amplio: en cualquier punto del mundo. La meta se colocará después, lo más
        // lejos posible de aquí. Con la cámara fija, el sitio de siempre
        Vector2 p;
        if (bounds.IsExpanded)
        {
            Rect a = bounds.Area;
            float mx = Mathf.Min(edgeMargin, a.width * 0.45f), my = Mathf.Min(edgeMargin, a.height * 0.45f);
            p = new Vector2(Random.Range(a.xMin + mx, a.xMax - mx), Random.Range(a.yMin + my, a.yMax - my));
        }
        else p = bounds.PointInArea(nx, normalizedY);
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