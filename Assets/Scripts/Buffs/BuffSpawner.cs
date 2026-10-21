using UnityEngine;

/// Decide si en este mapa aparece un buff, y cómo. Como mucho uno por mapa (por intento:
/// cada intento es un mapa distinto y vuelve a sortear).
///
///   Map Chance       en un punto al azar del mapa, quieto
///   Fragment Chance  como uno de los fragmentos que suelta una roca al romperse o un cometa
///                    al pasar, a la vista del jugador
///   Colossus Chance  lo trae el siguiente coloso, dando vueltas a su alrededor
///   el resto         no aparece
///
/// Si toca fragmento o coloso y en ese mapa no se rompe nada o no pasa ningún coloso, ese mapa
/// se queda sin buff. Son raros a propósito.
///
/// Va en el mismo objeto que el BuffSystem.
public class BuffSpawner : MonoBehaviour
{
    [Range(0f, 1f)] [SerializeField] private float mapChance = 0.22f;
    [Range(0f, 1f)] [SerializeField] private float fragmentChance = 0.27f;
    [Range(0f, 1f)] [SerializeField] private float colossusChance = 0.27f;

    [Header("Aspecto y colocación")]
    [Tooltip("Tamaño del icono en el mundo, en unidades")]
    [SerializeField] private float worldSize = 0.6f;
    [Tooltip("Capa física del buff. Tiene que chocar con Player en la matriz de colisiones " +
             "(Goal ya lo hace)")]
    [SerializeField] private string layer = "Goal";
    [Tooltip("En el mapa: distancia mínima al punto de aparición del jugador y a la meta")]
    [SerializeField] private float minDistanceFromPlayer = 8f;
    [SerializeField] private float minDistanceFromGoal = 5f;
    [Tooltip("Fragmento: segundos desde que empieza el nivel antes de poder salir")]
    [SerializeField] private float fragmentDelay = 3f;
    [Tooltip("Coloso: distancia a su superficie y grados por segundo de la órbita")]
    [SerializeField] private float colossusGap = 1.6f;
    [SerializeField] private float colossusOrbitSpeed = 35f;

    enum Pending { None, Fragment, Colossus }

    GameDirector director;
    PlayerController player;
    Goal goal;
    BuffPickup current;
    Pending pending;
    BuffType pendingType;
    float levelStart;

    void OnEnable()
    {
        director = FindFirstObjectByType<GameDirector>();
        player = FindFirstObjectByType<PlayerController>();
        goal = FindFirstObjectByType<Goal>();
        if (director != null) director.LevelStarted += OnLevelStarted;
        AsteroidBreaker.Broken += OnBroken;
        Asteroid.CometShed += OnCometShed;
        ColossusDirector.ColossusBodySpawned += OnColossus;
    }

    void OnDisable()
    {
        if (director != null) director.LevelStarted -= OnLevelStarted;
        AsteroidBreaker.Broken -= OnBroken;
        Asteroid.CometShed -= OnCometShed;
        ColossusDirector.ColossusBodySpawned -= OnColossus;
    }

    void OnLevelStarted(int level)
    {
        if (current != null) Destroy(current.gameObject);
        current = null;
        pending = Pending.None;
        levelStart = Time.time;

        var bs = BuffSystem.Instance;
        if (bs == null) return;
        pendingType = bs.PickRandom();

        float r = Random.value;
        if (r < mapChance) { PlaceOnMap(); return; }
        r -= mapChance;
        if (r < fragmentChance) { pending = Pending.Fragment; Log("saldrá entre los fragmentos"); return; }
        r -= fragmentChance;
        if (r < colossusChance) { pending = Pending.Colossus; Log("lo traerá el próximo coloso"); return; }
        Debug.Log("BuffSpawner: este mapa no tiene buff.");
    }

    void Log(string how) => Debug.Log($"BuffSpawner: {pendingType} {how}.");

    void PlaceOnMap()
    {
        var gb = GameBounds.Instance;
        if (gb == null || !gb.IsReady) return;
        Rect a = gb.Area;
        Vector2 spawn = player != null ? player.SpawnPoint : a.center;
        Vector2 g = goal != null ? (Vector2)goal.transform.position : new Vector2(1e6f, 1e6f);
        int mask = LayerMask.GetMask("AsteroidStatic");
        Physics2D.SyncTransforms();   // los estáticos del mapa nuevo acaban de colocarse
        for (int i = 0; i < 40; i++)
        {
            var p = new Vector2(Random.Range(a.xMin + 2f, a.xMax - 2f), Random.Range(a.yMin + 2f, a.yMax - 2f));
            if (Vector2.Distance(p, spawn) < minDistanceFromPlayer) continue;
            if (Vector2.Distance(p, g) < minDistanceFromGoal) continue;
            if (Physics2D.OverlapCircle(p, worldSize + 0.8f, mask) != null) continue;
            current = BuffPickup.Create(pendingType, p, worldSize, layer);
            Debug.Log($"BuffSpawner: {pendingType} en el mapa, en {p}.");
            return;
        }
        Debug.Log("BuffSpawner: no encontré sitio libre para el buff; este mapa se queda sin él.");
    }

    bool InView(Vector2 p)
    {
        var gb = GameBounds.Instance;
        return gb == null || !gb.IsReady || gb.CameraRect.Contains(p);
    }

    bool FragmentReady => pending == Pending.Fragment && current == null &&
                          Time.time - levelStart >= fragmentDelay && director != null && director.Running;

    void OnBroken(Vector2 pos, float size, int variant)
    {
        if (!FragmentReady || !InView(pos)) return;
        SpawnFragment(pos, Random.insideUnitCircle.normalized * Random.Range(1.5f, 3f));
    }

    void OnCometShed(Vector2 pos, Vector2 vel)
    {
        if (!FragmentReady || !InView(pos)) return;
        SpawnFragment(pos, vel + Random.insideUnitCircle * 0.8f);
    }

    void SpawnFragment(Vector2 pos, Vector2 vel)
    {
        pending = Pending.None;
        current = BuffPickup.Create(pendingType, pos, worldSize, layer);
        current.Drift(vel, 0.8f);
        Debug.Log($"BuffSpawner: {pendingType} salió entre los fragmentos en {pos}.");
    }

    void OnColossus(Asteroid colossus)
    {
        if (pending != Pending.Colossus || current != null || colossus == null) return;
        pending = Pending.None;
        current = BuffPickup.Create(pendingType, colossus.Body.position, worldSize, layer);
        current.Orbit(colossus, colossus.WorldRadius + colossusGap, colossusOrbitSpeed);
        Debug.Log($"BuffSpawner: {pendingType} viene con el coloso.");
    }

    [ContextMenu("Poner un buff en el mapa ahora")]
    void ForceMap()
    {
        if (current != null) Destroy(current.gameObject);
        var bs = BuffSystem.Instance;
        if (bs != null) pendingType = bs.PickRandom();
        PlaceOnMap();
    }
}
