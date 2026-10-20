using UnityEngine;

/// Ajusta cuántas cosas se dibujan según la potencia del móvil.
///
/// DOS PASOS
///
/// 1. Al arrancar adivina un nivel (Bajo, Medio, Alto) por la memoria y los núcleos del
///    teléfono. Es una primera estimación.
/// 2. Mientras se juega mide los fotogramas por segundo de verdad. Si va por debajo del
///    objetivo de forma sostenida, baja un nivel; si después va sobrado mucho rato, sube.
///    El cambio se aplica al terminar el nivel (al ganar o al morir), nunca a mitad: así no
///    aparecen ni desaparecen cosas delante del jugador.
///
/// QUÉ TOCA
///
/// Solo lo decorativo: estrellas, polvo, estrellas fugaces, lente, nebulosas, objetos
/// lejanos, rayas del hiperespacio, el hilo de la caída y el tope de escombros de cometa.
/// La cantidad de asteroides que pueden matarte NO cambia nunca: la dificultad es la misma en
/// todos los teléfonos.
///
/// El jugador puede fijar el nivel a mano en Ajustes (Settings.graphicsQuality): si elige
/// Bajo, Medio o Alto, manda su elección y la medición se apaga. En Auto manda la medición.
[DefaultExecutionOrder(-200)]
public class PerformanceManager : MonoBehaviour
{
    public enum Tier { Low = 0, Medium = 1, High = 2 }

    public static Tier Current { get; private set; } = Tier.High;
    /// Avisa cuando cambia el nivel (para quien quiera reaccionar al momento)
    public static event System.Action<Tier> Changed;

    [Header("Medición")]
    [Tooltip("Fotogramas por segundo que se buscan. Por debajo de Low Fps Ratio de esto, se baja")]
    [SerializeField] private int targetFps = 60;
    [Range(0.5f, 1f)] [SerializeField] private float lowFpsRatio = 0.85f;
    [Tooltip("Por encima de esta fracción se considera que va sobrado")]
    [Range(0.8f, 1f)] [SerializeField] private float highFpsRatio = 0.97f;
    [Tooltip("Segundos de cada medición")]
    [SerializeField] private float window = 4f;
    [Tooltip("Mediciones seguidas por debajo para bajar de nivel")]
    [SerializeField] private int windowsToDrop = 2;
    [Tooltip("Mediciones seguidas sobrado para volver a subir")]
    [SerializeField] private int windowsToRaise = 6;

    // ------------------------------------------------------------------ presupuestos
    public static float StarDensity => Pick(0.45f, 0.75f, 1f);
    public static float DustAmount => Pick(0.35f, 0.7f, 1f);
    public static bool ShootingStars => Current != Tier.Low;
    public static bool Lensing => Current != Tier.Low;
    public static int MaxNebulas => Current == Tier.Low ? 1 : 2;
    public static int MaxFarObjects => Current == Tier.Low ? 2 : 3;
    public static int WarpLines => Current == Tier.Low ? 30 : Current == Tier.Medium ? 50 : 70;
    public static bool FallGhosts => Current != Tier.Low;
    public static float DebrisAmount => Pick(0.6f, 0.8f, 1f);

    static float Pick(float low, float mid, float high) =>
        Current == Tier.Low ? low : Current == Tier.Medium ? mid : high;

    static PerformanceManager instance;
    GameDirector director;
    float acc, measured;
    int frames, below, above;
    Tier pending, ceiling;
    bool hasPending, manual, raisedBefore;

    void Awake()
    {
        instance = this;
        Current = Guess();
        ceiling = Current;
        Debug.Log($"PerformanceManager: {SystemInfo.deviceModel}, {SystemInfo.systemMemorySize} MB, " +
                  $"{SystemInfo.processorCount} núcleos -> nivel inicial {Current}.");
    }

    void OnEnable()
    {
        director = FindFirstObjectByType<GameDirector>();
        if (director == null) return;
        director.LevelCleared += ApplyPending;
        director.PlayerDied += ApplyPending;
    }

    void OnDisable()
    {
        if (director == null) return;
        director.LevelCleared -= ApplyPending;
        director.PlayerDied -= ApplyPending;
    }

    void Start() => ReadManualSetting();

    /// Para el menú de Ajustes: -1 = Auto, 0 = Bajo, 1 = Medio, 2 = Alto. Se aplica en el
    /// siguiente nivel
    public static void SetManual(int quality)
    {
        if (instance == null) return;
        instance.manual = quality >= 0;
        if (instance.manual) { instance.pending = (Tier)Mathf.Clamp(quality, 0, 2); instance.hasPending = true; }
        else { instance.ceiling = Guess(); instance.raisedBefore = false; }
    }

    void ReadManualSetting()
    {
        var gp = GameProgress.Instance;
        if (gp == null || gp.Data == null || gp.Data.settings == null) return;
        int q = gp.Data.settings.graphicsQuality;
        if (q >= 0) { manual = true; SetTier((Tier)Mathf.Clamp(q, 0, 2)); }
    }

    static Tier Guess()
    {
#if UNITY_EDITOR
        return Tier.High;
#else
        int mem = SystemInfo.systemMemorySize;
        int cores = SystemInfo.processorCount;
        Tier t = mem < 3000 ? Tier.Low : mem < 6000 ? Tier.Medium : Tier.High;
        if (cores < 6 && t > Tier.Low) t--;
        return t;
#endif
    }

    void Update()
    {
        if (manual) return;
        if (director != null && !director.Running) { acc = 0f; frames = 0; return; }

        acc += Time.unscaledDeltaTime;
        frames++;
        if (acc < window) return;

        measured = frames / acc;
        acc = 0f;
        frames = 0;

        if (measured < targetFps * lowFpsRatio)
        {
            below++; above = 0;
            if (below >= windowsToDrop && Current > Tier.Low)
            {
                Queue(Current - 1);
                // Si ya había subido y vuelve a no aguantar, ese es su techo: así no se pasa la
                // partida subiendo y bajando
                if (raisedBefore) ceiling = Current - 1;
                below = 0;
            }
        }
        else if (measured > targetFps * highFpsRatio)
        {
            above++; below = 0;
            if (above >= windowsToRaise && Current < ceiling) { Queue(Current + 1); raisedBefore = true; above = 0; }
        }
        else { below = 0; above = 0; }
    }

    void Queue(Tier t)
    {
        pending = t;
        hasPending = true;
        Debug.Log($"PerformanceManager: {measured:0} fps -> el siguiente nivel irá en {t}.");
    }

    void ApplyPending(int level)
    {
        if (!hasPending) return;
        hasPending = false;
        SetTier(pending);
    }

    static void SetTier(Tier t)
    {
        if (t == Current) return;
        Current = t;
        Changed?.Invoke(t);
    }

    [ContextMenu("Forzar Bajo")]  void ForceLow()  { SetTier(Tier.Low); }
    [ContextMenu("Forzar Medio")] void ForceMid()  { SetTier(Tier.Medium); }
    [ContextMenu("Forzar Alto")]  void ForceHigh() { SetTier(Tier.High); }
}