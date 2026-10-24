using System.Collections;
using UnityEngine;

/// El bucle de juego. Hasta ahora no había forma de terminar un nivel ni de avanzar: esto es
/// lo que convierte el cinturón de asteroides en un juego.
///
/// Lleva el nivel actual y su semilla, coloca la meta y al jugador, manda poblar, y reacciona
/// a las dos únicas cosas que pueden pasar: lo superas o mueres.
///
/// La semilla de cada nivel sale del número de nivel, de la partida y del número de intento:
/// cada intento es un mapa distinto (decisión del 20 oct). La pantalla de transición lo cuenta
/// como "variaciones del mundo". Con Same Level On Retry se vuelve al reparto fijo.
///
/// Desde el Hito 2 habla con GameProgress: de ahí saca la partida a continuar y ahí avisa de
/// cada nivel superado y de cada muerte. Si GameProgress no está en la escena, el director
/// sigue funcionando igual — sin guardar. Eso es a propósito: quiero poder probar niveles sin
/// ensuciar el guardado real.
[DefaultExecutionOrder(5)]      // antes de LevelPopulator (10): le quita el arranque automático
public class GameDirector : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private LevelPopulator populator;
    [SerializeField] private PlayerController player;
    [SerializeField] private PlayerStart playerStart;
    [SerializeField] private Goal goal;

    [Header("Partida")]
    [SerializeField] private int startLevel = 1;
    [Tooltip("0 = se sortea al empezar. Con un valor fijo, toda la partida es reproducible")]
    [SerializeField] private int runSeed = 0;
    [Tooltip("Al reintentar, el nivel se genera otra vez igual. Apagado (lo decidido): cada " +
             "intento es un mapa distinto")]
    [SerializeField] private bool sameLevelOnRetry = false;
    [Tooltip("Si hay una partida guardada a medias, continúa por donde iba. Desactívalo para " +
             "probar siempre desde Nivel Inicial sin tocar el guardado")]
    [SerializeField] private bool resumeFromSave = true;
    [Tooltip("Avisa a GameProgress de cada nivel superado y de cada muerte. Desactívalo para " +
             "probar niveles sin ensuciar el guardado real")]
    [SerializeField] private bool recordProgress = true;

    [Header("Ritmo")]
    [SerializeField] private float startDelay = 0.4f;
    [Tooltip("Tras caer al agujero, espera antes de la pantalla de transición (o del nivel " +
             "siguiente si no hay pantalla)")]
    [SerializeField] private float nextLevelDelay = 1.1f;

    [Header("Mundos oscuros")]
    [Tooltip("Probabilidad de que un nivel sea un mundo vacío y oscuro. Se sortea por nivel: " +
             "si salió oscuro, sigue oscuro al reintentar")]
    [Range(0f, 1f)] [SerializeField] private float darkWorldChance = 0.01f;
    [Tooltip("Multiplica el tamaño (y con él la masa y el alcance) del agujero negro en un mundo oscuro")]
    [SerializeField] private float darkGoalBoost = 1.35f;
    [Tooltip("Solo para probar: todos los niveles son oscuros")]
    [SerializeField] private bool alwaysDark = false;
    [SerializeField] private CometDirector comets;

    [Header("Mapas de fractura")]
    [Tooltip("Vacío = lo busca. La probabilidad y el nivel mínimo están en el propio FractureDirector")]
    [SerializeField] private FractureDirector fracture;
    [SerializeField] private ColossusDirector colossus;
    [Tooltip("Más colosos y cometas en los mapas de fractura (son lo que llena el entorno de objetos)")]
    [SerializeField] private float fractureColossusChance = 3f;
    [SerializeField] private int fractureExtraComets = 1;
    [Tooltip("Recorte extra de lo letal que se mueve en mapas con grieta: asteroides, cometas y colosos " +
             "(0.9 = un 10 % menos). Se suma a los recortes del LevelPopulator")]
    [Range(0.3f, 1f)] [SerializeField] private float fractureLethalCut = 0.927f;
    [Tooltip("Lo mismo en los mapas normales (ni grieta ni mundo oscuro): asteroides, cometas y " +
             "colosos (0.9 = un 10 % menos, en todos los niveles)")]
    [Range(0.3f, 1f)] [SerializeField] private float normalLethalCut = 0.945f;
    [Tooltip("Mundos oscuros: más o menos rocas letales sueltas (1.08 = un 8 % más a menudo)")]
    [Range(0.3f, 2f)] [SerializeField] private float darkLethalRate = 1.08f;
    [Tooltip("Solo para probar: todos los niveles (que no sean oscuros) son de fractura")]
    [SerializeField] private bool alwaysFracture = false;

    [Header("Pantalla de transición")]
    [Tooltip("Vacío = la busca en la escena. Si no hay ninguna, se pasa al nivel siguiente sin ella")]
    [SerializeField] private TransitionScreen transition;
    [SerializeField] private float respawnDelay = 1.4f;

    [Header("Meta y orientación")]
    [Tooltip("La meta se coloca RÍO ARRIBA: del lado por el que vienen los asteroides, para " +
             "que avances contra la corriente y las amenazas te lleguen de frente. Si la " +
             "corriente cambia de sentido, la meta y la salida se intercambian")]
    [SerializeField] private bool mirrorWithFlow = true;

    public int CurrentLevel { get; private set; }
    public int Deaths { get; private set; }
    public bool Running { get; private set; }
    public bool GoalMirrored { get; private set; }
    /// Semilla del nivel en curso. La necesita quien tenga que sortear algo con el nivel
    /// (la geoda, por ejemplo) y salir igual en cada intento.
    public int CurrentSeed { get; private set; }
    /// Este nivel es un mundo vacío y oscuro
    public bool IsDarkWorld { get; private set; }
    /// Este nivel es un mapa de fractura
    public bool IsFractureWorld { get; private set; }
    int forcedFractureLevel = -1;
    int forcedDarkLevel = -1;

    /// Para el HUD y el guardado
    public event System.Action<int> LevelStarted;
    public event System.Action<int> LevelCleared;
    public event System.Action<int> PlayerDied;

    int retries;
    float levelStartTime;
    // Lo del nivel en curso sumando los intentos fallidos. Sin GameProgress vive solo aquí
    int levelDeaths;
    float levelSeconds;

    GameProgress Progress => recordProgress ? GameProgress.Instance : null;

    void Awake()
    {
        // El populator ya no arranca solo: el nivel lo decide este director
        if (populator != null) populator.SuppressAutoStart();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (playerStart == null) playerStart = FindFirstObjectByType<PlayerStart>();
        if (goal == null) goal = FindFirstObjectByType<Goal>();
        if (transition == null) transition = FindFirstObjectByType<TransitionScreen>(FindObjectsInactive.Include);
        if (comets == null) comets = FindFirstObjectByType<CometDirector>();
        if (fracture == null) fracture = FindFirstObjectByType<FractureDirector>();
        if (colossus == null) colossus = FindFirstObjectByType<ColossusDirector>();
        if (runSeed == 0) runSeed = Random.Range(1, int.MaxValue);
    }

    void OnEnable()
    {
        if (player != null) player.OnDied += HandleDied;
        if (goal != null) goal.Reached += HandleCleared;
        if (fracture != null) fracture.Closed += OnFractureClosed;
    }

    void OnDisable()
    {
        if (player != null) player.OnDied -= HandleDied;
        if (goal != null) goal.Reached -= HandleCleared;
        if (fracture != null) fracture.Closed -= OnFractureClosed;
    }

    IEnumerator Start()
    {
        int level = Mathf.Max(1, startLevel);

        var p = Progress;
        if (p != null)
        {
            // Continuar: la semilla viene del guardado, o la partida entera no sería la misma
            if (resumeFromSave && p.HasRun)
            {
                runSeed = p.Data.run.runSeed;
                level = Mathf.Max(1, p.Data.run.level);
                Deaths = p.Data.run.deaths;
                // El intento por el que iba: así el mapa sigue la misma serie de variaciones
                levelDeaths = retries = p.Data.run.levelDeaths;
                levelSeconds = p.Data.run.levelSeconds;
                Debug.Log($"GameDirector: se continúa la partida guardada en el nivel {level}.");
            }
            else
            {
                p.BeginRun(runSeed, level);
            }
        }

        yield return new WaitForSeconds(startDelay);
        StartLevel(level);
    }

    int LayoutSeed(int level)
    {
        unchecked { return (runSeed * 16777619 ^ level * 374761393) & 0x7FFFFFFF; }
    }

    void OnFractureClosed(Vector2 at)
    {
        if (goal != null && Running) goal.PlaceAt(at);
    }

    bool DarkFor(int level)
    {
        if (darkWorldChance <= 0f) return false;
        unchecked
        {
            int h = runSeed * 73856093 ^ level * 19349663 ^ 0x5bd1e995;
            return new System.Random(h).NextDouble() < darkWorldChance;
        }
    }

    /// Semilla de un nivel: depende de la partida y del número, nunca del momento.
    int SeedFor(int level)
    {
        unchecked
        {
            int h = runSeed;
            h = h * 486187739 + level * 31;
            if (!sameLevelOnRetry) h += retries * 7919;
            return h & 0x7FFFFFFF;
        }
    }

    public void StartLevel(int level)
    {
        CurrentLevel = Mathf.Max(1, level);
        CurrentSeed = SeedFor(CurrentLevel);
        int seed = CurrentSeed;

        // 1. La corriente primero: de ella depende de qué lado va la meta
        if (FlowField.Instance != null) FlowField.Instance.Randomize(seed);

        // 2. Río arriba: si la corriente va hacia la derecha, los asteroides vienen de la
        //    izquierda, así que la meta se va a la izquierda y el jugador sale por la derecha.
        GoalMirrored = false;
        if (mirrorWithFlow && FlowField.Instance != null)
        {
            Vector2 d = FlowField.FromAngle(FlowField.Instance.BaseAngle);
            GoalMirrored = d.x > 0f;
        }
        if (GameBounds.Instance != null) GameBounds.Instance.SetGoalMirrored(GoalMirrored);

        // ¿Mundo oscuro? Sale del nivel y la partida, no del intento: reintentar no lo cambia
        // Enseñanza: en los primeros niveles solo en su nivel garantizado
        var teachDark = TeachingPlan.Dark(CurrentLevel);
        IsDarkWorld = alwaysDark || CurrentLevel == forcedDarkLevel || (teachDark ?? DarkFor(CurrentLevel));
        if (goal != null) goal.SizeBoost = IsDarkWorld ? darkGoalBoost : 1f;
        if (populator != null) populator.DarkWorld = IsDarkWorld;
        if (comets != null) comets.Suppressed = IsDarkWorld;

        // ¿Fractura? Nunca a la vez que un mundo oscuro. Igual en cada intento
        var teachFracture = TeachingPlan.Fracture(CurrentLevel);
        IsFractureWorld = !IsDarkWorld && fracture != null &&
            (alwaysFracture || CurrentLevel == forcedFractureLevel || (teachFracture ?? fracture.RollFor(CurrentLevel, runSeed)));
        if (populator != null) populator.FractureWorld = IsFractureWorld;
        bool normalMap = !IsFractureWorld && !IsDarkWorld;
        float cut = IsFractureWorld ? fractureLethalCut : normalMap ? normalLethalCut : 1f;
        if (colossus != null) colossus.ChanceMultiplier = IsFractureWorld ? fractureColossusChance * cut : cut;
        if (comets != null)
        {
            comets.ExtraAlive = IsFractureWorld ? fractureExtraComets : 0;
            comets.DelayMultiplier = 1f / Mathf.Max(0.1f, cut);   // más espera entre cometas = menos cometas
        }
        if (populator != null)
        {
            populator.FractureExtraCut = IsFractureWorld ? cut : 1f;
            populator.NormalCut = normalMap ? cut : 1f;
            populator.DarkLethalRate = IsDarkWorld ? darkLethalRate : 1f;
        }

        // 3. Jugador y meta, antes de poblar: el generador evita la zona de los dos. El
        //    jugador aparece cerca de un borde; la meta, en el lado contrario, pegada al
        //    borde más lejano y a una altura al azar
        if (playerStart != null) playerStart.Place(GoalMirrored);
        if (player != null) player.Respawn();
        if (IsFractureWorld)
        {
            // La meta no existe hasta cerrar la grieta. La grieta se prepara antes de poblar:
            // deja su zona libre de estáticos. Su semilla no depende del intento
            if (goal != null) goal.Hide();
            fracture.Prepare(true, LayoutSeed(CurrentLevel), player != null ? player.SpawnPoint : Vector2.zero, CurrentLevel);
        }
        else
        {
            if (fracture != null) fracture.Prepare(false, 0, Vector2.zero, CurrentLevel);
            if (goal != null)
            {
                // Su punto de aparición, no donde está: durante la llegada aún viene de fuera
                if (player != null) goal.PlaceOpposite(player.SpawnPoint);
                else goal.Place();
            }
        }

        // La cámara salta al jugador ANTES de poblar: los asteroides que se mueven nacen
        // alrededor de lo que se ve, y tiene que ser la vista del nivel nuevo
        if (CameraFollow.Instance != null) CameraFollow.Instance.Snap();

        // 4. A poblar. Vuelve a sortear la corriente con la misma semilla, así que sale igual
        if (populator != null) populator.StartLevel(CurrentLevel, seed);

        Running = true;
        levelStartTime = Time.time;
        LevelStarted?.Invoke(CurrentLevel);
        if (IsDarkWorld) Debug.Log($"GameDirector: el nivel {CurrentLevel} es un MUNDO OSCURO.");
        if (IsFractureWorld) Debug.Log($"GameDirector: el nivel {CurrentLevel} es un MAPA DE FRACTURA.");
        Debug.Log($"GameDirector: nivel {CurrentLevel} (semilla {seed}, meta " +
                  $"{(GoalMirrored ? "izquierda" : "derecha")}, intentos previos {retries})");
    }

    void HandleCleared()
    {
        if (!Running) return;
        Running = false;
        retries = 0;

        float seconds = Time.time - levelStartTime;
        var stats = new TransitionScreen.Stats
        {
            level = CurrentLevel,
            attemptSeconds = seconds,
            totalSeconds = levelSeconds + seconds,
            deaths = levelDeaths,
            variations = levelDeaths + 1,
        };
        levelDeaths = 0;
        levelSeconds = 0f;

        var p = Progress;
        if (p != null) stats.crystals = p.LevelCleared(CurrentLevel, seconds);

        LevelCleared?.Invoke(CurrentLevel);
        StartCoroutine(ClearSequence(stats));
    }

    void HandleDied()
    {
        if (!Running) { if (player != null) player.Shatter(); return; }
        Running = false;
        Deaths++;
        retries++;
        float seconds = Time.time - levelStartTime;
        levelDeaths++;
        levelSeconds += seconds;

        var p = Progress;
        if (p != null) p.PlayerDied(CurrentLevel, seconds);

        PlayerDied?.Invoke(CurrentLevel);
        // Mismo nivel: no se pierde el progreso por morir
        var cu = DeathCloseUp.Instance;
        if (cu != null && player != null && cu.TryPlay(player.transform.position))
            StartCoroutine(AfterCloseUp(CurrentLevel, cu));
        else
        {
            if (player != null) player.Shatter();
            StartCoroutine(Advance(CurrentLevel, respawnDelay));
        }
    }

    // Con primer plano: se reaparece en cuanto acaba (o al tocar para saltarlo)
    IEnumerator AfterCloseUp(int level, DeathCloseUp cu)
    {
        if (goal != null) goal.Deactivate();
        while (cu != null && cu.Playing) yield return null;
        StartLevel(level);
    }

    /// Abandonar la partida: cierra la marca y la mete en el top. Lo llamará el menú de pausa
    /// en el Hito 6; de momento está aquí para que el guardado quede cerrado y no a medias.
    public void AbandonRun()
    {
        Running = false;
        StopAllCoroutines();
        var p = Progress;
        if (p != null) p.SubmitRun();
    }

    // Superado: la pantalla de transición espera un toque; al tocar, el nivel siguiente
    // arranca debajo mientras la pantalla se desvanece y empieza la llegada
    IEnumerator ClearSequence(TransitionScreen.Stats stats)
    {
        if (goal != null) goal.Deactivate();
        yield return new WaitForSeconds(nextLevelDelay);
        if (transition != null)
        {
            transition.Show(stats);
            while (!transition.Continued) yield return null;
        }
        StartLevel(stats.level + 1);
    }

    IEnumerator Advance(int level, float delay)
    {
        if (goal != null) goal.Deactivate();
        yield return new WaitForSeconds(delay);
        StartLevel(level);
    }

    // Para probar sin jugar: clic derecho en el componente
    [ContextMenu("Siguiente nivel")]
    void NextFromMenu() { StopAllCoroutines(); retries = levelDeaths = 0; levelSeconds = 0f; StartLevel(CurrentLevel + 1); }

    [ContextMenu("Mundo oscuro en el siguiente nivel")]
    void DarkNext() { forcedDarkLevel = CurrentLevel + 1; Debug.Log($"GameDirector: el nivel {forcedDarkLevel} será oscuro."); }

    [ContextMenu("Fractura en el siguiente nivel")]
    void FractureNext() { forcedFractureLevel = CurrentLevel + 1; Debug.Log($"GameDirector: el nivel {forcedFractureLevel} será de fractura."); }

    [ContextMenu("Repetir nivel")]
    void RepeatFromMenu() { StopAllCoroutines(); StartLevel(CurrentLevel); }
}