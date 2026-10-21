using UnityEngine;

/// Los cometas del nivel. Van aparte del presupuesto de asteroides: así no le restan
/// densidad al cinturón y nunca falta un cometa.
///
/// En cada nivel se sortea UN tipo (fuego, hielo o veneno) y solo entra ese tipo hasta que el
/// nivel acaba. Se vuelve a sortear al empezar cada nivel, tanto al ganar como al morir.
///
/// El sorteo usa su propio generador, no la semilla del nivel: el juego repite la misma
/// semilla al reintentar un nivel, y con ella saldría siempre el mismo cometa.
public class CometDirector : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private AsteroidPool pool;
    [SerializeField] private Transform player;
    [Tooltip("Los tipos de cometa. En cada nivel se elige uno al azar")]
    [SerializeField] private AsteroidTypeData[] cometTypes;

    [Header("Cantidad")]
    [Tooltip("Cometas a la vez en el nivel. Se sortea en cada nivel dentro de este rango")]
    [SerializeField] private Vector2Int aliveRange = new Vector2Int(1, 2);

    [Header("Tiempos")]
    [Tooltip("Segundos desde que empieza el nivel hasta que entra el primero")]
    [SerializeField] private Vector2 firstDelay = new Vector2(3f, 6f);
    [Tooltip("Segundos desde que uno se va hasta que entra el siguiente")]
    [SerializeField] private Vector2 respawnDelay = new Vector2(4f, 9f);

    [Header("Entrada")]
    [Tooltip("No entra a menos de esta distancia del jugador")]
    [SerializeField] private float avoidPlayerRadius = 4f;

    public AsteroidTypeData CurrentType { get; private set; }
    public int TargetAlive { get; private set; }

    System.Random rng;
    float nextSpawn, nextCount;
    int lastAlive;

    void Awake()
    {
        rng = new System.Random(System.Environment.TickCount ^ GetInstanceID());
    }

    /// Mundo oscuro: no entra ningún cometa. Lo pone GameDirector en cada nivel
    public bool Suppressed { get; set; }

    public void BeginLevel(int level)
    {
        CometTrail.Clear();
        CurrentType = PickType(level);
        TargetAlive = CurrentType != null ? Range(aliveRange.x, aliveRange.y) : 0;
        lastAlive = 0;
        nextCount = 0f;
        nextSpawn = Time.time + Range(firstDelay.x, firstDelay.y);

        Debug.Log(CurrentType != null
            ? $"CometDirector: nivel {level}, cometa '{CurrentType.id}', {TargetAlive} a la vez."
            : $"CometDirector: nivel {level}, sin cometas disponibles.");
    }

    void Update()
    {
        if (CurrentType == null || pool == null || TargetAlive <= 0) return;
        if (Asteroid.WorldFrozen || Suppressed) return;   // TimeFreeze: no entra nada nuevo
        if (Time.time < nextCount) return;
        nextCount = Time.time + 0.25f;

        int alive = CountAlive();

        // Se fue uno: el siguiente espera su turno desde ahora, no entra de golpe
        if (alive < lastAlive)
            nextSpawn = Mathf.Max(nextSpawn, Time.time + Range(respawnDelay.x, respawnDelay.y));
        lastAlive = alive;

        if (alive >= TargetAlive || Time.time < nextSpawn) return;

        var flow = FlowField.Instance;
        if (flow == null) return;

        Vector2 avoid = player != null ? (Vector2)player.position : Vector2.zero;
        Vector2 p = flow.RandomEntryPoint(avoid, avoidPlayerRadius);
        if (pool.Spawn(CurrentType, p) != null)
        {
            lastAlive = alive + 1;
            nextSpawn = Time.time + Range(respawnDelay.x, respawnDelay.y);
        }
    }

    int CountAlive()
    {
        int n = 0;
        var active = pool.Active;
        for (int i = 0; i < active.Count; i++)
        {
            var a = active[i];
            if (a != null && a.Type == CurrentType) n++;
        }
        return n;
    }

    AsteroidTypeData PickType(int level)
    {
        if (cometTypes == null) return null;
        int valid = 0;
        foreach (var t in cometTypes) if (IsUsable(t, level)) valid++;
        if (valid == 0) return null;

        int pick = rng.Next(valid);
        foreach (var t in cometTypes)
        {
            if (!IsUsable(t, level)) continue;
            if (pick-- == 0) return t;
        }
        return null;
    }

    static bool IsUsable(AsteroidTypeData t, int level) =>
        t != null && t.comet != null && t.minLevel <= level && t.sprites != null && t.sprites.Length > 0;

    int Range(int min, int max) => rng.Next(Mathf.Min(min, max), Mathf.Max(min, max) + 1);
    float Range(float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

    [ContextMenu("Forzar cometa ahora")]
    void ForceNow() { nextSpawn = 0f; TargetAlive = Mathf.Max(TargetAlive, CountAlive() + 1); }
}
