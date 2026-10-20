using System.Collections.Generic;
using UnityEngine;

/// Nebulosas y objetos lejanos (planetas, lunas, galaxias) del fondo, distintos en cada mapa.
///
/// PROFUNDIDAD
///
/// Igual que el cielo: cada pieza acompaña a la cámara en una fracción (Follow). 1 = pegada a
/// la cámara (infinitamente lejos), 0 = clavada en el mundo. Las nebulosas van muy al fondo,
/// detrás de las estrellas. Los objetos, delante de las estrellas y detrás del polvo, cada uno
/// a su propia profundidad: los más lejanos se mueven menos, son más pequeños y más tenues.
///
/// MEMORIA
///
/// Las 23 nebulosas a la vez no caben bien en la memoria de un móvil. Por eso NO se
/// referencian en el Inspector: se cargan de una carpeta Resources solo las que pide el mapa,
/// y las del mapa anterior se liberan. Los objetos son pequeños y sí van en el Inspector.
///
/// Va en el mismo objeto que el SkyDirector o en otro vacío.
[DefaultExecutionOrder(150)]
public class BackdropDirector : MonoBehaviour
{
    [Header("Nebulosas (se cargan bajo demanda)")]
    [Tooltip("Carpeta dentro de Assets/Resources donde están los PNG de las nebulosas")]
    [SerializeField] private string nebulaFolder = "Nebulas";
    [Tooltip("Nombre de los archivos sin el número: bg_nebula_01, bg_nebula_02...")]
    [SerializeField] private string nebulaPrefix = "bg_nebula_";
    [Tooltip("Cuántas hay (numeradas desde 01)")]
    [SerializeField] private int nebulaCount = 23;
    [SerializeField] private Vector2Int nebulasPerMap = new Vector2Int(1, 2);
    [Range(0f, 1f)] [SerializeField] private float nebulaFollow = 0.93f;
    [Tooltip("Escala respecto a su tamaño de importación (una nebulosa a 1 mide una pantalla)")]
    [SerializeField] private Vector2 nebulaScale = new Vector2(0.8f, 1.5f);
    [Tooltip("Opacidad. Por debajo de 1 dejan ver las estrellas a través")]
    [SerializeField] private Vector2 nebulaAlpha = new Vector2(0.5f, 0.85f);
    [Tooltip("Brillo de la nebulosa (multiplica su color). Una nebulosa enorme y apagada se lee " +
             "como lejanísima; una pequeña y brillante, como cercana")]
    [SerializeField] private Vector2 nebulaBrightness = new Vector2(0.45f, 0.7f);

    [Header("Objetos lejanos")]
    [Tooltip("Los PNG de Art/Backgrounds/objects")]
    [SerializeField] private Sprite[] objects;
    [SerializeField] private Vector2Int objectsPerMap = new Vector2Int(1, 3);
    [Tooltip("Profundidad: el más lejano posible y el más cercano posible")]
    [SerializeField] private Vector2 objectFollow = new Vector2(0.92f, 0.8f);
    [Tooltip("Escala del más lejano y del más cercano")]
    [SerializeField] private Vector2 objectScale = new Vector2(0.3f, 1f);
    [Tooltip("Brillo del más lejano y del más cercano")]
    [SerializeField] private Vector2 objectBrightness = new Vector2(0.5f, 1f);
    [Tooltip("Color hacia el que se apagan los objetos lejanos: la luz que llega de muy lejos " +
             "llega más débil y más fría. Los cercanos conservan su color")]
    [SerializeField] private Color depthHaze = new Color(0.32f, 0.36f, 0.5f, 1f);
    [Tooltip("Separación mínima entre objetos, en unidades")]
    [SerializeField] private float objectSpacing = 4f;

    const string LayerNebula = "BG_Nebula";
    const string LayerObjects = "BG_Objects";

    class Piece
    {
        public SpriteRenderer sr;
        public Vector2 local;
        public float follow;
    }

    readonly List<Piece> nebulas = new List<Piece>();
    readonly List<Piece> things = new List<Piece>();
    readonly Dictionary<int, Sprite> loaded = new Dictionary<int, Sprite>();
    System.Random rng;
    GameDirector director;
    bool generated, warned;

    void OnEnable()
    {
        director = FindFirstObjectByType<GameDirector>();
        if (director != null) director.LevelStarted += OnLevelStarted;
    }

    void OnDisable()
    {
        if (director != null) director.LevelStarted -= OnLevelStarted;
    }

    void Start() { if (!generated) Generate(); }

    void OnLevelStarted(int level) => Generate();

    [ContextMenu("Generar otro fondo")]
    public void Generate()
    {
        generated = true;
        rng = new System.Random(System.Environment.TickCount ^ (GetInstanceID() * 31) ^ Random.Range(0, int.MaxValue));
        var report = new System.Text.StringBuilder();

        // ---- nebulosas: elegir, cargar las nuevas y soltar las que ya no hacen falta
        int nNeb = nebulaCount > 0 ? Range(nebulasPerMap.x, nebulasPerMap.y) : 0;
        var pick = new List<int>();
        var order = Shuffled(Mathf.Max(0, nebulaCount));
        for (int i = 0; i < order.Length && pick.Count < nNeb; i++) pick.Add(order[i] + 1);

        bool released = false;
        var keys = new List<int>(loaded.Keys);
        foreach (int k in keys)
            if (!pick.Contains(k)) { loaded.Remove(k); released = true; }

        // Sin sprite al apagarse: si no, la nebulosa vieja seguiría referenciada y no saldría de memoria
        foreach (var p in nebulas) { p.sr.sprite = null; p.sr.gameObject.SetActive(false); }
        Rect nr = LayerRect(nebulaFollow);
        int used = 0;
        foreach (int id in pick)
        {
            var sprite = LoadNebula(id);
            if (sprite == null) continue;
            var piece = Get(nebulas, used++, LayerNebula);
            piece.sr.sprite = sprite;
            piece.sr.sortingOrder = used;
            piece.follow = nebulaFollow;
            piece.local = new Vector2(Range(nr.xMin, nr.xMax), Range(nr.yMin, nr.yMax));
            var t = piece.sr.transform;
            t.localScale = Vector3.one * Range(nebulaScale.x, nebulaScale.y);
            t.localRotation = Quaternion.Euler(0f, 0f, Range(0f, 360f));
            piece.sr.flipX = rng.NextDouble() < 0.5;
            piece.sr.flipY = rng.NextDouble() < 0.5;
            float nb = Range(nebulaBrightness.x, nebulaBrightness.y);
            piece.sr.color = new Color(nb, nb, nb, Range(nebulaAlpha.x, nebulaAlpha.y));
            report.Append(nebulaPrefix).Append(id.ToString("00")).Append(' ');
        }

        // Lo que ya no tiene referencias (las nebulosas del mapa anterior) sale de memoria
        if (released) Resources.UnloadUnusedAssets();

        // ---- objetos lejanos
        foreach (var p in things) p.sr.gameObject.SetActive(false);
        int nObj = objects != null && objects.Length > 0 ? Range(objectsPerMap.x, objectsPerMap.y) : 0;
        var objOrder = Shuffled(objects != null ? objects.Length : 0);
        var placed = new List<Vector2>();
        int count = 0;
        for (int i = 0; i < objOrder.Length && count < nObj; i++)
        {
            var sprite = objects[objOrder[i]];
            if (sprite == null) continue;

            float follow = Range(Mathf.Min(objectFollow.x, objectFollow.y), Mathf.Max(objectFollow.x, objectFollow.y));
            Rect r = LayerRect(follow);
            Vector2 at = Vector2.zero;
            bool ok = false;
            for (int attempt = 0; attempt < 20 && !ok; attempt++)
            {
                at = new Vector2(Range(r.xMin, r.xMax), Range(r.yMin, r.yMax));
                ok = true;
                foreach (var q in placed) if (Vector2.Distance(q, at) < objectSpacing) { ok = false; break; }
            }
            if (!ok) continue;
            placed.Add(at);

            // 0 = lo más lejos posible, 1 = lo más cerca
            float near = Mathf.InverseLerp(objectFollow.x, objectFollow.y, follow);
            var piece = Get(things, count++, LayerObjects);
            piece.sr.sprite = sprite;
            piece.sr.sortingOrder = Mathf.RoundToInt(near * 100f);
            piece.follow = follow;
            piece.local = at;
            piece.sr.transform.localScale = Vector3.one * Mathf.Lerp(objectScale.x, objectScale.y, near) * Range(0.85f, 1.15f);
            piece.sr.transform.localRotation = Quaternion.identity;   // la luz del dibujo no se gira
            piece.sr.flipX = rng.NextDouble() < 0.5;
            float b = Mathf.Lerp(objectBrightness.x, objectBrightness.y, near);
            Color c = Color.Lerp(depthHaze, Color.white, near) * b;
            c.a = 1f;
            piece.sr.color = c;
            report.Append(sprite.name).Append(' ');
        }

        Place(CameraPos());
        Debug.Log($"BackdropDirector: {report}");
    }

    Sprite LoadNebula(int id)
    {
        if (loaded.TryGetValue(id, out var s) && s != null) return s;
        string path = $"{nebulaFolder}/{nebulaPrefix}{id:00}";
        s = Resources.Load<Sprite>(path);
        if (s == null)
        {
            if (!warned)
            {
                warned = true;
                Debug.LogWarning($"BackdropDirector: no encuentro '{path}' en Resources. Mueve la " +
                                 $"carpeta de nebulosas a Assets/Resources/{nebulaFolder} (desde Unity, " +
                                 "para que no se rompan las referencias).", this);
            }
            return null;
        }
        loaded[id] = s;
        return s;
    }

    void LateUpdate()
    {
        if (generated) Place(CameraPos());
    }

    void Place(Vector2 cam)
    {
        foreach (var p in nebulas) if (p.sr.gameObject.activeSelf) p.sr.transform.position = cam * p.follow + p.local;
        foreach (var p in things)  if (p.sr.gameObject.activeSelf) p.sr.transform.position = cam * p.follow + p.local;
    }

    // Lo que la cámara puede llegar a ver de una capa recorriendo el mundo entero. Las piezas
    // se colocan dentro, así siempre hay alguna posibilidad de cruzárselas
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
        Vector2 half = Vector2.Max(Vector2.zero, area.size - screen) * 0.5f * k + screen * 0.4f;
        return new Rect(area.center * k - half, half * 2f);
    }

    Piece Get(List<Piece> list, int index, string layer)
    {
        while (list.Count <= index)
        {
            var go = new GameObject(layer == LayerNebula ? "Nebula" : "FarObject");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = layer;
            list.Add(new Piece { sr = sr });
        }
        var p = list[index];
        p.sr.gameObject.SetActive(true);
        return p;
    }

    Vector2 CameraPos()
    {
        if (CameraFollow.Instance != null) return CameraFollow.Instance.transform.position;
        var c = Camera.main;
        return c != null ? (Vector2)c.transform.position : Vector2.zero;
    }

    float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    int Range(int a, int b) => rng.Next(Mathf.Min(a, b), Mathf.Max(a, b) + 1);

    int[] Shuffled(int n)
    {
        var a = new int[n];
        for (int i = 0; i < n; i++) a[i] = i;
        for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
        return a;
    }
}