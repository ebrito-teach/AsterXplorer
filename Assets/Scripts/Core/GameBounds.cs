using UnityEngine;

// Se ejecuta antes que los demás Start para que el área esté lista cuando otros la consulten
[DefaultExecutionOrder(-50)]
public class GameBounds : MonoBehaviour
{
    public static GameBounds Instance { get; private set; }

    [Header("Referencias")]
    [SerializeField] private Camera cam;
    [SerializeField] private PhysicsMaterial2D wallMaterial;

    [Header("Zonas de la interfaz que NO son área jugable (opcional)")]
    [Tooltip("Su borde SUPERIOR marca dónde empieza el área jugable (zona de controles)")]
    [SerializeField] private RectTransform bottomReserved;
    [Tooltip("Su borde INFERIOR marca dónde termina el área jugable (zona del HUD)")]
    [SerializeField] private RectTransform topReserved;

    [Header("Paredes")]
    [SerializeField] private float wallThickness = 2f;
    [SerializeField] private float padding = 0f;

    /// Área jugable en unidades de mundo (dentro de las paredes).
    public Rect Area { get; private set; }
    /// Todo lo que ve la cámara.
    public Rect CameraRect { get; private set; }
    public bool IsReady { get; private set; }

    BoxCollider2D top, bottom, left, right;
    float lastSize, lastAspect;
    int lastW, lastH;
    Rect lastSafe;
    readonly Vector3[] corners = new Vector3[4];

    void Awake()
    {
        Instance = this;
        if (cam == null) cam = Camera.main;
        top    = CreateWall("Wall_Top");
        bottom = CreateWall("Wall_Bottom");
        left   = CreateWall("Wall_Left");
        right  = CreateWall("Wall_Right");
    }

    // Start (y no Awake) para que CameraAspectFitter ya haya fijado el tamaño de la cámara
    void Start() { Rebuild(); }

    void LateUpdate()
    {
        if (!Mathf.Approximately(cam.orthographicSize, lastSize) ||
            !Mathf.Approximately(cam.aspect, lastAspect) ||
            Screen.width != lastW || Screen.height != lastH ||
            Screen.safeArea != lastSafe)
            Rebuild();
    }

    BoxCollider2D CreateWall(string wallName)
    {
        var go = new GameObject(wallName);
        go.transform.SetParent(transform, false);
        int layer = LayerMask.NameToLayer("Walls");
        if (layer < 0) Debug.LogError("Falta la capa 'Walls' en Tags and Layers.");
        else go.layer = layer;
        var col = go.AddComponent<BoxCollider2D>();
        col.sharedMaterial = wallMaterial;
        return col;
    }

    void Rebuild()
    {
        lastSize = cam.orthographicSize;
        lastAspect = cam.aspect;
        lastW = Screen.width;
        lastH = Screen.height;
        lastSafe = Screen.safeArea;

        float h = cam.orthographicSize * 2f;
        float w = h * cam.aspect;
        Vector2 c = cam.transform.position;

        float xMin = c.x - w / 2f, xMax = c.x + w / 2f;
        float yMin = c.y - h / 2f, yMax = c.y + h / 2f;
        CameraRect = new Rect(xMin, yMin, w, h);

        // Recorta el área jugable según las zonas de interfaz reservadas
        Canvas.ForceUpdateCanvases();
        if (bottomReserved != null)
            yMin = Mathf.Max(yMin, ScreenToWorldY(EdgeScreenY(bottomReserved, true)));
        if (topReserved != null)
            yMax = Mathf.Min(yMax, ScreenToWorldY(EdgeScreenY(topReserved, false)));

        float areaH = Mathf.Max(0.5f, (yMax - yMin) - 2f * padding);
        Area = new Rect(xMin + padding, yMin + padding, w - 2f * padding, areaH);

        float t = wallThickness;
        Place(top,    new Vector2(Area.center.x, Area.yMax + t / 2f), new Vector2(Area.width + 2f * t, t));
        Place(bottom, new Vector2(Area.center.x, Area.yMin - t / 2f), new Vector2(Area.width + 2f * t, t));
        Place(left,   new Vector2(Area.xMin - t / 2f, Area.center.y), new Vector2(t, Area.height + 2f * t));
        Place(right,  new Vector2(Area.xMax + t / 2f, Area.center.y), new Vector2(t, Area.height + 2f * t));

        IsReady = true;
    }

    // Coordenada Y en píxeles de pantalla del borde superior (o inferior) de un RectTransform.
    // Con el Canvas en Screen Space - Overlay, las esquinas ya vienen en píxeles.
    float EdgeScreenY(RectTransform rt, bool topEdge)
    {
        rt.GetWorldCorners(corners);        // 0=abajo-izq, 1=arriba-izq
        return topEdge ? corners[1].y : corners[0].y;
    }

    float ScreenToWorldY(float screenY)
    {
        float z = Mathf.Abs(cam.transform.position.z);
        return cam.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f, screenY, z)).y;
    }

    static void Place(BoxCollider2D col, Vector2 pos, Vector2 size)
    {
        col.transform.position = pos;
        col.size = size;
    }

    /// Punto dentro del área jugable: (0,0) = esquina inferior izquierda, (1,1) = superior derecha.
    public Vector2 PointInArea(float nx, float ny) =>
        new Vector2(Mathf.Lerp(Area.xMin, Area.xMax, nx), Mathf.Lerp(Area.yMin, Area.yMax, ny));

    public bool Contains(Vector2 p, float margin = 0f) =>
        p.x > Area.xMin + margin && p.x < Area.xMax - margin &&
        p.y > Area.yMin + margin && p.y < Area.yMax - margin;

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireCube(CameraRect.center, CameraRect.size);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Area.center, Area.size);
    }
}