using System.Collections.Generic;
using UnityEngine;

/// Nubes de polvo y gas en primer plano: más cerca de la cámara que el jugador, pasan por
/// delante de él semitransparentes y se mueven más rápido que todo lo demás (la cámara es en
/// perspectiva, así que la profundidad lo hace sola). Dan la sensación de estar dentro de algo
/// inmenso.
///
/// Se construye sola en un objeto vacío con este componente. La cantidad sigue la calidad
/// gráfica (PerformanceManager.DustAmount).
public class ForegroundDust : MonoBehaviour
{
    [SerializeField] private int clouds = 6;
    [Tooltip("Cuánto más cerca de la cámara que el plano del juego (z), mínimo y máximo")]
    [SerializeField] private Vector2 depth = new Vector2(2f, 4.5f);
    [Tooltip("Tamaño de cada nube en unidades")]
    [SerializeField] private Vector2 size = new Vector2(4f, 9f);
    [Tooltip("Opacidad: muy baja, para no tapar el juego")]
    [SerializeField] private Vector2 alpha = new Vector2(0.05f, 0.14f);
    [SerializeField] private Color tintA = new Color(0.7f, 0.65f, 0.9f);
    [SerializeField] private Color tintB = new Color(0.9f, 0.6f, 0.65f);
    [SerializeField] private float drift = 0.25f;
    [SerializeField] private string sortingLayer = "Player";
    [SerializeField] private int sortingOrder = 700;

    class Cloud { public SpriteRenderer sr; public Vector2 vel; public float z, spin, alpha; }
    readonly List<Cloud> list = new List<Cloud>();
    Sprite sprite;
    Camera cam;

    void Start()
    {
        cam = Camera.main;
        sprite = MakeCloud(128, Random.Range(0, 9999));
        int n = Mathf.RoundToInt(clouds * PerformanceManager.DustAmount);
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("NubeDelante");
            go.transform.SetParent(transform, false);
            var c = new Cloud { sr = go.AddComponent<SpriteRenderer>() };
            c.sr.sprite = sprite;
            c.sr.sortingLayerName = sortingLayer;
            c.sr.sortingOrder = sortingOrder + i;
            Respawn(c, true);
            list.Add(c);
        }
    }

    void Respawn(Cloud c, bool anywhere)
    {
        if (cam == null) cam = Camera.main;
        c.z = -Random.Range(depth.x, depth.y);
        Vector2 half = HalfAt(c.z);
        Vector2 cp = cam != null ? (Vector2)cam.transform.position : Vector2.zero;
        float s = Random.Range(size.x, size.y);
        Vector2 p = anywhere
            ? cp + new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y))
            : cp + Random.insideUnitCircle.normalized * (half.magnitude + s * 0.5f);
        c.sr.transform.position = new Vector3(p.x, p.y, c.z);
        c.sr.transform.localScale = Vector3.one * (s / Mathf.Max(0.01f, sprite.bounds.size.x));
        c.sr.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        c.vel = Random.insideUnitCircle * drift;
        c.spin = Random.Range(-3f, 3f);
        c.alpha = Random.Range(alpha.x, alpha.y);
        Color t = Color.Lerp(tintA, tintB, Random.value);
        c.sr.color = new Color(t.r, t.g, t.b, c.alpha);
    }

    // Lo que ve la cámara a la profundidad de la nube
    Vector2 HalfAt(float z)
    {
        if (cam == null) return new Vector2(10f, 6f);
        float planeHalf = CameraView.HalfHeight(cam);
        float d0 = CameraView.Distance(cam);
        float dz = Mathf.Max(0.5f, Mathf.Abs(cam.transform.position.z - z));
        float h = cam.orthographic ? planeHalf : planeHalf * dz / d0;
        return new Vector2(h * cam.aspect, h);
    }

    void LateUpdate()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        Vector2 cp = cam.transform.position;
        foreach (var c in list)
        {
            var t = c.sr.transform;
            t.position += (Vector3)(c.vel * Time.deltaTime);
            t.Rotate(0f, 0f, c.spin * Time.deltaTime);
            Vector2 half = HalfAt(c.z);
            float s = t.localScale.x * sprite.bounds.size.x;
            Vector2 d = (Vector2)t.position - cp;
            // Fuera de la vista: vuelve a entrar por el otro lado
            if (Mathf.Abs(d.x) > half.x + s || Mathf.Abs(d.y) > half.y + s)
            {
                Vector2 q = cp - d.normalized * (half.magnitude + s * 0.4f);
                t.position = new Vector3(q.x, q.y, c.z);
            }
        }
    }

    static Sprite MakeCloud(int n, int seed)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float ox = seed * 0.37f, oy = seed * 0.71f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float f = 0f, amp = 1f, tot = 0f, fr = 2.2f;
                for (int o = 0; o < 4; o++)
                {
                    f += Mathf.PerlinNoise(ox + x / (float)n * fr, oy + y / (float)n * fr) * amp;
                    tot += amp; amp *= 0.5f; fr *= 2f;
                }
                f /= tot;
                float a = Mathf.Clamp01(1f - r) * Mathf.Clamp01((f - 0.35f) * 2.2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }
}
