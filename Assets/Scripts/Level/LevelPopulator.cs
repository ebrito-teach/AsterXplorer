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

    [Header("Órbitas")]
    [Tooltip("De qué están hechos los cinturones si Belt Types está vacío (por ejemplo Debris_Gravel y Debris_Fragment)")]
    [SerializeField] private AsteroidTypeData[] satelliteTypes;
    [Tooltip("Velocidad angular (grados/s) de un cuerpo en órbita a la distancia de referencia de " +
             "un cuerpo de masa media. Con Kepler, el resto sale solo")]
    [SerializeField] private float keplerSpeed = 55f;
    [Tooltip("Radio de órbita (unidades) al que corresponde esa velocidad")]
    [SerializeField] private float keplerRefRadius = 2.5f;

    [Header("Mundo oscuro")]
    [Tooltip("Mundo vacío: fracción del presupuesto normal del nivel (estáticos y lo que fluye)")]
    [Range(0.05f, 1f)] [SerializeField] private float darkBudgetFactor = 0.3f;
    [Tooltip("Lo que fluye no mata. Cada este rango de segundos entra UNO que sí mata (lento o " +
             "rápido), y nunca hay más de uno a la vez")]
    [SerializeField] private Vector2 darkLethalEvery = new Vector2(20f, 40f);

    /// Mundo oscuro en este nivel. Lo pone GameDirector antes de StartLevel: sin colosos,
    /// lo que fluye inofensivo salvo raras excepciones; los estáticos y sus satélites, igual
    public bool DarkWorld { get; set; }

    [Header("Mapa de fractura")]
    [Tooltip("Fracción del presupuesto normal: menos asteroides normales (cometas y colosos van aparte, y hay más)")]
    [Range(0.1f, 1f)] [SerializeField] private float fractureBudgetFactor = 0.6f;
    /// Mapa de fractura en este nivel. Lo pone GameDirector antes de StartLevel
    public bool FractureWorld { get; set; }

    [Header("Estáticos agrupados")]
    [Tooltip("Los estáticos no se reparten uniformes: a veces nacen junto a otro, formando grupos con huecos entre ellos")]
    [SerializeField] private bool staticClusters = true;
    [Range(0f, 1f)] [SerializeField] private float clusterChance = 0.4f;
    [Tooltip("Hueco entre dos estáticos del mismo grupo (mínimo, máximo)")]
    [SerializeField] private Vector2 clusterGap = new Vector2(0.3f, 1.6f);

    [Header("Planetas")]
    [Tooltip("El tipo planeta (Behavior Static, Planet marcado, los 8 dibujos en Sprites). " +
             "Vacío = mapas sin planetas")]
    [SerializeField] private AsteroidTypeData planetType;
    [Tooltip("Probabilidad de que un mapa tenga planetas")]
    [Range(0f, 1f)] [SerializeField] private float planetMapChance = 0.4f;
    [Tooltip("Cuántos planetas en un mapa que los tenga (mínimo, máximo)")]
    [SerializeField] private Vector2Int planetCount = new Vector2Int(1, 4);
    [Tooltip("Pasillo libre mínimo entre el sistema de un planeta (con su anillo, cinturón o lunas) y cualquier otro planeta o estático")]
    [SerializeField] private float planetCorridor = 3f;
    [Tooltip("Parte del mundo que pueden ocupar como mucho todos los sistemas de planetas juntos")]
    [Range(0.05f, 0.6f)] [SerializeField] private float planetMaxWorldShare = 0.3f;
    [Tooltip("Distancia mínima del sistema a la grieta en los mapas de fractura")]
    [SerializeField] private float planetFractureClearance = 4f;
    [Tooltip("Qué lleva cada planeta: nada, anillos, cinturón o lunas (pesos relativos)")]
    [SerializeField] private Vector4 planetSystemWeights = new Vector4(0.25f, 0.25f, 0.25f, 0.25f);

    [Header("Planetas: anillos")]
    [SerializeField] private PlanetRing.Look[] ringLooks;
    [Tooltip("Radio del hueco del anillo respecto al radio del planeta (mínimo, máximo)")]
    [SerializeField] private Vector2 ringInner = new Vector2(1.25f, 1.5f);
    [Tooltip("Inclinación al azar del anillo, en grados a cada lado de la horizontal")]
    [SerializeField] private float ringTilt = 30f;
    [Tooltip("Cuánto frena el anillo al cruzarlo (0.5 = un 50 % más lento). No mata")]
    [Range(0f, 0.95f)] [SerializeField] private float ringSlow = 0.5f;

    [Header("Planetas: cinturón (mata)")]
    [Tooltip("De qué rocas está hecho (vacío = las de Satellite Types)")]
    [SerializeField] private AsteroidTypeData[] beltTypes;
    [Tooltip("Hueco entre el planeta y el cinturón, y ancho de la banda (mínimo, máximo)")]
    [SerializeField] private float beltGap = 1.2f;
    [SerializeField] private Vector2 beltWidth = new Vector2(1.8f, 3.2f);
    [Tooltip("Rocas por unidad cuadrada de banda (la cantidad sale del área)")]
    [SerializeField] private float beltDensity = 0.35f;
    [SerializeField] private int beltMax = 30;
    [SerializeField] private Vector2Int beltClusters = new Vector2Int(2, 4);
    [Tooltip("Cuánto se abre cada grupo a lo largo de la banda (grados)")]
    [SerializeField] private float beltClusterSpread = 35f;
    [Tooltip("Multiplica la velocidad de Kepler del cinturón (un planeta pesa mucho y giraría muy rápido)")]
    [SerializeField] private float beltSpeedFactor = 0.35f;

    [Header("Planetas: lunas (no matan)")]
    [Tooltip("El tipo planetoide (Behavior Debris, Kills Player desmarcado, los dibujos de los planetoides)")]
    [SerializeField] private AsteroidTypeData moonType;
    [SerializeField] private Vector2Int moonCount = new Vector2Int(1, 4);
    [Tooltip("Distancia de cada luna a la superficie del planeta (mínimo, máximo)")]
    [SerializeField] private Vector2 moonDistance = new Vector2(0.8f, 2.6f);
    [Tooltip("Parte de las lunas que orbitan; las demás se quedan en su sitio girando sobre sí mismas")]
    [Range(0f, 1f)] [SerializeField] private float moonOrbitShare = 0.5f;
    [Tooltip("Velocidad de las que orbitan, en grados/s (mínimo, máximo)")]
    [SerializeField] private Vector2 moonOrbitSpeed = new Vector2(8f, 20f);

    [Tooltip("Tope de cuerpos en órbita en todo el nivel (rendimiento)")]
    [SerializeField] private int maxOrbiters = 220;

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
        PlanetRing.ClearAll();
        planets.Clear();
        PlanetZones.Clear();
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
            if (t.planet) continue;           // los planetas van aparte (Planet Type)
            if (t.sprites == null || t.sprites.Length == 0 || t.minLevel > currentLevel) continue;
            unlocked.Add(t);
        }
        if (unlocked.Count == 0) { Debug.LogWarning("LevelPopulator: no hay tipos disponibles para este nivel."); return; }

        // Compra asteroides con el presupuesto del nivel
        float budget = difficulty.Budget(currentLevel) * (DarkWorld ? darkBudgetFactor : 1f) * (FractureWorld ? fractureBudgetFactor : 1f);
        darkLethal = null;
        nextDarkLethal = Time.time + Random.Range(darkLethalEvery.x, darkLethalEvery.y);
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
        var placedStatics = new List<(Vector2 pos, float r)>();
        var statics = new List<Asteroid>();
        int count = 0;

        // Planetas primero. Sustituyen a estáticos: cada uno quita de la compra estáticos por
        // el valor de su coste, empezando por los más caros
        PlacePlanets(area, playerPos, goal);
        if (planets.Count > 0)
        {
            float refund = Cost(planetType) * planets.Count;
            shopping.Sort((x, y) => Cost(y).CompareTo(Cost(x)));
            for (int i = 0; i < shopping.Count && refund > 0f; )
            {
                if (shopping[i].behavior == AsteroidBehavior.Static) { refund -= Cost(shopping[i]); shopping.RemoveAt(i); }
                else i++;
            }
            // Que no se queden todos los grandes juntos al principio de la lista
            for (int i = shopping.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (shopping[i], shopping[j]) = (shopping[j], shopping[i]); }
            foreach (var o in planets)
            {
                placed.Add((o.body.Body.position, o.footprint));
                PlanetZones.Add(new Vector3(o.body.Body.position.x, o.body.Body.position.y, o.footprint));
            }
        }

        foreach (var t in shopping)
        {
            bool isStatic = t.behavior == AsteroidBehavior.Static;
            float size = t.RandomSize();
            float approxR = size * 0.7f;
            Rect rect = isStatic ? staticRect : movingRect;

            int lane = isStatic || FlowField.Instance == null ? -1 : FlowField.Instance.PickLane(FlowField.KindOf(t));
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var p = new Vector2(Random.Range(rect.xMin, rect.xMax), Random.Range(rect.yMin, rect.yMax));
                // Estáticos agrupados: a veces junto a uno ya colocado (varios juntos y huecos)
                if (isStatic && staticClusters && placedStatics.Count > 0 && Random.value < clusterChance)
                {
                    var anchor = placedStatics[Random.Range(0, placedStatics.Count)];
                    p = anchor.pos + Random.insideUnitCircle.normalized *
                        (anchor.r + approxR + Random.Range(clusterGap.x, clusterGap.y));
                    if (!rect.Contains(p)) continue;
                }
                // Lo que se mueve nace dentro de su carril (lentos y rápidos por caminos distintos)
                if (lane >= 0 && attempt < 30 && !FlowField.Instance.InLane(p, lane)) continue;

                if (Vector2.Distance(p, playerPos) < playerSafeRadius + approxR) continue;
                if (isStatic && (goal.Contains(p) || bounds.IsBlocked(p, blockedMargin + approxR))) continue;
                if (isStatic && InPlanetZone(p, approxR)) continue;
                if (Overlaps(placed, p, approxR)) continue;

                var spawned = pool.Spawn(t, p, size);
                if (isStatic && spawned != null) { statics.Add(spawned); placedStatics.Add((p, approxR)); }
                if (!isStatic) MarkDark(spawned);
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

        SetMassRef(statics);
        BuildPlanetSystems();
        if (planets.Count > 0) fairness += $", {planets.Count} planetas";
        if (DarkWorld) fairness += ", MUNDO OSCURO";
        if (FractureWorld) fairness += ", FRACTURA";

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
        if (pool == null || Asteroid.WorldFrozen) return;   // TimeFreeze: no entra nada nuevo
        if (DarkWorld) TickDarkLethal();
        if (!keepPopulation || targetPopulation <= 0) return;
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
            if (a.IsOrbiting) continue;   // los de cinturones y lunas no son población que fluye
            if (a.Type.planet) continue;
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
        if (!DarkWorld && !wasColossus && colossus != null && colossus.ShouldSpawn(currentLevel))
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
            p = FlowField.Instance.RandomEntryPoint(avoid, playerSafeRadius, type);
            if (player == null || difficulty == null || GivesTimeToReact(p, type, avoid)) break;
        }

        MarkDark(pool.Spawn(type, p));
    }

    void MarkDark(Asteroid a)
    {
        if (a != null && DarkWorld) a.Harmless = true;
    }

    // Mundo vacío: de vez en cuando entra uno que sí mata, de uno en uno
    Asteroid darkLethal;
    int darkLethalSerial;
    float nextDarkLethal;

    void TickDarkLethal()
    {
        bool alive = darkLethal != null && darkLethal.isActiveAndEnabled && darkLethal.Serial == darkLethalSerial;
        if (alive) return;
        if (darkLethal != null) { darkLethal = null; nextDarkLethal = Time.time + Random.Range(darkLethalEvery.x, darkLethalEvery.y); return; }
        if (Time.time < nextDarkLethal || FlowField.Instance == null) return;

        var type = PickAnyMoving();
        if (type == null) return;
        Vector2 avoid = player != null ? (Vector2)player.position : Vector2.zero;
        var a = pool.Spawn(type, FlowField.Instance.RandomEntryPoint(avoid, playerSafeRadius, type));
        if (a == null) return;
        a.Harmless = false;
        darkLethal = a;
        darkLethalSerial = a.Serial;
        Debug.Log($"LevelPopulator: mundo oscuro, entra un '{type.id}' que SÍ mata.");
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
        float hitRadius = playerRadius + type.sizeRange.y * AsteroidTypeData.ScaleOf(type) * 0.7f;
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

    // Masa de referencia (la mediana de los estáticos): con ella se calculan las velocidades de órbita
    void SetMassRef(List<Asteroid> statics)
    {
        statics.RemoveAll(a => a == null || !a.isActiveAndEnabled);
        if (statics.Count == 0) return;
        var masses = new List<float>();
        foreach (var a in statics) masses.Add(a.Mass);
        masses.Sort();
        Asteroid.StaticMassRef = Mathf.Max(0.001f, masses[masses.Count / 2]);
    }

    // ------------------------------------------------------------- planetas
    enum PlanetSystem { None, Ring, Belt, Moons }

    class PlanetPlan
    {
        public Asteroid body;
        public PlanetSystem system;
        public float footprint;          // radio que ocupa con su sistema
        public PlanetRing.Look ring;
        public float ringInner;          // radio del hueco del anillo (eje largo)
        public float beltWidth;
        public int moons;
    }

    readonly List<PlanetPlan> planets = new List<PlanetPlan>();
    /// Dónde están los sistemas de planetas del mapa actual: x, y = centro, z = radio que ocupan.
    /// Lo usa el fondo para que sus planetas lejanos no se confundan con estos
    public static readonly List<Vector3> PlanetZones = new List<Vector3>();
    Transform planetRoot;

    // Coloca los planetas (solo el cuerpo y su hueco). Van antes que los estáticos, que luego
    // dejan su pasillo alrededor de cada sistema
    void PlacePlanets(Rect area, Vector2 playerPos, Rect goal)
    {
        planets.Clear();
        if (planetType == null || planetType.sprites == null || planetType.sprites.Length == 0) return;
        if (Random.value >= planetMapChance) return;
        int noRoom = 0, tooBig = 0;

        int want = Random.Range(planetCount.x, planetCount.y + 1);
        var sprites = new List<Sprite>(planetType.sprites);
        sprites.RemoveAll(x => x == null);
        for (int i = sprites.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (sprites[i], sprites[j]) = (sprites[j], sprites[i]); }

        float worldArea = area.width * area.height, used = 0f;
        var fd = FractureDirector.Instance;
        bool fracture = FractureWorld && fd != null && fd.Active;

        for (int k = 0; k < want && k < sprites.Count; k++)
        {
            var body = pool.Spawn(planetType, sprites[k], area.center, planetType.RandomSize());
            if (body == null) continue;
            var plan = PlanSystem(body);
            float fp = plan.footprint;
            if (used + Mathf.PI * fp * fp > worldArea * planetMaxWorldShare)
            {
                tooBig++;
                Debug.LogWarning($"LevelPopulator: planeta descartado, su sistema ({plan.system}, radio {fp:0.0} u, planeta {body.WorldRadius:0.0} u) " +
                                 $"ocuparía más del {planetMaxWorldShare:P0} del mundo ({area.width:0}×{area.height:0} u). Baja el Size Range del tipo planeta o sube sus Pixels Per Unit.");
                pool.Release(body); continue;
            }

            Rect inner = Shrink(area, fp + 1f);
            bool ok = false;
            for (int attempt = 0; attempt < 60 && inner.width > 0f && inner.height > 0f; attempt++)
            {
                var p = new Vector2(Random.Range(inner.xMin, inner.xMax), Random.Range(inner.yMin, inner.yMax));
                if (Vector2.Distance(p, playerPos) < fp + playerSafeRadius + 1f) continue;
                if (DistanceToRect(p, goal) < fp + goalMargin) continue;
                if (GameBounds.Instance.IsBlocked(p, blockedMargin + fp)) continue;
                if (fracture && (p.x - fd.WallX(p.y)) * fd.Front.x < fp + planetFractureClearance) continue;
                bool clash = false;
                foreach (var o in planets)
                    if (Vector2.Distance(p, o.body.Body.position) < fp + o.footprint + planetCorridor) { clash = true; break; }
                if (clash) continue;

                body.Body.position = p;
                body.transform.position = new Vector3(p.x, p.y, body.transform.position.z);
                ok = true;
                break;
            }
            if (!ok)
            {
                noRoom++;
                Debug.LogWarning($"LevelPopulator: planeta sin sitio ({plan.system}, radio del sistema {fp:0.0} u, planeta {body.WorldRadius:0.0} u, mundo {area.width:0}×{area.height:0} u).");
                pool.Release(body); continue;
            }

            // Sin giro: la luz del dibujo viene de arriba a la izquierda
            body.Body.angularVelocity = 0f;
            body.Body.rotation = 0f;
            body.transform.rotation = Quaternion.identity;
            used += Mathf.PI * fp * fp;
            planets.Add(plan);
        }
        Debug.Log($"LevelPopulator: mapa con planetas, {planets.Count} de {want} colocados" +
                  (tooBig + noRoom > 0 ? $" ({tooBig} demasiado grandes, {noRoom} sin sitio)." : "."));
    }

    PlanetPlan PlanSystem(Asteroid body)
    {
        var plan = new PlanetPlan { body = body, system = PlanetSystem.None };
        float R = body.WorldRadius;
        var w = planetSystemWeights;
        bool canRing = ringLooks != null && System.Array.Exists(ringLooks, l => l != null && l.sprite != null);
        bool canBelt = (beltTypes != null && beltTypes.Length > 0) || (satelliteTypes != null && satelliteTypes.Length > 0);
        bool canMoons = moonType != null && moonType.sprites != null && moonType.sprites.Length > 0;
        float wr = canRing ? w.y : 0f, wb = canBelt ? w.z : 0f, wm = canMoons ? w.w : 0f;
        float total = Mathf.Max(0f, w.x) + wr + wb + wm;
        float pick = Random.value * Mathf.Max(0.0001f, total);
        if (pick < w.x || total <= 0f) plan.system = PlanetSystem.None;
        else if (pick < w.x + wr) plan.system = PlanetSystem.Ring;
        else if (pick < w.x + wr + wb) plan.system = PlanetSystem.Belt;
        else plan.system = PlanetSystem.Moons;

        plan.footprint = R;
        switch (plan.system)
        {
            case PlanetSystem.Ring:
                var valid = new List<PlanetRing.Look>();
                foreach (var l in ringLooks) if (l != null && l.sprite != null) valid.Add(l);
                plan.ring = valid[Random.Range(0, valid.Count)];
                plan.ringInner = R * Random.Range(ringInner.x, ringInner.y);
                plan.footprint = plan.ringInner / Mathf.Max(0.05f, plan.ring.innerRatio);
                break;
            case PlanetSystem.Belt:
                plan.beltWidth = Random.Range(beltWidth.x, beltWidth.y);
                plan.footprint = R + beltGap + plan.beltWidth + 0.6f;
                break;
            case PlanetSystem.Moons:
                plan.moons = Random.Range(moonCount.x, moonCount.y + 1);
                plan.footprint = R + moonDistance.y + moonType.sizeRange.y * 0.7f;
                break;
        }
        return plan;
    }

    // ¿Cae dentro del sistema de un planeta o de su pasillo?
    bool InPlanetZone(Vector2 p, float r)
    {
        foreach (var o in planets)
            if (Vector2.Distance(p, o.body.Body.position) < o.footprint + planetCorridor + r) return true;
        return false;
    }

    // Anillos, cinturones y lunas (después de los estáticos: las órbitas usan su masa media)
    void BuildPlanetSystems()
    {
        if (planets.Count == 0) return;
        if (planetRoot == null) planetRoot = new GameObject("Anillos").transform;
        var beltSet = beltTypes != null && beltTypes.Length > 0 ? beltTypes : satelliteTypes;
        int orbiters = 0, nr = 0, nb = 0, nm = 0;
        foreach (var plan in planets)
        {
            var a = plan.body;
            if (a == null || !a.isActiveAndEnabled) continue;
            switch (plan.system)
            {
                case PlanetSystem.Ring:
                    PlanetRing.Create(a, plan.ring, plan.ringInner, Random.Range(-ringTilt, ringTilt), ringSlow, planetRoot);
                    nr++;
                    break;
                case PlanetSystem.Belt:
                {
                    float r0 = a.WorldRadius + beltGap, r1 = r0 + plan.beltWidth;
                    int count = Mathf.Min(beltMax, Mathf.RoundToInt(Mathf.PI * (r1 * r1 - r0 * r0) * beltDensity));
                    count = Mathf.Min(count, maxOrbiters - orbiters);
                    if (count <= 0 || beltSet == null) break;
                    orbiters += a.SpawnBelt(beltSet, beltGap, plan.beltWidth, count,
                                            Random.Range(beltClusters.x, beltClusters.y + 1), beltClusterSpread,
                                            keplerSpeed * beltSpeedFactor, keplerRefRadius);
                    a.SetMoonsLethal(true);
                    nb++;
                    break;
                }
                case PlanetSystem.Moons:
                {
                    float sense = Random.value < 0.5f ? -1f : 1f;
                    float a0 = Random.Range(0f, 360f), step = 360f / Mathf.Max(1, plan.moons);
                    for (int i = 0; i < plan.moons && orbiters < maxOrbiters; i++)
                    {
                        var m = pool.Spawn(moonType, a.Body.position, moonType.RandomSize());
                        if (m == null) continue;
                        float r = a.WorldRadius + m.WorldRadius + Random.Range(moonDistance.x, moonDistance.y);
                        bool orbits = Random.value < moonOrbitShare;
                        float speed = orbits ? Random.Range(moonOrbitSpeed.x, moonOrbitSpeed.y) * sense : 0f;
                        m.EnterOrbit(a, a0 + step * i + Random.Range(-step * 0.25f, step * 0.25f), r, speed);
                        orbiters++;
                    }
                    a.SetMoonsLethal(false);
                    nm++;
                    break;
                }
            }
        }
        Debug.Log($"LevelPopulator: {planets.Count} planetas ({nr} con anillos, {nb} con cinturón, {nm} con lunas; {orbiters} cuerpos en órbita).");
    }

    static float DistanceToRect(Vector2 p, Rect r)
    {
        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
        float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // Más grande, más caro: el coste crece con el área, así la densidad se mantiene al
    // probar rocas más grandes con AsteroidScaleTuner
    static float Cost(AsteroidTypeData t)
    {
        float s = AsteroidTypeData.ScaleOf(t);
        return Mathf.Max(0.5f, t.cost) * s * s;
    }

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