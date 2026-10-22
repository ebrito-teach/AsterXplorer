using UnityEngine;

/// La curva de dificultad de un juego que no termina.
///
/// Aquí ha habido tres intentos y conviene saber por qué fallaron los dos primeros:
///
///   1. LINEAL (+1.2 por nivel, para siempre). Apretaba demasiado al principio y se clavaba
///      en el tope: desde el nivel 50 todos los niveles valían lo mismo y eran idénticos.
///
///   2. SATURADA, n/(n+K). Arreglaba el principio, pero el 80% del recorrido se gastaba
///      antes del nivel 250 y a partir de ahí la curva era, en la práctica, plana. Un techo
///      alcanzable es un techo, aunque la fórmula diga que nunca lo toca.
///
///   3. LOGARÍTMICA, que es esto:
///
///          valor(n) = inicio + crecimiento · intensidad · ln(1 + (n-1) / rodilla)
///
/// El logaritmo **no tiene tope**. No se acerca a un máximo: sigue subiendo siempre. Lo que
/// hace es subir cada vez más despacio, y eso es exactamente la forma que buscábamos:
///
///   · nivel  20 → +0.52 de cantidad por nivel. Se nota en cada nivel.
///   · nivel 250 → +0.07 por nivel. Se nota cada veinte.
///   · nivel 999 → +0.02 por nivel. Microscópico, pero **sigue subiendo**.
///
/// No existe un "nivel máximo de dificultad" que alcanzar. Y como la curva nunca se agota,
/// siempre queda sitio por arriba para meter mecánicas nuevas sin tener que rehacerla.
///
/// EL MANDO
///
/// `intensity` multiplica el crecimiento de TODOS los ejes a la vez. Ese es el valor que vas
/// a tocar: cuando entren los cometas, el viento solar o lo que venga, el juego se vuelve más
/// duro por sí solo, y lo compensas bajando la intensidad en vez de retocar nueve números.
/// Los `start` de cada eje son otra cosa — son la identidad del nivel 1 — y casi nunca se
/// tocan.
[CreateAssetMenu(fileName = "DifficultyProfile", menuName = "Game/Difficulty Profile")]
public class DifficultyProfile : ScriptableObject
{
    [Header("EL MANDO")]
    [Tooltip("Multiplica el crecimiento de todos los ejes. 1 = la curva calibrada. Al meter " +
             "una mecánica nueva que añade presión por sí sola, bájalo para compensar sin " +
             "tocar nada más. No afecta al nivel 1: eso lo deciden los 'start'")]
    [Range(0.3f, 2f)] public float intensity = 1f;

    [Header("Etapas")]
    [Tooltip("Cada cuántos niveles hay un salto de etapa. Solo desbloquea tipos de asteroide " +
             "y alimenta los mensajes: la dificultad ya no da saltos por etapa")]
    public int tierLength = 20;

    [Header("Cantidad — presupuesto de puntos")]
    [Tooltip("Nivel 1. La pantalla mide 19.4 x 9 unidades; con 32 se ve despejada, que es lo " +
             "que tiene que ser")]
    public float budgetStart = 32f;
    [Tooltip("Cuánto sube por cada multiplicación de la rodilla. Sube el valor y toda la " +
             "curva se empina, sin tope")]
    public float budgetGrowth = 19.6f;
    [Tooltip("Dónde empieza a frenar la subida. Bajo = aprieta muy pronto y luego se calma")]
    public float budgetKnee = 19f;

    [Header("Velocidad")]
    public float speedStart = 1f;
    public float speedGrowth = 0.19f;
    public float speedKnee = 25f;

    [Header("Curvas por tramos (calibración H)")]
    [Tooltip("Marcado: velocidad, crucero del jugador y cantidad salen de las tablas de abajo " +
             "(lo calibrado jugando). Desmarcado: las curvas logarítmicas de arriba")]
    public bool useKeyCurves = true;
    [Tooltip("Velocidad de las rocas por nivel (x = nivel, y = multiplicador). Entre puntos se " +
             "interpola; pasado el último sigue la tendencia hasta Speed Cap")]
    public Vector2[] speedKeys =
    {
        new Vector2(1, 1.5f), new Vector2(10, 1.8f), new Vector2(20, 2.1f), new Vector2(30, 2.4f),
        new Vector2(50, 2.9f), new Vector2(100, 3.8f), new Vector2(200, 4.6f), new Vector2(500, 5.8f),
        new Vector2(1000, 6.8f),
    };
    [Tooltip("Techo de la velocidad de las rocas. Más allá deja de poder verse a tiempo: la " +
             "dificultad sigue subiendo por patrones, combinaciones y contenido nuevo")]
    public float speedCap = 7f;
    [Tooltip("La velocidad sube en escalones de tantos niveles (saltos cortos y frecuentes)")]
    public int speedStep = 10;
    [Tooltip("Velocidad de crucero del jugador (u/s), en escalones: desde el nivel x vale y, hasta " +
             "el siguiente punto. Sube poco a propósito: moverse más rápido es ventaja")]
    public Vector2[] cruiseKeys =
    {
        new Vector2(1, 6f), new Vector2(20, 6.5f), new Vector2(30, 6.8f), new Vector2(40, 7f),
        new Vector2(60, 7.2f), new Vector2(80, 7.4f), new Vector2(100, 7.6f), new Vector2(150, 7.9f),
        new Vector2(200, 8.2f), new Vector2(300, 8.6f), new Vector2(400, 8.9f), new Vector2(500, 9.2f),
        new Vector2(700, 9.6f), new Vector2(1000, 10f),
    };
    [Tooltip("Techo del crucero: nunca pasa de aquí")]
    public float cruiseCap = 10f;
    [Tooltip("Cantidad (presupuesto) por nivel. Pasado el último punto se queda ahí: la " +
             "dificultad la pone la velocidad, y el reparto por cobertura evita huecos")]
    public Vector2[] budgetKeys =
    {
        new Vector2(1, 46f), new Vector2(10, 58f), new Vector2(20, 100f), new Vector2(30, 110f),
        new Vector2(60, 115f), new Vector2(100, 120f),
    };

    [Header("Corriente — grados de curvatura")]
    public float flowStart = 32f;
    public float flowGrowth = 13f;
    public float flowKnee = 22f;
    [Tooltip("Límite FÍSICO, no de dificultad: pasados los 90° la corriente se invierte y " +
             "los asteroides empezarían a entrar por el lado de la meta")]
    public float flowHardLimit = 88f;

    [Header("Rendimiento (no es dificultad)")]
    [Tooltip("Tope de asteroides en movimiento a la vez. Esto NO es un tope de dificultad: " +
             "es lo que aguanta un móvil. La dificultad sigue subiendo por tamaño, velocidad " +
             "y corriente aunque la cantidad se quede aquí")]
    public int maxMovingAsteroids = 55;

    [Header("Ritmo: respiro y apretón")]
    [Tooltip("Un nivel de cada tantos es un respiro. 0 = ninguno")]
    public int calmEvery = 7;
    [Tooltip("Un nivel de cada tantos aprieta de golpe. Son los que se recuerdan")]
    public int surgeEvery = 5;
    [Range(0.3f, 1f)] public float calmFactor = 0.65f;
    [Range(1f, 2.5f)] public float surgeFactor = 1.45f;
    [Tooltip("Cuánto del pico afecta también a la velocidad (1 = igual que a la cantidad)")]
    [Range(0f, 1f)] public float moodAffectsSpeed = 0.5f;

    [Header("Justicia — el nivel tiene que ser pasable")]
    [Tooltip("Verifica al generar el nivel que existe al menos un camino hasta la meta, y si " +
             "no existe retira asteroides hasta que lo haya. Apágalo solo para ver lo que el " +
             "generador produce en crudo")]
    public bool enforceSolvable = true;
    [Tooltip("Segundos hacia delante que simula el verificador. Tiene que dar para cruzar la " +
             "pantalla A LA VELOCIDAD DEL JUGADOR y sobrar margen para rodear")]
    public float solveHorizon = 12f;
    [Tooltip("Tamaño de celda del verificador, en unidades. Más fino = más exacto y más lento")]
    public float solveCell = 0.45f;
    [Tooltip("Cuántos asteroides puede retirar como mucho para abrir un paso. Si en el log " +
             "ves que llega a este número a menudo, el problema NO es el verificador: es que " +
             "la cantidad está demasiado alta para la velocidad del jugador")]
    public int solveMaxRemovals = 8;
    [Tooltip("Margen que se le deja al jugador por encima de su radio real al comprobar si " +
             "cabe. Un paso que cabe justo no es un paso")]
    public float solveClearance = 0.12f;

    [Header("Justicia — ventana de reacción")]
    [Tooltip("Segundos mínimos entre que un asteroide entra y podría alcanzar al jugador. " +
             "El reflejo puro son 250 ms, pero VER la amenaza, decidir y empezar a moverse " +
             "pide unos 600")]
    public float reactionWindow = 0.6f;

    [Header("Justicia — densidad local")]
    [Tooltip("Radio alrededor del jugador en el que se cuentan los asteroides")]
    public float crowdRadius = 3.2f;
    [Tooltip("Si ya hay tantos cerca, la reposición espera. No quita nada de lo que hay: " +
             "solo evita que el repoblador empeore una situación ya mala")]
    public int crowdCap = 6;

    // ------------------------------------------------------------------ curva
    /// El logaritmo que nunca se agota. Sube siempre; cada vez más despacio.
    float Grow(int level, float start, float growth, float knee)
    {
        float n = Mathf.Max(0f, level - 1);
        return start + growth * intensity * Mathf.Log(1f + n / Mathf.Max(1f, knee));
    }

    /// Cuánto sube este eje al pasar de este nivel al siguiente. Es la derivada, y es el
    /// número que de verdad dice si la curva "sigue viva" en el nivel 999.
    public float BudgetStepAt(int level) =>
        budgetGrowth * intensity / (Mathf.Max(0f, level - 1) + Mathf.Max(1f, budgetKnee));

    public int Tier(int level) => Mathf.Max(0, (level - 1) / Mathf.Max(1, tierLength));

    /// 0.65 en un nivel de respiro, 1.45 en uno de apretón, 1 en los demás.
    /// Depende solo del número de nivel, así que es igual en todas las partidas.
    public float Mood(int level)
    {
        if (calmEvery > 0 && level % calmEvery == 0) return calmFactor;
        if (surgeEvery > 0 && level % surgeEvery == 0) return surgeFactor;
        return 1f;
    }

    public bool IsCalm(int level) => calmEvery > 0 && level % calmEvery == 0;
    public bool IsSurge(int level) => !IsCalm(level) && surgeEvery > 0 && level % surgeEvery == 0;

    /// Sin tope. El único límite real a la cantidad es maxMovingAsteroids, que es de
    /// rendimiento: cuando se alcanza, la dificultad sigue subiendo por los otros ejes.
    public float Budget(int level) =>
        (useKeyCurves && budgetKeys != null && budgetKeys.Length > 0 ? Key(budgetKeys, level, false, float.MaxValue)
                                                                      : Grow(level, budgetStart, budgetGrowth, budgetKnee)) * Mood(level);

    public float SpeedMultiplier(int level)
    {
        float mood = Mathf.Lerp(1f, Mood(level), Mathf.Clamp01(moodAffectsSpeed));
        float v = useKeyCurves && speedKeys != null && speedKeys.Length > 0
            ? Key(speedKeys, StepLevel(level), true, speedCap)
            : Grow(level, speedStart, speedGrowth, speedKnee);
        return v * mood;
    }

    /// Velocidad de crucero del jugador en este nivel (0 = no tocarla)
    public float PlayerCruise(int level)
    {
        if (!useKeyCurves || cruiseKeys == null || cruiseKeys.Length == 0) return 0f;
        float v = cruiseKeys[0].y;
        foreach (var k in cruiseKeys) if (level >= k.x) v = k.y;
        return Mathf.Min(cruiseCap, v);
    }

    // El nivel con el que se lee la tabla: el primero de su escalón (1, 10, 20, 30...)
    int StepLevel(int level)
    {
        int st = Mathf.Max(1, speedStep);
        return Mathf.Max(1, level / st * st);
    }

    // Interpola en la tabla según el logaritmo del nivel (los puntos lejanos no se notan como
    // saltos). Pasado el último punto: sigue la tendencia del último tramo si extend, o se queda
    static float Key(Vector2[] keys, int level, bool extend, float cap)
    {
        float lx = Mathf.Log(Mathf.Max(1, level));
        if (level <= keys[0].x) return Mathf.Min(cap, keys[0].y);
        for (int i = 1; i < keys.Length; i++)
        {
            if (level > keys[i].x) continue;
            float a = Mathf.Log(Mathf.Max(1f, keys[i - 1].x)), b = Mathf.Log(Mathf.Max(1f, keys[i].x));
            float t = b > a ? (lx - a) / (b - a) : 1f;
            return Mathf.Min(cap, Mathf.Lerp(keys[i - 1].y, keys[i].y, t));
        }
        var last = keys[keys.Length - 1];
        if (!extend || keys.Length < 2) return Mathf.Min(cap, last.y);
        var prev = keys[keys.Length - 2];
        float la = Mathf.Log(Mathf.Max(1f, prev.x)), lb = Mathf.Log(Mathf.Max(1f, last.x));
        float slope = lb > la ? (last.y - prev.y) / (lb - la) : 0f;
        return Mathf.Min(cap, last.y + slope * (lx - lb));
    }

    public float FlowAmplitude(int level) =>
        Mathf.Min(flowHardLimit, Grow(level, flowStart, flowGrowth, flowKnee));

#if UNITY_EDITOR
    [ContextMenu("Ver la curva")]
    void Report()
    {
        var sb = new System.Text.StringBuilder($"DifficultyProfile  (intensidad {intensity:0.00})\n");
        sb.AppendLine("   nivel  cantidad  +/nivel   veloc.  corriente");
        foreach (int n in new[] { 1, 10, 20, 50, 100, 250, 500, 999, 3000, 10000 })
            sb.AppendLine($"  {n,6}  {Grow(n, budgetStart, budgetGrowth, budgetKnee),8:0.#}  " +
                          $"{BudgetStepAt(n),7:0.000}  {Grow(n, speedStart, speedGrowth, speedKnee),6:0.00}  " +
                          $"{FlowAmplitude(n),8:0}°");
        sb.AppendLine("  (cantidad y velocidad sin el ritmo: el respiro resta un 35% y el apretón suma un 45%)");
        sb.AppendLine($"  En el nivel 999 la cantidad aún sube {BudgetStepAt(999):0.000} por nivel. No hay techo.");
        Debug.Log(sb.ToString());
    }
#endif
}
