using UnityEngine;

/// Panel de calibración para H. Solo en el editor y en compilaciones de desarrollo.
///
/// Un botón "CAL" arriba a la izquierda lo abre. Abierto, el juego se pausa (se puede jugar con
/// él abierto marcando "Jugar abierto"). Muestra lo que pasa en el nivel y deja cambiar en vivo:
///
///   Velocidad de las rocas     se nota al momento en lo que entra nuevo
///   Densidad                   se aplica al reiniciar el nivel
///   Cadencia y velocidad de los proyectiles de la grieta, y cada cuánto sale el QTE
///
/// "Copiar valores" deja en el portapapeles (y en la consola) el nivel y los multiplicadores
/// que te gustaron, para pasarlos luego a los perfiles.
///
/// Va en un objeto vacío de la escena.
public class CalibrationPanel : MonoBehaviour
{
    [Tooltip("Desmarcado: también sale en compilaciones finales (no lo hagas)")]
    [SerializeField] private bool onlyInDevelopment = true;
    [SerializeField] private float uiScale = 1f;

    bool open, playOpen;
    float savedTimeScale = 1f;
    float fps = 60f;
    LevelPopulator pop;
    GameDirector director;
    AsteroidPool pool;
    PlayerController player;
    Vector2 scroll;
    GUIStyle box, label, small, button;
    string lastCopy = "";

    void Awake()
    {
        if (onlyInDevelopment && !Application.isEditor && !Debug.isDebugBuild) enabled = false;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt > 0f) fps = Mathf.Lerp(fps, 1f / dt, 0.05f);
        if (pop == null) pop = FindFirstObjectByType<LevelPopulator>();
        if (director == null) director = FindFirstObjectByType<GameDirector>();
        if (pool == null) pool = FindFirstObjectByType<AsteroidPool>();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
#if ENABLE_INPUT_SYSTEM
        var k = UnityEngine.InputSystem.Keyboard.current;
        if (k != null && k.f1Key.wasPressedThisFrame) Toggle();
#else
        if (Input.GetKeyDown(KeyCode.F1)) Toggle();
#endif
    }

    void Toggle()
    {
        open = !open;
        if (open && !playOpen) { savedTimeScale = Time.timeScale; Time.timeScale = 0f; }
        else if (!open && Time.timeScale == 0f) Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;
    }

    void Styles()
    {
        if (box != null) return;
        box = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.UpperLeft };
        label = new GUIStyle(GUI.skin.label) { fontSize = 22 };
        small = new GUIStyle(GUI.skin.label) { fontSize = 18 };
        button = new GUIStyle(GUI.skin.button) { fontSize = 22 };
    }

    void OnGUI()
    {
        Styles();
        float s = Screen.height / 1080f * uiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        if (GUI.Button(new Rect(20, 20, 110, 60), open ? "CERRAR" : "CAL", button)) Toggle();
        if (!open) return;

        GUILayout.BeginArea(new Rect(20, 90, 620, 960), box);
        scroll = GUILayout.BeginScrollView(scroll);

        // ---- lo que está pasando
        int level = director != null ? director.CurrentLevel : (pop != null ? pop.CurrentLevel : 0);
        var diff = pop != null ? pop.Difficulty : null;
        int moving = 0, lethal = 0, orbiters = 0, statics = 0, planets = 0;
        int nRocks = 0, nComets = 0, nColossus = 0, nDebris = 0, nLoose = 0;
        if (pool != null)
            foreach (var a in pool.Active)
            {
                if (a == null || a.Type == null) continue;
                if (a.IsOrbiting) { orbiters++; continue; }
                if (a.Type.planet) { planets++; continue; }
                if (a.Type.behavior == AsteroidBehavior.Static && !a.IsDislodged) { statics++; continue; }
                moving++;
                if (a.Lethal) lethal++;
                if (a.IsDislodged) nLoose++;
                else if (a.Type.comet != null) nComets++;
                else if (a.Type.behavior == AsteroidBehavior.Colossus) nColossus++;
                else if (a.Type.behavior == AsteroidBehavior.Debris) nDebris++;
                else nRocks++;
            }
        var fd = FractureDirector.Instance;
        float pSpeed = player != null && player.Body != null ? player.Body.linearVelocity.magnitude : 0f;

        GUILayout.Label($"FPS {fps:0}   calidad {PerformanceManager.Current}", label);
        GUILayout.Label($"Nivel {level}" + (diff != null ? $"   etapa {diff.Tier(level)}" +
                        (diff.IsCalm(level) ? "  RESPIRO" : diff.IsSurge(level) ? "  APRETÓN" : "") : ""), label);
        if (diff != null)
            GUILayout.Label($"Presupuesto {diff.Budget(level) * LevelPopulator.CalibDensity:0.#}   " +
                            $"velocidad x{Asteroid.SpeedMultiplier:0.00}   corriente {diff.FlowAmplitude(level):0}°", small);
        GUILayout.Label($"En movimiento {moving} (matan {lethal})   en órbita {orbiters}", small);
        GUILayout.Label($"  rocas {nRocks} · cometas {nComets} · colosos {nColossus} · restos {nDebris} · estáticos soltados {nLoose}", small);
        GUILayout.Label($"Estáticos {statics}   planetas {planets}   población objetivo {(pop != null ? pop.TargetPopulation : 0)}   presupuesto final {(pop != null ? pop.LastBudget : 0f):0.#}", small);
        if (fd != null && fd.Active)
            GUILayout.Label($"GRIETA: proyectiles {fd.ShotsAlive}   cargas {fd.Charge:0.00}/{fd.MaxCharges}", small);
        GUILayout.Label($"Velocidad del jugador {pSpeed:0.0} u/s (crucero {(player != null ? player.CruiseSpeed : 0f):0.0})", small);

        GUILayout.Space(12);

        // ---- nivel
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-1", button)) Go(level - 1);
        if (GUILayout.Button("+1", button)) Go(level + 1);
        if (GUILayout.Button("+10", button)) Go(level + 10);
        if (GUILayout.Button("Reiniciar", button)) Go(level);
        GUILayout.EndHorizontal();

        GUILayout.Space(12);

        // ---- multiplicadores
        LevelPopulator.CalibSpeed = Slider("Velocidad de las rocas", LevelPopulator.CalibSpeed, 0.25f, 4f, "al momento");
        LevelPopulator.CalibDensity = Slider("Densidad", LevelPopulator.CalibDensity, 0.25f, 3f, "al reiniciar");
        FractureDirector.CalibFireRate = Slider("Cadencia de la grieta", FractureDirector.CalibFireRate, 0.25f, 4f, "al momento");
        FractureDirector.CalibShotSpeed = Slider("Velocidad de proyectiles", FractureDirector.CalibShotSpeed, 0.25f, 4f, "nuevos");
        LevelPopulator.CalibPlayer = Slider($"Crucero del jugador (curva {(pop != null ? pop.LevelCruise : 0f):0.0} u/s)", LevelPopulator.CalibPlayer, 0.4f, 3f, "al momento");
        // Más alto = salen más a menudo (internamente divide el tiempo entre perseguidores)
        float freq = Slider("Frecuencia del QTE (más = más a menudo)", 1f / Mathf.Max(0.05f, FractureQTE.CalibInterval), 0.3f, 5f, "siguiente");
        FractureQTE.CalibInterval = 1f / Mathf.Max(0.05f, freq);

        GUILayout.Space(8);
        playOpen = GUILayout.Toggle(playOpen, " Jugar abierto (sin pausa)", label);
        if (playOpen && Time.timeScale == 0f) Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Todo a 1", button))
        {
            LevelPopulator.CalibSpeed = LevelPopulator.CalibDensity = LevelPopulator.CalibPlayer = 1f;
            FractureDirector.CalibFireRate = FractureDirector.CalibShotSpeed = 1f;
            FractureQTE.CalibInterval = 1f;
        }
        if (GUILayout.Button("Copiar valores", button))
        {
            lastCopy = $"Nivel {level}: velocidad rocas x{LevelPopulator.CalibSpeed:0.00} (total x{Asteroid.SpeedMultiplier:0.00}), " +
                       $"densidad x{LevelPopulator.CalibDensity:0.00}, cadencia grieta x{FractureDirector.CalibFireRate:0.00}, " +
                       $"proyectiles x{FractureDirector.CalibShotSpeed:0.00}, frecuencia QTE x{1f / Mathf.Max(0.05f, FractureQTE.CalibInterval):0.00}, " +
                       $"en movimiento {moving} (rocas {nRocks}, cometas {nComets}, colosos {nColossus}, restos {nDebris}, soltados {nLoose}), " +
                       $"presupuesto final {(pop != null ? pop.LastBudget : 0f):0.#}, " +
                       $"crucero jugador {(player != null ? player.CruiseSpeed : 0f):0.0} (x{LevelPopulator.CalibPlayer:0.00}), mapa {(FractureDirector.Instance != null && FractureDirector.Instance.Active ? "con grieta" : "normal")}, " +
                       $"fps {fps:0}";
            GUIUtility.systemCopyBuffer = lastCopy;
            Debug.Log("CALIBRACIÓN — " + lastCopy);
        }
        GUILayout.EndHorizontal();
        if (lastCopy.Length > 0) GUILayout.Label("Copiado: " + lastCopy, small);

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    float Slider(string name, float v, float min, float max, string when, float step = 0.05f)
    {
        GUILayout.Label($"{name}: {(step < 0.1f ? "x" : "")}{v:0.00}  ({when})", small);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-", button, GUILayout.Width(60))) v = Mathf.Max(min, v - step);
        v = GUILayout.HorizontalSlider(v, min, max, GUILayout.Height(30));
        if (GUILayout.Button("+", button, GUILayout.Width(60))) v = Mathf.Min(max, v + step);
        GUILayout.EndHorizontal();
        return Mathf.Round(v * 100f) / 100f;
    }

    void Go(int lvl)
    {
        if (director == null) return;
        director.StartLevel(Mathf.Max(1, lvl));
    }

    void OnDisable()
    {
        if (open && Time.timeScale == 0f) Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;
    }
}
