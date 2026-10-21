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
        if (runSeed == 0) runSeed = Random.Range(1, int.MaxValue);
    }

    void OnEnable()
    {
        if (player != null) player.OnDied += HandleDied;
        if (goal != null) goal.Reached += HandleCleared;
    }

    void OnDisable()
    {
        if (player != null) player.OnDied -= HandleDied;
        if (goal != null) goal.Reached -= HandleCleared;
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

        // 3. Jugador y meta, antes de poblar: el generador evita la zona de los dos. El
        //    jugador aparece cerca de un borde; la meta, en el lado contrario, pegada al
        //    borde más lejano y a una altura al azar
        if (playerStart != null) playerStart.Place(GoalMirrored);
        if (player != null) player.Respawn();
        if (goal != null)
        {
            // Su punto de aparición, no donde está: durante la llegada aún viene de fuera
            if (player != null) goal.PlaceOpposite(player.SpawnPoint);
            else goal.Place();
        }

        // La cámara salta al jugador ANTES de poblar: los asteroides que se mueven nacen
        // alrededor de lo que se ve, y tiene que ser la vista del nivel nuevo
        if (CameraFollow.Instance != null) CameraFollow.Instance.Snap();

        // 4. A poblar. Vuelve a sortear la corriente con la misma semilla, así que sale igual
        if (populator != null) populator.StartLevel(CurrentLevel, seed);

        Running = true;
        levelStartTime = Time.time;
        LevelStarted?.Invoke(CurrentLevel);
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
        if (!Running) return;
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
        StartCoroutine(Advance(CurrentLevel, respawnDelay));
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

    [ContextMenu("Repetir nivel")]
    void RepeatFromMenu() { StopAllCoroutines(); StartLevel(CurrentLevel); }
}