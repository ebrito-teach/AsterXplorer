using System.Collections.Generic;
using UnityEngine;

/// Fondo vivo: fenómenos del espacio, unos más raros que otros, todos dibujados por código.
///
///   AMBIENTALES (duran todo el mapa; como mucho 2, en la grieta 1)
///     Relámpagos en nebulosa  60 % de los mapas con nebulosa   profundidad de su nebulosa
///     Viento estelar          30 %                             0.75–0.85
///     Campos magnéticos       20 %                             0.85–0.9
///     Binaria que se eclipsa  15 %                             0.97
///     Púlsar con haces        12 %                             0.97
///   EVENTOS (una vez, en un momento al azar, a la vista)
///     Lluvia de meteoros      20 %                             0.8
///     Cometa lejano           12 %  diminuto y tenue           0.8, detrás de las estrellas
///     Tormenta solar           5 %                             0.87
///     Supernova lejana         1 %                             0.97
///
/// VARIANTES: cada uno sortea la suya al aparecer: tamaño pequeño (el de antes) 50 %, mediano
/// 35 %, grande 15 % (lo grande, algo más cerca), y además duración, giro o densidad propios.
/// Los eventos no se repiten: duran más (meteoros 15–30 s, cometa 40–60 s, tormenta 8–12 s,
/// supernova 30–45 s). Magnético, binaria y púlsar te acompañan: si te alejas, se quedan en el
/// borde de la vista cada vez más tenues, pero nunca desaparecen.
///
/// Reglas: nada que parezca peligro (colores suaves, nada rojo, en las capas de fondo); nunca
/// tapa ni toca las constelaciones (comprueba toda la zona por la que puede pasar la cámara);
/// en los mundos oscuros no sale; en los móviles débiles, menos (PerformanceManager).
///
/// Va en un objeto vacío de la escena. Necesita SkyDirector y BackdropDirector en la escena.
[DefaultExecutionOrder(160)]   // después de la cámara y del cielo
public class LivingSky : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private GameDirector director;

    [Header("Ambientales: probabilidad por mapa")]
    [Range(0f, 1f)] [SerializeField] private float lightningChance = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float windChance = 0.3f;
    [Range(0f, 1f)] [SerializeField] private float magneticChance = 0.2f;
    [Range(0f, 1f)] [SerializeField] private float binaryChance = 0.15f;
    [Range(0f, 1f)] [SerializeField] private float pulsarChance = 0.12f;
    [SerializeField] private int maxAmbient = 2;
    [SerializeField] private int maxAmbientFracture = 1;

    [Header("Eventos: probabilidad por mapa")]
    [Range(0f, 1f)] [SerializeField] private float meteorChance = 0.2f;
    [Range(0f, 1f)] [SerializeField] private float cometChance = 0.12f;
    [Range(0f, 1f)] [SerializeField] private float stormChance = 0.05f;
    [Range(0f, 1f)] [SerializeField] private float supernovaChance = 0.01f;
    [Tooltip("Segundos desde el inicio del nivel en los que puede ocurrir el evento")]
    [SerializeField] private Vector2 eventTime = new Vector2(6f, 40f);

    [Header("Distancia a las constelaciones")]
    [Tooltip("Margen libre entre un fenómeno y la caja de una constelación")]
    [SerializeField] private float constellationMargin = 1f;

    [Header("Los gigantes te acompañan (magnético, binaria, púlsar)")]
    [Tooltip("Hasta qué parte de la vista pueden alejarse antes de quedarse en el borde (1 = el borde)")]
    [Range(0.5f, 1f)] [SerializeField] private float leashEdge = 0.9f;
    [Tooltip("Lo más tenues que llegan a estar cuando te alejas mucho (nunca 0)")]
    [Range(0.05f, 1f)] [SerializeField] private float leashMinAlpha = 0.3f;

    [Header("Pruebas")]
    [Tooltip("Ignora las probabilidades y fuerza este fenómeno (Ninguno = normal)")]
    [SerializeField] private Kind forceKind = Kind.Ninguno;

    public enum Kind { Ninguno, Relampagos, Viento, Magnetico, Binaria, Pulsar, Meteoros, Cometa, Tormenta, Supernova }

    const string LNebula = "BG_Nebula", LStars = "BG_Stars", LObjects = "BG_Objects", LDust = "BG_Dust";

    abstract class Fx
    {
        public Transform root;
        public float follow;
        public Vector2 local;
        public bool done;
        /// Los gigantes (magnético, binaria, púlsar) te acompañan: si te alejas, se quedan en el
        /// borde de la vista y se van apagando, pero nunca del todo
        public bool leash;
        public float alphaMul = 1f;
        public abstract void Tick(LivingSky s, float t, float dt);
    }

    readonly List<Fx> fxs = new List<Fx>();
    readonly List<Kind> pendingEvents = new List<Kind>();
    readonly List<float> pendingAt = new List<float>();
    SkyDirector sky;
    BackdropDirector backdrop;
    float levelStart;
    int generateAt = -1;
    Sprite glow, dot, streak, ring;
    Sprite[] bolts;
    Sprite field, field6;

    // ------------------------------------------------------------------ ciclo de vida
    void Awake()
    {
        glow = MakeGlow(64);
        dot = MakeDot(16);
        streak = MakeStreak(64, 8);
        ring = MakeRing(128);
        bolts = new Sprite[4];
        for (int i = 0; i < bolts.Length; i++) bolts[i] = MakeBolt(128, i * 7919 + 13);
        field = MakeField(256, new[] { 0.18f, 0.28f, 0.38f, 0.47f });
        field6 = MakeField(256, new[] { 0.12f, 0.19f, 0.26f, 0.33f, 0.40f, 0.47f });
    }

    void OnEnable()
    {
        if (director == null) director = FindFirstObjectByType<GameDirector>();
        if (director != null) director.LevelStarted += OnLevelStarted;
    }

    void OnDisable()
    {
        if (director != null) director.LevelStarted -= OnLevelStarted;
    }

    // Un fotograma después: el cielo y las nebulosas se generan con el mismo aviso
    void OnLevelStarted(int level) { Clear(); generateAt = Time.frameCount + 1; }

    void Clear()
    {
        foreach (var f in fxs) if (f.root != null) Destroy(f.root.gameObject);
        fxs.Clear();
        pendingEvents.Clear();
        pendingAt.Clear();
    }

    void Update()
    {
        if (generateAt >= 0 && Time.frameCount >= generateAt) { generateAt = -1; Generate(); }

        // Eventos programados
        for (int i = pendingEvents.Count - 1; i >= 0; i--)
        {
            if (Time.time - levelStart < pendingAt[i]) continue;
            var k = pendingEvents[i];
            pendingEvents.RemoveAt(i); pendingAt.RemoveAt(i);
            StartEvent(k);
        }
    }

    void LateUpdate()
    {
        Vector2 cam = CameraPos();
        float t = Time.time, dt = Time.deltaTime;
        for (int i = fxs.Count - 1; i >= 0; i--)
        {
            var f = fxs[i];
            if (f.root == null) { fxs.RemoveAt(i); continue; }
            f.root.position = cam * f.follow + f.local;
            if (f.leash) Leash(f, cam);
            f.Tick(this, t, dt);
            if (f.done) { Destroy(f.root.gameObject); fxs.RemoveAt(i); }
        }
    }

    // Si el fenómeno se sale de la vista, se queda en su borde; cuanto más lejos estaría, más
    // tenue, hasta Leash Min Alpha (nunca desaparece)
    void Leash(Fx f, Vector2 cam)
    {
        Vector2 half = ScreenHalf() * leashEdge;
        Vector2 rel = (Vector2)f.root.position - cam;
        Vector2 cl = new Vector2(Mathf.Clamp(rel.x, -half.x, half.x), Mathf.Clamp(rel.y, -half.y, half.y));
        float excess = (rel - cl).magnitude;
        if (excess > 0.001f) f.root.position = cam + cl;
        f.alphaMul = Mathf.Max(leashMinAlpha, 1f - excess / Mathf.Max(0.01f, half.x * 2f));
    }

    // ------------------------------------------------------------------ sorteo
    [ContextMenu("Sortear el fondo vivo otra vez")]
    void Generate()
    {
        Clear();
        levelStart = Time.time;
        if (director != null && director.IsDarkWorld) return;
        if (sky == null) sky = FindFirstObjectByType<SkyDirector>();
        if (backdrop == null) backdrop = FindFirstObjectByType<BackdropDirector>();

        bool low = PerformanceManager.Current == PerformanceManager.Tier.Low;
        bool fracture = director != null && director.IsFractureWorld;
        int cap = fracture ? maxAmbientFracture : maxAmbient;
        if (low) cap = Mathf.Min(cap, 1);

        var amb = new List<Kind>();
        var ev = new List<Kind>();
        if (forceKind != Kind.Ninguno)
        {
            if (forceKind <= Kind.Pulsar) amb.Add(forceKind); else ev.Add(forceKind);
        }
        else
        {
            var nebs = new List<SpriteRenderer>();
            if (backdrop != null) backdrop.GetNebulas(nebs, new List<float>(), new List<Vector2>());
            if (nebs.Count > 0 && Random.value < lightningChance) amb.Add(Kind.Relampagos);
            if (!low && Random.value < windChance) amb.Add(Kind.Viento);
            if (Random.value < magneticChance) amb.Add(Kind.Magnetico);
            if (Random.value < binaryChance) amb.Add(Kind.Binaria);
            if (Random.value < pulsarChance) amb.Add(Kind.Pulsar);
            // Tope: se quedan los primeros de una lista barajada
            for (int i = amb.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var x = amb[i]; amb[i] = amb[j]; amb[j] = x; }
            while (amb.Count > cap) amb.RemoveAt(amb.Count - 1);

            if (Random.value < meteorChance) ev.Add(Kind.Meteoros);
            if (Random.value < cometChance) ev.Add(Kind.Cometa);
            if (Random.value < stormChance) ev.Add(Kind.Tormenta);
            if (Random.value < supernovaChance) ev.Add(Kind.Supernova);
            while (ev.Count > 1) ev.RemoveAt(Random.Range(0, ev.Count));   // un evento por mapa
        }

        foreach (var k in amb) StartAmbient(k);
        foreach (var k in ev) { pendingEvents.Add(k); pendingAt.Add(forceKind != Kind.Ninguno ? 1.5f : Random.Range(eventTime.x, eventTime.y)); }
        if (amb.Count + ev.Count > 0) Debug.Log($"LivingSky: {string.Join(", ", amb)}{(ev.Count > 0 ? " · evento: " + string.Join(", ", ev) : "")}.");
    }

    void StartAmbient(Kind k)
    {
        switch (k)
        {
            case Kind.Relampagos: TryAdd(MakeLightning()); break;
            case Kind.Viento: TryAdd(MakeWind()); break;
            case Kind.Magnetico: TryAdd(MakeMagnetic()); break;
            case Kind.Binaria: TryAdd(MakeBinary()); break;
            case Kind.Pulsar: TryAdd(MakePulsar()); break;
        }
    }

    void StartEvent(Kind k)
    {
        switch (k)
        {
            case Kind.Meteoros: TryAdd(MakeMeteors()); break;
            case Kind.Cometa: TryAdd(MakeComet()); break;
            case Kind.Tormenta: TryAdd(MakeStorm()); break;
            case Kind.Supernova: TryAdd(MakeSupernova()); break;
        }
    }

    void TryAdd(Fx f)
    {
        if (f == null) return;
        f.root.SetParent(transform, true);
        fxs.Add(f);
    }

    // ------------------------------------------------------------------ dónde
    Vector2 CameraPos()
    {
        if (CameraFollow.Instance != null) return CameraFollow.Instance.transform.position;
        var c = Camera.main;
        return c != null ? (Vector2)c.transform.position : Vector2.zero;
    }

    Vector2 ScreenHalf()
    {
        var c = Camera.main;
        return c != null ? CameraView.Half(c) : new Vector2(10f, 5.75f);
    }

    // Las posiciones posibles de la cámara (5 × 5 puntos del recorrido)
    readonly List<Vector2> camSamples = new List<Vector2>();
    void SampleCams()
    {
        camSamples.Clear();
        var gb = GameBounds.Instance;
        Vector2 half = ScreenHalf();
        Rect a = gb != null && gb.IsReady ? gb.Area : new Rect(CameraPos() - half, half * 2f);
        float x0 = a.xMin + half.x, x1 = a.xMax - half.x, y0 = a.yMin + half.y, y1 = a.yMax - half.y;
        if (x1 < x0) x0 = x1 = a.center.x;
        if (y1 < y0) y0 = y1 = a.center.y;
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
                camSamples.Add(new Vector2(Mathf.Lerp(x0, x1, i / 4f), Mathf.Lerp(y0, y1, j / 4f)));
    }

    // ¿Un círculo (en coordenadas de su capa) queda lejos de toda constelación, se mueva la cámara como se mueva?
    bool ClearOfConstellations(Vector2 local, float follow, float radius)
    {
        if (sky == null) sky = FindFirstObjectByType<SkyDirector>();
        if (sky == null || sky.ConstellationBoxes.Count == 0) return true;
        if (camSamples.Count == 0) SampleCams();
        float mf = sky.MidFollow;
        float need = radius + constellationMargin;
        foreach (var cam in camSamples)
        {
            Vector2 c = cam * follow + local;
            foreach (var b in sky.ConstellationBoxes)
            {
                Vector2 o = b.position + cam * mf;
                float dx = Mathf.Max(o.x - c.x, 0f, c.x - (o.x + b.width));
                float dy = Mathf.Max(o.y - c.y, 0f, c.y - (o.y + b.height));
                if (dx * dx + dy * dy < need * need) return false;
            }
        }
        return true;
    }

    // ¿Un círculo (en coordenadas de su capa) queda lejos de toda constelación con la cámara de ahora?
    bool ClearNow(Vector2 local, float follow, float radius)
    {
        if (sky == null) sky = FindFirstObjectByType<SkyDirector>();
        if (sky == null || sky.ConstellationBoxes.Count == 0) return true;
        Vector2 cam = CameraPos();
        Vector2 c = cam * follow + local;
        float need = radius + constellationMargin;
        foreach (var b in sky.ConstellationBoxes)
        {
            Vector2 o = b.position + cam * sky.MidFollow;
            float dx = Mathf.Max(o.x - c.x, 0f, c.x - (o.x + b.width));
            float dy = Mathf.Max(o.y - c.y, 0f, c.y - (o.y + b.height));
            if (dx * dx + dy * dy < need * need) return false;
        }
        return true;
    }

    // Un sitio de la capa por el que la cámara pueda pasar (ambientales)
    bool PickAnywhere(float follow, float radius, out Vector2 local)
    {
        SampleCams();
        var gb = GameBounds.Instance;
        Vector2 half = ScreenHalf();
        Rect a = gb != null && gb.IsReady ? gb.Area : new Rect(CameraPos() - half, half * 2f);
        for (int i = 0; i < 40; i++)
        {
            // Un punto del mundo visto desde una cámara al azar, pasado a la capa
            Vector2 cam = camSamples[Random.Range(0, camSamples.Count)];
            Vector2 world = cam + new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y)) * 0.8f;
            local = world - cam * follow;
            if (ClearOfConstellations(local, follow, radius)) return true;
        }
        local = Vector2.zero;
        return false;
    }

    // Un sitio a la vista ahora mismo (eventos)
    bool PickInView(float follow, float radius, float spread, out Vector2 local)
    {
        SampleCams();
        Vector2 cam = CameraPos(), half = ScreenHalf();
        for (int i = 0; i < 40; i++)
        {
            Vector2 world = cam + new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y)) * spread;
            local = world - cam * follow;
            if (ClearOfConstellations(local, follow, radius)) return true;
        }
        local = Vector2.zero;
        return false;
    }

    // ------------------------------------------------------------------ piezas
    SpriteRenderer Sr(Transform parent, string name, Sprite s, string layer, int order, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.sortingLayerName = layer; sr.sortingOrder = order; sr.color = c;
        return sr;
    }

    static void Size(SpriteRenderer sr, float w, float h)
    {
        var b = sr.sprite.bounds.size;
        sr.transform.localScale = new Vector3(w / Mathf.Max(0.0001f, b.x), h / Mathf.Max(0.0001f, b.y), 1f);
    }

    static Transform NewRoot(string name) => new GameObject(name).transform;

    // Variantes: pequeña (la de antes) 50 %, mediana 35 %, grande 15 %
    static int Variant() { float r = Random.value; return r < 0.5f ? 0 : r < 0.85f ? 1 : 2; }
    static float By(int v, float a, float b, float c) => v == 0 ? a : v == 1 ? b : c;
    static readonly string[] VName = { "pequeña", "mediana", "grande" };

    // ---------- Relámpagos en nebulosa: destellos breves dentro de una nebulosa
    class Lightning : Fx
    {
        public SpriteRenderer flash, bolt;
        public Bounds nebula;   // en coordenadas de su capa
        public float next, until, size;
        public int flickers;
        public Color tint;
        public override void Tick(LivingSky s, float t, float dt)
        {
            if (t >= next && flickers == 0)
            {
                // Un sitio dentro de la nebulosa y lejos de las constelaciones; rayo normal o grande
                for (int i = 0; i < 12; i++)
                {
                    var p = (Vector2)nebula.center + new Vector2((Random.value - 0.5f) * nebula.size.x, (Random.value - 0.5f) * nebula.size.y) * 0.7f;
                    size = Random.Range(1.2f, 2.6f) * (Random.value < 0.65f ? 1f : 1.5f);
                    if (!s.ClearOfConstellations(p, follow, size)) continue;
                    flash.transform.localPosition = p - local;
                    bolt.transform.localPosition = p - local;
                    bolt.sprite = s.bolts[Random.Range(0, s.bolts.Length)];
                    bolt.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                    bolt.flipY = Random.value < 0.5f;
                    LivingSky.Size(flash, size * 1.6f, size * 1.6f);
                    LivingSky.Size(bolt, size, size);
                    flickers = Random.Range(2, 4);
                    until = t;
                    break;
                }
                if (flickers == 0) next = t + 2f;
            }
            if (flickers > 0)
            {
                float k = (t - until) / 0.12f;   // cada parpadeo, 0.12 s
                if (k >= 1f) { flickers--; until = t + Random.Range(0.02f, 0.08f); if (flickers == 0) next = t + Random.Range(3f, 7f); k = 1f; }
                float a = k < 0f ? 0f : Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
                var c = tint; c.a = 0.35f * a; flash.color = c;
                var cb = Color.Lerp(tint, Color.white, 0.6f); cb.a = 0.6f * a; bolt.color = cb;
            }
            else { flash.color = Color.clear; bolt.color = Color.clear; }
        }
    }

    Fx MakeLightning()
    {
        if (backdrop == null) return null;
        var rs = new List<SpriteRenderer>(); var fs = new List<float>(); var ls = new List<Vector2>();
        backdrop.GetNebulas(rs, fs, ls);
        if (rs.Count == 0) return null;
        int i = Random.Range(0, rs.Count);
        var root = NewRoot("FondoVivo_Relampagos");
        var f = new Lightning { root = root, follow = fs[i], local = ls[i] };
        Vector2 cam = CameraPos();
        var b = rs[i].bounds;
        f.nebula = new Bounds((Vector2)b.center - cam * f.follow, b.size);
        f.tint = new Color(0.75f, 0.7f, 1f, 1f);
        f.flash = Sr(root, "Destello", glow, LNebula, 60, Color.clear);
        f.bolt = Sr(root, "Rayo", bolts[0], LNebula, 61, Color.clear);
        f.next = Time.time + Random.Range(1f, 4f);
        return f;
    }

    // ---------- Viento estelar: hebras muy tenues que fluyen en una dirección
    class Wind : Fx
    {
        public SpriteRenderer[] bits;
        public Vector2[] pos;
        public float[] speed, len, alpha;
        public Vector2 dir, half;
        public override void Tick(LivingSky s, float t, float dt)
        {
            // En su capa (parallax), pero siempre alrededor de la vista: lo que sale por un lado
            // de la pantalla entra por el contrario
            Vector2 drift = s.CameraPos() * (1f - follow);
            for (int i = 0; i < bits.Length; i++)
            {
                pos[i] += dir * speed[i] * dt;
                Vector2 rel = pos[i] - drift;
                if (rel.x > half.x) rel.x -= half.x * 2f; else if (rel.x < -half.x) rel.x += half.x * 2f;
                if (rel.y > half.y) rel.y -= half.y * 2f; else if (rel.y < -half.y) rel.y += half.y * 2f;
                pos[i] = rel + drift;
                bits[i].transform.localPosition = pos[i];
                // Dentro de una constelación, se apaga
                bool clear = s.ClearNow(local + pos[i], follow, len[i] * 0.5f);
                var c = bits[i].color; c.a = Mathf.MoveTowards(c.a, clear ? alpha[i] : 0f, dt * 0.5f); bits[i].color = c;
            }
        }
    }

    Fx MakeWind()
    {
        var root = NewRoot("FondoVivo_Viento");
        // Densidad del mapa: suave, normal o ráfaga (la ráfaga, no en gama baja)
        int v = Variant();
        if (v == 2 && PerformanceManager.Current == PerformanceManager.Tier.Low) v = 1;
        int n = Mathf.RoundToInt(By(v, 15f, 28f, 45f) * PerformanceManager.DustAmount);
        var f = new Wind { root = root, follow = Random.Range(0.75f, 0.85f) };
        f.half = ScreenHalf() * 1.1f;
        float ang = Random.Range(0f, 360f);
        f.dir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
        f.bits = new SpriteRenderer[n]; f.pos = new Vector2[n]; f.speed = new float[n]; f.len = new float[n]; f.alpha = new float[n];
        var tint = Random.value < 0.5f ? new Color(0.75f, 0.85f, 1f) : new Color(0.85f, 0.8f, 1f);
        for (int i = 0; i < n; i++)
        {
            var sr = Sr(root, "Hebra", streak, LDust, 3, new Color(tint.r, tint.g, tint.b, 0f));
            // Mezcla: la mayoría como antes, un 30 % más gruesas y largas
            float big = Random.value < 0.3f ? Random.Range(1.5f, 2f) : 1f;
            f.len[i] = Random.Range(0.6f, 1.5f) * big;
            Size(sr, f.len[i], 0.035f * big);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
            f.bits[i] = sr;
            f.pos[i] = CameraPos() * (1f - f.follow) + new Vector2(Random.Range(-f.half.x, f.half.x), Random.Range(-f.half.y, f.half.y));
            f.speed[i] = Random.Range(1.2f, 2.8f) * (big > 1f ? 1.2f : 1f);
            f.alpha[i] = Random.Range(0.06f, 0.14f) * (big > 1f ? 1.15f : 1f);
        }
        Debug.Log($"LivingSky: viento {(v == 0 ? "suave" : v == 1 ? "normal" : "ráfaga")} ({n} hebras).");
        return f;
    }

    // ---------- Campos magnéticos: líneas de campo de un dipolo alrededor de una estrella
    class Magnetic : Fx
    {
        public SpriteRenderer lines, core;
        public float phase, spin;
        public Color tint;
        public override void Tick(LivingSky s, float t, float dt)
        {
            float a = (0.13f + 0.06f * Mathf.Sin(t * 0.7f + phase)) * alphaMul;
            var c = tint; c.a = a; lines.color = c;
            lines.transform.localRotation = Quaternion.Euler(0f, 0f, spin + 4f * Mathf.Sin(t * 0.2f + phase));
            var cc = Color.Lerp(tint, Color.white, 0.6f); cc.a = (0.5f + 0.15f * Mathf.Sin(t * 1.3f)) * alphaMul; core.color = cc;
        }
    }

    Fx MakeMagnetic()
    {
        int v = Variant();
        float follow = Random.Range(0.85f, 0.9f) - By(v, 0f, 0.02f, 0.04f);
        float size = Random.Range(3f, 5f) * By(v, 1f, 1.6f, 2.3f);
        if (!PickAnywhere(follow, size * 0.55f, out var local)) return null;
        var root = NewRoot("FondoVivo_CampoMagnetico");
        var f = new Magnetic { root = root, follow = follow, local = local, leash = true, phase = Random.Range(0f, 10f), spin = Random.Range(0f, 180f) };
        f.tint = Random.value < 0.5f ? new Color(0.55f, 1f, 0.85f) : new Color(0.6f, 0.8f, 1f);
        f.lines = Sr(root, "Lineas", Random.value < 0.5f ? field : field6, LObjects, 150, Color.clear);
        Size(f.lines, size, size);
        f.core = Sr(root, "Estrella", glow, LObjects, 151, Color.clear);
        Size(f.core, 0.35f * Mathf.Sqrt(By(v, 1f, 1.6f, 2.3f)), 0.35f * Mathf.Sqrt(By(v, 1f, 1.6f, 2.3f)));
        Debug.Log($"LivingSky: campo magnético {VName[v]}.");
        return f;
    }

    // ---------- Binaria que se eclipsa: dos estrellas que giran juntas; al taparse, baja el brillo
    class Binary : Fx
    {
        public SpriteRenderer a, b, halo;
        public float r, period, phase;
        public Color ca, cb;
        public override void Tick(LivingSky s, float t, float dt)
        {
            float ang = (t / period + phase) * Mathf.PI * 2f;
            Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.35f) * r;   // órbita casi de canto
            a.transform.localPosition = d;
            b.transform.localPosition = -d * 1.4f;
            bool aFront = Mathf.Sin(ang) < 0f;
            a.sortingOrder = aFront ? 6 : 5; b.sortingOrder = aFront ? 5 : 6;
            float overlap = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Cos(ang)) * 4f);
            var c1 = ca; c1.a = (aFront ? 0.95f : 0.95f * (1f - 0.7f * overlap)) * alphaMul; a.color = c1;
            var c2 = cb; c2.a = (aFront ? 0.9f * (1f - 0.7f * overlap) : 0.9f) * alphaMul; b.color = c2;
            var h = Color.Lerp(ca, cb, 0.5f); h.a = 0.18f * (1f - 0.4f * overlap) * alphaMul; halo.color = h;
        }
    }

    Fx MakeBinary()
    {
        int v = Variant();
        float k = By(v, 1f, 1.8f, 2.6f);
        float follow = 0.97f - By(v, 0f, 0.01f, 0.02f);
        if (!PickAnywhere(follow, 0.6f * k, out var local)) return null;
        var root = NewRoot("FondoVivo_Binaria");
        bool fast = Random.value < 0.5f;
        var f = new Binary { root = root, follow = follow, local = local, leash = true, r = Random.Range(0.1f, 0.16f) * k,
                             period = fast ? Random.Range(3f, 5f) : Random.Range(8f, 12f), phase = Random.value };
        f.ca = new Color(1f, 0.92f, 0.75f); f.cb = new Color(0.7f, 0.82f, 1f);
        f.halo = Sr(root, "Halo", glow, LStars, 4, Color.clear); Size(f.halo, 0.9f * k, 0.9f * k);
        f.a = Sr(root, "A", dot, LStars, 6, f.ca); Size(f.a, 0.12f * k, 0.12f * k);
        f.b = Sr(root, "B", dot, LStars, 5, f.cb); Size(f.b, 0.08f * k, 0.08f * k);
        Debug.Log($"LivingSky: binaria {VName[v]}, giro {(fast ? "rápido" : "lento")}.");
        return f;
    }

    // ---------- Púlsar: punto brillante con dos haces que giran
    class Pulsar : Fx
    {
        public SpriteRenderer core, beamA, beamB;
        public float rate, len, k;
        public override void Tick(LivingSky s, float t, float dt)
        {
            float ang = t * rate;
            beamA.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
            beamB.transform.localRotation = Quaternion.Euler(0f, 0f, ang + 180f);
            beamA.transform.localPosition = Quaternion.Euler(0f, 0f, ang) * Vector3.right * len * 0.5f;
            beamB.transform.localPosition = Quaternion.Euler(0f, 0f, ang + 180f) * Vector3.right * len * 0.5f;
            // Destello cuando un haz pasa "hacia nosotros" (dos veces por vuelta)
            float pulse = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * Mathf.Deg2Rad)), 12f);
            core.color = new Color(0.8f, 0.9f, 1f, (0.55f + 0.45f * pulse) * alphaMul);
            float cs = (0.12f + 0.12f * pulse) * k;
            Size(core, cs, cs);
            var cb = new Color(0.7f, 0.85f, 1f, 0.18f * alphaMul); beamA.color = cb; beamB.color = cb;
        }
    }

    Fx MakePulsar()
    {
        int v = Variant();
        float k = By(v, 1f, 1.6f, 2.2f);
        float follow = 0.97f - By(v, 0f, 0.01f, 0.02f);
        bool slow = Random.value < 0.5f;   // lento y largo, o rápido y corto
        float len = Random.Range(1.5f, 2.6f) * k * (slow ? 1.2f : 0.85f);
        if (!PickAnywhere(follow, len, out var local)) return null;
        var root = NewRoot("FondoVivo_Pulsar");
        float rate = (slow ? Random.Range(40f, 70f) : Random.Range(120f, 180f)) * (Random.value < 0.5f ? -1f : 1f);
        var f = new Pulsar { root = root, follow = follow, local = local, leash = true, rate = rate, len = len, k = k };
        f.beamA = Sr(root, "HazA", streak, LStars, 4, Color.clear); Size(f.beamA, len, 0.05f * k);
        f.beamB = Sr(root, "HazB", streak, LStars, 4, Color.clear); Size(f.beamB, len, 0.05f * k);
        f.beamA.flipX = true; f.beamB.flipX = true;   // brillante en el centro, apagado en la punta
        f.core = Sr(root, "Nucleo", glow, LStars, 6, Color.white); Size(f.core, 0.15f * k, 0.15f * k);
        Debug.Log($"LivingSky: púlsar {VName[v]}, {(slow ? "lento y largo" : "rápido y corto")}.");
        return f;
    }

    // ---------- Lluvia de meteoros: estelas que salen de un mismo punto durante 15–30 s
    class Meteors : Fx
    {
        public List<SpriteRenderer> pool = new List<SpriteRenderer>();
        public List<Vector2> p = new List<Vector2>(), v = new List<Vector2>();
        public List<float> age = new List<float>(), life = new List<float>();
        public float end, next, k;
        public Vector2 gap;
        public override void Tick(LivingSky s, float t, float dt)
        {
            if (t < end && t >= next)
            {
                next = t + Random.Range(gap.x, gap.y);
                float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 start = d * Random.Range(0.3f, 1.2f) * k;
                // Solo si el recorrido no pasa por una constelación
                if (s.ClearNow(local + start, follow, 0.3f) && s.ClearNow(local + start + d * 3f * k, follow, 0.3f))
                {
                    SpriteRenderer sr = null;
                    int idx = -1;
                    for (int i = 0; i < pool.Count; i++) if (!pool[i].enabled) { sr = pool[i]; idx = i; break; }
                    if (sr == null)
                    {
                        sr = s.Sr(root, "Meteoro", s.streak, LDust, 4, Color.clear);
                        pool.Add(sr); p.Add(Vector2.zero); v.Add(Vector2.zero); age.Add(0f); life.Add(0f); idx = pool.Count - 1;
                    }
                    sr.enabled = true;
                    p[idx] = start; v[idx] = d * Random.Range(7f, 11f) * Mathf.Sqrt(k); age[idx] = 0f; life[idx] = Random.Range(0.35f, 0.55f);
                    sr.transform.localRotation = Quaternion.Euler(0f, 0f, ang * Mathf.Rad2Deg);
                }
            }
            bool any = false;
            for (int i = 0; i < pool.Count; i++)
            {
                if (!pool[i].enabled) continue;
                any = true;
                age[i] += dt;
                float u = age[i] / life[i];
                if (u >= 1f) { pool[i].enabled = false; continue; }
                p[i] += v[i] * dt;
                pool[i].transform.localPosition = p[i];
                LivingSky.Size(pool[i], (1.3f * Mathf.Sin(u * Mathf.PI) + 0.1f) * k, 0.04f * k);
                pool[i].color = new Color(0.95f, 0.95f, 1f, 0.55f * Mathf.Sin(u * Mathf.PI));
            }
            if (t >= end && !any) done = true;
        }
    }

    Fx MakeMeteors()
    {
        int v = Variant();
        float k = By(v, 1f, 1.5f, 2.2f);
        float follow = 0.8f - By(v, 0f, 0.02f, 0.05f);
        if (!PickInView(follow, 1.2f * k, 0.6f, out var local)) return null;
        var f = new Meteors { root = NewRoot("FondoVivo_Meteoros"), follow = follow, local = local, k = k };
        bool low = PerformanceManager.Current == PerformanceManager.Tier.Low;
        bool heavy = Random.value < 0.5f;   // lluvia suave o intensa
        f.gap = heavy ? new Vector2(0.6f, 1.5f) : new Vector2(1.5f, 3f);
        if (low) f.gap *= 1.6f;
        f.end = Time.time + Random.Range(15f, 30f);
        f.next = Time.time;
        Debug.Log($"LivingSky: lluvia de meteoros {VName[v]}, {(heavy ? "intensa" : "suave")}.");
        return f;
    }

    // ---------- Cometa lejano: tenue y lento, detrás de las estrellas; cuanto más grande, más lento
    class Comet : Fx
    {
        public SpriteRenderer head, tail;
        public Vector2 vel, pos;
        public float born, life, k;
        public override void Tick(LivingSky s, float t, float dt)
        {
            float u = (t - born) / life;
            if (u >= 1f) { done = true; return; }
            pos += vel * dt;
            head.transform.localPosition = pos;
            tail.transform.localPosition = pos - vel.normalized * 0.45f * k;
            float fade = Mathf.Clamp01(u * 6f) * Mathf.Clamp01((1f - u) * 6f);
            bool clear = s.ClearNow(local + pos, follow, 0.6f * k);
            float a = fade * (clear ? 1f : 0f);
            head.color = new Color(0.85f, 0.95f, 1f, 0.45f * a);
            tail.color = new Color(0.7f, 0.85f, 1f, 0.25f * a);
        }
    }

    Fx MakeComet()
    {
        int v = Variant();
        float k = By(v, 1f, 2f, 3f);
        float follow = 0.8f - By(v, 0f, 0.03f, 0.06f);
        if (!PickInView(follow, 1f * k, 0.5f, out var local)) return null;
        var root = NewRoot("FondoVivo_Cometa");
        float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        var f = new Comet { root = root, follow = follow, local = local, born = Time.time, life = Random.Range(40f, 60f), k = k };
        f.vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(0.18f, 0.3f) / Mathf.Sqrt(k);
        f.pos = -f.vel * f.life * 0.5f;   // cruza por el sitio elegido a mitad de su vida
        f.tail = Sr(root, "Cola", streak, LStars, -2, Color.clear); Size(f.tail, 0.9f * k, 0.06f * k);
        f.tail.transform.localRotation = Quaternion.Euler(0f, 0f, ang * Mathf.Rad2Deg);
        f.head = Sr(root, "Cabeza", glow, LStars, -1, Color.clear); Size(f.head, 0.12f * k, 0.12f * k);
        Debug.Log($"LivingSky: cometa lejano {VName[v]}.");
        return f;
    }

    // ---------- Tormenta solar: una estrella lejana se aviva y suelta ondas de color
    class Storm : Fx
    {
        public SpriteRenderer star, wash;
        public SpriteRenderer[] rings;
        public float born, life, k;
        public override void Tick(LivingSky s, float t, float dt)
        {
            float u = (t - born) / life;
            if (u >= 1f) { done = true; return; }
            float flare = u < 0.25f ? u / 0.25f : 1f - (u - 0.25f) / 0.75f;
            star.color = new Color(1f, 0.9f, 0.7f, 0.5f + 0.5f * flare);
            LivingSky.Size(star, (0.25f + 0.35f * flare) * k, (0.25f + 0.35f * flare) * k);
            wash.color = new Color(1f, 0.7f, 0.45f, 0.12f * flare);
            for (int i = 0; i < rings.Length; i++)
            {
                float q = (u - 0.15f - i * 0.12f) / 0.5f;
                if (q < 0f || q > 1f) { rings[i].color = Color.clear; continue; }
                float r = Mathf.Lerp(0.3f, 3.2f, q) * k;
                LivingSky.Size(rings[i], r * 2f, r * 2f);
                rings[i].color = new Color(1f, 0.8f, 0.55f, 0.3f * (1f - q));
            }
        }
    }

    Fx MakeStorm()
    {
        int v = Variant();
        float k = By(v, 1f, 1.5f, 2f);
        float follow = 0.87f - By(v, 0f, 0.02f, 0.04f);
        if (!PickInView(follow, 3.4f * k, 0.5f, out var local)) return null;
        var root = NewRoot("FondoVivo_TormentaSolar");
        var f = new Storm { root = root, follow = follow, local = local, born = Time.time, life = Random.Range(8f, 12f), k = k };
        f.wash = Sr(root, "Resplandor", glow, LObjects, 152, Color.clear); Size(f.wash, 7f * k, 7f * k);
        f.rings = new SpriteRenderer[3];
        for (int i = 0; i < 3; i++) f.rings[i] = Sr(root, "Onda", ring, LObjects, 153, Color.clear);
        f.star = Sr(root, "Estrella", glow, LObjects, 154, Color.clear);
        Debug.Log($"LivingSky: tormenta solar {VName[v]}.");
        return f;
    }

    // ---------- Supernova lejana: un punto que se enciende muchísimo y deja una cáscara
    class Supernova : Fx
    {
        public SpriteRenderer core, spikeH, spikeV, shell, haze;
        public float born, life, k;
        public override void Tick(LivingSky s, float t, float dt)
        {
            float u = (t - born) / life;
            if (u >= 1f) { done = true; return; }
            float peak = u < 0.08f ? u / 0.08f : Mathf.Pow(1f - (u - 0.08f) / 0.92f, 2f);
            core.color = new Color(1f, 1f, 0.95f, peak);
            LivingSky.Size(core, (0.15f + 0.6f * peak) * k, (0.15f + 0.6f * peak) * k);
            var sp = new Color(0.9f, 0.95f, 1f, 0.6f * peak);
            spikeH.color = sp; spikeV.color = sp;
            LivingSky.Size(spikeH, (0.4f + 2.2f * peak) * k, 0.04f * k); LivingSky.Size(spikeV, (0.4f + 2.2f * peak) * k, 0.04f * k);
            float q = Mathf.Clamp01((u - 0.05f) / 0.95f);
            float r = Mathf.Lerp(0.2f, 2.6f, Mathf.Sqrt(q)) * k;
            LivingSky.Size(shell, r * 2f, r * 2f);
            shell.color = new Color(0.75f, 0.8f, 1f, 0.35f * (1f - q));
            LivingSky.Size(haze, r * 2.2f, r * 2.2f);
            haze.color = new Color(0.8f, 0.7f, 1f, 0.12f * (1f - q));
        }
    }

    Fx MakeSupernova()
    {
        int v = Variant();
        float k = By(v, 1f, 1.3f, 1.6f);
        float follow = 0.97f - By(v, 0f, 0.01f, 0.02f);
        if (!PickInView(follow, 3f * k, 0.5f, out var local)) return null;
        var root = NewRoot("FondoVivo_Supernova");
        var f = new Supernova { root = root, follow = follow, local = local, born = Time.time, life = Random.Range(30f, 45f), k = k };
        f.haze = Sr(root, "Bruma", glow, LStars, 7, Color.clear);
        f.shell = Sr(root, "Cascara", ring, LStars, 8, Color.clear);
        f.spikeH = Sr(root, "PuntaH", dot, LStars, 9, Color.clear);
        f.spikeV = Sr(root, "PuntaV", dot, LStars, 9, Color.clear);
        f.spikeV.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        f.core = Sr(root, "Nucleo", glow, LStars, 10, Color.clear);
        Debug.Log($"LivingSky: supernova {VName[v]}.");
        return f;
    }

    // ------------------------------------------------------------------ texturas
    static Sprite MakeGlow(int n)
    {
        var tex = NewTex(n, n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    static Sprite MakeDot(int n)
    {
        var tex = NewTex(n, n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) / 0.3f)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    // Estela: brillante a la derecha (la cabeza), se apaga hacia la izquierda
    static Sprite MakeStreak(int w, int h)
    {
        var tex = NewTex(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h * 2f - 1f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(u, 1.5f) * Mathf.Clamp01(1f - v * v)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
    }

    static Sprite MakeRing(int n)
    {
        var tex = NewTex(n, n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - Mathf.Abs(r - 0.92f) / 0.06f)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    // Un rayo quebrado con una rama, de lado a lado de la textura
    static Sprite MakeBolt(int n, int seed)
    {
        var rng = new System.Random(seed);
        var tex = NewTex(n, n);
        var px = new float[n * n];
        Vector2 p = new Vector2(4f, n * 0.5f);
        int steps = 9;
        for (int i = 0; i < steps; i++)
        {
            Vector2 q = new Vector2(4f + (n - 8f) * (i + 1) / steps, n * 0.5f + (float)(rng.NextDouble() - 0.5) * n * 0.35f);
            Line(px, n, p, q, 1.6f);
            if (i == steps / 2)
            {
                Vector2 b = q, bq = q + new Vector2(n * 0.2f, (float)(rng.NextDouble() < 0.5 ? -1 : 1) * n * 0.22f);
                Line(px, n, b, Vector2.Lerp(b, bq, 0.5f) + new Vector2(0f, (float)(rng.NextDouble() - 0.5) * 8f), 1.1f);
                Line(px, n, Vector2.Lerp(b, bq, 0.5f), bq, 0.9f);
            }
            p = q;
        }
        for (int i = 0; i < px.Length; i++) tex.SetPixel(i % n, i / n, new Color(1f, 1f, 1f, Mathf.Clamp01(px[i])));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    // Líneas de campo de un dipolo: r = L·sen²θ, a varias L, a los dos lados
    static Sprite MakeField(int n, float[] Ls)
    {
        var tex = NewTex(n, n);
        var px = new float[n * n];
        Vector2 c = new Vector2(n * 0.5f, n * 0.5f);
        foreach (float L in Ls)
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 prev = c;
                for (int i = 1; i <= 60; i++)
                {
                    float th = Mathf.PI * i / 60f;
                    float r = L * n * Mathf.Sin(th) * Mathf.Sin(th);
                    Vector2 q = c + new Vector2(side * r * Mathf.Sin(th), r * Mathf.Cos(th));
                    Line(px, n, prev, q, 1.1f);
                    prev = q;
                }
            }
        for (int i = 0; i < px.Length; i++) tex.SetPixel(i % n, i / n, new Color(1f, 1f, 1f, Mathf.Clamp01(px[i])));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    static void Line(float[] px, int n, Vector2 a, Vector2 b, float width)
    {
        int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 2f) + 1;
        int rad = Mathf.CeilToInt(width * 2f);
        for (int s = 0; s <= steps; s++)
        {
            Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    int x = Mathf.RoundToInt(p.x) + dx, y = Mathf.RoundToInt(p.y) + dy;
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    float d = Vector2.Distance(new Vector2(x, y), p);
                    float a2 = Mathf.Clamp01(1f - d / (width * 2f));
                    if (a2 > px[y * n + x]) px[y * n + x] = a2;
                }
        }
    }

    static Texture2D NewTex(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var clear = new Color32[w * h];
        t.SetPixels32(clear);
        return t;
    }

    [ContextMenu("Probar: evento ya")]
    void EventNow()
    {
        for (int i = 0; i < pendingAt.Count; i++) pendingAt[i] = 0f;
        if (pendingEvents.Count == 0) StartEvent((Kind)Random.Range((int)Kind.Meteoros, (int)Kind.Supernova + 1));
    }
}
