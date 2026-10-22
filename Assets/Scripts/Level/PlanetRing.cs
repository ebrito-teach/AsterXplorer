using System.Collections.Generic;
using UnityEngine;

/// Anillo de un planeta: dos mitades del mismo dibujo, la de atrás por debajo del planeta y la
/// de delante por encima, así se ve como un anillo que lo rodea (tipo Saturno).
///
/// No mata: frena. Mientras el jugador esté sobre la banda del anillo (la elipse que se ve, sin
/// el hueco del centro) va un Slow más lento.
///
/// Lo crea el LevelPopulator; no hace falta añadirlo a mano.
public class PlanetRing : MonoBehaviour
{
    [System.Serializable]
    public class Look
    {
        [Tooltip("Dibujo del anillo, en horizontal (el eje largo de izquierda a derecha)")]
        public Sprite sprite;
        [Tooltip("Ancho del hueco del centro respecto al ancho total del dibujo")]
        [Range(0.2f, 0.9f)] public float innerRatio = 0.5f;
    }

    static readonly List<PlanetRing> all = new List<PlanetRing>();

    float outerA, innerA, flat, slow;
    Vector2 center;
    float angleRad;

    /// Frenado (0..1) en ese punto por estar sobre algún anillo
    public static float SlowAt(Vector2 p)
    {
        float best = 0f;
        foreach (var r in all)
        {
            if (r == null) continue;
            Vector2 d = p - r.center;
            float c = Mathf.Cos(-r.angleRad), s = Mathf.Sin(-r.angleRad);
            float x = d.x * c - d.y * s, y = d.x * s + d.y * c;
            // A coordenadas del círculo original (deshace el aplastamiento)
            float rr = Mathf.Sqrt(x * x + (y / r.flat) * (y / r.flat));
            if (rr < r.innerA || rr > r.outerA) continue;
            // Entra y sale suave en los bordes de la banda
            float band = r.outerA - r.innerA;
            float e = Mathf.Clamp01(Mathf.Min(rr - r.innerA, r.outerA - rr) / Mathf.Max(0.01f, band * 0.15f));
            best = Mathf.Max(best, r.slow * e);
        }
        return best;
    }

    public static void ClearAll()
    {
        foreach (var r in all) if (r != null) Destroy(r.gameObject);
        all.Clear();
    }

    /// Crea el anillo alrededor del planeta. innerRadius: radio del hueco (eje largo) en unidades
    public static PlanetRing Create(Asteroid planet, Look look, float innerRadius, float tiltDeg, float slowFactor, Transform parent)
    {
        if (planet == null || look == null || look.sprite == null) return null;
        var go = new GameObject("Anillo_" + planet.name);
        go.transform.SetParent(parent, false);
        go.transform.position = planet.transform.position;
        go.transform.rotation = Quaternion.Euler(0f, 0f, tiltDeg);
        var r = go.AddComponent<PlanetRing>();

        Sprite src = look.sprite;
        Rect rect = src.rect;
        float ppu = src.pixelsPerUnit;
        float widthUnits = rect.width / ppu;
        float outerRadius = innerRadius / Mathf.Max(0.05f, look.innerRatio);
        float scale = outerRadius * 2f / Mathf.Max(0.01f, widthUnits);

        r.center = planet.transform.position;
        r.angleRad = tiltDeg * Mathf.Deg2Rad;
        r.innerA = innerRadius;
        r.outerA = outerRadius;
        r.flat = Mathf.Max(0.05f, rect.height / rect.width);
        r.slow = Mathf.Clamp01(slowFactor);

        // Las dos mitades: arriba (detrás del planeta) y abajo (delante)
        var psr = planet.GetComponentInChildren<SpriteRenderer>();
        string layer = psr != null ? psr.sortingLayerName : "Asteroids";
        int order = psr != null ? psr.sortingOrder : 0;
        float half = rect.height * 0.5f;
        var back = Sprite.Create(src.texture, new Rect(rect.x, rect.y + half, rect.width, half), new Vector2(0.5f, 0f), ppu);
        var front = Sprite.Create(src.texture, new Rect(rect.x, rect.y, rect.width, half), new Vector2(0.5f, 1f), ppu);
        r.Half("Atras", back, layer, order - 1, scale);
        r.Half("Delante", front, layer, order + 1, scale);

        all.Add(r);
        return r;
    }

    void Half(string name, Sprite s, string layer, int order, float scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.sortingLayerName = layer;
        sr.sortingOrder = order;
    }

    /// Radio exterior (eje largo), para saber cuánto sitio ocupa
    public float OuterRadius => outerA;

    void OnDestroy() { all.Remove(this); }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        for (int k = 0; k < 2; k++)
        {
            float rad = k == 0 ? innerA : outerA;
            Vector2 prev = Vector2.zero;
            for (int i = 0; i <= 48; i++)
            {
                float t = i / 48f * Mathf.PI * 2f;
                Vector2 q = new Vector2(Mathf.Cos(t) * rad, Mathf.Sin(t) * rad * flat);
                float c = Mathf.Cos(angleRad), s = Mathf.Sin(angleRad);
                q = center + new Vector2(q.x * c - q.y * s, q.x * s + q.y * c);
                if (i > 0) Gizmos.DrawLine(prev, q);
                prev = q;
            }
        }
    }
}
