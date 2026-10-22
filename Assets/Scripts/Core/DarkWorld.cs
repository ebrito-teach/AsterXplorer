using UnityEngine;

/// Mundo vacío y oscuro: no se ve nada salvo una pequeña luz alrededor del jugador.
///
/// Todo sigue ahí (estrellas, nebulosas, rocas, la meta): solo se tapa. Dentro de la luz del
/// jugador se ve con normalidad. La meta no se ve hasta que el jugador entra en su alcance de
/// atracción, y aun entonces se ve poco.
///
/// Cómo: una capa negra por encima de todo el mundo (por debajo de la interfaz) con un
/// agujero suave donde está el jugador. La capa es una textura pequeña que se recalcula en
/// cada fotograma y se estira a la pantalla; al estirarla, los bordes de la luz salen suaves
/// solos. No necesita shaders ni luces 2D.
///
/// Va en un objeto vacío de la escena. Lo enciende GameDirector en los niveles oscuros.
[DefaultExecutionOrder(200)]   // después de la cámara (100): usa su posición final
public class DarkWorld : MonoBehaviour
{
    public static DarkWorld Instance { get; private set; }

    [Header("Luz del jugador")]
    [Tooltip("Radio de la luz alrededor del jugador, en unidades: lo que se ve")]
    [SerializeField] private float visionRadius = 1.5f;
    [Tooltip("Ancho del borde suave de la luz, en unidades")]
    [SerializeField] private float softEdge = 0.6f;
    [Tooltip("Oscuridad fuera de la luz (1 = negro total)")]
    [Range(0.5f, 1f)] [SerializeField] private float darkness = 1f;
    [Tooltip("Un leve parpadeo de la luz, como una linterna (0 = fija)")]
    [Range(0f, 0.2f)] [SerializeField] private float flicker = 0.04f;

    [Header("Meta")]
    [Tooltip("Cuánto se deja ver la meta cuando el jugador está en su alcance de atracción " +
             "(0 = nada, 1 = del todo)")]
    [Range(0f, 1f)] [SerializeField] private float goalReveal = 0.3f;
    [Tooltip("Radio que se descubre alrededor de la meta, en radios de su sombra negra")]
    [SerializeField] private float goalRevealRadius = 2.5f;

    [Header("Calidad")]
    [Tooltip("Ancho de la textura de oscuridad en píxeles. Más = borde más preciso y más coste")]
    [SerializeField] private int resolution = 160;
    [SerializeField] private string sortingLayer = "Player";
    [SerializeField] private int sortingOrder = 30000;

    /// Lo que ve el jugador. Se puede cambiar en juego (por ejemplo, desde un buff futuro)
    public float VisionRadius { get => visionRadius; set => visionRadius = Mathf.Max(0f, value); }
    public bool On { get; private set; }

    SpriteRenderer overlay;
    Texture2D tex;
    Color32[] px;
    int w, h;
    PlayerController player;
    Goal goal;
    GameDirector director;
    float goalAmount;

    void Awake() => Instance = this;

    void OnEnable()
    {
        director = FindFirstObjectByType<GameDirector>();
        if (director != null) director.LevelStarted += OnLevelStarted;
    }

    void OnDisable()
    {
        if (director != null) director.LevelStarted -= OnLevelStarted;
        SetOn(false);
    }

    void OnLevelStarted(int level) => SetOn(director != null && director.IsDarkWorld);

    public void SetOn(bool on)
    {
        On = on;
        if (on) Build();
        if (overlay != null) overlay.enabled = on;
        goalAmount = 0f;
    }

    void Build()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (goal == null) goal = FindFirstObjectByType<Goal>();
        var cam = Camera.main;
        if (cam == null) return;

        int nw = Mathf.Clamp(resolution, 32, 512);
        int nh = Mathf.Max(16, Mathf.RoundToInt(nw / Mathf.Max(0.5f, cam.aspect)));
        if (tex == null || nw != w || nh != h)
        {
            w = nw; h = nh;
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "DarkWorld"
            };
            px = new Color32[w * h];
        }
        if (overlay == null)
        {
            var go = new GameObject("DarkWorldOverlay");
            go.transform.SetParent(transform, false);
            overlay = go.AddComponent<SpriteRenderer>();
            overlay.sortingLayerName = sortingLayer;
            overlay.sortingOrder = sortingOrder;
        }
        overlay.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
    }

    void LateUpdate()
    {
        if (!On || overlay == null || tex == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        // Cubre lo que ve la cámara con un poco de margen (por el temblor de los golpes)
        const float margin = 1.12f;
        float viewH = CameraView.HalfHeight(cam) * 2f * margin;
        float viewW = viewH * cam.aspect;
        Vector3 c = cam.transform.position;
        overlay.transform.position = new Vector3(c.x, c.y, 0f);
        overlay.transform.rotation = Quaternion.identity;
        overlay.transform.localScale = new Vector3(viewW / w, viewH / h, 1f);

        float unitPerPx = viewW / w;
        Vector2 origin = new Vector2(c.x - viewW * 0.5f, c.y - viewH * 0.5f);

        // Luz del jugador
        Vector2 pp = player != null ? (Vector2)player.transform.position : (Vector2)c;
        if (player != null && player.Visual != null) pp = player.Visual.position;
        bool playerLit = player != null && !player.IsDead;
        float flick = 1f + (flicker > 0f ? (Mathf.PerlinNoise(Time.time * 6f, 0.3f) - 0.5f) * 2f * flicker : 0f);
        float r0 = Mathf.Max(0f, visionRadius * flick);
        float soft = Mathf.Max(0.01f, softEdge);

        // Meta: solo dentro de su alcance de atracción, y poco
        bool goalNear = false;
        Vector2 gp = Vector2.zero;
        float gr = 0f;
        if (goal != null && goal.Active && player != null)
        {
            gp = goal.transform.position;
            goalNear = Vector2.Distance(pp, gp) <= goal.PullRadius;
            gr = goal.CoreRadius * goalRevealRadius;
        }
        goalAmount = Mathf.MoveTowards(goalAmount, goalNear ? 1f : 0f, Time.deltaTime / 1.2f);

        byte full = (byte)Mathf.RoundToInt(255f * darkness);
        for (int y = 0; y < h; y++)
        {
            float wy = origin.y + (y + 0.5f) * (viewH / h);
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float wx = origin.x + (x + 0.5f) * unitPerPx;
                float a = 1f;
                if (playerLit)
                {
                    float dx = wx - pp.x, dy = wy - pp.y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01((d - (r0 - soft)) / soft);
                    a = t * t * (3f - 2f * t);
                }
                if (goalAmount > 0f && gr > 0f)
                {
                    float dx = wx - gp.x, dy = wy - gp.y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01(d / gr);
                    float g = 1f - goalReveal * goalAmount * (1f - t * t);
                    if (g < a) a = g;
                }
                px[row + x] = new Color32(0, 0, 0, (byte)(full * a));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false);
    }

    [ContextMenu("Encender ahora (prueba)")] void TestOn() => SetOn(true);
    [ContextMenu("Apagar ahora (prueba)")] void TestOff() => SetOn(false);
}
