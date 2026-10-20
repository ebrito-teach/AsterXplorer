using System.Collections.Generic;
using UnityEngine;

/// El cielo de fondo, generado por código y distinto en cada nivel y en cada intento.
///
/// CAPAS, DE LO MÁS LEJANO A LO MÁS CERCANO
///
///   Estrellas lejanas   muchas, pequeñas y tenues. Casi no se mueven al viajar.
///   Estrellas medias    menos y más brillantes. Aquí van las constelaciones reales y, muy de
///                       vez en cuando, una de las palabras escondidas.
///   Estrellas fugaces   cruzan de vez en cuando por delante de las lejanas.
///   Polvo               motas y nubes muy tenues, más cerca: se mueven bastante más que las
///                       estrellas al viajar, y eso es lo que da la profundidad.
///
/// La profundidad es el parallax: cada capa acompaña a la cámara en una fracción (Follow).
/// 1 = va pegada a la cámara (infinitamente lejos), 0 = clavada en el mundo como las rocas.
///
/// CLARIDAD
///
/// Todo va en las capas de fondo (BG_*), por detrás de asteroides y jugador, y el polvo tiene
/// tope de opacidad muy bajo. Nada de esto puede confundirse con algo que mata.
///
/// No necesita arte: genera sus propias texturas. Va en un objeto vacío de la escena.
[DefaultExecutionOrder(150)]   // después de CameraFollow, para leer la cámara ya movida
public class SkyDirector : MonoBehaviour
{
    [Header("Profundidad (parallax)")]
    [Tooltip("Cuánto acompañan a la cámara las estrellas lejanas. 1 = pegadas a ella")]
    [Range(0f, 1f)] [SerializeField] private float farFollow = 0.96f;
    [Tooltip("Estrellas medias, constelaciones y palabras escondidas")]
    [Range(0f, 1f)] [SerializeField] private float midFollow = 0.9f;
    [Tooltip("El polvo, lo más cercano del fondo")]
    [Range(0f, 1f)] [SerializeField] private float dustFollow = 0.6f;

    [Header("Estrellas")]
    [Tooltip("Estrellas lejanas por unidad cuadrada")]
    [SerializeField] private float farDensity = 1.6f;
    [Tooltip("Estrellas medias por unidad cuadrada")]
    [SerializeField] private float midDensity = 0.3f;
    [SerializeField] private Vector2 farSize = new Vector2(0.025f, 0.06f);
    [SerializeField] private Vector2 midSize = new Vector2(0.05f, 0.11f);
    [Tooltip("Tope de estrellas por capa (rendimiento en móvil)")]
    [SerializeField] private int maxStarsPerLayer = 400;

    [Header("Parpadeo")]
    [Tooltip("Velocidad del parpadeo, mínima y máxima")]
    [SerializeField] private Vector2 twinkleSpeed = new Vector2(0.6f, 2.8f);
    [Tooltip("Cuánto baja el brillo al parpadear (0 = nada, 1 = se apaga)")]
    [Range(0f, 1f)] [SerializeField] private float twinkleAmount = 0.45f;

    [Header("Constelaciones reales (las 88 oficiales)")]
    [Tooltip("Probabilidad de que un mapa tenga constelaciones")]
    [Range(0f, 1f)] [SerializeField] private float constellationChance = 0.6f;
    [SerializeField] private Vector2Int constellationCount = new Vector2Int(1, 2);
    [Tooltip("Lado más largo de una constelación, en unidades: la más pequeña del cielo y la " +
             "más grande (Hidra). Las demás, en proporción a su tamaño real")]
    [SerializeField] private Vector2 constellationSize = new Vector2(3f, 8f);
    [Tooltip("Giro máximo respecto a como se ven desde la Tierra, en grados")]
    [SerializeField] private float constellationMaxTilt = 35f;
    [Tooltip("Tamaño de sus estrellas: la más débil y la más brillante")]
    [SerializeField] private Vector2 constellationStarSize = new Vector2(0.09f, 0.22f);
    [Tooltip("Dibujar las líneas que las unen, muy tenues")]
    [SerializeField] private bool drawLines = true;
    [Range(0f, 0.5f)] [SerializeField] private float lineAlpha = 0.12f;
    [SerializeField] private float lineWidth = 0.025f;

    [Header("Palabras escondidas")]
    [Tooltip("Probabilidad de que en un mapa salga UNA de las palabras (EB, NB o NdB)")]
    [Range(0f, 1f)] [SerializeField] private float easterEggChance = 0.04f;
    [Tooltip("Tamaño de una letra: unidades por casilla de la cuadrícula (las letras miden 4 de alto)")]
    [SerializeField] private float easterEggUnit = 0.8f;

    [Header("Estrellas fugaces")]
    [Tooltip("Segundos entre una y otra, mínimo y máximo")]
    [SerializeField] private Vector2 shootingInterval = new Vector2(5f, 14f);
    [SerializeField] private Vector2 shootingSpeed = new Vector2(16f, 26f);
    [SerializeField] private Vector2 shootingLife = new Vector2(0.45f, 0.85f);
    [SerializeField] private float shootingLength = 2.2f;

    [Header("Polvo")]
    [Tooltip("Nubes grandes y muy tenues")]
    [SerializeField] private int dustClouds = 14;
    [Tooltip("Motas pequeñas")]
    [SerializeField] private int dustSpecks = 80;
    [SerializeField] private Vector2 cloudSize = new Vector2(3f, 7f);
    [Tooltip("Opacidad de las nubes. Muy baja: no puede tapar nada")]
    [SerializeField] private Vector2 cloudAlpha = new Vector2(0.02f, 0.06f);
    [SerializeField] private Vector2 speckSize = new Vector2(0.02f, 0.05f);
    [SerializeField] private Vector2 speckAlpha = new Vector2(0.06f, 0.22f);
    [Tooltip("Deriva propia del polvo, en unidades por segundo")]
    [SerializeField] private float dustDrift = 0.12f;

    const string LayerStars = "BG_Stars";
    const string LayerDust = "BG_Dust";

    class Twinkler
    {
        public SpriteRenderer sr;
        public Color color;
        public float speed, phase, amount;
    }

    class Mote
    {
        public SpriteRenderer sr;
        public Vector2 rel, vel;
        public Color color;
        public float phase;
    }

    Sprite dotSprite, sparkleSprite, lineSprite, streakSprite, cloudSprite;
    Transform farRoot, midRoot, dustRoot;
    readonly List<SpriteRenderer> farPool = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> midPool = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> brightPool = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> linePool = new List<SpriteRenderer>();
    readonly List<Twinkler> twinklers = new List<Twinkler>();
    readonly List<Mote> motes = new List<Mote>();
    readonly List<Rect> taken = new List<Rect>();

    SpriteRenderer shooting;
    Vector2 shootRel, shootDir;
    float shootAge, shootLife = -1f, nextShoot, shootSpeed;

    System.Random rng;
    Vector2 lastCam;
    bool built, generated;
    GameDirector director;

    /// Lo que salió en el último mapa (para depurar y para la bitácora, si algún día se usa)
    public string LastReport { get; private set; }

    // ------------------------------------------------------------------ ciclo de vida
    void OnEnable()
    {
        director = FindFirstObjectByType<GameDirector>();
        if (director != null) director.LevelStarted += OnLevelStarted;
    }

    void OnDisable()
    {
        if (director != null) director.LevelStarted -= OnLevelStarted;
    }

    void Start()
    {
        if (!generated) Generate();
    }

    void OnLevelStarted(int level) => Generate();

    // ------------------------------------------------------------------ generación
    [ContextMenu("Generar otro cielo")]
    public void Generate()
    {
        if (!built) Build();
        rng = new System.Random(System.Environment.TickCount ^ (GetInstanceID() * 7919) ^ Random.Range(0, int.MaxValue));
        generated = true;
        taken.Clear();
        twinklers.Clear();
        // Todo apagado; lo que use este cielo se vuelve a encender al pedirlo
        foreach (var pool in new[] { farPool, midPool, brightPool, linePool })
            foreach (var sr in pool) sr.gameObject.SetActive(false);
        var report = new System.Text.StringBuilder();

        Vector2 cam = CameraPos();
        lastCam = cam;
        PlaceLayers(cam);

        int farUsed = Scatter(farPool, farRoot, LayerRect(farFollow), farDensity, farSize, 0, 0.35f, 0.85f);
        int midUsed = Scatter(midPool, midRoot, LayerRect(midFollow), midDensity, midSize, 1, 0.55f, 1f);

        int bright = 0, lines = 0;
        Rect mid = LayerRect(midFollow);

        if (Chance(constellationChance))
        {
            int n = Range(constellationCount.x, constellationCount.y);
            var order = Shuffled(SkyCatalog.Real.Length);
            for (int i = 0, placed = 0; i < order.Length && placed < n; i++)
            {
                if (PlaceConstellation(SkyCatalog.Real[order[i]], mid, ref bright, ref lines))
                {
                    report.Append(SkyCatalog.Real[order[i]].name).Append(", ");
                    placed++;
                }
            }
        }

        if (Chance(easterEggChance))
        {
            int which = rng.Next(SkyCatalog.EasterEggs.Length);
            if (PlaceWord(SkyCatalog.EasterEggs[which], mid, ref bright, ref lines))
                report.Append(SkyCatalog.EasterEggNames[which]).Append(", ");
        }

        ScatterDust(cam);
        shootLife = -1f;
        if (shooting != null) shooting.enabled = false;
        nextShoot = Time.time + Range(shootingInterval.x, shootingInterval.y);

        LastReport = report.Length > 0 ? report.ToString(0, report.Length - 2) : "ninguna";
        Debug.Log($"SkyDirector: {farUsed} estrellas lejanas, {midUsed} medias. Constelaciones: {LastReport}.");
    }

    // La zona que cubre una capa en sus propias coordenadas: lo que la cámara puede llegar a
    // ver de ella recorriendo el mundo entero, más un margen
    Rect LayerRect(float follow)
    {
        var gb = GameBounds.Instance;
        Rect area; Vector2 screen;
        if (gb != null && gb.IsReady) { area = gb.Area; screen = gb.ScreenSize; }
        else
        {
            var c = Camera.main;
            float h = c != null ? c.orthographicSize * 2f : 9f;
            screen = new Vector2(h * (c != null ? c.aspect : 2f), h);
            area = new Rect(-screen / 2f, screen);
        }

        float k = 1f - follow;
        Vector2 travel = Vector2.Max(Vector2.zero, area.size - screen) * 0.5f * k;
        Vector2 half = travel + screen * 0.5f + Vector2.one * 1.5f;
        Vector2 center = area.center * k;
        return new Rect(center - half, half * 2f);
    }

    int Scatter(List<SpriteRenderer> pool, Transform root, Rect r, float density, Vector2 size, int order,
                float minAlpha, float maxAlpha)
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(r.width * r.height * density), 0, maxStarsPerLayer);
        for (int i = 0; i < count; i++)
        {
            var sr = Get(pool, root, dotSprite, LayerStars, order);
            sr.transform.localPosition = new Vector2(Range(r.xMin, r.xMax), Range(r.yMin, r.yMax));
            float s = Range(size.x, size.y);
            // Las pocas grandes, más grandes: el cielo real tiene muchas débiles y pocas brillantes
            if (rng.NextDouble() < 0.06) s *= 1.7f;
            sr.transform.localScale = Vector3.one * s;
            Twinkle(sr, StarColor(), Range(minAlpha, maxAlpha), 1f);
        }
        return count;
    }

    bool PlaceConstellation(SkyCatalog.Figure c, Rect layer, ref int bright, ref int lines)
    {
        // Ya viene proyectada y con el lado más largo = 1. Se escala según su tamaño real en
        // el cielo (raíz cuadrada: que Hidra no se coma la pantalla) y se gira un poco
        float t = Mathf.Sqrt(Mathf.InverseLerp(4f, 96f, c.extentDeg));
        float scale = Mathf.Lerp(constellationSize.x, constellationSize.y, t);
        Quaternion tilt = Quaternion.Euler(0f, 0f, Range(-constellationMaxTilt, constellationMaxTilt));

        int n = c.StarCount;
        var pts = new Vector2[n];
        var mags = new float[n];
        var cols = new Color[n];
        for (int i = 0; i < n; i++)
        {
            int k = i * 6;
            pts[i] = tilt * (new Vector2(c.stars[k], c.stars[k + 1]) * scale);
            mags[i] = c.stars[k + 2];
            cols[i] = new Color(c.stars[k + 3], c.stars[k + 4], c.stars[k + 5]);
        }
        return PlaceFigure(pts, c.lines, mags, cols, layer, ref bright, ref lines);
    }

    bool PlaceWord(SkyCatalog.Glyph[] word, Rect layer, ref int bright, ref int lines)
    {
        var pts = new List<Vector2>();
        var idx = new List<int>();
        float x = 0f;
        foreach (var g in word)
        {
            int start = pts.Count;
            foreach (var p in g.points) pts.Add(new Vector2(x + p.x, p.y));
            foreach (var l in g.lines) idx.Add(start + l);
            x += g.width + SkyCatalog.GlyphGap;
        }
        float w = x - SkyCatalog.GlyphGap;
        var arr = pts.ToArray();
        // Una inclinación pequeña: que no se vea puesto a mano, pero que se pueda leer
        Quaternion tilt = Quaternion.Euler(0f, 0f, Range(-12f, 12f));
        for (int i = 0; i < arr.Length; i++)
            arr[i] = tilt * ((arr[i] - new Vector2(w * 0.5f, 2f)) * easterEggUnit);

        // Brillo de estrella normal: tienen que pasar desapercibidas si no las buscas
        var mags = new float[arr.Length];
        for (int i = 0; i < mags.Length; i++) mags[i] = Range(2.2f, 3.4f);
        return PlaceFigure(arr, idx.ToArray(), mags, null, layer, ref bright, ref lines);
    }

    // Coloca una figura centrada en (0,0) en un hueco libre de la capa media
    bool PlaceFigure(Vector2[] pts, int[] pairs, float[] mags, Color[] colors, Rect layer, ref int bright, ref int lines)
    {
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        Vector2 size = max - min + Vector2.one;

        Rect inner = new Rect(layer.xMin - min.x + 0.5f, layer.yMin - min.y + 0.5f,
                              Mathf.Max(0f, layer.width - size.x - 1f), Mathf.Max(0f, layer.height - size.y - 1f));
        for (int attempt = 0; attempt < 25; attempt++)
        {
            Vector2 at = new Vector2(Range(inner.xMin, inner.xMax), Range(inner.yMin, inner.yMax));
            var box = new Rect(at + min - Vector2.one * 0.5f, size);
            bool overlaps = false;
            foreach (var t in taken) if (t.Overlaps(box)) { overlaps = true; break; }
            if (overlaps) continue;
            taken.Add(box);

            for (int i = 0; i < pts.Length; i++)
            {
                var sr = Get(brightPool, midRoot, sparkleSprite, LayerStars, 3);
                sr.transform.localPosition = at + pts[i];
                float t = Mathf.InverseLerp(4.8f, -0.5f, mags[i]);       // 1 = la más brillante
                sr.transform.localScale = Vector3.one * Mathf.Lerp(constellationStarSize.x, constellationStarSize.y, t);
                // Constelaciones reales: el color de verdad de cada estrella. Palabras: al azar
                Twinkle(sr, colors != null ? colors[i] : StarColor(), Mathf.Lerp(0.6f, 1f, t), 0.5f);
                bright++;
            }

            if (drawLines && lineAlpha > 0f)
            {
                for (int i = 0; i + 1 < pairs.Length; i += 2)
                {
                    Vector2 a = at + pts[pairs[i]], b = at + pts[pairs[i + 1]];
                    var sr = Get(linePool, midRoot, lineSprite, LayerStars, 2);
                    Vector2 d = b - a;
                    var tr = sr.transform;
                    tr.localPosition = (a + b) * 0.5f;
                    tr.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    tr.localScale = new Vector3(d.magnitude, lineWidth / lineSprite.bounds.size.y, 1f);
                    sr.color = new Color(0.75f, 0.85f, 1f, lineAlpha);
                    lines++;
                }
            }
            return true;
        }
        return false;
    }

    void ScatterDust(Vector2 cam)
    {
        Vector2 half = ViewHalf() + Vector2.one * 3f;
        int total = dustClouds + dustSpecks;
        while (motes.Count < total)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(dustRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = LayerDust;
            motes.Add(new Mote { sr = sr });
        }

        // Un tinte por mapa, para que el polvo de cada lugar tenga su propio color
        Color tint = Color.Lerp(new Color(0.75f, 0.8f, 1f), new Color(1f, 0.85f, 0.7f), (float)rng.NextDouble());

        for (int i = 0; i < motes.Count; i++)
        {
            var m = motes[i];
            bool active = i < total;
            m.sr.gameObject.SetActive(active);
            if (!active) continue;

            bool cloud = i < dustClouds;
            m.sr.sprite = cloud ? cloudSprite : dotSprite;
            m.sr.sortingOrder = cloud ? 0 : 1;
            m.rel = new Vector2(Range(-half.x, half.x), Range(-half.y, half.y));
            m.vel = Random2() * dustDrift * Range(0.3f, 1f);
            m.phase = Range(0f, 10f);
            float s = cloud ? Range(cloudSize.x, cloudSize.y) : Range(speckSize.x, speckSize.y);
            m.sr.transform.localScale = cloud ? new Vector3(s, s * Range(0.5f, 0.9f), 1f) : Vector3.one * s;
            m.sr.transform.localRotation = Quaternion.Euler(0f, 0f, Range(0f, 360f));
            float a = cloud ? Range(cloudAlpha.x, cloudAlpha.y) : Range(speckAlpha.x, speckAlpha.y);
            m.color = new Color(tint.r, tint.g, tint.b, a);
            m.sr.color = m.color;
            m.sr.transform.position = cam + m.rel;
        }
    }

    // ------------------------------------------------------------------ cada fotograma
    void LateUpdate()
    {
        if (!generated) return;
        Vector2 cam = CameraPos();
        Vector2 delta = cam - lastCam;
        // Un salto enorme es un cambio de nivel (la cámara salta al jugador): no es viaje
        if (delta.sqrMagnitude > 100f) delta = Vector2.zero;
        lastCam = cam;
        PlaceLayers(cam);

        float t = Time.time;
        float dt = Time.deltaTime;

        for (int i = 0; i < twinklers.Count; i++)
        {
            var w = twinklers[i];
            // Dos ondas que no encajan: el parpadeo nunca se repite igual
            float n = 0.5f + 0.25f * Mathf.Sin(t * w.speed + w.phase) + 0.25f * Mathf.Sin(t * w.speed * 2.73f + w.phase * 1.7f);
            Color c = w.color;
            c.a *= 1f - w.amount * twinkleAmount * n;
            w.sr.color = c;
        }

        Vector2 half = ViewHalf() + Vector2.one * 3f;
        float k = 1f - dustFollow;
        for (int i = 0; i < motes.Count; i++)
        {
            var m = motes[i];
            if (!m.sr.gameObject.activeSelf) continue;
            m.rel -= delta * k;
            m.rel += m.vel * dt;
            if (m.rel.x < -half.x) m.rel.x += 2f * half.x; else if (m.rel.x > half.x) m.rel.x -= 2f * half.x;
            if (m.rel.y < -half.y) m.rel.y += 2f * half.y; else if (m.rel.y > half.y) m.rel.y -= 2f * half.y;
            m.sr.transform.position = cam + m.rel;
        }

        TickShootingStar(cam, dt);
    }

    void TickShootingStar(Vector2 cam, float dt)
    {
        if (shootLife < 0f)
        {
            if (Time.time < nextShoot) return;
            Vector2 half = ViewHalf();
            shootRel = new Vector2(Range(-half.x, half.x), Range(0f, half.y));
            // Siempre cayendo un poco, hacia un lado u otro
            float ang = Range(200f, 250f);
            if (rng.NextDouble() < 0.5) ang = 180f - ang;
            shootDir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
            shootLife = Range(shootingLife.x, shootingLife.y);
            shootSpeed = Range(shootingSpeed.x, shootingSpeed.y);
            shootAge = 0f;
            shooting.enabled = true;
            shooting.transform.rotation = Quaternion.Euler(0f, 0f, ang);
        }

        shootAge += dt;
        float u = shootAge / shootLife;
        if (u >= 1f)
        {
            shooting.enabled = false;
            shootLife = -1f;
            nextShoot = Time.time + Range(shootingInterval.x, shootingInterval.y);
            return;
        }
        shootRel += shootDir * shootSpeed * dt;
        shooting.transform.position = cam + shootRel;
        float len = shootingLength * Mathf.Sin(u * Mathf.PI);
        shooting.transform.localScale = new Vector3(Mathf.Max(0.01f, len), 0.06f / streakSprite.bounds.size.y, 1f);
        shooting.color = new Color(1f, 1f, 1f, Mathf.Sin(u * Mathf.PI));
    }

    void PlaceLayers(Vector2 cam)
    {
        farRoot.position = cam * farFollow;
        midRoot.position = cam * midFollow;
    }

    // ------------------------------------------------------------------ utilidades
    Vector2 CameraPos()
    {
        if (CameraFollow.Instance != null) return CameraFollow.Instance.transform.position;
        var c = Camera.main;
        return c != null ? (Vector2)c.transform.position : Vector2.zero;
    }

    Vector2 ViewHalf()
    {
        var c = Camera.main;
        if (c == null) return new Vector2(8f, 4f);
        return new Vector2(c.orthographicSize * c.aspect, c.orthographicSize);
    }

    void Twinkle(SpriteRenderer sr, Color c, float alpha, float amountScale)
    {
        c.a = alpha;
        sr.color = c;
        twinklers.Add(new Twinkler {
            sr = sr, color = c,
            speed = Range(twinkleSpeed.x, twinkleSpeed.y),
            phase = Range(0f, 20f),
            amount = Range(0.3f, 1f) * amountScale });
    }

    // Colores reales de estrella según su temperatura: muchas blancas, algunas azuladas,
    // amarillas, naranjas y pocas rojas
    Color StarColor()
    {
        double r = rng.NextDouble();
        if (r < 0.18) return new Color(0.72f, 0.82f, 1f);
        if (r < 0.62) return new Color(0.95f, 0.96f, 1f);
        if (r < 0.82) return new Color(1f, 0.95f, 0.82f);
        if (r < 0.95) return new Color(1f, 0.82f, 0.62f);
        return new Color(1f, 0.66f, 0.55f);
    }

    SpriteRenderer Get(List<SpriteRenderer> pool, Transform root, Sprite sprite, string layer, int order)
    {
        foreach (var sr in pool)
            if (!sr.gameObject.activeSelf) { sr.gameObject.SetActive(true); sr.transform.localRotation = Quaternion.identity; return sr; }
        var go = new GameObject("Star");
        go.transform.SetParent(root, false);
        var n = go.AddComponent<SpriteRenderer>();
        n.sprite = sprite;
        n.sortingLayerName = layer;
        n.sortingOrder = order;
        pool.Add(n);
        return n;
    }

    float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    int Range(int a, int b) => rng.Next(Mathf.Min(a, b), Mathf.Max(a, b) + 1);
    bool Chance(float p) => rng.NextDouble() < p;
    Vector2 Random2() { float a = Range(0f, Mathf.PI * 2f); return new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }

    int[] Shuffled(int n)
    {
        var a = new int[n];
        for (int i = 0; i < n; i++) a[i] = i;
        for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
        return a;
    }

    // ------------------------------------------------------------------ montaje (una vez)
    void Build()
    {
        built = true;
        farRoot = new GameObject("Sky_Far").transform;  farRoot.SetParent(transform, false);
        midRoot = new GameObject("Sky_Mid").transform;  midRoot.SetParent(transform, false);
        dustRoot = new GameObject("Sky_Dust").transform; dustRoot.SetParent(transform, false);

        dotSprite = MakeSprite(16, 16, (u, v) => Gauss(u, v, 0.28f));
        sparkleSprite = MakeSprite(32, 32, (u, v) =>
        {
            float core = Gauss(u, v, 0.14f);
            // Cruz de cuatro puntas, fina: el destello de las estrellas brillantes
            float cx = Mathf.Exp(-(v - 0.5f) * (v - 0.5f) / 0.0009f) * Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 2f);
            float cy = Mathf.Exp(-(u - 0.5f) * (u - 0.5f) / 0.0009f) * Mathf.Clamp01(1f - Mathf.Abs(v - 0.5f) * 2f);
            return Mathf.Clamp01(core + 0.55f * (cx + cy));
        });
        lineSprite = MakeSprite(8, 8, (u, v) => Mathf.Clamp01(1f - Mathf.Abs(v - 0.5f) * 2f));
        streakSprite = MakeSprite(32, 4, (u, v) => Mathf.Pow(u, 1.6f) * Mathf.Clamp01(1f - Mathf.Abs(v - 0.5f) * 2f) * Mathf.Clamp01((1f - u) * 12f + 0.4f));
        cloudSprite = MakeSprite(32, 32, (u, v) => Gauss(u, v, 0.22f));

        var go = new GameObject("ShootingStar");
        go.transform.SetParent(transform, false);
        shooting = go.AddComponent<SpriteRenderer>();
        shooting.sprite = streakSprite;
        shooting.sortingLayerName = LayerStars;
        shooting.sortingOrder = 5;
        shooting.enabled = false;
    }

    static float Gauss(float u, float v, float sigma)
    {
        float dx = u - 0.5f, dy = v - 0.5f;
        return Mathf.Exp(-(dx * dx + dy * dy) / (2f * sigma * sigma * 0.25f));
    }

    // Textura blanca con la opacidad que diga la función; ocupa 1 unidad de ancho
    static Sprite MakeSprite(int w, int h, System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha((x + 0.5f) / w, (y + 0.5f) / h)));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
    }
}
