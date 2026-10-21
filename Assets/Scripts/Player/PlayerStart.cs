using UnityEngine;

public class PlayerStart : MonoBehaviour
{
    /// Dónde cae algo a lo largo de un borde: en el centro, en una esquina o en cualquier punto
    public enum EdgeSlot { Center, Corner, Any }

    /// Dónde estaba la meta del nivel anterior. Por donde entras, sales: si caíste por un
    /// agujero en el centro de un borde, apareces en el centro; si fue en una esquina, en una
    /// esquina (otra cualquiera). No siempre: también se mezcla, para que haya variedad
    public static EdgeSlot? LastGoalSlot;

    /// Posición a lo largo del borde (0 = un extremo, 1 = el otro) para un tipo de sitio
    public static float SlotT(EdgeSlot k)
    {
        switch (k)
        {
            case EdgeSlot.Center: return Random.Range(0.4f, 0.6f);
            case EdgeSlot.Corner: return Random.value < 0.5f ? Random.Range(0f, 0.07f) : Random.Range(0.93f, 1f);
            default: return Random.value;
        }
    }

    public static EdgeSlot PickSlot(float center, float corner, float any)
    {
        float total = Mathf.Max(0.0001f, center + corner + any);
        float r = Random.value * total;
        if (r < center) return EdgeSlot.Center;
        if (r < center + corner) return EdgeSlot.Corner;
        return EdgeSlot.Any;
    }

    [Header("Sitio a lo largo del borde (mundo amplio)")]
    [Tooltip("Probabilidad de aparecer en el mismo tipo de sitio por el que cayó en el nivel " +
             "anterior (centro con centro, esquina con esquina)")]
    [Range(0f, 1f)] [SerializeField] private float followPreviousGoal = 0.6f;
    [Tooltip("Si no sigue al nivel anterior: peso de aparecer en el centro de un borde")]
    [SerializeField] private float centerWeight = 0.35f;
    [Tooltip("Peso de aparecer en una esquina")]
    [SerializeField] private float cornerWeight = 0.35f;
    [Tooltip("Peso de aparecer en cualquier punto del borde")]
    [SerializeField] private float anyWeight = 0.3f;

    /// El tipo de sitio en que apareció esta vez
    public EdgeSlot LastSlot { get; private set; }

    [Range(0f, 1f)] [SerializeField] private float normalizedX = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float normalizedY = 0.12f;
    [Tooltip("Mundo amplio: a qué distancia de la pared se para el jugador. Aparece cerca de " +
             "un borde, entrando desde fuera de él")]
    [SerializeField] private float edgeInset = 2.5f;
    [Tooltip("Distancia a la pared de arriba o abajo cuando aparece en una esquina")]
    [SerializeField] private float cornerMargin = 2.5f;
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

            // Qué tipo de sitio: el del agujero por el que cayó, o uno al azar
            EdgeSlot slot = LastGoalSlot.HasValue && Random.value < followPreviousGoal
                ? LastGoalSlot.Value
                : PickSlot(centerWeight, cornerWeight, anyWeight);
            LastSlot = slot;
            float t = SlotT(slot);

            float pick = useTopBottomEdges ? Random.value * 2f * (a.width + a.height)
                                           : Random.value * 2f * a.height;
            float yAlong = Mathf.Lerp(a.yMin + cy, a.yMax - cy, t);
            float xAlong = Mathf.Lerp(a.xMin + cx, a.xMax - cx, t);
            if (pick < a.height)                      p = new Vector2(a.xMin + ix, yAlong);
            else if (pick < 2f * a.height)            p = new Vector2(a.xMax - ix, yAlong);
            else if (pick < 2f * a.height + a.width)  p = new Vector2(xAlong, a.yMin + iy);
            else                                      p = new Vector2(xAlong, a.yMax - iy);
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