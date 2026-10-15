using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

/// Joystick de dos ejes. Sustituye a la palanca de uno solo.
///
/// En modo direccional este es el único mando de movimiento que hay: no hacen falta botones
/// de dirección, porque el stick ya da las cuatro (y las diagonales, que en un esquivador son
/// la mitad de las jugadas).
///
/// TRES DECISIONES QUE IMPORTAN MÁS DE LO QUE PARECE
///
/// **Flotante.** El stick aparece donde pones el dedo. Un joystick clavado en un sitio obliga
/// a mirar la pantalla para encontrarlo, y en un juego donde no puedes apartar la vista ni
/// medio segundo eso es un problema de verdad.
///
/// **Toda la pantalla.** La zona sensible es la pantalla entera, no media. Que el pulgar
/// tenga que caer en una mitad concreta es una regla que el jugador no ve y no entiende: solo
/// nota que a veces no responde. Los botones siguen funcionando porque se dibujan por encima
/// y el raycaster se queda con el de arriba.
///
/// **Acotado.** El anillo nunca se sale de la pantalla, por cerca del borde que toques.
///
/// Va en un RectTransform que cubra la zona táctil, CON un Image de alfa 0 y Raycast Target
/// marcado: sin un Graphic, el raycaster no puede verlo y no llega ni un evento.
public class VirtualJoystick : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [System.Serializable] public class Vector2Event : UnityEvent<Vector2> { }

    [Header("Referencias")]
    [Tooltip("La base del stick. Se mueve al dedo si está en modo flotante")]
    [SerializeField] private RectTransform ring;
    [Tooltip("La perilla, hija del anillo")]
    [SerializeField] private RectTransform handle;
    [Tooltip("La zona táctil. Vacío = este mismo objeto. NECESITA un Image con alfa 0 y " +
             "Raycast Target marcado, o el raycaster no puede verlo")]
    [SerializeField] private RectTransform zone;

    [Header("Comportamiento")]
    [Tooltip("El stick aparece donde pones el dedo. Recomendado: así no hay que mirar para " +
             "encontrarlo, y en este juego no puedes apartar la vista")]
    [SerializeField] private bool floating = true;
    [Tooltip("La zona sensible es la pantalla ENTERA. Obligar al pulgar a caer en una mitad " +
             "concreta es una regla invisible: el jugador no la entiende, solo nota que a " +
             "veces no responde. Los botones siguen ganando porque se dibujan por encima")]
    [SerializeField] private bool fullScreenZone = true;
    [Tooltip("Impide que el anillo se salga por los bordes al tocar cerca de uno")]
    [SerializeField] private bool keepRingOnScreen = true;
    [Tooltip("Recorrido del dedo hasta el máximo, en unidades del Canvas. Como el Canvas " +
             "Scaler ya escala con la pantalla, este número NO hay que volver a escalarlo")]
    [SerializeField] private float radius = 120f;
    [Range(0f, 0.4f)] [SerializeField] private float deadZone = 0.12f;
    [Tooltip("Lo rápido que vuelve al centro al soltar")]
    [SerializeField] private float returnSpeed = 10f;

    [Header("Aspecto")]
    [Tooltip("Opacidad en reposo. Un stick a media tinta estorba menos y sigue viéndose")]
    [Range(0f, 1f)] [SerializeField] private float idleAlpha = 0.35f;
    [Range(0f, 1f)] [SerializeField] private float activeAlpha = 0.85f;
    [SerializeField] private float fadeSpeed = 8f;
    [SerializeField] private Sprite handleNormal;
    [SerializeField] private Sprite handleActive;
    [Tooltip("Oculta el stick del todo mientras no se usa. Para quien quiera la pantalla limpia")]
    [SerializeField] private bool hideWhenIdle = false;

    [SerializeField] private Vector2Event onValueChanged;

    public Vector2 Value { get; private set; }
    public bool Held { get; private set; }

    CanvasGroup group;
    Image handleImage;
    Vector2 rawValue;
    Vector2 homePosition;
    int pointerId = -1;

    void Awake()
    {
        if (zone == null) zone = transform as RectTransform;
        if (ring == null) ring = zone;

        group = ring.GetComponent<CanvasGroup>();
        if (group == null) group = ring.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;      // los toques los recoge la zona, no el dibujo

        if (handle != null)
        {
            handleImage = handle.GetComponent<Image>();
            if (handleImage != null) handleImage.preserveAspect = true;
        }

        if (zone.GetComponent<Graphic>() == null)
            Debug.LogWarning("VirtualJoystick: la zona no tiene ningún Image. Sin un Graphic " +
                             "con Raycast Target, el raycaster no puede verla y el stick no " +
                             "recibe ni un toque.", this);

        homePosition = ring.anchoredPosition;
        Apply(Vector2.zero);
        group.alpha = hideWhenIdle ? 0f : idleAlpha;
    }

    float Radius() => Mathf.Max(10f, radius);

    public void OnPointerDown(PointerEventData e)
    {
        if (Held) return;                  // un solo dedo manda
        Held = true;
        pointerId = e.pointerId;

        if (floating) MoveRingTo(e);

        SetActiveVisual(true);
        UpdateFrom(e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.pointerId != pointerId) return;
        UpdateFrom(e);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (e.pointerId != pointerId) return;
        Held = false;
        pointerId = -1;
        SetActiveVisual(false);
        if (floating) ring.anchoredPosition = homePosition;
    }

    /// Lleva el anillo bajo el dedo, sin que se salga de la zona.
    ///
    /// Cuidado con los dos sistemas de coordenadas, que es donde esto fallaba: el punto que
    /// devuelve ScreenPointToLocalPointInRectangle va medido desde el PIVOTE DE LA ZONA,
    /// mientras que anchoredPosition se mide desde el ANCLA DEL ANILLO. Asignar uno al otro
    /// directamente desplaza el anillo justo la distancia que separa esos dos puntos — por eso
    /// aparecía corrido y a veces medio fuera de la pantalla.
    void MoveRingTo(PointerEventData e)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                zone, e.position, e.pressEventCamera, out Vector2 local)) return;

        Rect zr = zone.rect;

        if (keepRingOnScreen)
        {
            // El centro del anillo se queda a media anchura de cada borde, así que el dibujo
            // entero cabe. Si la zona fuera más pequeña que el anillo, se centra y ya.
            float hw = ring.rect.width * 0.5f;
            float hh = ring.rect.height * 0.5f;
            local.x = (zr.width >= ring.rect.width)
                    ? Mathf.Clamp(local.x, zr.xMin + hw, zr.xMax - hw) : zr.center.x;
            local.y = (zr.height >= ring.rect.height)
                    ? Mathf.Clamp(local.y, zr.yMin + hh, zr.yMax - hh) : zr.center.y;
        }

        // Dónde cae el ancla del anillo dentro de la zona, en ese mismo espacio local
        Vector2 anchorPos = new Vector2(zr.xMin + ring.anchorMin.x * zr.width,
                                        zr.yMin + ring.anchorMin.y * zr.height);
        ring.anchoredPosition = local - anchorPos;
    }

    void UpdateFrom(PointerEventData e)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                ring, e.position, e.pressEventCamera, out Vector2 local)) return;

        // El punto local va contra el PIVOTE del anillo; restando su centro, el cero es
        // siempre el medio del dibujo sea cual sea el pivote que le hayas puesto
        Vector2 fromCenter = local - ring.rect.center;
        Apply(Vector2.ClampMagnitude(fromCenter / Radius(), 1f));
    }

    void Update()
    {
        if (!Held && rawValue.sqrMagnitude > 0.000001f)
            Apply(Vector2.MoveTowards(rawValue, Vector2.zero, returnSpeed * Time.unscaledDeltaTime));

        float wantAlpha = Held ? activeAlpha : (hideWhenIdle ? 0f : idleAlpha);
        group.alpha = Mathf.MoveTowards(group.alpha, wantAlpha, fadeSpeed * Time.unscaledDeltaTime);
    }

    void Apply(Vector2 v)
    {
        rawValue = v;
        if (handle != null) handle.anchoredPosition = v * Radius();

        float m = v.magnitude;
        // Reescalado tras la zona muerta: al salir de ella el mando empieza en cero en vez
        // de pegar un salto al 12%
        Value = m < deadZone
              ? Vector2.zero
              : v.normalized * Mathf.InverseLerp(deadZone, 1f, m);

        onValueChanged?.Invoke(Value);
    }

    void SetActiveVisual(bool active)
    {
        if (handleImage == null) return;
        if (handleNormal != null && handleActive != null)
            handleImage.sprite = active ? handleActive : handleNormal;
    }

    /// Lo llama el gestor de controles cuando el jugador cambia el lado en Ajustes.
    ///
    /// Con la zona a pantalla completa, el lado ya no decide dónde se puede tocar — se toca
    /// en cualquier parte — sino dónde DESCANSA el anillo cuando nadie lo usa.
    public void SetSide(bool left, float marginFromEdge = 0.22f)
    {
        if (zone == null) return;

        if (fullScreenZone)
        {
            zone.anchorMin = Vector2.zero;
            zone.anchorMax = Vector2.one;
        }
        else
        {
            zone.anchorMin = left ? new Vector2(0f, 0f) : new Vector2(0.5f, 0f);
            zone.anchorMax = left ? new Vector2(0.5f, 1f) : new Vector2(1f, 1f);
        }
        zone.offsetMin = zone.offsetMax = Vector2.zero;

        if (ring == null || ring == zone) return;

        // El margen se mide sobre la mitad que le toca, vaya la zona a media pantalla o entera
        float x = fullScreenZone
                ? (left ? marginFromEdge * 0.5f : 1f - marginFromEdge * 0.5f)
                : (left ? marginFromEdge : 1f - marginFromEdge);

        ring.anchorMin = ring.anchorMax = new Vector2(x, 0.34f);
        ring.anchoredPosition = Vector2.zero;
        homePosition = Vector2.zero;
    }

    void OnDisable()
    {
        Held = false;
        pointerId = -1;
        SetActiveVisual(false);
        Apply(Vector2.zero);
        if (floating && ring != null) ring.anchoredPosition = homePosition;
    }
}