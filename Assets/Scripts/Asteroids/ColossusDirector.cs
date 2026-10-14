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

    /// Posición de entrada y tipo. Aquí se conectarán la alerta del HUD y el sonido.
    public event System.Action<Vector2, AsteroidTypeData> ColossusSpawned;
    public int AliveCount => alive;

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
        return guaranteed || Random.value < ChanceAt(level);
    }

    public Asteroid Spawn(int level)
    {
        var type = PickType(level);
        var flow = FlowField.Instance;
        if (type == null || flow == null || pool == null) return null;

        Vector2 p = EntryPoint(flow);

        var a = pool.Spawn(type, p);
        alive++;
        spawnedThisLevel++;
        lastSpawnTime = Time.time;

        Debug.Log($"ColossusDirector: entra '{type.id}' en {p}. Probabilidad actual por asteroide: {ChanceAt(level):P2}");
        ColossusSpawned?.Invoke(p, type);
        return a;
    }

    /// Nace fuera de pantalla pero SIEMPRE a una altura (o anchura) visible, para que su
    /// trayectoria cruce el campo de juego y el jugador lo vea venir.
    Vector2 EntryPoint(FlowField flow)
    {
        Rect spawn = flow.SpawnRect;
        Rect cam = GameBounds.Instance != null ? GameBounds.Instance.CameraRect : spawn;
        Vector2 avoid = player != null ? (Vector2)player.position : Vector2.zero;

        Vector2 p = flow.RandomEntryPoint(avoid, avoidPlayerRadius);
        const float eps = 0.05f;
        bool fromSide = p.x <= spawn.xMin + eps || p.x >= spawn.xMax - eps;

        // Banda visible, con un margen por dentro para que no entre rozando la esquina
        float inset = Mathf.Max(0f, visibleInset);
        if (fromSide)
        {
            float min = cam.yMin + inset, max = cam.yMax - inset;
            if (min > max) { min = max = cam.center.y; }
            p.y = PickAway(min, max, avoid.y, avoidPlayerRadius);
            p.x += p.x <= spawn.xMin + eps ? -extraPush : extraPush;
        }
        else
        {
            float min = cam.xMin + inset, max = cam.xMax - inset;
            if (min > max) { min = max = cam.center.x; }
            p.x = PickAway(min, max, avoid.x, avoidPlayerRadius);
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
