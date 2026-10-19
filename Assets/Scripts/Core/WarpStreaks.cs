using UnityEngine;

/// El salto al hiperespacio: las estrellas se estiran en líneas mientras el jugador llega a
/// toda velocidad, y se recogen a medida que frena hasta que el espacio vuelve a la normalidad.
///
/// No necesita arte ni nada montado en la escena: crea sus propias líneas la primera vez y las
/// reutiliza. Van colgadas de la cámara, así que cubren siempre lo que se ve.
///
/// Las líneas viajan en sentido CONTRARIO al del jugador: es el espacio el que parece pasar
/// de largo. Su longitud y su brillo siguen a la velocidad del jugador, así que la frenada
/// se lee sola: las rayas se acortan y se apagan a la vez que él se para.
public class WarpStreaks : MonoBehaviour
{
    static WarpStreaks instance;

    const int Count = 70;
    const string SortingLayer = "BG_Dust";
    const int SortingOrder = 100;

    SpriteRenderer[] lines;
    Vector2[] pos;
    float[] speedMul, alphaMul, thick;
    Vector2 dir;
    float strength;      // 0..1, lo pone quien lo usa en cada fotograma
    Camera cam;

    /// Enciende el efecto. `travelDir` es hacia donde va el jugador.
    public static void Begin(Vector2 travelDir)
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (instance == null) instance = Create(cam);
        instance.cam = cam;
        instance.dir = travelDir.sqrMagnitude > 0.0001f ? travelDir.normalized : Vector2.right;
        instance.strength = 1f;
        instance.Scatter();
        instance.gameObject.SetActive(true);
    }

    /// 1 = a toda velocidad, 0 = parado. Se llama cada fotograma durante la llegada.
    public static void SetStrength(float s)
    {
        if (instance != null) instance.strength = Mathf.Clamp01(s);
    }

    public static void End()
    {
        if (instance != null) instance.gameObject.SetActive(false);
    }

    static WarpStreaks Create(Camera cam)
    {
        var go = new GameObject("WarpStreaks");
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, 10f);   // delante de la cámara
        var w = go.AddComponent<WarpStreaks>();
        w.Build();
        return w;
    }

    void Build()
    {
        // Una raya blanca con los extremos difuminados, hecha a mano: 32 x 4 píxeles
        var tex = new Texture2D(32, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < 32; x++)
        {
            float u = x / 31f;
            float a = Mathf.Sin(u * Mathf.PI);           // se apaga en las puntas
            a *= Mathf.Lerp(0.35f, 1f, u);               // y la cola es más tenue que la cabeza
            for (int y = 0; y < 4; y++)
            {
                float v = (y == 0 || y == 3) ? 0.35f : 1f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * v));
            }
        }
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, 32, 4), new Vector2(0.5f, 0.5f), 32f);

        lines = new SpriteRenderer[Count];
        pos = new Vector2[Count];
        speedMul = new float[Count];
        alphaMul = new float[Count];
        thick = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            var go = new GameObject("Streak");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = SortingLayer;
            sr.sortingOrder = SortingOrder;
            lines[i] = sr;
        }
    }

    Vector2 Half()
    {
        float h = cam != null ? cam.orthographicSize : 4f;
        float w = h * (cam != null ? cam.aspect : 2f);
        return new Vector2(w + 2f, h + 2f);
    }

    void Scatter()
    {
        Vector2 half = Half();
        for (int i = 0; i < Count; i++)
        {
            pos[i] = new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
            speedMul[i] = Random.Range(0.5f, 1.4f);      // unas parecen más cerca que otras
            alphaMul[i] = Random.Range(0.35f, 1f);
            thick[i] = Random.Range(0.6f, 1.4f);
        }
    }

    void LateUpdate()
    {
        if (lines == null) return;
        float dt = Time.deltaTime;
        Vector2 half = Half();
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float s = strength;

        for (int i = 0; i < Count; i++)
        {
            // El espacio pasa en contra del viaje
            pos[i] -= dir * (60f * speedMul[i] * s * s * dt);

            // Lo que sale por un lado entra por el contrario
            if (pos[i].x < -half.x) pos[i].x += 2f * half.x; else if (pos[i].x > half.x) pos[i].x -= 2f * half.x;
            if (pos[i].y < -half.y) pos[i].y += 2f * half.y; else if (pos[i].y > half.y) pos[i].y -= 2f * half.y;

            var t = lines[i].transform;
            t.localPosition = pos[i];
            t.localRotation = Quaternion.Euler(0f, 0f, angle);
            float len = Mathf.Lerp(0.05f, 6f * speedMul[i], s * s);
            t.localScale = new Vector3(len, 0.12f * thick[i], 1f);
            lines[i].color = new Color(1f, 1f, 1f, alphaMul[i] * s);
        }
    }
}
