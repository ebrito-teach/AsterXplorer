using UnityEngine;

/// La cámara sigue al jugador y se adelanta hacia donde va.
///
/// Va en un objeto padre (CameraRig) y la cámara cuelga de él. No mueve la cámara
/// directamente porque ImpactFeedback ya la mueve para el temblor: si los dos tocaran la
/// misma posición, se pisarían. Así el padre viaja y la hija tiembla encima.
///
/// LA ANTICIPACIÓN
///
/// Se adelanta en proporción a la velocidad: quieto, el jugador queda en el centro; a
/// crucero, la cámara enseña más de lo que tiene delante y menos de lo que deja atrás. Es lo
/// que hace justa la velocidad letal: lo que te puede matar entra en pantalla con tiempo.
/// En horizontal el adelanto puede ser mayor que en vertical, porque la pantalla es ancha.
///
/// Se suaviza en dos capas: el adelanto cambia despacio (un giro brusco no sacude la vista)
/// y la cámara persigue ese punto algo más rápido (no se queda atrás).
[DefaultExecutionOrder(100)]
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("Referencias")]
    [Tooltip("Vacío = busca el PlayerController")]
    [SerializeField] private Transform target;
    [Tooltip("Vacío = la cámara hija de este objeto")]
    [SerializeField] private Camera cam;

    [Header("Anticipación")]
    [Tooltip("Segundos de velocidad que se adelanta. 0.6 a crucero (5 u/s) = 3 unidades")]
    [SerializeField] private float lookAheadTime = 0.6f;
    [Tooltip("Adelanto máximo en unidades, en horizontal (x) y en vertical (y)")]
    [SerializeField] private Vector2 maxLookAhead = new Vector2(4f, 2f);
    [Tooltip("Lo que tarda el adelanto en cambiar. Más alto = gira la vista más despacio")]
    [SerializeField] private float lookAheadSmooth = 0.35f;

    [Header("Seguimiento")]
    [Tooltip("Lo que tarda la cámara en alcanzar su punto. Más bajo = más pegada")]
    [SerializeField] private float followSmooth = 0.12f;

    [Header("Límites")]
    [Tooltip("La cámara nunca enseña lo que hay fuera del mundo")]
    [SerializeField] private bool clampToWorld = true;

    /// Velocidad real de la cámara (unidades/s). La usa el FlowField para meter asteroides
    /// también por el lado hacia el que viaja la vista.
    public Vector2 Velocity { get; private set; }

    Rigidbody2D targetRb;
    Vector2 lookAhead, lookAheadVel, posVel;

    void Awake()
    {
        Instance = this;
        if (cam == null) cam = GetComponentInChildren<Camera>();
        if (cam == null) Debug.LogError("CameraFollow: no hay ninguna cámara hija.", this);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start() => FindTarget();

    void FindTarget()
    {
        if (target == null)
        {
            var p = FindFirstObjectByType<PlayerController>();
            if (p != null) target = p.transform;
        }
        if (target != null) targetRb = target.GetComponent<Rigidbody2D>();
    }

    void LateUpdate()
    {
        var gb = GameBounds.Instance;
        if (gb == null || !gb.IsExpanded) return;
        if (target == null) { FindTarget(); if (target == null) return; }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector2 vel = targetRb != null ? targetRb.linearVelocity : Vector2.zero;
        Vector2 want = new Vector2(
            Mathf.Clamp(vel.x * lookAheadTime, -maxLookAhead.x, maxLookAhead.x),
            Mathf.Clamp(vel.y * lookAheadTime, -maxLookAhead.y, maxLookAhead.y));
        lookAhead = Vector2.SmoothDamp(lookAhead, want, ref lookAheadVel, lookAheadSmooth, Mathf.Infinity, dt);

        Vector2 goal = Clamp((Vector2)target.position + lookAhead);
        Vector2 pos = Vector2.SmoothDamp(transform.position, goal, ref posVel, followSmooth, Mathf.Infinity, dt);
        pos = Clamp(pos);

        Velocity = (pos - (Vector2)transform.position) / dt;
        transform.position = new Vector3(pos.x, pos.y, transform.position.z);
        gb.RefreshCamera();
    }

    /// Salta al jugador sin recorrido. Al empezar un nivel: si no, la cámara cruzaría el mapa
    /// desde donde estaba, y los asteroides nacerían alrededor de una vista que ya no vale.
    public void Snap()
    {
        var gb = GameBounds.Instance;
        if (target == null) FindTarget();
        if (target == null || gb == null) return;

        lookAhead = lookAheadVel = posVel = Velocity = Vector2.zero;
        Vector2 pos = gb.IsExpanded ? Clamp(target.position) : (Vector2)transform.position;
        transform.position = new Vector3(pos.x, pos.y, transform.position.z);
        gb.RefreshCamera();
    }

    // El centro de la cámara, metido para que el borde de la vista no pase de la pared
    Vector2 Clamp(Vector2 p)
    {
        var gb = GameBounds.Instance;
        if (!clampToWorld || gb == null || cam == null) return p;

        Rect w = gb.Area;
        float halfH = gb.ScreenSize.y * 0.5f;
        float halfW = gb.ScreenSize.x * 0.5f;

        p.x = w.width  > 2f * halfW ? Mathf.Clamp(p.x, w.xMin + halfW, w.xMax - halfW) : w.center.x;
        p.y = w.height > 2f * halfH ? Mathf.Clamp(p.y, w.yMin + halfH, w.yMax - halfH) : w.center.y;
        return p;
    }
}