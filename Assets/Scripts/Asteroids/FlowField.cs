using UnityEngine;

/// Campo de flujo del cinturón: una dirección que varía suavemente por el espacio y el tiempo.
/// Los asteroides lo siguen y por eso sus trayectorias hacen arcos.
public class FlowField : MonoBehaviour
{
    public static FlowField Instance { get; private set; }

    [Header("Dirección general")]
    [Tooltip("0 = hacia la derecha, 90 = arriba, 180 = hacia la izquierda, 270 = abajo")]
    [SerializeField] private float baseAngle = 180f;
    [SerializeField] private bool randomizeOnAwake = false;

    [Header("Curvatura (los arcos)")]
    [Tooltip("Cuánto se desvía la corriente de la dirección general (grados)")]
    [SerializeField] private float amplitude = 60f;
    [Tooltip("Tamaño de las curvas: más bajo = curvas más amplias")]
    [SerializeField] private float noiseScale = 0.12f;
    [Tooltip("Velocidad a la que cambia la corriente con el tiempo")]
    [SerializeField] private float drift = 0.05f;

    [Header("Entrada y salida")]
    [Tooltip("Los asteroides aparecen en este borde, fuera de la pantalla")]
    [SerializeField] private float spawnMargin = 2f;
    [Tooltip("Al salir de este borde se reciclan")]
    [SerializeField] private float despawnMargin = 3.5f;

    [Header("Entrada por el lado del viaje (mundo amplio)")]
    [Tooltip("Con la cámara en movimiento, cuánto pesa el borde HACIA el que viaja frente al " +
             "borde de donde viene la corriente. Sin esto, al moverte en contra o en " +
             "perpendicular a la corriente la zona que se descubre llega vacía. 0 = solo la " +
             "corriente, como antes")]
    [SerializeField] private float travelEntryWeight = 1f;
    [Tooltip("Velocidad de cámara a la que el peso del viaje llega a su máximo")]
    [SerializeField] private float travelFullSpeed = 5f;

    float seedX, seedY;

    public float BaseAngle => baseAngle;
    public Rect SpawnRect => Expand(CurrentCameraRect(), spawnMargin);
    public Rect DespawnRect => Expand(CurrentCameraRect(), despawnMargin);

    void Awake()
    {
        Instance = this;
        seedX = Random.Range(0f, 1000f);
        seedY = Random.Range(0f, 1000f);
        if (randomizeOnAwake) Randomize();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
    public void SetAmplitude(float degrees) { amplitude = degrees; }
    public void SetBaseAngle(float degrees) { baseAngle = degrees; }

    /// Elige dirección general y forma de las corrientes. Con una semilla, siempre el mismo resultado.
    public void Randomize(int seed = -1)
    {
        var prev = Random.state;
        if (seed >= 0) Random.InitState(seed);

        // Solo izquierda o derecha, con desviación. Con corriente vertical no habría un
        // "lado" al que llevar la meta, y abajo están la palanca y los botones. Además es más
        // fiel: un cinturón tiene un eje dominante, porque el movimiento orbital es tangencial.
        float side = Random.value < 0.5f ? 180f : 0f;
        baseAngle = side + Random.Range(-35f, 35f);
        seedX = Random.Range(0f, 1000f);
        seedY = Random.Range(0f, 1000f);

        if (seed >= 0) Random.state = prev;
    }

    /// Ángulo (en grados) de la corriente en un punto del mundo.
    public float AngleAt(Vector2 p)
    {
        float n = Mathf.PerlinNoise(p.x * noiseScale + seedX,
                                    p.y * noiseScale + seedY + Time.time * drift);
        return baseAngle + (n - 0.5f) * 2f * amplitude;
    }

    public static Vector2 FromAngle(float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }

    /// Punto de entrada en el borde "de donde viene" la corriente, lejos del jugador.
    public Vector2 RandomEntryPoint(Vector2 avoid, float avoidRadius)
    {
        Rect r = SpawnRect;
        Vector2 d = FromAngle(baseAngle);

        // Bordes: izquierdo, derecho, inferior, superior. Pesa más el que mira contra la corriente.
        float wL = Mathf.Max(0f, Vector2.Dot(Vector2.right, d)) * r.height;   // entra por la izquierda si fluye a la derecha
        float wR = Mathf.Max(0f, Vector2.Dot(Vector2.left,  d)) * r.height;
        float wB = Mathf.Max(0f, Vector2.Dot(Vector2.up,    d)) * r.width;
        float wT = Mathf.Max(0f, Vector2.Dot(Vector2.down,  d)) * r.width;
        // El borde hacia el que viaja la cámara también recibe asteroides, en proporción a
        // lo rápido que va: la zona que se descubre no aparece vacía
        var follow = CameraFollow.Instance;
        if (follow != null && travelEntryWeight > 0f)
        {
            Vector2 tv = follow.Velocity;
            float k = travelEntryWeight * Mathf.Clamp01(tv.magnitude / Mathf.Max(0.1f, travelFullSpeed));
            if (k > 0f)
            {
                Vector2 t = tv.normalized;
                wL += k * Mathf.Max(0f, Vector2.Dot(Vector2.left,  t)) * r.height;
                wR += k * Mathf.Max(0f, Vector2.Dot(Vector2.right, t)) * r.height;
                wB += k * Mathf.Max(0f, Vector2.Dot(Vector2.down,  t)) * r.width;
                wT += k * Mathf.Max(0f, Vector2.Dot(Vector2.up,    t)) * r.width;
            }
        }

        float total = wL + wR + wB + wT;
        if (total <= 0.0001f) { wR = 1f; total = 1f; }

        Vector2 p = r.center;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float pick = Random.value * total;
            if      (pick < wL)            p = new Vector2(r.xMin, Random.Range(r.yMin, r.yMax));
            else if (pick < wL + wR)       p = new Vector2(r.xMax, Random.Range(r.yMin, r.yMax));
            else if (pick < wL + wR + wB)  p = new Vector2(Random.Range(r.xMin, r.xMax), r.yMin);
            else                           p = new Vector2(Random.Range(r.xMin, r.xMax), r.yMax);

            if (Vector2.Distance(p, avoid) > avoidRadius) break;
        }
        return p;
    }

    static Rect CurrentCameraRect() =>
        GameBounds.Instance != null ? GameBounds.Instance.CameraRect : new Rect(-5f, -3f, 10f, 6f);

    static Rect Expand(Rect r, float m) =>
        new Rect(r.xMin - m, r.yMin - m, r.width + 2f * m, r.height + 2f * m);

    // Dibuja las corrientes en la vista Scene (y en Game con Gizmos activado)
    void OnDrawGizmos()
    {
        if (!Application.isPlaying || GameBounds.Instance == null) return;

        Rect cr = GameBounds.Instance.CameraRect;
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.7f);
        for (float x = cr.xMin + 1f; x < cr.xMax; x += 2f)
        {
            for (float y = cr.yMin + 1f; y < cr.yMax; y += 2f)
            {
                Vector2 p = new Vector2(x, y);
                Vector2 end = p + FromAngle(AngleAt(p)) * 0.8f;
                Gizmos.DrawLine(p, end);
                Gizmos.DrawSphere(end, 0.07f);
            }
        }
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(SpawnRect.center, SpawnRect.size);
    }
}