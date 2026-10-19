using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Se ejecuta antes que los demás Start para que el área esté lista cuando otros la consulten
[DefaultExecutionOrder(-50)]
public class GameBounds : MonoBehaviour
{
    public static GameBounds Instance { get; private set; }

    [Header("Referencias")]
    [SerializeField] private Camera cam;
    [SerializeField] private PhysicsMaterial2D wallMaterial;

    [Header("Zonas de la interfaz que el jugador no puede pisar")]
    [Tooltip("RectTransforms bajo la palanca y los botones (sin imagen). Se vuelven obstáculos invisibles")]
    [SerializeField] private RectTransform[] blockedZones;
    [Tooltip("Evita que el jugador entre bajo el notch o las barras del sistema")]
    [SerializeField] private bool respectSafeArea = true;

    [Header("Tamaño del mundo")]
    [Tooltip("Cuántas pantallas mide el mundo a lo ancho (x) y a lo alto (y). 1 × 1 = el " +
             "juego de antes, con cámara fija. 2 × 2 = cuatro pantallas de área")]
    [SerializeField] private Vector2 worldScreens = new Vector2(2f, 2f);

    [Header("Paredes")]
    [SerializeField] private float wallThickness = 2f;
    [SerializeField] private float padding = 0f;

    [Header("Esquina de la meta (fracciones del área jugable)")]
    [Tooltip("x, y = esquina inferior izquierda de la zona; w, h = tamaño. Valores entre 0 y 1")]
    [SerializeField] private Rect goalCornerNormalized = new Rect(0.80f, 0.55f, 0.15f, 0.35f);

    /// Área jugable en unidades de mundo (dentro de las paredes). Es el MUNDO entero, no
    /// lo que se ve: con un mundo de 2 × 2 pantallas, mide cuatro pantallas.
    public Rect Area { get; private set; }
    /// ¿El mundo es más grande que la pantalla? Entonces la cámara se mueve.
    public bool IsExpanded => worldScreens.x > 1.01f || worldScreens.y > 1.01f;
    /// Tamaño de una pantalla en unidades, sin el zoom de los impactos.
    public Vector2 ScreenSize { get; private set; }
    /// Todo lo que ve la cámara.
    public Rect CameraRect { get; private set; }
    /// Esquina donde aparecerá el agujero negro.
    public Rect GoalCorner { get; private set; }
    public IReadOnlyList<Rect> BlockedRects => blockedRects;
    public bool IsReady { get; private set; }
    /// La meta está en el lado contrario al habitual (nivel espejado)
    public bool GoalMirrored { get; private set; }

    bool goalZoneSet;
    BoxCollider2D top, bottom, left, right;
    BoxCollider2D[] blockedColliders;
    readonly List<Rect> blockedRects = new List<Rect>();
    readonly Vector3[] corners = new Vector3[4];
    int lastW, lastH;
    Rect lastSafe;
    Vector2 worldCenter;
    bool centerCaptured;

    void Awake()
    {
        Instance = this;
        if (cam == null) cam = Camera.main;

        top    = CreateWall("Wall_Top");
        bottom = CreateWall("Wall_Bottom");
        left   = CreateWall("Wall_Left");
        right  = CreateWall("Wall_Right");

        int n = blockedZones != null ? blockedZones.Length : 0;
        blockedColliders = new BoxCollider2D[n];
        for (int i = 0; i < n; i++)
        {
            blockedColliders[i] = CreateWall("Blocked_" + i);
            // Nace APAGADO, y esto no es cautela de más. Un BoxCollider2D recién creado mide
            // 1x1 y se queda en la posición del padre. Si la ranura del array está vacía
            // (pasa en cuanto rehaces el Canvas: las referencias se rompen pero el hueco se
            // queda), nadie lo coloca nunca y te deja un muro invisible de 1x1 plantado en
            // mitad del campo, en la capa Walls, sin sprite que lo delate. Solo se enciende
            // cuando hay una zona de verdad que medir.
            blockedColliders[i].enabled = false;
        }
    }

    // Start (y no Awake) para que CameraAspectFitter ya haya fijado el tamaño de la cámara
    void Start()
    {
        Rebuild();
        StartCoroutine(RebuildNextFrame());   // el Canvas termina de calcular su escala en el primer frame
    }

    IEnumerator RebuildNextFrame()
    {
        yield return null;
        Rebuild();
    }

    // El mundo solo se rehace si cambia la PANTALLA (rotar, otra resolución). Antes también
    // se rehacía al cambiar el tamaño de la cámara, y el zoom de los impactos lo cambia: con
    // un mundo grande, cada golpe habría movido las paredes.
    void LateUpdate()
    {
        if (Screen.width != lastW || Screen.height != lastH || Screen.safeArea != lastSafe)
            Rebuild();
        else
            RefreshCamera();
    }

    /// Vuelve a medir lo que ve la cámara. La llama CameraFollow cada vez que se mueve, para
    /// que quien pregunte en el mismo frame (los que nacen, los que se reciclan) no lea la
    /// posición del frame anterior.
    public void RefreshCamera()
    {
        if (cam == null) return;
        float h = cam.orthographicSize * 2f;
        float w = h * cam.aspect;
        Vector2 c = cam.transform.position;
        CameraRect = new Rect(c.x - w / 2f, c.y - h / 2f, w, h);
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
        lastW = Screen.width;
        lastH = Screen.height;
        lastSafe = Screen.safeArea;

        RefreshCamera();
        float h = CameraRect.height;
        float w = CameraRect.width;
        ScreenSize = new Vector2(w, h);

        // El centro del mundo es donde está la cámara la primera vez, y ya no se mueve. Si
        // siguiera a la cámara, el mundo viajaría con el jugador.
        if (!centerCaptured)
        {
            worldCenter = CameraRect.center;
            centerCaptured = true;
        }

        // Márgenes del sistema (notch, esquinas redondeadas, barra de gestos), en unidades de mundo
        float insetL = 0f, insetR = 0f, insetT = 0f, insetB = 0f;
        if (respectSafeArea && Screen.width > 0 && Screen.height > 0)
        {
            Rect sa = Screen.safeArea;
            float ux = w / Screen.width, uy = h / Screen.height;
            insetL = sa.xMin * ux;
            insetR = (Screen.width - sa.xMax) * ux;
            insetB = sa.yMin * uy;
            insetT = (Screen.height - sa.yMax) * uy;
        }

        float sx = Mathf.Max(1f, worldScreens.x), sy = Mathf.Max(1f, worldScreens.y);
        float ww = w * sx, wh = h * sy;
        Rect world = new Rect(worldCenter.x - ww / 2f, worldCenter.y - wh / 2f, ww, wh);

        // Los márgenes del sistema solo importan en el borde del mundo, que es donde la
        // cámara se detiene y el borde de la pantalla coincide con la pared
        Area = new Rect(world.xMin + insetL + padding,
                        world.yMin + insetB + padding,
                        Mathf.Max(1f, ww - insetL - insetR - 2f * padding),
                        Mathf.Max(1f, wh - insetT - insetB - 2f * padding));

        float t = wallThickness;
        Place(top,    new Vector2(Area.center.x, Area.yMax + t / 2f), new Vector2(Area.width + 2f * t, t));
        Place(bottom, new Vector2(Area.center.x, Area.yMin - t / 2f), new Vector2(Area.width + 2f * t, t));
        Place(left,   new Vector2(Area.xMin - t / 2f, Area.center.y), new Vector2(t, Area.height + 2f * t));
        Place(right,  new Vector2(Area.xMax + t / 2f, Area.center.y), new Vector2(t, Area.height + 2f * t));

        ApplyGoalCorner();

        RebuildBlockedZones();
        IsReady = true;
    }

    /// La meta va RÍO ARRIBA. Si la corriente del nivel va al revés, la esquina se refleja
    /// en horizontal (nunca en vertical: abajo están la palanca y los botones).
    public void SetGoalMirrored(bool mirrored)
    {
        GoalMirrored = mirrored;
        if (IsReady || Area.width > 0f) ApplyGoalCorner();
    }

    /// La meta ya eligió su sitio (mundo amplio): esta es la zona que el generador debe dejar
    /// libre de estáticos para que no quede encerrada.
    public void SetGoalZone(Rect zone)
    {
        GoalCorner = zone;
        goalZoneSet = true;
    }

    void ApplyGoalCorner()
    {
        if (goalZoneSet) return;
        var g = goalCornerNormalized;
        float x = GoalMirrored ? 1f - g.x - g.width : g.x;
        GoalCorner = new Rect(Area.xMin + x * Area.width, Area.yMin + g.y * Area.height,
                              g.width * Area.width, g.height * Area.height);
    }

    void RebuildBlockedZones()
    {
        blockedRects.Clear();
        if (blockedZones == null) return;

        // Con la cámara siguiendo al jugador, las zonas bajo los botones ya no están en un
        // sitio fijo del mundo: el jugador nunca se queda debajo de un botón porque la cámara
        // lo lleva consigo. Se apagan para no dejar muros invisibles por el mapa.
        if (IsExpanded)
        {
            foreach (var c in blockedColliders) if (c != null) c.enabled = false;
            return;
        }

        Canvas.ForceUpdateCanvases();
        for (int i = 0; i < blockedZones.Length; i++)
        {
            var col = blockedColliders[i];
            if (col == null) continue;

            var rt = blockedZones[i];

            // Ranura vacía, u oculta por el esquema de control: no hay nada que bloquear.
            // Apagarlo, no dejarlo donde estaba — si no, el muro de los botones de giro
            // sobrevive al cambio a joystick y el jugador choca con algo que ya no se ve.
            if (rt == null || !rt.gameObject.activeInHierarchy)
            {
                col.enabled = false;
                continue;
            }

            // Con el Canvas en Screen Space - Overlay, las esquinas vienen en píxeles de pantalla
            rt.GetWorldCorners(corners);
            Vector2 a = ScreenToWorld(corners[0]);
            Vector2 b = ScreenToWorld(corners[2]);
            var r = new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                             Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));

            // Una zona que ocupa casi todo el campo no es una zona de control: es la del
            // joystick, que ahora es de pantalla completa. Bloquearla encerraría al jugador
            // en una caja del tamaño del campo, sin salida y sin nada visible que lo explique.
            if (r.width > Area.width * 0.6f && r.height > Area.height * 0.6f)
            {
                Debug.LogWarning("GameBounds: la zona bloqueada " + i + " (" + rt.name + ") cubre " +
                                 "casi todo el campo. Se ignora — con el joystick flotante la " +
                                 "zona táctil es la pantalla entera y no debe bloquear nada.", rt);
                col.enabled = false;
                continue;
            }

            col.enabled = true;
            blockedRects.Add(r);
            Place(col, r.center, r.size);
        }
    }

    Vector2 ScreenToWorld(Vector3 screen)
    {
        float z = Mathf.Abs(cam.transform.position.z);
        return cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, z));
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

    /// ¿Cae el punto dentro de alguna zona de controles (con margen extra)?
    public bool IsBlocked(Vector2 p, float margin = 0f)
    {
        foreach (var r in blockedRects)
        {
            var e = new Rect(r.xMin - margin, r.yMin - margin, r.width + 2f * margin, r.height + 2f * margin);
            if (e.Contains(p)) return true;
        }
        return false;
    }

    public Vector2 RandomGoalPoint() =>
        new Vector2(Random.Range(GoalCorner.xMin, GoalCorner.xMax),
                    Random.Range(GoalCorner.yMin, GoalCorner.yMax));

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || !IsReady) return;

        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireCube(CameraRect.center, CameraRect.size);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Area.center, Area.size);
        Gizmos.color = Color.red;
        foreach (var r in blockedRects) Gizmos.DrawWireCube(r.center, r.size);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireCube(GoalCorner.center, GoalCorner.size);
    }
}