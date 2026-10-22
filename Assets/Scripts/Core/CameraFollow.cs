using UnityEngine;

/// La cámara sigue al jugador y se adelanta hacia donde va.
///
/// Va en un objeto padre (CameraRig) y la cámara cuelga de él. No mueve la cámara
/// directamente porque ImpactFeedback ya la mueve para el temblor: si los dos tocaran la
/// misma posición, se pisarían. Así el padre viaja y la hija tiembla encima.
///
/// LA ANTICIPACIÓN
///
/// Se adelanta en proporción a la velocidad: quieto, el jugador queda en el centro; a
/// crucero, la cámara enseña más de lo que tiene delante y menos de lo que deja atrás. Es lo
/// que hace justa la velocidad letal: lo que te puede matar entra en pantalla con tiempo.
/// En horizontal el adelanto puede ser mayor que en vertical, porque la pantalla es ancha.
///
/// Se suaviza en dos capas: el adelanto cambia despacio (un giro brusco no sacude la vista)
/// y la cámara persigue ese punto algo más rápido (no se queda atrás).
[DefaultExecutionOrder(100)]
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("Referencias")]
    [Tooltip("Vacío = busca el PlayerController")]
    [SerializeField] private Transform target;
    [Tooltip("Vacío = la cámara hija de este objeto")]
    [SerializeField] private Camera cam;

    [Header("Anticipación")]
    [Tooltip("Segundos de velocidad que se adelanta. 0.6 a crucero (5 u/s) = 3 unidades")]
    [SerializeField] private float lookAheadTime = 0.6f;
    [Tooltip("Adelanto máximo en unidades, en horizontal (x) y en vertical (y)")]
    [SerializeField] private Vector2 maxLookAhead = new Vector2(4f, 2f);
    [Tooltip("Lo que tarda el adelanto en cambiar. Más alto = gira la vista más despacio")]
    [SerializeField] private float lookAheadSmooth = 0.35f;

    [Header("Seguimiento")]
    [Tooltip("Lo que tarda la cámara en alcanzar su punto. Más bajo = más pegada")]
    [SerializeField] private float followSmooth = 0.12f;

    [Header("Límites")]
    [Tooltip("La cámara nunca enseña lo que hay fuera del mundo")]
    [SerializeField] private bool clampToWorld = true;
    [Tooltip("La cámara se asoma más allá del límite del mundo justo lo que el dibujo del " +
             "jugador sobresale de su zona de choque. Así, pegado a la pared, el astronauta toca " +
             "el borde de la pantalla: ni se corta ni queda un hueco que parezca un muro invisible")]
    [SerializeField] private bool autoReveal = true;
    [Tooltip("Asomarse más allá de eso, en unidades. 0 = el borde del mundo es el de la pantalla")]
    [SerializeField] private float extraReveal = 0f;
    [Tooltip("Distancia mínima del jugador a los bordes de la pantalla, en unidades. Si un " +
             "empujón o la gravedad lo lanzan más rápido de lo que la cámara le sigue, la " +
             "cámara se corrige al momento para no perderlo de vista")]
    [SerializeField] private float keepInView = 1.2f;

    [Header("Encuadre de la grieta")]
    [Tooltip("Cerca de una grieta la cámara se aleja para que se vea lo grande que es, y se " +
             "inclina hacia la sección abierta para que se sepa cuál es la vulnerable")]
    [SerializeField] private bool frameFracture = true;
    [Tooltip("A cuántas unidades de la grieta empieza (x) y a cuántas es completo (y)")]
    [SerializeField] private Vector2 fractureRange = new Vector2(16f, 7f);
    [Tooltip("Alejamiento máximo (1 = nada). Tope: 1.6")]
    [Range(1f, 1.6f)] [SerializeField] private float fractureZoom = 1f;
    [Tooltip("Cuánto se desplaza la vista hacia la grieta en vertical (hacia la sección abierta si la hay)")]
    [Range(0f, 1f)] [SerializeField] private float fracturePull = 0.35f;
    [Tooltip("Lo que tarda el punto al que mira en pasar de un eslabón a otro (evita saltos)")]
    [SerializeField] private float fractureFocusSmooth = 0.8f;
    [Tooltip("Temblor lento cerca de la grieta (unidades), que crece con su latido")]
    [SerializeField] private float fractureRumble = 0.06f;

    [Header("Encuadre de la meta")]
    [Tooltip("Al acercarse al agujero negro, la cámara se desplaza en vertical para dejarlo " +
             "contra el borde de arriba o de abajo de la pantalla: se ve solo una porción, no " +
             "centrado. Va hacia la pared que tenga más cerca el agujero")]
    [SerializeField] private bool frameGoal = true;
    [Tooltip("A cuántas unidades del borde de la sombra negra empieza a encuadrar")]
    [SerializeField] private float frameRange = 6f;
    [Tooltip("A esta distancia el encuadre ya es completo. Entre las dos, se mezcla suave")]
    [SerializeField] private float frameFullAt = 2.5f;
    [Tooltip("Cuánto del radio de la sombra negra entra en la pantalla con el encuadre completo. " +
             "0 = el centro justo en el borde (se ve un cuarto), 1 = la sombra entera en vertical")]
    [Range(0f, 1.5f)] [SerializeField] private float coreInside = 0.5f;

    /// Velocidad real de la cámara (unidades/s). La usa el FlowField para meter asteroides
    /// también por el lado hacia el que viaja la vista.
    public Vector2 Velocity { get; private set; }

    Rigidbody2D targetRb;
    PlayerController targetPc;
    float playerOverhang;      // lo que el dibujo sobresale de la zona de choque
    Vector2 lookAhead, lookAheadVel, posVel;
    Goal hole;

    void Awake()
    {
        Instance = this;
        if (cam == null) cam = GetComponentInChildren<Camera>();
        if (cam == null) Debug.LogError("CameraFollow: no hay ninguna cámara hija.", this);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start() => FindTarget();

    void FindTarget()
    {
        if (target == null)
        {
            var p = FindFirstObjectByType<PlayerController>();
            if (p != null) target = p.transform;
        }
        if (target != null)
        {
            targetRb = target.GetComponent<Rigidbody2D>();
            targetPc = target.GetComponent<PlayerController>();
            MeasureOverhang();
        }
    }

    void LateUpdate()
    {
        var gb = GameBounds.Instance;
        if (gb == null || !gb.IsExpanded) return;
        if (target == null) { FindTarget(); if (target == null) return; }

        // Tiempo real: con Slow el mundo va a cámara lenta pero el jugador no, y la cámara
        // tiene que seguirle a su ritmo. Con el juego en pausa (escala 0) no se mueve
        float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
        if (dt <= 0f) return;

        Vector2 vel = targetRb != null ? targetRb.linearVelocity : Vector2.zero;
        Vector2 want = new Vector2(
            Mathf.Clamp(vel.x * lookAheadTime, -maxLookAhead.x, maxLookAhead.x),
            Mathf.Clamp(vel.y * lookAheadTime, -maxLookAhead.y, maxLookAhead.y));
        lookAhead = Vector2.SmoothDamp(lookAhead, want, ref lookAheadVel, lookAheadSmooth, Mathf.Infinity, dt);

        Vector2 goal = Clamp(FrameFracture(FrameGoal((Vector2)target.position + lookAhead), dt));
        Vector2 pos = Vector2.SmoothDamp(transform.position, goal, ref posVel, followSmooth, Mathf.Infinity, dt);
        // Durante la llegada el jugador viene de fuera del mundo: ahí la vista no lo persigue
        bool arriving = targetPc != null && targetPc.IsExpelling;
        pos = arriving ? Clamp(pos) : Clamp(KeepInView(Clamp(pos), target.position));

        // Presencia de la grieta: un temblor lento y sordo que crece al latir
        var fdr = FractureDirector.Instance;
        if (fdr != null && fdr.Presence > 0.01f && fractureRumble > 0f)
        {
            float tt = Time.time;
            float amp = fractureRumble * fdr.Presence * (0.6f + 0.4f * fdr.Heartbeat);
            pos += new Vector2((Mathf.PerlinNoise(tt * 1.7f, 3.1f) - 0.5f) * 2f, (Mathf.PerlinNoise(7.3f, tt * 1.7f) - 0.5f) * 2f) * amp;
        }

        Velocity = (pos - (Vector2)transform.position) / dt;
        WarnIfOutside(gb);
        transform.position = new Vector3(pos.x, pos.y, transform.position.z);
        gb.RefreshCamera();
    }

    /// Salta al jugador sin recorrido. Al empezar un nivel: si no, la cámara cruzaría el mapa
    /// desde donde estaba, y los asteroides nacerían alrededor de una vista que ya no vale.
    public void Snap()
    {
        var gb = GameBounds.Instance;
        if (target == null) FindTarget();
        if (target == null || gb == null) return;

        lookAhead = lookAheadVel = posVel = Velocity = Vector2.zero;
        // Durante la llegada el jugador aún está fuera: la vista se coloca donde se va a parar
        Vector2 at = target.position;
        var pc = target.GetComponent<PlayerController>();
        if (pc != null && pc.IsExpelling) at = pc.SpawnPoint;
        Vector2 pos = gb.IsExpanded ? Clamp(at) : (Vector2)transform.position;
        transform.position = new Vector3(pos.x, pos.y, transform.position.z);
        gb.RefreshCamera();
    }

    // Diagnóstico: si el jugador acaba fuera del mundo, la cámara no puede seguirlo (no pasa
    // de la pared + Edge Reveal). Eso sería que ha atravesado una pared, y lo dice una vez
    bool warnedOutside;
    void WarnIfOutside(GameBounds gb)
    {
        if (warnedOutside || target == null || (targetPc != null && (targetPc.IsExpelling || targetPc.IsAbsorbed))) return;
        Vector2 p = target.position;
        Rect a = gb.Area;
        if (p.x < a.xMin - 0.5f || p.x > a.xMax + 0.5f || p.y < a.yMin - 0.5f || p.y > a.yMax + 0.5f)
        {
            warnedOutside = true;
            Debug.LogWarning($"CameraFollow: el jugador está fuera del mundo en {p}. Mundo = {a}. " +
                             "Ha atravesado una pared: revisa la capa Walls en la matriz de colisiones.", this);
        }
    }

    void MeasureOverhang()
    {
        if (target == null) return;
        // Durante la llegada o la caída el dibujo está estirado: se mide luego, con su forma normal
        if (targetPc != null && (targetPc.IsExpelling || targetPc.IsAbsorbed || targetPc.IsDead)) return;
        float drawn = 0f;
        foreach (var r in target.GetComponentsInChildren<SpriteRenderer>())
            if (r.enabled && r.sprite != null) drawn = Mathf.Max(drawn, r.bounds.extents.x, r.bounds.extents.y);
        var col = target.GetComponentInChildren<Collider2D>();
        float hit = col != null ? Mathf.Min(col.bounds.extents.x, col.bounds.extents.y) : 0f;
        playerOverhang = Mathf.Max(0f, drawn - hit);
    }

    // Cerca de la meta, la vista baja o sube para que el agujero quede contra el borde de la
    // pantalla. Keep In View se aplica después: el jugador nunca sale de la vista por esto
    float zoomNow = 1f, focusY, focusVel;
    bool focusReady;

    // Cerca de la grieta: se aleja la cámara y la vista se inclina hacia ella (hacia la sección
    // abierta, si la hay). Keep In View sigue mandando: el jugador nunca sale de la vista
    Vector2 FrameFracture(Vector2 p, float dt)
    {
        var fd = FractureDirector.Instance;
        float w = 0f;
        Vector2 focus = p;
        if (frameFracture && fd != null && fd.Active && target != null)
        {
            Vector2 me = target.position;
            float dist = Mathf.Abs(Vector2.Dot(me - fd.Center, fd.Front));   // distancia al borde de la grieta
            w = 1f - Mathf.InverseLerp(fractureRange.y, fractureRange.x, dist);
            w = w * w * (3f - 2f * w);
            var open = fd.OpenSection;
            focus = open != null ? open.WeakPoint : fd.Center;
        }
        float want = Mathf.Lerp(1f, fractureZoom, w);
        zoomNow = Mathf.MoveTowards(zoomNow, want, dt * 0.8f);
        CameraAspectFitter.Zoom = zoomNow;
        // El punto al que mira se desliza: cambiar de eslabón no da saltos
        if (!focusReady) { focusY = focus.y; focusReady = true; }
        focusY = Mathf.SmoothDamp(focusY, focus.y, ref focusVel, fractureFocusSmooth, Mathf.Infinity, dt);
        if (w > 0f) p.y = Mathf.Lerp(p.y, focusY, fracturePull * w);
        else focusY = p.y;
        return p;
    }

    Vector2 FrameGoal(Vector2 p)
    {
        if (!frameGoal || cam == null || target == null) return p;
        if (hole == null) { hole = FindFirstObjectByType<Goal>(FindObjectsInactive.Include); if (hole == null) return p; }
        if (!hole.isActiveAndEnabled) return p;   // escondida: mapa de fractura sin cerrar
        var gb = GameBounds.Instance;
        if (gb == null) return p;

        Vector2 g = hole.transform.position;
        float core = hole.CoreRadius;
        float dist = Vector2.Distance(target.position, g) - core;
        float w = 1f - Mathf.InverseLerp(Mathf.Min(frameFullAt, frameRange - 0.01f), frameRange, dist);
        if (w <= 0f) return p;
        w = w * w * (3f - 2f * w);

        // Agujero en la mitad de arriba del mundo: queda arriba en la pantalla (la vista baja)
        float side = g.y >= gb.Area.center.y ? 1f : -1f;
        float camY = g.y - side * (CameraView.HalfHeight(cam) - coreInside * core);
        p.y = Mathf.Lerp(p.y, camY, w);
        return p;
    }

    // El jugador nunca a menos de Keep In View del borde de la pantalla
    Vector2 KeepInView(Vector2 camPos, Vector2 player)
    {
        if (cam == null || keepInView <= 0f) return camPos;
        float halfH = CameraView.HalfHeight(cam);
        float halfW = halfH * cam.aspect;
        float mx = Mathf.Max(0f, halfW - keepInView), my = Mathf.Max(0f, halfH - keepInView);
        camPos.x = Mathf.Clamp(camPos.x, player.x - mx, player.x + mx);
        camPos.y = Mathf.Clamp(camPos.y, player.y - my, player.y + my);
        return camPos;
    }

    // El centro de la cámara, metido para que el borde de la vista no pase de la pared más
    // Edge Reveal: se ve el límite y un poco de lo que hay detrás
    Vector2 Clamp(Vector2 p)
    {
        var gb = GameBounds.Instance;
        if (!clampToWorld || gb == null || cam == null) return p;

        if (autoReveal && playerOverhang <= 0f) MeasureOverhang();
        float rv = Mathf.Max(0f, (autoReveal ? playerOverhang : 0f) + extraReveal);
        Rect w = new Rect(gb.Area.xMin - rv, gb.Area.yMin - rv, gb.Area.width + 2f * rv, gb.Area.height + 2f * rv);
        // Lo que ve la cámara AHORA, no el tamaño de pantalla guardado al arrancar: si este se
        // midió antes de que la cámara tomara su tamaño final, el tope dejaba sin ver el borde
        float halfH = CameraView.HalfHeight(cam);
        float halfW = halfH * cam.aspect;

        p.x = w.width  > 2f * halfW ? Mathf.Clamp(p.x, w.xMin + halfW, w.xMax - halfW) : w.center.x;
        p.y = w.height > 2f * halfH ? Mathf.Clamp(p.y, w.yMin + halfH, w.yMax - halfH) : w.center.y;
        return p;
    }
}