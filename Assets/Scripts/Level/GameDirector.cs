using System.Collections;
using UnityEngine;

/// El bucle de juego. Hasta ahora no había forma de terminar un nivel ni de avanzar: esto es
/// lo que convierte el cinturón de asteroides en un juego.
///
/// Lleva el nivel actual y su semilla, coloca la meta y al jugador, manda poblar, y reacciona
/// a las dos únicas cosas que pueden pasar: lo superas o mueres.
///
/// La semilla de cada nivel sale del número de nivel y de la partida, así que el nivel 7
/// siempre es el mismo nivel 7 dentro de una partida. Reintentar te devuelve EXACTAMENTE el
/// mismo reparto: puedes aprendértelo, que es lo que hace que fallar no sea frustrante.
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
    [Tooltip("Al reintentar, el nivel se genera otra vez igual. Desactívalo solo si prefieres " +
             "que cada intento sea distinto")]
    [SerializeField] private bool sameLevelOnRetry = true;
    [Tooltip("Si hay una partida guardada a medias, continúa por donde iba. Desactívalo para " +
             "probar siempre desde Nivel Inicial sin tocar el guardado")]
    [SerializeField] private bool resumeFromSave = true;
    [Tooltip("Avisa a GameProgress de cada nivel superado y de cada muerte. Desactívalo para " +
             "probar niveles sin ensuciar el guardado real")]
    [SerializeField] private bool recordProgress = true;

    [Header("Ritmo")]
    [SerializeField] private float startDelay = 0.4f;
    [SerializeField] private float nextLevelDelay = 1.1f;
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

    GameProgress Progress => recordProgress ? GameProgress.Instance : null;

    void Awake()
    {
        // El populator ya no arranca solo: el nivel lo decide este director
        if (populator != null) populator.SuppressAutoStart();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (playerStart == null) playerStart = FindFirstObjectByType<PlayerStart>();
        if (goal == null) goal = FindFirstObjectByType<Goal>();
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

        // 3. Meta y jugador, antes de poblar: el generador evita la zona del jugador
        if (goal != null) goal.Place();
        if (playerStart != null) playerStart.Place(GoalMirrored);
        if (player != null) player.Respawn();

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
        var p = Progress;
        if (p != null) p.LevelCleared(CurrentLevel, seconds);

        LevelCleared?.Invoke(CurrentLevel);
        StartCoroutine(Advance(CurrentLevel + 1, nextLevelDelay));
    }

    void HandleDied()
    {
        if (!Running) return;
        Running = false;
        Deaths++;
        retries++;

        var p = Progress;
        if (p != null) p.PlayerDied(CurrentLevel);

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

    IEnumerator Advance(int level, float delay)
    {
        if (goal != null) goal.Deactivate();
        yield return new WaitForSeconds(delay);
        StartLevel(level);
    }

    // Para probar sin jugar: clic derecho en el componente
    [ContextMenu("Siguiente nivel")]
    void NextFromMenu() { StopAllCoroutines(); retries = 0; StartLevel(CurrentLevel + 1); }

    [ContextMenu("Repetir nivel")]
    void RepeatFromMenu() { StopAllCoroutines(); StartLevel(CurrentLevel); }
}