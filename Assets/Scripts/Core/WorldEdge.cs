using UnityEngine;

/// Bordes del mundo que no se sienten como una caja:
///   Niebla: se espesa al acercarse al borde, con las esquinas redondeadas. Tapa también lo que
///           hay más allá de la pared.
///   Empuje: solo en las esquinas redondeadas, una fuerza suave te devuelve hacia dentro
///           siguiendo la curva. En los lados rectos llegas a la pared como siempre.
/// Cerca de la meta no empuja (la meta suele estar pegada a un borde), y alrededor de ella no
/// hay niebla: el agujero negro conserva sus colores y brillos aunque esté en el borde.
///
/// Va en un objeto vacío de la escena.
public class WorldEdge : MonoBehaviour
{
    [Header("Forma")]
    [Tooltip("Radio de las esquinas, como fracción del lado corto del mundo (0.5 = casi elipse)")]
    [Range(0f, 0.5f)] [SerializeField] private float cornerRoundness = 0.3f;

    [Header("Empuje suave")]
    [Tooltip("Solo empuja en las esquinas redondeadas. En los lados rectos llegas hasta la pared, " +
             "como siempre")]
    [SerializeField] private bool cornersOnly = true;
    [Tooltip("Desde cuántas unidades antes de la curva empieza a empujar")]
    [SerializeField] private float pushZone = 1f;
    [Tooltip("Aceleración hacia dentro en el mismo borde (u/s²); crece suave desde el inicio de la zona")]
    [SerializeField] private float pushStrength = 22f;
    [Tooltip("Sin empuje a esta distancia de la meta o menos")]
    [SerializeField] private float goalSafeDistance = 8f;

    [Header("Niebla")]
    [SerializeField] private Color fogColor = new Color(0.05f, 0.06f, 0.12f, 1f);
    [Tooltip("Opacidad de la niebla en el borde (y más allá, entera)")]
    [Range(0f, 1f)] [SerializeField] private float fogAtEdge = 0.85f;
    [Tooltip("Desde cuántas unidades antes del borde empieza la niebla")]
    [SerializeField] private float fogZone = 2.5f;
    [Tooltip("Cuánto se extiende la niebla por fuera del mundo (tapa lo que deja ver la cámara)")]
    [SerializeField] private float fogOutside = 14f;
    [Tooltip("Alrededor del agujero negro no hay niebla: margen extra sobre su dibujo (halo incluido)")]
    [SerializeField] private float goalClearMargin = 1f;
    [Tooltip("Ancho del paso suave entre el hueco de la meta y la niebla")]
    [SerializeField] private float goalClearSoft = 1.5f;
    [Tooltip("Píxeles por unidad de la textura de niebla (es suave: con poco basta)")]
    [SerializeField] private float fogResolution = 3f;
    [Tooltip("Por encima de las rocas y por debajo del jugador: el jugador siempre se ve")]
    [SerializeField] private string sortingLayer = "Asteroids";
    [SerializeField] private int sortingOrder = 32000;

    PlayerController player;
    Goal goal;
    SpriteRenderer fog;
    Rect builtFor;

    void FixedUpdate()
    {
        var gb = GameBounds.Instance;
        if (gb == null || !gb.IsReady) return;
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (player == null || player.IsDead || player.IsExpelling || player.IsAbsorbed) return;
        if (goal == null) goal = FindFirstObjectByType<Goal>(FindObjectsInactive.Include);

        Vector2 p = player.transform.position;
        if (goal != null && goal.isActiveAndEnabled && Vector2.Distance(p, goal.transform.position) <= goalSafeDistance) return;

        // Distancia al borde redondeado (positiva = dentro) y hacia dónde queda el centro
        float inside = Inside(gb.Area, p, out Vector2 inward, out bool inCorner);
        if (cornersOnly && !inCorner) return;
        if (inside >= pushZone) return;
        float k = 1f - Mathf.Clamp01(inside / Mathf.Max(0.01f, pushZone));
        k = k * k;
        player.AddGravity(inward * pushStrength * k);
    }

    Vector2 holeAt;
    float holeR = -1f;

    void LateUpdate()
    {
        var gb = GameBounds.Instance;
        if (gb == null || !gb.IsReady) return;
        if (goal == null) goal = FindFirstObjectByType<Goal>(FindObjectsInactive.Include);

        // Hueco sin niebla donde está la meta: se rehace si la meta cambia de sitio o aparece
        Vector2 at = holeAt; float r = -1f;
        if (goal != null && goal.isActiveAndEnabled)
        {
            at = goal.transform.position;
            r = GoalExtent() + goalClearMargin;
        }
        bool holeChanged = (r < 0f) != (holeR < 0f) || (r >= 0f && (Vector2.Distance(at, holeAt) > 0.25f || r > holeR + 0.5f));
        // (si el halo late, el hueco solo crece: no se rehace cada fotograma)
        if (fog == null || builtFor != gb.Area || holeChanged)
        {
            if (r >= 0f && holeR >= 0f && Vector2.Distance(at, holeAt) <= 0.25f) r = Mathf.Max(r, holeR);
            holeAt = at; holeR = r;
            BuildFog(gb.Area);
        }
    }

    // Lo que ocupa el dibujo de la meta (disco, halo y lo demás que cuelgue de ella)
    float GoalExtent()
    {
        float e = 0f;
        Vector2 c = goal.transform.position;
        foreach (var sr in goal.GetComponentsInChildren<SpriteRenderer>())
        {
            if (!sr.enabled || sr.sprite == null) continue;
            var b = sr.bounds;
            e = Mathf.Max(e, Vector2.Distance(c, b.center) + Mathf.Max(b.extents.x, b.extents.y));
        }
        return e > 0f ? e : 3f;
    }

    // Rectángulo de esquinas redondeadas: cuánto dentro está p (unidades) y la dirección hacia dentro
    float Inside(Rect area, Vector2 p, out Vector2 inward) => Inside(area, p, out inward, out _);

    float Inside(Rect area, Vector2 p, out Vector2 inward, out bool inCorner)
    {
        Vector2 c = area.center;
        Vector2 half = area.size * 0.5f;
        float r = Mathf.Min(half.x, half.y) * 2f * cornerRoundness;
        Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - (half - Vector2.one * r);
        Vector2 qp = Vector2.Max(q, Vector2.zero);
        float sd = qp.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;   // < 0 dentro
        Vector2 n;
        inCorner = q.x > 0f && q.y > 0f;   // en la zona de la curva, no en un lado recto
        if (inCorner) n = qp.normalized;
        else n = q.x > q.y ? Vector2.right : Vector2.up;
        n = new Vector2(n.x * Mathf.Sign(p.x - c.x), n.y * Mathf.Sign(p.y - c.y));
        inward = -n;
        return -sd;
    }

    void BuildFog(Rect area)
    {
        builtFor = area;
        Rect big = new Rect(area.xMin - fogOutside, area.yMin - fogOutside, area.width + fogOutside * 2f, area.height + fogOutside * 2f);
        int w = Mathf.Clamp(Mathf.RoundToInt(big.width * fogResolution), 16, 512);
        int h = Mathf.Clamp(Mathf.RoundToInt(big.height * fogResolution), 16, 512);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2(big.xMin + (x + 0.5f) / w * big.width, big.yMin + (y + 0.5f) / h * big.height);
                float inside = Inside(area, p, out _);
                float a;
                if (inside <= 0f) a = Mathf.Lerp(fogAtEdge, 1f, Mathf.Clamp01(-inside / 3f));
                else { float t = 1f - Mathf.Clamp01(inside / Mathf.Max(0.01f, fogZone)); a = fogAtEdge * t * t; }
                if (holeR > 0f)
                {
                    float dh = Vector2.Distance(p, holeAt) - holeR;
                    a *= Mathf.Clamp01(dh / Mathf.Max(0.01f, goalClearSoft));
                }
                var c = fogColor; c.a = a;
                px[y * w + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply();

        if (fog == null)
        {
            var go = new GameObject("NieblaBorde");
            go.transform.SetParent(transform, false);
            fog = go.AddComponent<SpriteRenderer>();
            fog.sortingLayerName = sortingLayer;
            fog.sortingOrder = sortingOrder;
        }
        else if (fog.sprite != null) { Destroy(fog.sprite.texture); Destroy(fog.sprite); }
        fog.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w / big.width);
        fog.transform.position = new Vector3(big.center.x, big.center.y, 0f);
        // El alto de la textura no sale exacto al redondear: se corrige con la escala
        float ppu = w / big.width;
        fog.transform.localScale = new Vector3(1f, big.height / (h / ppu), 1f);
    }

    [ContextMenu("Rehacer la niebla")]
    void Rebuild() { var gb = GameBounds.Instance; if (gb != null && gb.IsReady) BuildFog(gb.Area); }
}
