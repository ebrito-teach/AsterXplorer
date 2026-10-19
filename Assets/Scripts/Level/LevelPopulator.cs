using System.Collections.Generic;
using UnityEngine;

// Se ejecuta después de GameBounds y de PlayerStart
[DefaultExecutionOrder(10)]
public class LevelPopulator : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private AsteroidPool pool;
    [SerializeField] private Transform player;
    [SerializeField] private DifficultyProfile difficulty;
    [SerializeField] private ColossusDirector colossus;     // opcional
    [Tooltip("Los cometas van por su cuenta, fuera del presupuesto. Si un tipo de la lista " +
             "Types tiene perfil de cometa, aquí se ignora: lo pone el CometDirector")]
    [SerializeField] private CometDirector comets;          // opcional
    [SerializeField] private AsteroidTypeData[] types;

    [Header("Nivel (para probar)")]
    [SerializeField] private int level = 1;
    [SerializeField] private bool randomSeed = true;
    [SerializeField] private int seed = 12345;

    [Header("Colocación")]
    [SerializeField] private float playerSafeRadius = 2.5f;
    [Header("Medidas del jugador (se leen solas)")]
    [Tooltip("Las lee del jugador en cada nivel: el radio de su collider y el Max Speed de " +
             "su PlayerController. Déjalo marcado — así el día que cambies cómo se mueve el " +
             "jugador, el verificador se entera solo. Desmárcalo únicamente para probar")]
    [SerializeField] private bool readPlayerMetrics = true;
    [Tooltip("Radio del jugador en unidades. Por dónde cabe")]
    [SerializeField] private float playerRadius = 0.3f;
    [Tooltip("Velocidad del jugador en unidades por segundo. De esto depende que el " +
             "verificador sepa si un hueco da tiempo a aprovecharlo. Poner aquí más de lo " +
             "real hace que apruebe niveles que NO se pueden pasar")]
    [SerializeField] private float playerSpeed = 2.6f;
    [SerializeField] private float staticEdgeMargin = 0.8f;
    [SerializeField] private float blockedMargin = 0.6f;
    [SerializeField] private float goalMargin = 1.5f;

    [Header("Mantenimiento de la población")]
    [Tooltip("Repone asteroides hasta volver a la cantidad del nivel. Sin esto, cada paso " +
             "del coloso vacía la pantalla para siempre y el nivel se regala")]
    [SerializeField] private bool keepPopulation = true;
    [Tooltip("Cada cuánto comprueba la población (segundos)")]
    [SerializeField] private float topUpInterval = 0.6f;
    [Tooltip("Cuántos repone como mucho en cada comprobación, para que el cinturón se " +
             "rellene poco a poco y no de golpe")]
    [SerializeField] private int topUpPerCheck = 2;
    [Tooltip("La corriente cambia de dirección en cada nivel, derivada de la semilla. " +
             "Sin esto todos los niveles fluyen hacia el mismo lado y se sienten iguales")]
    [SerializeField] private bool randomizeFlowPerLevel = true;

    readonly List<AsteroidTypeData> unlocked = new List<AsteroidTypeData>();
    int currentLevel;
    int targetPopulation;
    float nextTopUp;

    bool autoStart = true;

    /// El GameDirector llama a esto en su Awake: a partir de ahí el nivel lo decide él.
    public void SuppressAutoStart() { autoStart = false; }

    void Start()
    {
        if (!autoStart) return;
        StartLevel(level, randomSeed ? Random.Range(0, int.MaxValue) : seed);
    }

    void OnDestroy() { if (pool != null) pool.OnExited -= HandleExit; }

    [ContextMenu("Reiniciar nivel")]
    void RestartFromMenu() { StartLevel(level, randomSeed ? Random.Range(0, int.MaxValue) : seed); }

    /// Con la misma semilla, el reparto inicial es el mismo.
    public void StartLevel(int newLevel, int newSeed)
    {
        var bounds = GameBounds.Instance;
        if (bounds == null || !bounds.IsReady) { Debug.LogWarning("LevelPopulator: GameBounds no está listo."); return; }
        if (pool == null || difficulty == null) { Debug.LogError("LevelPopulator: falta el Pool o el Difficulty Profile."); return; }

        currentLevel = Mathf.Max(1, newLevel);
        Random.InitState(newSeed);

        pool.OnExited -= HandleExit;
        pool.OnExited += HandleExit;
        pool.ReleaseAll();
        if (colossus != null) colossus.BeginLevel(currentLevel);
        if (comets != null) comets.BeginLevel(currentLevel);

        Asteroid.SpeedMultiplier = difficulty.SpeedMultiplier(currentLevel);
        if (FlowField.Instance != null)
        {
            // Cada nivel tiene su propia corriente, reproducible con la semilla
            if (randomizeFlowPerLevel) FlowField.Instance.Randomize(newSeed);
            FlowField.Instance.SetAmplitude(difficulty.FlowAmplitude(currentLevel));
        }

        // Tipos disponibles en este nivel (los colosos los gestiona su director)
        unlocked.Clear();
        foreach (var t in types)
        {
            if (t == null || t.behavior == AsteroidBehavior.Colossus || t.behavior == AsteroidBehavior.Debris) continue;
            if (t.comet != null) continue;   // los cometas los pone el CometDirector
            if (t.sprites == null || t.sprites.Length == 0 || t.minLevel > currentLevel) continue;
            unlocked.Add(t);
        }
        if (unlocked.Count == 0) { Debug.LogWarning("LevelPopulator: no hay tipos disponibles para este nivel."); return; }

        // Compra asteroides con el presupuesto del nivel
        float budget = difficulty.Budget(currentLevel);
        var shopping = new List<AsteroidTypeData>();
        int moving = 0;
        for (int guard = 0; guard < 500; guard++)
        {
            var t = PickWeighted(unlocked, budget, moving >= difficulty.maxMovingAsteroids);
            if (t == null) break;
            shopping.Add(t);
            budget -= Cost(t);
            if (t.behavior != AsteroidBehavior.Static) moving++;
        }

        // Mundo amplio: el presupuesto está pensado para UNA pantalla. Los que se mueven
        // nacen alrededor de la cámara, así que con eso basta. Los estáticos se reparten por
        // todo el mundo y son persistentes: hay que comprar los suyos para cada pantalla de
        // área, o el mundo quedaría con la cuarta parte de densidad de terreno.
        int extraStatics = 0;
        if (bounds.IsExpanded)
        {
            float screenArea = Mathf.Max(1f, bounds.ScreenSize.x * bounds.ScreenSize.y);
            float ratio = (bounds.Area.width * bounds.Area.height) / screenArea;
            float staticSpent = 0f;
            foreach (var t in shopping)
                if (t.behavior == AsteroidBehavior.Static) staticSpent += Cost(t);

            float extra = staticSpent * Mathf.Max(0f, ratio - 1f);
            for (int guard = 0; guard < 2000 && extra > 0f; guard++)
            {
                var t = PickWeighted(unlocked, extra, true);
                if (t == null) break;
                shopping.Add(t);
                extra -= Cost(t);
                extraStatics++;
            }
        }

        // Colócalos
        Rect area = bounds.Area;
        Rect staticRect = Shrink(area, staticEdgeMargin);
        Rect movingRect = FlowField.Instance != null ? FlowField.Instance.SpawnRect : area;
        Rect goal = Expand(bounds.GoalCorner, goalMargin);
        // El punto donde se PARA, no donde está: durante la expulsión aún viene de camino
        Vector2 playerPos = area.center;
        if (player != null)
        {
            var pc = player.GetComponentInParent<PlayerController>();
            playerPos = pc != null ? pc.SpawnPoint : (Vector2)player.position;
        }
        var placed = new List<(Vector2 pos, float r)>();
        int count = 0;

        foreach (var t in shopping)
        {
            bool isStatic = t.behavior == AsteroidBehavior.Static;
            float size = t.RandomSize();
            float approxR = size * 0.7f;
            Rect rect = isStatic ? staticRect : movingRect;

            for (int attempt = 0; attempt < 40; attempt++)
            {
                var p = new Vector2(Random.Range(rect.xMin, rect.xMax), Random.Range(rect.yMin, rect.yMax));

                if (Vector2.Distance(p, playerPos) < playerSafeRadius + approxR) continue;
                if (isStatic && (goal.Contains(p) || bounds.IsBlocked(p, blockedMargin + approxR))) continue;
                if (Overlaps(placed, p, approxR)) continue;

                pool.Spawn(t, p, size);
                placed.Add((p, approxR));
                count++;
                break;
            }
        }

        // Garantía de que el nivel se puede pasar. Va DESPUÉS de colocar: comprueba el
        // reparto real, no el que pretendíamos. Si hay paso no toca nada.
        string fairness = "verificación desactivada";
        // Con el mundo grande, el verificador de camino completo deja de tener sentido (la
        // meta está escondida y lo que fluye aún no existe). Se sustituye por dos
        // comprobaciones locales en un paso posterior del Hito 3.5; hasta entonces, apagado.
        if (bounds.IsExpanded) fairness = "verificación de camino apagada (mundo amplio)";
        else if (difficulty.enforceSolvable)
        {
            ReadPlayerMetrics();
            var r = SolvabilityGate.Enforce(
                pool.Active, playerPos, playerRadius, playerSpeed,
                area, bounds.GoalCorner, difficulty,
                a => { pool.Release(a); count--; });

            fairness = $"{r.Describe()} [jugador r{playerRadius:0.00} v{playerSpeed:0.0}, " +
                       $"{r.steps} pasos, {r.millis:0.#} ms]";
            if (!r.solvable)
                Debug.LogWarning($"LevelPopulator: nivel {currentLevel} sigue sin paso después " +
                                 $"de retirar {r.removed}. Si se repite, baja budgetCap o budgetK.");
            else if (r.removed >= Mathf.Max(1, difficulty.solveMaxRemovals - 2))
                Debug.LogWarning($"LevelPopulator: nivel {currentLevel} necesitó retirar {r.removed} " +
                                 "asteroides. El verificador está tapando un problema de la curva.");
        }

        targetPopulation = count;
        nextTopUp = Time.time + 2f;

        string mood = difficulty.IsCalm(currentLevel) ? ", respiro"
                    : difficulty.IsSurge(currentLevel) ? ", APRETÓN" : "";
        if (extraStatics > 0) fairness += $", +{extraStatics} estáticos por el mundo amplio";
        Debug.Log($"LevelPopulator: nivel {currentLevel} (etapa {difficulty.Tier(currentLevel)}{mood}), semilla {newSeed}: " +
                  $"{count}/{shopping.Count} asteroides, presupuesto {difficulty.Budget(currentLevel):0.#}, " +
                  $"velocidad x{Asteroid.SpeedMultiplier:0.00}, corriente {(FlowField.Instance != null ? FlowField.Instance.BaseAngle : 0f):0}° — {fairness}");
    }

    // ------------------------------------------------------------- población
    // Red de seguridad contra TODA fuga de asteroides, presente y futura: trozos que
    // caducan, estáticos soltados, asteroides que se comen un coloso al entrar. En vez de
    // tapar cada caso, se cuenta lo que hay vivo y se repone hasta la cantidad del nivel.
    void Update()
    {
        if (!keepPopulation || pool == null || targetPopulation <= 0) return;
        if (Time.time < nextTopUp) return;
        nextTopUp = Time.time + Mathf.Max(0.1f, topUpInterval);

        int live = 0;
        var active = pool.Active;
        for (int i = 0; i < active.Count; i++)
        {
            var a = active[i];
            if (a == null || a.Type == null) continue;
            if (a.Type.behavior == AsteroidBehavior.Debris) continue;
            if (a.Type.behavior == AsteroidBehavior.Colossus) continue;
            if (a.Type.comet != null) continue;
            live++;
        }

        // Tope de densidad local: si el jugador ya está rodeado, la reposición espera.
        // No quita nada de lo que hay — eso sí sería regalar el nivel — solo evita que el
        // repoblador siga echando leña a una situación que ya está mal. Es la diferencia
        // entre un momento difícil y un momento imposible.
        if (difficulty != null && difficulty.crowdCap > 0 && player != null
            && CrowdAroundPlayer() >= difficulty.crowdCap) return;

        int missing = Mathf.Min(targetPopulation - live, Mathf.Max(1, topUpPerCheck));
        for (int i = 0; i < missing; i++) SpawnAtBorder(PickAnyMoving());
    }

    /// Toma el radio y la velocidad del jugador de verdad, en vez de fiarse de dos números
    /// escritos a mano que se quedan viejos en cuanto se retoca cómo se mueve.
    ///
    /// Esto importa más de lo que parece: el verificador decide si un hueco se puede
    /// aprovechar comparando el tamaño del hueco con lo que el jugador recorre en ese tiempo.
    /// Si aquí pone 5 y el jugador va a 2.6, aprobará como pasables niveles que no lo son —
    /// y lo hará en silencio, que es lo peor que puede hacer un sistema de justicia.
    void ReadPlayerMetrics()
    {
        if (!readPlayerMetrics || player == null) return;

        var col = player.GetComponentInChildren<Collider2D>();
        if (col != null)
            playerRadius = Mathf.Max(0.05f, Mathf.Min(col.bounds.extents.x, col.bounds.extents.y));

        var pc = player.GetComponentInParent<PlayerController>();
        if (pc != null) playerSpeed = Mathf.Max(0.2f, pc.MaxSpeed);
        
    }

    int CrowdAroundPlayer()
    {
        float r = difficulty.crowdRadius;
        float r2 = r * r;
        Vector2 p = player.position;
        int n = 0;

        var active = pool.Active;
        for (int i = 0; i < active.Count; i++)
        {
            var a = active[i];
            if (a == null || a.Type == null || !a.Lethal) continue;
            if (((Vector2)a.transform.position - p).sqrMagnitude <= r2) n++;
        }
        return n;
    }

    // Un asteroide salió del área: entra otro por el lado de entrada
    void HandleExit(Asteroid a)
    {
        bool wasColossus = a.Type.behavior == AsteroidBehavior.Colossus;
        bool wasStatic = a.Type.behavior == AsteroidBehavior.Static;
        var oldType = a.Type;
        pool.Release(a);

        // Un cometa que se va no se sustituye aquí: el CometDirector decide cuándo entra el
        // siguiente, y siempre del tipo sorteado para el nivel
        if (oldType.comet != null) return;

        if (wasColossus && colossus != null) colossus.NotifyGone();
        if (FlowField.Instance == null) return;

        // ¿Toca un coloso? Entra ADEMÁS, no en lugar del asteroide: si lo sustituyera,
        // cada aparición le quitaría un cuerpo al cinturón para siempre.
        if (!wasColossus && colossus != null && colossus.ShouldSpawn(currentLevel))
            colossus.Spawn(currentLevel);

        // Un estático que sale es uno que el coloso soltó: ya no es terreno, así que se
        // repone con uno en movimiento. Colocar un estático nuevo en mitad del nivel
        // lo haría aparecer de la nada encima del jugador.
        var type = (wasColossus || wasStatic) ? PickAnyMoving() : PickReplacement(oldType);
        SpawnAtBorder(type);
    }

    void SpawnAtBorder(AsteroidTypeData type)
    {
        if (type == null || FlowField.Instance == null) return;
        Vector2 avoid = player != null ? (Vector2)player.position : Vector2.zero;

        // Ventana de reacción: un asteroide que entra ya apuntado al jugador y le llega
        // antes de que le dé tiempo a verlo no es dificultad, es una emboscada. Se prueban
        // varios puntos de entrada y se descarta el que no deje margen.
        //
        // Ojo a lo que NO hace: no impide que te alcance. Solo garantiza que, cuando entra,
        // tienes tiempo de verlo y decidir. Lo que pase después es tuyo.
        Vector2 p = Vector2.zero;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            p = FlowField.Instance.RandomEntryPoint(avoid, playerSafeRadius);
            if (player == null || difficulty == null || GivesTimeToReact(p, type, avoid)) break;
        }

        pool.Spawn(type, p);
    }

    /// ¿Deja este punto de entrada tiempo de reacción? Calcula el momento de máxima
    /// aproximación entre el asteroide y el jugador quieto: si va a pasarle por encima y lo
    /// hace antes de la ventana, no vale.
    bool GivesTimeToReact(Vector2 entry, AsteroidTypeData type, Vector2 playerPos)
    {
        float window = difficulty.reactionWindow;
        if (window <= 0f) return true;

        float speed = Mathf.Max(0.1f,
            (type.speedRange.x + type.speedRange.y) * 0.5f * Asteroid.SpeedMultiplier);
        Vector2 v = FlowField.FromAngle(FlowField.Instance.AngleAt(entry)) * speed;

        Vector2 rel = playerPos - entry;
        float tClosest = Vector2.Dot(rel, v) / Mathf.Max(0.0001f, v.sqrMagnitude);
        if (tClosest <= 0f) return true;                 // se aleja: no es amenaza

        float missBy = (rel - v * tClosest).magnitude;
        float hitRadius = playerRadius + type.sizeRange.y * 0.7f;
        if (missBy > hitRadius) return true;             // no le pasa por encima

        return tClosest >= window;
    }

    AsteroidTypeData PickReplacement(AsteroidTypeData old)
    {
        float total = 0f;
        foreach (var t in unlocked)
            if (t.behavior == old.behavior) total += Mathf.Max(0f, t.spawnWeight);
        if (total <= 0f) return PickAnyMoving();

        float pick = Random.value * total;
        foreach (var t in unlocked)
        {
            float w = Mathf.Max(0f, t.spawnWeight);
            if (t.behavior != old.behavior || w <= 0f) continue;
            pick -= w;
            if (pick <= 0f) return t;
        }
        return old;
    }

    AsteroidTypeData PickAnyMoving()
    {
        float total = 0f;
        foreach (var t in unlocked)
            if (t.behavior != AsteroidBehavior.Static) total += Mathf.Max(0f, t.spawnWeight);
        if (total <= 0f) return null;

        float pick = Random.value * total;
        foreach (var t in unlocked)
        {
            float w = Mathf.Max(0f, t.spawnWeight);
            if (t.behavior == AsteroidBehavior.Static || w <= 0f) continue;
            pick -= w;
            if (pick <= 0f) return t;
        }
        return null;
    }

    static float Cost(AsteroidTypeData t) => Mathf.Max(0.5f, t.cost);

    static AsteroidTypeData PickWeighted(List<AsteroidTypeData> list, float maxCost, bool onlyStatic)
    {
        float total = 0f;
        foreach (var t in list)
            if (Cost(t) <= maxCost && (!onlyStatic || t.behavior == AsteroidBehavior.Static))
                total += Mathf.Max(0f, t.spawnWeight);
        if (total <= 0f) return null;

        float pick = Random.value * total;
        foreach (var t in list)
        {
            float w = Mathf.Max(0f, t.spawnWeight);
            if (Cost(t) > maxCost || (onlyStatic && t.behavior != AsteroidBehavior.Static) || w <= 0f) continue;
            pick -= w;
            if (pick <= 0f) return t;
        }
        return null;
    }

    static bool Overlaps(List<(Vector2 pos, float r)> placed, Vector2 p, float r)
    {
        foreach (var o in placed)
            if (Vector2.Distance(p, o.pos) < r + o.r + 0.1f) return true;
        return false;
    }

    static Rect Shrink(Rect r, float m) =>
        new Rect(r.xMin + m, r.yMin + m, r.width - 2f * m, r.height - 2f * m);

    static Rect Expand(Rect r, float m) =>
        new Rect(r.xMin - m, r.yMin - m, r.width + 2f * m, r.height + 2f * m);
}