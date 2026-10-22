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

    [Header("Carriles (lentos y rápidos por caminos distintos)")]
    [Tooltip("Los lentos y los rápidos van por bandas distintas a lo largo de la corriente, así " +
             "se cruzan en pocos sitios y chocan mucho menos. Cometas y colosos van libres")]
    [SerializeField] private bool lanes = true;
    [SerializeField] private Vector2Int laneCount = new Vector2Int(2, 3);
    [Tooltip("Ancho de cada carril, en fracción del ancho del mundo atravesado a la corriente")]
    [SerializeField] private Vector2 laneWidth = new Vector2(0.16f, 0.3f);
    [Tooltip("Cuánto se corrige el rumbo de una roca que se sale de su carril (grados)")]
    [SerializeField] private float laneSteer = 25f;

    public struct Lane { public float s0, s1; public int kind; }   // kind 0 = lentos, 1 = rápidos
    readonly System.Collections.Generic.List<Lane> laneList = new System.Collections.Generic.List<Lane>();
    public System.Collections.Generic.IReadOnlyList<Lane> Lanes => laneList;

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
        BuildLanes(seed);
    }

    // ------------------------------------------------------------------ carriles
    Vector2 Perp => FromAngle(baseAngle + 90f);

    /// Qué carril corresponde a un tipo: 0 lentos (y rompibles), 1 rápidos, -1 ninguno (libre)
    public static int KindOf(AsteroidTypeData t)
    {
        if (t == null || t.comet != null) return -1;
        switch (t.behavior)
        {
            case AsteroidBehavior.Slow:
            case AsteroidBehavior.Breakable: return 0;
            case AsteroidBehavior.Fast: return 1;
            default: return -1;
        }
    }

    void BuildLanes(int seed)
    {
        laneList.Clear();
        var gb = GameBounds.Instance;
        if (!lanes || gb == null || !gb.IsReady) return;
        Rect a = gb.Area;
        Vector2 pp = Perp;
        float sMin = float.MaxValue, sMax = float.MinValue;
        foreach (var c in new[] { new Vector2(a.xMin, a.yMin), new Vector2(a.xMax, a.yMin), new Vector2(a.xMin, a.yMax), new Vector2(a.xMax, a.yMax) })
        {
            float s = Vector2.Dot(c, pp);
            sMin = Mathf.Min(sMin, s); sMax = Mathf.Max(sMax, s);
        }
        float span = sMax - sMin;
        var rng = new System.Random(seed >= 0 ? seed * 7 + 13 : Random.Range(0, int.MaxValue));
        int n = rng.Next(laneCount.x, laneCount.y + 1);
        var widths = new float[n];
        float sum = 0f;
        for (int i = 0; i < n; i++) { widths[i] = span * Mathf.Lerp(laneWidth.x, laneWidth.y, (float)rng.NextDouble()); sum += widths[i]; }
        if (sum > span * 0.85f) for (int i = 0; i < n; i++) widths[i] *= span * 0.85f / sum;
        sum = 0f; foreach (var w in widths) sum += w;
        // Huecos al azar entre carriles: ahí quedan zonas tranquilas
        float free = span - sum;
        var gaps = new float[n + 1];
        float gsum = 0f;
        for (int i = 0; i <= n; i++) { gaps[i] = 0.3f + (float)rng.NextDouble(); gsum += gaps[i]; }
        // Al menos un carril de lentos y uno de rápidos
        var kinds = new int[n];
        for (int i = 0; i < n; i++) kinds[i] = i == 0 ? 0 : i == 1 ? 1 : rng.Next(2);
        for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); (kinds[i], kinds[j]) = (kinds[j], kinds[i]); }
        float s0 = sMin;
        for (int i = 0; i < n; i++)
        {
            s0 += free * gaps[i] / gsum;
            laneList.Add(new Lane { s0 = s0, s1 = s0 + widths[i], kind = kinds[i] });
            s0 += widths[i];
        }
    }

    /// Un carril al azar del tipo pedido (-1 si no hay)
    public int PickLane(int kind)
    {
        if (kind < 0 || laneList.Count == 0) return -1;
        int count = 0;
        foreach (var l in laneList) if (l.kind == kind) count++;
        if (count == 0) return -1;
        int pick = Random.Range(0, count);
        for (int i = 0; i < laneList.Count; i++)
            if (laneList[i].kind == kind && pick-- == 0) return i;
        return -1;
    }

    /// El carril del tipo pedido en el que está un punto (-1 si ninguno)
    public int LaneAt(Vector2 p, int kind)
    {
        float s = Vector2.Dot(p, Perp);
        for (int i = 0; i < laneList.Count; i++)
            if (laneList[i].kind == kind && s >= laneList[i].s0 && s <= laneList[i].s1) return i;
        return -1;
    }

    /// Corrección de rumbo (grados) para volver al centro de su carril si se aleja de él
    public float LaneCorrection(int lane, Vector2 p)
    {
        if (lane < 0 || lane >= laneList.Count) return 0f;
        var l = laneList[lane];
        float s = Vector2.Dot(p, Perp);
        float half = Mathf.Max(0.1f, (l.s1 - l.s0) * 0.5f);
        float off = (s - (l.s0 + l.s1) * 0.5f) / half;
        float k = Mathf.Clamp01((Mathf.Abs(off) - 0.6f) / 0.6f);
        return -Mathf.Sign(off) * laneSteer * k;
    }

    /// Un punto dentro del carril (para colocar al empezar el nivel)
    public bool InLane(Vector2 p, int lane)
    {
        if (lane < 0 || lane >= laneList.Count) return true;
        float s = Vector2.Dot(p, Perp);
        return s >= laneList[lane].s0 && s <= laneList[lane].s1;
    }

    /// Punto de entrada para un tipo: dentro de uno de sus carriles si los hay
    public Vector2 RandomEntryPoint(Vector2 avoid, float avoidRadius, AsteroidTypeData type)
    {
        int lane = PickLane(KindOf(type));
        if (lane < 0) return RandomEntryPoint(avoid, avoidRadius);
        for (int i = 0; i < 14; i++)
        {
            var p = RandomEntryPoint(avoid, avoidRadius);
            if (InLane(p, lane)) return p;
        }
        // Construido: en el borde de donde viene la corriente, a la altura del carril
        Rect r = SpawnRect;
        Vector2 d = FromAngle(baseAngle), pp = Perp;
        var l = laneList[lane];
        float s = Random.Range(l.s0, l.s1);
        Vector2 e;
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
        {
            float x = d.x > 0f ? r.xMin : r.xMax;
            float y = Mathf.Abs(pp.y) > 0.2f ? (s - x * pp.x) / pp.y : Random.Range(r.yMin, r.yMax);
            e = new Vector2(x, Mathf.Clamp(y, r.yMin, r.yMax));
        }
        else
        {
            float y = d.y > 0f ? r.yMin : r.yMax;
            float x = Mathf.Abs(pp.x) > 0.2f ? (s - y * pp.y) / pp.x : Random.Range(r.xMin, r.xMax);
            e = new Vector2(Mathf.Clamp(x, r.xMin, r.xMax), y);
        }
        if (HasBlockedSide && Vector2.Dot(e - BlockedPoint, BlockedNormal) < 0f) return RandomEntryPoint(avoid, avoidRadius);
        return e;
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

    /// Zona por la que no puede entrar nada (detrás de una rasgadura). Un punto de su línea y
    /// la normal hacia el lado permitido. La pone FractureDirector
    public static bool HasBlockedSide;
    public static Vector2 BlockedPoint, BlockedNormal;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBlocked() { HasBlockedSide = false; }

    /// Punto de entrada en el borde "de donde viene" la corriente, lejos del jugador.
    public Vector2 RandomEntryPoint(Vector2 avoid, float avoidRadius)
    {
        Vector2 e = RawEntryPoint(avoid, avoidRadius);
        if (!HasBlockedSide || Vector2.Dot(e - BlockedPoint, BlockedNormal) >= 0f) return e;

        // Detrás de la rasgadura no entra nada: se cambia por un punto del borde de arriba o
        // de abajo, en la parte de delante
        Rect r = SpawnRect;
        float lineX = BlockedPoint.x;
        float x0 = BlockedNormal.x < 0f ? r.xMin : Mathf.Max(r.xMin, lineX + 1f);
        float x1 = BlockedNormal.x < 0f ? Mathf.Min(r.xMax, lineX - 1f) : r.xMax;
        if (x1 <= x0) { x0 = r.xMin; x1 = r.xMax; }
        float y = Random.value < 0.5f ? r.yMin : r.yMax;
        return new Vector2(Random.Range(x0, x1), y);
    }

    Vector2 RawEntryPoint(Vector2 avoid, float avoidRadius)
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