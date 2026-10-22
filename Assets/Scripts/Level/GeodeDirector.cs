using UnityEngine;
using UnityEngine.UI;

/// La geoda: una roca rara, flotando en un sitio apartado del camino a la meta.
///
/// CÓMO VA
///
/// En cada nivel se decide con la semilla de la partida si hay geoda (EconomyProfile → Geode
/// Chance) y cuánto vale (20–120). Si la hay, aparece quieta, girando despacio, lejos del
/// camino directo entre la salida y la meta: ir a por ella es un desvío con riesgo.
///
/// Se recoge tocándola. Aún NO son cristales: es una piedra. Arriba, junto al botón de pausa,
/// aparece su dibujo para recordar que la llevas. Si superas el nivel, se abre en la pantalla
/// de nivel superado y ahí se cobra. Si mueres en ese nivel (la hayas cogido o no), la geoda
/// de ese nivel se pierde para siempre: los siguientes intentos ya no la tienen.
///
/// Sale en todos los tipos de mapa. Va en un objeto vacío de la escena.
[DefaultExecutionOrder(160)]
public class GeodeDirector : MonoBehaviour
{
    public static GeodeDirector Instance { get; private set; }

    [Header("Aspecto")]
    [Tooltip("Los dibujos de geoda (Art/Economia/geoda_01..06). Cada geoda sale con uno al azar")]
    [SerializeField] private Sprite[] geodeSprites;
    [Tooltip("Tamaño en el mapa, en unidades")]
    [SerializeField] private float worldSize = 0.9f;
    [SerializeField] private Color glowColor = new Color(0.75f, 0.55f, 1f, 0.55f);
    [SerializeField] private string sortingLayer = "Player";
    [SerializeField] private int sortingOrder = -4;
    [Tooltip("Capa física: tiene que chocar con Player en la matriz de colisiones (la misma que los buffs)")]
    [SerializeField] private string physicsLayer = "Goal";

    [Header("Dónde aparece")]
    [Tooltip("Distancia al camino recto salida → meta (mínimo, máximo): el desvío que cuesta ir a por ella")]
    [SerializeField] private Vector2 detour = new Vector2(4f, 10f);
    [SerializeField] private float minFromSpawn = 6f;
    [SerializeField] private float minFromGoal = 9f;
    [Tooltip("Tramo del camino salida → meta en el que puede quedar (0 = salida, 1 = meta). Así nunca " +
             "queda más allá de la meta ni pegada a ella")]
    [SerializeField] private Vector2 pathSpan = new Vector2(0.15f, 0.75f);
    [SerializeField] private float edgeMargin = 2f;
    [Tooltip("Hueco libre a su alrededor (sin estáticos ni planetas)")]
    [SerializeField] private float clearRadius = 1.3f;

    [Header("HUD (la llevas encima)")]
    [Tooltip("Posición respecto a la esquina de arriba a la derecha (el botón de pausa está en -40, -40 y mide 110)")]
    [SerializeField] private Vector2 hudPosition = new Vector2(-175f, -45f);
    [SerializeField] private float hudSize = 95f;
    [SerializeField] private int canvasOrder = 102;

    [Header("Pruebas")]
    [Tooltip("Geoda en todos los niveles (para probar)")]
    [SerializeField] private bool alwaysGeode = false;

    /// Dibujo de la geoda recogida en este nivel (lo usa la pantalla de nivel superado)
    public Sprite TakenSprite { get; private set; }
    /// Lo que vale la geoda recogida en este nivel (0 si no llevas)
    public int TakenAmount { get; private set; }

    GameDirector director;
    PlayerController player;
    GameObject pickup;
    SpriteRenderer pickupSr, pickupGlow;
    Vector2 pickupBase;
    int level, spriteIndex = -1;
    bool presentThisAttempt;
    RectTransform hudRt;
    Image hudImg;
    float hudPop = 1f, collectFx = -1f;
    Vector2 collectAt;
    SpriteRenderer fxRing;

    void Awake()
    {
        Instance = this;
        BuildHud();
    }

    void OnEnable()
    {
        director = FindFirstObjectByType<GameDirector>();
        if (director != null) director.LevelStarted += OnLevelStarted;
        player = FindFirstObjectByType<PlayerController>();
        if (player != null) player.OnDied += OnDied;
    }

    void OnDisable()
    {
        if (director != null) director.LevelStarted -= OnLevelStarted;
        if (player != null) player.OnDied -= OnDied;
    }

    // ------------------------------------------------------------------ nivel
    void OnLevelStarted(int lvl)
    {
        level = lvl;
        RemovePickup();
        presentThisAttempt = false;
        TakenSprite = null;
        TakenAmount = 0;
        SetHud(null);

        var gp = GameProgress.Instance;
        if (gp == null || gp.Economy == null) { if (alwaysGeode) Debug.LogWarning("GeodeDirector: falta GameProgress o su EconomyProfile."); return; }
        var run = gp.Data.run;

        // Ya la cogiste en este nivel (cerraste el juego con ella encima): sigue contigo
        if (run.geodeTakenLevel == lvl)
        {
            presentThisAttempt = true;
            spriteIndex = run.geodeSprite;
            TakenSprite = SpriteAt(spriteIndex);
            SetHud(TakenSprite);
            return;
        }
        // Moriste en este nivel con geoda: ya no la tiene
        if (run.geodeLostLevel == lvl) return;
        if (!alwaysGeode && !gp.Economy.GeodeAppears(lvl, run.runSeed)) return;

        Spawn();
    }

    void OnDied()
    {
        if (!presentThisAttempt) return;
        var gp = GameProgress.Instance;
        if (gp != null) gp.ForfeitGeode(level);
        presentThisAttempt = false;
        RemovePickup();
        SetHud(null);
        TakenSprite = null;
        Debug.Log($"GeodeDirector: moriste con geoda en el nivel {level}; ese nivel ya no la tendrá.");
    }

    // ------------------------------------------------------------------ colocar
    void Spawn()
    {
        var gb = GameBounds.Instance;
        if (gb == null || !gb.IsReady || geodeSprites == null || geodeSprites.Length == 0) return;
        Rect area = gb.Area;
        Rect inner = new Rect(area.xMin + edgeMargin, area.yMin + edgeMargin, area.width - 2f * edgeMargin, area.height - 2f * edgeMargin);

        Vector2 start = player != null ? player.SpawnPoint : area.center;
        Vector2 goal = area.center;
        var g = FindFirstObjectByType<Goal>();
        if (g != null) goal = g.transform.position;
        var fd = FractureDirector.Instance;
        bool fracture = fd != null && fd.Active;
        if (fracture) goal = fd.Center;

        int statics = LayerMask.GetMask("AsteroidStatic");
        Vector2 best = Vector2.zero;
        bool found = false;
        for (int attempt = 0; attempt < 120 && !found; attempt++)
        {
            var p = new Vector2(Random.Range(inner.xMin, inner.xMax), Random.Range(inner.yMin, inner.yMax));
            float along = AlongSegment(p, start, goal);
            if (along < pathSpan.x || along > pathSpan.y) continue;
            float off = DistToSegment(p, start, goal);
            // Si no encuentra con el desvío pedido, se va relajando
            float relax = attempt < 80 ? 1f : 0.6f;
            if (off < detour.x * relax || off > detour.y / relax) continue;
            if (Vector2.Distance(p, start) < minFromSpawn || Vector2.Distance(p, goal) < minFromGoal) continue;
            if (gb.IsBlocked(p, clearRadius)) continue;
            if (fracture && (p.x - fd.WallX(p.y)) * fd.Front.x < 3f) continue;
            if (Physics2D.OverlapCircle(p, clearRadius, statics) != null) continue;
            bool inPlanet = false;
            foreach (var z in LevelPopulator.PlanetZones)
                if (Vector2.Distance(p, new Vector2(z.x, z.y)) < z.z + clearRadius) { inPlanet = true; break; }
            if (inPlanet) continue;
            best = p; found = true;
        }
        if (!found) { Debug.LogWarning("GeodeDirector: el nivel tenía geoda pero no encontré sitio para ponerla."); return; }

        var valid = new System.Collections.Generic.List<int>();
        for (int i = 0; i < geodeSprites.Length; i++) if (geodeSprites[i] != null) valid.Add(i);
        if (valid.Count == 0) { Debug.LogWarning("GeodeDirector: Geode Sprites está vacío; asigna los dibujos de geoda."); return; }
        spriteIndex = valid[Random.Range(0, valid.Count)];
        pickup = new GameObject("Geoda");
        int l = LayerMask.NameToLayer(physicsLayer);
        if (l >= 0) pickup.layer = l;
        pickup.transform.position = best;
        pickupBase = best;
        var rb = pickup.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        var col = pickup.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = worldSize * 0.5f;
        pickup.AddComponent<GeodeTouch>().owner = this;

        var glow = new GameObject("Brillo");
        glow.transform.SetParent(pickup.transform, false);
        pickupGlow = glow.AddComponent<SpriteRenderer>();
        pickupGlow.sprite = SoftDisc();
        pickupGlow.sortingLayerName = sortingLayer;
        pickupGlow.sortingOrder = sortingOrder - 1;
        pickupGlow.color = glowColor;
        glow.transform.localScale = Vector3.one * (worldSize * 2.2f / Mathf.Max(0.01f, pickupGlow.sprite.bounds.size.x));

        var body = new GameObject("Roca");
        body.transform.SetParent(pickup.transform, false);
        pickupSr = body.AddComponent<SpriteRenderer>();
        pickupSr.sprite = SpriteAt(spriteIndex);
        pickupSr.sortingLayerName = sortingLayer;
        pickupSr.sortingOrder = sortingOrder;
        if (pickupSr.sprite != null)
            body.transform.localScale = Vector3.one * (worldSize / Mathf.Max(0.01f, Mathf.Max(pickupSr.sprite.bounds.size.x, pickupSr.sprite.bounds.size.y)));

        presentThisAttempt = true;
        Debug.Log($"GeodeDirector: geoda en el nivel {level}, en {best}.");
    }

    // ------------------------------------------------------------------ recoger
    public void Touched(Collider2D other)
    {
        if (pickup == null || other.GetComponentInParent<PlayerController>() == null) return;
        if (player != null && (player.IsDead || player.IsExpelling || player.IsAbsorbed)) return;
        var gp = GameProgress.Instance;
        int amount = 0;
        if (gp != null) amount = gp.CollectGeode(level, gp.Data.run.runSeed, spriteIndex);
        TakenSprite = SpriteAt(spriteIndex);
        TakenAmount = amount;
        collectAt = pickup.transform.position;
        collectFx = 0f;
        RemovePickup();
        SetHud(TakenSprite);
        hudPop = 0f;
        Debug.Log($"GeodeDirector: geoda recogida (vale {amount}; se cobra al superar el nivel).");
    }

    void RemovePickup()
    {
        if (pickup != null) Destroy(pickup);
        pickup = null;
    }

    Sprite SpriteAt(int i) => geodeSprites != null && i >= 0 && i < geodeSprites.Length ? geodeSprites[i] : null;

    void Update()
    {
        float t = Time.time;
        if (pickup != null)
        {
            // Gira despacio y flota; el brillo late
            pickup.transform.position = pickupBase + Vector2.up * Mathf.Sin(t * 1.3f) * 0.08f;
            if (pickupSr != null) pickupSr.transform.localRotation = Quaternion.Euler(0f, 0f, t * 18f);
            if (pickupGlow != null)
            {
                float k = 0.65f + 0.35f * Mathf.Sin(t * 2.4f);
                pickupGlow.color = new Color(glowColor.r, glowColor.g, glowColor.b, glowColor.a * k);
            }
        }

        // Destello al recogerla
        if (collectFx >= 0f)
        {
            if (fxRing == null)
            {
                var go = new GameObject("GeodaDestello");
                fxRing = go.AddComponent<SpriteRenderer>();
                fxRing.sprite = SoftDisc();
                fxRing.sortingLayerName = sortingLayer;
                fxRing.sortingOrder = sortingOrder + 1;
            }
            collectFx += Time.deltaTime / 0.45f;
            float e = Mathf.Clamp01(collectFx);
            fxRing.enabled = e < 1f;
            fxRing.transform.position = collectAt;
            fxRing.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 3.5f, e) * worldSize;
            fxRing.color = new Color(1f, 0.95f, 1f, (1f - e) * 0.9f);
            if (e >= 1f) collectFx = -1f;
        }

        // El icono del HUD entra con un pequeño salto y luego late muy suave
        // Se recoloca siempre: así Hud Position y Hud Size se pueden ajustar en Play
        if (hudRt != null)
        {
            hudRt.sizeDelta = Vector2.one * hudSize;
            hudRt.anchoredPosition = hudPosition + new Vector2(-hudSize * 0.5f, -hudSize * 0.5f);
        }
        if (hudImg != null && hudImg.enabled)
        {
            hudPop = Mathf.MoveTowards(hudPop, 1f, Time.unscaledDeltaTime / 0.35f);
            float pop = hudPop < 1f ? 1f + Mathf.Sin(hudPop * Mathf.PI) * 0.45f : 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 2.5f);
            hudRt.localScale = Vector3.one * pop;
        }
    }

    // ------------------------------------------------------------------ HUD
    void BuildHud()
    {
        var go = new GameObject("GeodaHUD", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var icon = new GameObject("Icono", typeof(RectTransform));
        icon.layer = 5;
        hudRt = (RectTransform)icon.transform;
        hudRt.SetParent(go.transform, false);
        hudRt.anchorMin = hudRt.anchorMax = new Vector2(1f, 1f);
        hudRt.pivot = new Vector2(0.5f, 0.5f);
        hudRt.sizeDelta = Vector2.one * hudSize;
        hudRt.anchoredPosition = hudPosition + new Vector2(-hudSize * 0.5f, -hudSize * 0.5f);
        hudImg = icon.AddComponent<Image>();
        hudImg.preserveAspect = true;
        hudImg.raycastTarget = false;
        hudImg.enabled = false;
    }

    void SetHud(Sprite s)
    {
        if (hudImg == null) return;
        hudImg.sprite = s;
        hudImg.enabled = s != null;
    }

    // ------------------------------------------------------------------ utilidades
    // 0 en la salida, 1 en la meta (sin recortar: menos de 0 o más de 1 = fuera del tramo)
    static float AlongSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        return ab.sqrMagnitude > 0.0001f ? Vector2.Dot(p - a, ab) / ab.sqrMagnitude : 0f;
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    static Sprite softDisc;
    static Sprite SoftDisc()
    {
        if (softDisc != null) return softDisc;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        softDisc = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 64f);
        return softDisc;
    }

    [ContextMenu("Mostrar el icono del HUD (para colocarlo)")]
    void ShowHudNow()
    {
        Sprite s = null;
        if (geodeSprites != null) foreach (var g in geodeSprites) if (g != null) { s = g; break; }
        SetHud(s);
    }

    [ContextMenu("Poner una geoda ya")]
    void SpawnNow() { RemovePickup(); Spawn(); }
}
