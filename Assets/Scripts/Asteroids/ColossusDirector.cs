using UnityEngine;

public class ColossusDirector : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private AsteroidPool pool;
    [SerializeField] private Transform player;
    [Tooltip("Assets con Behavior = Colossus. Se elige uno al azar en cada aparición")]
    [SerializeField] private AsteroidTypeData[] colossusTypes;

    [Header("Probabilidad por cada asteroide que entra")]
    [Tooltip("1/150 en el nivel 1")]
    [SerializeField] private float baseChance = 1f / 150f;
    [Tooltip("1.04 = la probabilidad se multiplica por 1.04 en cada nivel")]
    [SerializeField] private float growthPerLevel = 1.04f;
    [SerializeField] private float maxChance = 0.25f;

    [Header("Garantía")]
    [Tooltip("Hasta este nivel, si pasan los segundos indicados sin ningún coloso, entra uno")]
    [SerializeField] private int guaranteeUntilLevel = 1;
    [SerializeField] private float guaranteeAfterSeconds = 45f;

    [Header("Límites")]
    [SerializeField] private float minSecondsAfterStart = 6f;
    [SerializeField] private int maxAlive = 1;
    [SerializeField] private float cooldownSeconds = 30f;
    [SerializeField] private float cooldownReductionPerTier = 3f;
    [SerializeField] private float minCooldown = 12f;
    [SerializeField] private int tierLength = 20;

    [Header("Entrada")]
    [SerializeField] private float avoidPlayerRadius = 4f;
    [Tooltip("Cuánto más allá del borde nace, para que no aparezca de golpe. Debe ser menor que (Despawn Margin - Spawn Margin) del FlowField")]
    [SerializeField] private float extraPush = 1f;
    [Tooltip("Margen por dentro del borde visible. El coloso entra siempre dentro de esta banda, " +
             "así que SIEMPRE cruza la pantalla y se ve. Sin esto podía nacer en la esquina del " +
             "margen de spawn y atravesar el nivel entero por fuera de cámara")]
    [SerializeField] private float visibleInset = 1.2f;

    [Header("Paso justo")]
    [Tooltip("Unidades libres que el coloso DEBE dejar siempre al otro lado, para que el " +
             "jugador tenga por dónde pasar. Si el tamaño sorteado no cabe, se recorta. " +
             "Un coloso que ocupa toda la pantalla no es difícil: es imposible")]
    [SerializeField] private float safeCorridor = 2.8f;
    [Tooltip("Radio del collider por unidad de escala, para calcular el tamaño máximo sin " +
             "haberlo creado aún. 0.45 vale para un sprite de roca normal")]
    [SerializeField] private float approxRadiusPerUnit = 0.45f;
    [Tooltip("Se pega al borde más lejano del jugador en vez de cruzar por el centro, " +
             "de modo que el pasillo libre queda siempre del lado del jugador")]
    [SerializeField] private bool hugFarEdge = true;

    /// Posición de entrada y tipo. Aquí se conectarán la alerta del HUD y el sonido.
    public event System.Action<Vector2, AsteroidTypeData> ColossusSpawned;
    /// El coloso recién creado (lo usa el BuffSpawner para que uno traiga un buff)
    public static event System.Action<Asteroid> ColossusBodySpawned;
    public int AliveCount => alive;
    /// Mapas de fractura: más colosos. Lo pone GameDirector en cada nivel
    public float ChanceMultiplier { get; set; } = 1f;

    float levelStart, lastSpawnTime = -999f;
    int alive, spawnedThisLevel, currentLevel = 1;

    public float ChanceAt(int level) =>
        Mathf.Min(maxChance, baseChance * Mathf.Pow(growthPerLevel, Mathf.Max(0, level - 1)));

    public void BeginLevel(int level)
    {
        currentLevel = Mathf.Max(1, level);
        levelStart = Time.time;
        lastSpawnTime = -999f;
        alive = 0;
        spawnedThisLevel = 0;
    }

    /// ¿Debe entrar un coloso en lugar del próximo asteroide normal?
    public bool ShouldSpawn(int level)
    {
        if (pool == null || PickType(level) == null) return false;
        if (alive >= maxAlive) return false;

        float sinceStart = Time.time - levelStart;
        if (sinceStart < minSecondsAfterStart) return false;

        int tier = Mathf.Max(0, (level - 1) / Mathf.Max(1, tierLength));
        float cooldown = Mathf.Max(minCooldown, cooldownSeconds - tier * cooldownReductionPerTier);
        if (Time.time - lastSpawnTime < cooldown) return false;

        bool guaranteed = level <= guaranteeUntilLevel && guaranteeAfterSeconds > 0f &&
                          spawnedThisLevel == 0 && sinceStart >= guaranteeAfterSeconds;
        return guaranteed || Random.value < Mathf.Min(1f, ChanceAt(level) * ChanceMultiplier);
    }

    public Asteroid Spawn(int level)
    {
        var type = PickType(level);
        var flow = FlowField.Instance;
        if (type == null || flow == null || pool == null) return null;

        // Tamaño recortado para que SIEMPRE quede un pasillo transitable al otro lado
        Rect camRect = GameBounds.Instance != null ? GameBounds.Instance.CameraRect : flow.SpawnRect;
        float size = type.RandomSize();
        float r = Mathf.Max(0.05f, approxRadiusPerUnit);
        float maxSize = Mathf.Max(type.sizeRange.x * 0.5f,
                                  (camRect.height - Mathf.Max(0f, safeCorridor)) / (2f * r));
        if (size > maxSize) size = maxSize;

        Vector2 p = EntryPoint(flow, size * r);

        var a = pool.Spawn(type, p, size);
        alive++;
        spawnedThisLevel++;
        lastSpawnTime = Time.time;

        float corridor = camRect.height - (a != null ? a.WorldRadius * 2f : size * 2f * r);
        Debug.Log($"ColossusDirector: entra '{type.id}' tamaño {size:0.00} en {p}. " +
                  $"Pasillo libre: {corridor:0.0} u. Probabilidad: {ChanceAt(level):P2}");
        ColossusSpawned?.Invoke(p, type);
        if (a != null) ColossusBodySpawned?.Invoke(a);
        return a;
    }

    /// Nace fuera de pantalla pero SIEMPRE a una altura (o anchura) visible, para que su
    /// trayectoria cruce el campo de juego y el jugador lo vea venir.
    Vector2 EntryPoint(FlowField flow, float worldRadius)
    {
        Rect spawn = flow.SpawnRect;
        Rect cam = GameBounds.Instance != null ? GameBounds.Instance.CameraRect : spawn;
        Vector2 avoid = player != null ? (Vector2)player.position : cam.center;

        Vector2 p = flow.RandomEntryPoint(avoid, avoidPlayerRadius);
        const float eps = 0.05f;
        bool fromSide = p.x <= spawn.xMin + eps || p.x >= spawn.xMax - eps;

        float inset = Mathf.Max(0f, visibleInset);
        if (fromSide)
        {
            if (hugFarEdge)
            {
                // Se pega al borde contrario al jugador: así el pasillo libre queda
                // entero del lado del jugador, en vez de partido en dos por el centro.
                p.y = avoid.y > cam.center.y
                    ? cam.yMin + worldRadius * 0.9f
                    : cam.yMax - worldRadius * 0.9f;
            }
            else
            {
                float min = cam.yMin + inset, max = cam.yMax - inset;
                if (min > max) min = max = cam.center.y;
                p.y = PickAway(min, max, avoid.y, avoidPlayerRadius);
            }
            p.x += p.x <= spawn.xMin + eps ? -extraPush : extraPush;
        }
        else
        {
            if (hugFarEdge)
            {
                p.x = avoid.x > cam.center.x
                    ? cam.xMin + worldRadius * 0.9f
                    : cam.xMax - worldRadius * 0.9f;
            }
            else
            {
                float min = cam.xMin + inset, max = cam.xMax - inset;
                if (min > max) min = max = cam.center.x;
                p.x = PickAway(min, max, avoid.x, avoidPlayerRadius);
            }
            p.y += p.y <= spawn.yMin + eps ? -extraPush : extraPush;
        }
        return p;
    }

    // Un valor dentro de la banda visible, lejos del jugador. Si no hay hueco, el extremo más lejano.
    static float PickAway(float min, float max, float avoid, float radius)
    {
        for (int i = 0; i < 8; i++)
        {
            float v = Random.Range(min, max);
            if (Mathf.Abs(v - avoid) > radius) return v;
        }
        return Mathf.Abs(min - avoid) > Mathf.Abs(max - avoid) ? min : max;
    }

    public void NotifyGone() { alive = Mathf.Max(0, alive - 1); }

    AsteroidTypeData PickType(int level)
    {
        if (colossusTypes == null) return null;
        float total = 0f;
        foreach (var t in colossusTypes)
            if (t != null && t.behavior == AsteroidBehavior.Colossus && t.minLevel <= level &&
                t.sprites != null && t.sprites.Length > 0)
                total += 1f;
        if (total <= 0f) return null;

        float pick = Random.value * total;
        foreach (var t in colossusTypes)
        {
            if (t == null || t.behavior != AsteroidBehavior.Colossus || t.minLevel > level ||
                t.sprites == null || t.sprites.Length == 0) continue;
            pick -= 1f;
            if (pick <= 0f) return t;
        }
        return null;
    }

    // Para probar sin esperar: clic derecho en el componente
    [ContextMenu("Forzar coloso ahora")]
    void ForceNow() { Spawn(currentLevel); }
}
