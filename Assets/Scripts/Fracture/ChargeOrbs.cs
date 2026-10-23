using System.Collections.Generic;
using UnityEngine;

/// Las cargas de onda, a la vista: esferas blancas que giran por encima de la cabeza del
/// jugador, una por cada carga completa. La órbita va pegada a la cabeza: si el personaje gira,
/// gira con él. La que se está llenando crece y se aclara según su parte.
///
///   2 cargas   temblor de cámara y 1 rayo azul alrededor del cuerpo
///   5 cargas   temblor más fuerte y 2 rayos
///   7 cargas   onda expansiva desde el personaje y, mientras sigan las 7, un aura de fuego
///              con llamas en punta que suben alrededor (como la de Critical Explosion) y 4 rayos
///
/// Todo con sprites (sin sistemas de partículas). Solo en mapas con grieta. Va en el mismo
/// objeto que el FractureDirector.
[RequireComponent(typeof(FractureDirector))]
public class ChargeOrbs : MonoBehaviour
{
    [Header("Esferas")]
    [Tooltip("Distancia del centro de la órbita a la cabeza, en tamaños del jugador")]
    [SerializeField] private float aboveHead = 1.05f;
    [Tooltip("Radio de la órbita (a lo ancho, en profundidad); se abre un poco con más esferas")]
    [SerializeField] private Vector2 orbitRadius = new Vector2(0.32f, 0.08f);
    [SerializeField] private float orbitSpeed = 140f;
    [SerializeField] private float orbSize = 0.16f;
    [SerializeField] private Color orbColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color orbGlow = new Color(0.7f, 0.88f, 1f, 0.6f);

    [Header("Hitos (temblor en unidades de mundo)")]
    [SerializeField] private float shakeAt2 = 0.25f;
    [SerializeField] private float shakeAt5 = 0.4f;
    [SerializeField] private float shakeAt7 = 0.55f;
    [SerializeField] private float shockRadius = 3.5f;
    [SerializeField] private Color shockColor = new Color(1f, 0.75f, 0.4f, 0.95f);

    [Header("Aura de fuego a cargas máximas")]
    [SerializeField] private int flames = 34;
    [Tooltip("Radio en el que nacen las llamas, en tamaños del jugador")]
    [SerializeField] private float auraRadius = 0.32f;
    [SerializeField] private Vector2 flameLife = new Vector2(0.3f, 0.55f);
    [SerializeField] private Vector2 flameRise = new Vector2(1.4f, 2.6f);
    [Tooltip("Tamaño de cada llama: ancho y alto (en punta)")]
    [SerializeField] private Vector2 flameSize = new Vector2(0.12f, 0.42f);
    [SerializeField] private Color flameCore = new Color(1f, 0.97f, 0.85f, 1f);
    [SerializeField] private Color flameMid = new Color(1f, 0.5f, 0.12f, 0.95f);
    [SerializeField] private Color flameEnd = new Color(0.8f, 0.08f, 0.05f, 0f);
    [SerializeField] private Color auraGlow = new Color(1f, 0.3f, 0.08f, 0.5f);

    [Tooltip("Desmarcado: el aura solo lleva los rayos azules, sin llamas")]
    [SerializeField] private bool flamesOn = true;

    [Header("Rayos azules del aura (FX_Arco_1..3)")]
    [Tooltip("Los fotogramas de los rayos. Vacío = sin rayos")]
    [SerializeField] private Sprite[] arcFrames;
    [Tooltip("Rayos con 2 cargas o más, con 5 o más y con las 7 (con el aura)")]
    [SerializeField] private int arcsAt2 = 1;
    [SerializeField] private int arcsAt5 = 2;
    [SerializeField] private int arcs = 4;
    [Tooltip("Tamaño de cada rayo en unidades del mundo (para un personaje de escala 1)")]
    [SerializeField] private float arcSize = 0.6f;
    [Tooltip("A qué distancia del centro del personaje salen (multiplica Aura Radius)")]
    [SerializeField] private float arcSpread = 1.5f;
    [SerializeField] private float arcFrameTime = 0.06f;
    [SerializeField] private Vector2 arcLife = new Vector2(0.14f, 0.3f);
    [SerializeField] private Color arcColor = new Color(1f, 1f, 1f, 1f);

    [SerializeField] private string sortingLayer = "FX";
    [SerializeField] private int sortingOrder = 50;

    FractureDirector fd;
    PlayerController player;
    readonly List<SpriteRenderer> orbs = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> glows = new List<SpriteRenderer>();
    SpriteRenderer auraBack, shock;
    SpriteRenderer[] flameSr;
    SpriteRenderer[] arcSr;
    float[] arcAge, arcMax;
    int[] arcFrame0;
    Vector2[] arcOff;
    Vector2[] flamePos, flameVel;
    float[] flameAge, flameMax, flameW, flameH;
    float spin, flash, shockT = -1f;
    int lastFull;
    Sprite disc, soft, ring, spike;

    void Awake()
    {
        fd = GetComponent<FractureDirector>();
        disc = MakeShape(32, 0);
        soft = MakeShape(32, 1);
        ring = MakeShape(128, 2);
        spike = MakeShape(64, 3);

        shock = NewSr("OndaCargas", ring, sortingLayer, sortingOrder + 3);
        auraBack = NewSr("AuraBrillo", soft, "Player", -10);

        int n = Mathf.Max(1, flames);
        flameSr = new SpriteRenderer[n];
        flamePos = new Vector2[n]; flameVel = new Vector2[n];
        flameAge = new float[n]; flameMax = new float[n]; flameW = new float[n]; flameH = new float[n];
        for (int i = 0; i < n; i++)
        {
            // La mitad, detrás del personaje; la otra mitad, delante
            flameSr[i] = NewSr("Llama", spike, i % 2 == 0 ? "Player" : sortingLayer, i % 2 == 0 ? -9 : sortingOrder - 1);
            flameAge[i] = flameMax[i] = 1f;
        }

        int na = Mathf.Max(0, Mathf.Max(arcs, Mathf.Max(arcsAt2, arcsAt5)));
        arcSr = new SpriteRenderer[na];
        arcAge = new float[na]; arcMax = new float[na]; arcFrame0 = new int[na]; arcOff = new Vector2[na];
        for (int i = 0; i < na; i++)
        {
            arcSr[i] = NewSr("Rayo", null, i % 2 == 0 ? "Player" : sortingLayer, i % 2 == 0 ? -8 : sortingOrder + 2);
            arcAge[i] = arcMax[i] = 1f;
            arcAge[i] = Random.Range(0f, 1f);   // que no nazcan todos a la vez
        }
    }

    SpriteRenderer NewSr(string name, Sprite s, string layer, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.sortingLayerName = layer; sr.sortingOrder = order; sr.enabled = false;
        return sr;
    }

    void EnsureOrbs(int n)
    {
        while (orbs.Count < n)
        {
            glows.Add(NewSr("BrilloEsfera", soft, sortingLayer, sortingOrder));
            orbs.Add(NewSr("Esfera", disc, sortingLayer, sortingOrder + 1));
        }
    }

    void Update()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        bool show = fd != null && fd.Active && player != null && !player.IsDead && !player.IsAbsorbed;
        int max = fd != null ? Mathf.Max(1, fd.MaxCharges) : 1;
        EnsureOrbs(max);
        if (!show) { Hide(); return; }

        float charge = Mathf.Clamp(fd.Charge, 0f, max);
        int full = Mathf.FloorToInt(charge + 0.0001f);
        float part = charge - full;
        if (full > lastFull) { flash = 1f; Milestone(lastFull, full, max); }
        lastFull = full;
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime / 0.35f);

        // La cabeza manda: arriba y a lo ancho según hacia dónde mira el dibujo del personaje
        Transform head = player.Visual != null ? player.Visual : player.transform;
        Vector2 up = head.up, side = head.right;
        float s = Mathf.Abs(player.transform.lossyScale.y);
        Vector2 me = player.transform.position;
        Vector2 center = me + up * (aboveHead * s);

        spin += orbitSpeed * Time.deltaTime;
        int visible = full + (part > 0.02f && full < max ? 1 : 0);
        float rx = orbitRadius.x + 0.04f * Mathf.Max(0, visible - 2);
        for (int i = 0; i < orbs.Count; i++)
        {
            bool on = i < visible;
            orbs[i].enabled = glows[i].enabled = on;
            if (!on) continue;
            float k = i < full ? 1f : part;
            float a = (spin + 360f * i / Mathf.Max(1, visible)) * Mathf.Deg2Rad;
            // Órbita plana alrededor del eje de la cabeza: nunca la atraviesa
            Vector2 p = center + (side * (Mathf.Cos(a) * rx) + up * (Mathf.Sin(a) * orbitRadius.y)) * s;
            orbs[i].transform.position = p;
            glows[i].transform.position = p;
            float depth = 0.85f + 0.15f * -Mathf.Sin(a);
            float size = orbSize * s * Mathf.Lerp(0.35f, 1f, k) * depth * (1f + 0.4f * flash);
            orbs[i].transform.localScale = Vector3.one * (size / disc.bounds.size.x);
            glows[i].transform.localScale = Vector3.one * (size * 3f / soft.bounds.size.x);
            orbs[i].color = new Color(orbColor.r, orbColor.g, orbColor.b, orbColor.a * Mathf.Lerp(0.35f, 1f, k));
            glows[i].color = new Color(orbGlow.r, orbGlow.g, orbGlow.b,
                orbGlow.a * Mathf.Lerp(0.2f, 1f, k) * (0.75f + 0.25f * Mathf.Sin(Time.time * 6f + i)));
        }

        bool fullCharge = full >= max;
        // Rayos que crecen con las cargas: 1 con 2, 2 con 5, todos con las 7
        int nArcs = fullCharge ? arcs : full >= 5 ? arcsAt5 : full >= 2 ? arcsAt2 : 0;
        UpdateArcs(nArcs, me, s, Time.deltaTime);
        UpdateAura(fullCharge, me, up, s);
        UpdateShock(me);
    }

    // Llamas en punta que nacen alrededor del personaje y suben (hacia su "arriba"), de blanco a
    // naranja y a rojo, encogiéndose
    void UpdateAura(bool on, Vector2 me, Vector2 up, float s)
    {
        float dt = Time.deltaTime;
        auraBack.enabled = on;
        if (on)
        {
            auraBack.transform.position = me;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 11f);
            auraBack.transform.localScale = Vector3.one * (1.7f * s * pulse / soft.bounds.size.x);
            auraBack.color = auraGlow;
        }
        float rot = Mathf.Atan2(up.y, up.x) * Mathf.Rad2Deg - 90f;
        for (int i = 0; i < flameSr.Length; i++)
        {
            if (!flamesOn) { flameSr[i].enabled = false; flameAge[i] = flameMax[i]; continue; }
            flameAge[i] += dt;
            if (flameAge[i] >= flameMax[i])
            {
                if (!on) { flameSr[i].enabled = false; continue; }
                // Nace otra
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector2 off = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * auraRadius * s * Random.Range(0.6f, 1f);
                flamePos[i] = me + off - up * 0.1f * s;
                flameVel[i] = up * Random.Range(flameRise.x, flameRise.y) * s * 0.5f + off.normalized * 0.15f * s;
                flameAge[i] = 0f;
                flameMax[i] = Random.Range(flameLife.x, flameLife.y);
                flameW[i] = flameSize.x * s * Random.Range(0.7f, 1.2f);
                flameH[i] = flameSize.y * s * Random.Range(0.7f, 1.3f);
            }
            float e = flameAge[i] / flameMax[i];
            flamePos[i] += flameVel[i] * dt;
            var sr = flameSr[i];
            sr.enabled = true;
            sr.transform.position = flamePos[i];
            sr.transform.rotation = Quaternion.Euler(0f, 0f, rot + Mathf.Sin((Time.time + i) * 12f) * 6f);
            float shrink = 1f - e * 0.7f;
            sr.transform.localScale = new Vector3(flameW[i] * shrink / spike.bounds.size.x,
                                                  flameH[i] * (0.6f + 0.6f * Mathf.Sin(e * Mathf.PI)) / spike.bounds.size.y, 1f);
            sr.color = e < 0.35f ? Color.Lerp(flameCore, flameMid, e / 0.35f) : Color.Lerp(flameMid, flameEnd, (e - 0.35f) / 0.65f);
        }
    }

    // Rayos azules (los primeros Count): aparecen en puntos al azar alrededor del personaje, giran al azar, pasan
    // por sus fotogramas y se apagan; luego renacen en otro sitio
    void UpdateArcs(int count, Vector2 me, float s, float dt)
    {
        if (arcSr == null) return;
        bool has = arcFrames != null && arcFrames.Length > 0;
        for (int i = 0; i < arcSr.Length; i++)
        {
            bool on = i < count;
            var sr = arcSr[i];
            arcAge[i] += dt;
            if (arcAge[i] >= arcMax[i])
            {
                if (!on || !has) { sr.enabled = false; continue; }
                float a = Random.Range(0f, Mathf.PI * 2f);
                arcOff[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * auraRadius * arcSpread * Random.Range(0.4f, 1f);
                arcAge[i] = 0f;
                arcMax[i] = Random.Range(arcLife.x, arcLife.y);
                arcFrame0[i] = Random.Range(0, arcFrames.Length);
                sr.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                sr.flipX = Random.value < 0.5f;
            }
            if (!on || !has) { sr.enabled = false; continue; }
            var spr = arcFrames[(arcFrame0[i] + Mathf.FloorToInt(arcAge[i] / Mathf.Max(0.01f, arcFrameTime))) % arcFrames.Length];
            sr.sprite = spr;
            if (spr == null) { sr.enabled = false; continue; }
            sr.enabled = true;
            sr.transform.position = me + arcOff[i] * s;
            float e = arcAge[i] / arcMax[i];
            sr.transform.localScale = Vector3.one * (arcSize * s / Mathf.Max(0.01f, spr.bounds.size.x));
            var c = arcColor; c.a *= e < 0.7f ? 1f : 1f - (e - 0.7f) / 0.3f;
            sr.color = c;
        }
    }

    void UpdateShock(Vector2 me)
    {
        if (shockT < 0f) { shock.enabled = false; return; }
        shockT += Time.deltaTime / 0.45f;
        float e = Mathf.Clamp01(shockT);
        shock.enabled = e < 1f;
        shock.transform.position = me;
        float rr = Mathf.Lerp(0.3f, shockRadius, 1f - (1f - e) * (1f - e));
        shock.transform.localScale = Vector3.one * (rr * 2f / ring.bounds.size.x);
        shock.color = new Color(shockColor.r, shockColor.g, shockColor.b, shockColor.a * (1f - e));
        if (e >= 1f) shockT = -1f;
    }

    void Milestone(int before, int now, int max)
    {
        var fx = ImpactFeedback.Instance;
        if (fx == null) Debug.LogWarning("ChargeOrbs: no hay ImpactFeedback en la escena; sin temblor.");
        if (before < max && now >= max)
        {
            shockT = 0f;
            if (fx != null) fx.Shake(shakeAt7, 0.45f);
        }
        else if (before < 5 && now >= 5) { if (fx != null) fx.Shake(shakeAt5, 0.35f); }
        else if (before < 2 && now >= 2) { if (fx != null) fx.Shake(shakeAt2, 0.28f); }
    }

    void Hide()
    {
        foreach (var o in orbs) o.enabled = false;
        foreach (var g in glows) g.enabled = false;
        if (auraBack != null) auraBack.enabled = false;
        if (flameSr != null) for (int i = 0; i < flameSr.Length; i++) { flameSr[i].enabled = false; flameAge[i] = flameMax[i]; }
        if (arcSr != null) for (int i = 0; i < arcSr.Length; i++) { arcSr[i].enabled = false; arcAge[i] = arcMax[i]; }
        lastFull = 0;
    }

    // 0 = disco, 1 = mancha suave, 2 = aro, 3 = llama en punta (ancha abajo, afilada arriba)
    static Sprite MakeShape(int n, int kind)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float dx = u * 2f - 1f, dy = v * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a;
                if (kind == 0) a = Mathf.Clamp01((1f - r) / 0.15f);
                else if (kind == 1) a = Mathf.Pow(Mathf.Clamp01(1f - r), 2f);
                else if (kind == 2) a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.88f) / 0.08f);
                else
                {
                    float half = 0.5f * Mathf.Pow(1f - v, 0.7f) * Mathf.Clamp01(v * 4f);   // ancho según la altura
                    float d = Mathf.Abs(u - 0.5f);
                    a = Mathf.Clamp01((half - d) / Mathf.Max(0.02f, half * 0.6f)) * Mathf.Clamp01((1f - v) * 3f);
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, kind == 3 ? 0.1f : 0.5f), n);
    }
}
