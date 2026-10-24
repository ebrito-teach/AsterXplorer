using System.Collections.Generic;
using UnityEngine;

/// Luces de visión: solo en los mundos oscuros. Puntitos de luz azul repartidos al azar por el
/// mapa; al tocarlos, la luz del jugador crece. Empieza en el radio del DarkWorld (1.5) y nunca
/// pasa de Max Vision (3.2).
///
///   Cantidad:  0..Max Count al azar en cada intento (a veces ninguna).
///   Valor:     +0.1, +0.2 o +0.3 al azar; como mucho 2 de +0.3 y 3 de +0.2. La suma de las luces
///              del mapa nunca pasa de lo que falta hasta Max Vision (no se desperdicia luz).
///   Se ven:    brillan en la oscuridad, pero solo a menos de Show Distance (8 u) del jugador.
///   Duran:     ese intento. Al morir todo vuelve a 1.5 y las luces se sortean de nuevo.
///
/// Va en un objeto vacío de la escena, con los fotogramas azules del brillo en Frames.
public class DarkLights : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private GameDirector director;
    [Tooltip("La luz quieta, esperando: se anima en bucle (p. ej. FX_Brillo_5, FX_Brillo_6)")]
    [SerializeField] private Sprite[] frames;
    [Tooltip("El destello al recogerla, una vez (p. ej. FX_Brillo_2 … FX_Brillo_8)")]
    [SerializeField] private Sprite[] pickFrames;
    [SerializeField] private float pickFrameTime = 0.05f;

    [Header("Reparto")]
    [SerializeField] private int maxCount = 10;
    [SerializeField] private float maxVision = 3.2f;
    [SerializeField] private int maxOf03 = 2;
    [SerializeField] private int maxOf02 = 3;
    [Tooltip("Distancia mínima a la salida del jugador, a la meta y entre luces")]
    [SerializeField] private float minFromPlayer = 5f;
    [SerializeField] private float minFromGoal = 5f;
    [SerializeField] private float minBetween = 4f;
    [Tooltip("Margen con las paredes del mundo")]
    [SerializeField] private float edgeMargin = 2f;

    [Header("Aspecto")]
    [Tooltip("Solo se ve a esta distancia del jugador o menos")]
    [SerializeField] private float showDistance = 8f;
    [Tooltip("Tamaño (diámetro en unidades) de las luces de +0.1, +0.2 y +0.3")]
    [SerializeField] private Vector3 sizes = new Vector3(0.9f, 1.15f, 1.4f);
    [SerializeField] private Color tint = new Color(0.6f, 0.85f, 1f, 1f);
    [SerializeField] private float frameTime = 0.08f;
    [Tooltip("Se recoge al pasar a esta distancia del centro del jugador")]
    [SerializeField] private float pickRadius = 0.6f;
    [Tooltip("Segundos que tarda la luz del jugador en crecer al recoger una")]
    [SerializeField] private float growTime = 0.4f;
    [SerializeField] private string sortingLayer = "Player";
    [Tooltip("Por encima de la capa de oscuridad del DarkWorld (30000)")]
    [SerializeField] private int sortingOrder = 30010;

    class Light { public SpriteRenderer sr; public float value, size, phase, takenAt; public bool taken; }
    readonly List<Light> lights = new List<Light>();
    PlayerController player;
    Goal goal;
    float baseVision = -1f, targetVision, shownVision;

    void OnEnable()
    {
        if (director == null) director = FindFirstObjectByType<GameDirector>();
        if (director != null) director.LevelStarted += OnLevelStarted;
    }

    void OnDisable()
    {
        if (director != null) director.LevelStarted -= OnLevelStarted;
    }

    void OnLevelStarted(int level)
    {
        var dw = DarkWorld.Instance;
        if (dw != null)
        {
            if (baseVision < 0f) baseVision = dw.VisionRadius;   // el del Inspector, una vez
            dw.VisionRadius = baseVision;
        }
        targetVision = shownVision = baseVision;
        Clear();
        if (director != null && director.IsDarkWorld) Spawn();
    }

    void Clear()
    {
        foreach (var l in lights) if (l.sr != null) Destroy(l.sr.gameObject);
        lights.Clear();
    }

    // Sorteo: cantidad 0..maxCount, valores 0.1/0.2/0.3 con topes; la suma nunca pasa de lo que falta
    List<float> RollValues()
    {
        var vals = new List<float>();
        float budget = Mathf.Max(0f, maxVision - baseVision) + 0.0001f;
        int n = Random.Range(0, maxCount + 1);
        int n3 = 0, n2 = 0;
        for (int i = 0; i < n && budget >= 0.1f; i++)
        {
            var options = new List<float> { 0.1f };
            if (n2 < maxOf02 && budget >= 0.2f) options.Add(0.2f);
            if (n3 < maxOf03 && budget >= 0.3f) options.Add(0.3f);
            float v = options[Random.Range(0, options.Count)];
            if (v > 0.25f) n3++; else if (v > 0.15f) n2++;
            vals.Add(v);
            budget -= v;
        }
        return vals;
    }

    void Spawn()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (goal == null) goal = FindFirstObjectByType<Goal>(FindObjectsInactive.Include);
        var gb = GameBounds.Instance;
        if (gb == null || !gb.IsReady) return;
        Rect area = gb.Area;
        Vector2 from = player != null ? player.SpawnPoint : area.center;
        Vector2 to = goal != null ? (Vector2)goal.transform.position : area.center;
        int blockMask = LayerMask.GetMask("AsteroidStatic");

        var vals = RollValues();
        var placed = new List<Vector2>();
        foreach (float v in vals)
        {
            for (int tries = 0; tries < 40; tries++)
            {
                var p = new Vector2(Random.Range(area.xMin + edgeMargin, area.xMax - edgeMargin),
                                    Random.Range(area.yMin + edgeMargin, area.yMax - edgeMargin));
                if (Vector2.Distance(p, from) < minFromPlayer || Vector2.Distance(p, to) < minFromGoal) continue;
                bool near = false;
                foreach (var q in placed) if (Vector2.Distance(p, q) < minBetween) { near = true; break; }
                if (near) continue;
                if (blockMask != 0 && Physics2D.OverlapCircle(p, 1f, blockMask) != null) continue;
                placed.Add(p);
                lights.Add(Make(p, v));
                break;
            }
        }
        float sum = 0f; foreach (var l in lights) sum += l.value;
        Debug.Log($"DarkLights: {lights.Count} luces (de {vals.Count} sorteadas), +{sum:0.0} de visión en total.");
    }

    Light Make(Vector2 at, float value)
    {
        var go = new GameObject("LuzDeVision");
        go.transform.SetParent(transform, false);
        go.transform.position = at;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = sortingLayer;
        sr.sortingOrder = sortingOrder;
        sr.color = tint;
        float size = value > 0.25f ? sizes.z : value > 0.15f ? sizes.y : sizes.x;
        return new Light { sr = sr, value = value, size = size, phase = Random.Range(0f, 10f) };
    }

    void Update()
    {
        var dw = DarkWorld.Instance;
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        bool alive = player != null && !player.IsDead && !player.IsAbsorbed;
        Vector2 me = player != null ? (Vector2)player.transform.position : Vector2.zero;
        bool hasFrames = frames != null && frames.Length > 0;

        foreach (var l in lights)
        {
            if (l.sr == null) continue;
            if (l.taken)
            {
                // Destello al recogerla, y se apaga
                int f = Mathf.FloorToInt((Time.time - l.takenAt) / Mathf.Max(0.01f, pickFrameTime));
                bool on = pickFrames != null && f < pickFrames.Length && pickFrames[f] != null;
                l.sr.enabled = on;
                if (on)
                {
                    var ps = pickFrames[f];
                    l.sr.sprite = ps;
                    l.sr.transform.localScale = Vector3.one * (l.size * 1.4f / Mathf.Max(0.01f, ps.rect.width / ps.pixelsPerUnit));
                    l.sr.color = tint;
                }
                continue;
            }
            Vector2 p = l.sr.transform.position;
            float d = Vector2.Distance(p, me);
            if (alive && d <= pickRadius + l.size * 0.3f)
            {
                l.taken = true;
                l.takenAt = Time.time;
                l.sr.enabled = false;
                targetVision = Mathf.Min(maxVision, targetVision + l.value);
                continue;
            }
            // Solo a menos de Show Distance; aparece suave en el último metro
            float a = Mathf.Clamp01(showDistance - d);
            l.sr.enabled = a > 0.01f && hasFrames;
            if (!l.sr.enabled) continue;
            float t = Time.time + l.phase;
            var spr = frames[Mathf.FloorToInt(t / Mathf.Max(0.01f, frameTime)) % frames.Length];
            l.sr.sprite = spr;
            if (spr == null) continue;
            float pulse = 1f + 0.12f * Mathf.Sin(t * 4f);
            l.sr.transform.localScale = Vector3.one * (l.size * pulse / Mathf.Max(0.01f, spr.rect.width / spr.pixelsPerUnit));
            var c = tint; c.a *= a; l.sr.color = c;
        }

        // La luz del jugador crece poco a poco hasta lo recogido
        if (dw != null && baseVision >= 0f && !Mathf.Approximately(shownVision, targetVision))
        {
            float speed = 0.3f / Mathf.Max(0.05f, growTime);
            shownVision = Mathf.MoveTowards(shownVision, targetVision, speed * Time.deltaTime);
            dw.VisionRadius = shownVision;
        }
    }

    [ContextMenu("Sortear luces otra vez")]
    void Reroll() { if (director != null) OnLevelStarted(director.CurrentLevel); }
}
