using UnityEngine;

public class PlayerStart : MonoBehaviour
{
    [Range(0f, 1f)] [SerializeField] private float normalizedX = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float normalizedY = 0.12f;
    [Tooltip("Mundo amplio: a qué distancia de la pared se para el jugador. Aparece cerca de " +
             "un borde, entrando desde fuera de él")]
    [SerializeField] private float edgeInset = 2.5f;
    [Tooltip("Margen con las esquinas a lo largo del borde")]
    [SerializeField] private float cornerMargin = 3f;
    [Tooltip("Apagado: solo aparece en los bordes izquierdo y derecho, y la meta va al lado " +
             "contrario. En horizontal es la distancia más larga posible. Encendido: también " +
             "arriba y abajo, donde el lado contrario queda mucho más cerca")]
    [SerializeField] private bool useTopBottomEdges = false;

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
            // Un borde al azar, con más probabilidad para los largos, y un punto a lo largo de
            // él. La cámara se detiene en el borde del mundo, así que el jugador queda cerca
            // del borde de la pantalla y no en el centro
            Rect a = bounds.Area;
            float ix = Mathf.Min(edgeInset, a.width * 0.45f), iy = Mathf.Min(edgeInset, a.height * 0.45f);
            float cx = Mathf.Min(cornerMargin, a.width * 0.45f), cy = Mathf.Min(cornerMargin, a.height * 0.45f);
            float pick = useTopBottomEdges ? Random.value * 2f * (a.width + a.height)
                                           : Random.value * 2f * a.height;
            if (pick < a.height)                      p = new Vector2(a.xMin + ix, Random.Range(a.yMin + cy, a.yMax - cy));
            else if (pick < 2f * a.height)            p = new Vector2(a.xMax - ix, Random.Range(a.yMin + cy, a.yMax - cy));
            else if (pick < 2f * a.height + a.width)  p = new Vector2(Random.Range(a.xMin + cx, a.xMax - cx), a.yMin + iy);
            else                                      p = new Vector2(Random.Range(a.xMin + cx, a.xMax - cx), a.yMax - iy);
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