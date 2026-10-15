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
/// DOS DECISIONES QUE IMPORTAN MÁS DE LO QUE PARECE
///
/// **Flotante.** El stick aparece donde pones el dedo, dentro de su zona. Un joystick clavado
/// en un sitio obliga a mirar la pantalla para encontrarlo, y en un juego donde no puedes
/// apartar la vista ni medio segundo eso es un problema de verdad. Con el flotante apoyas el
/// pulgar donde te caiga y ya estás centrado.
///
/// **Lado elegible.** Zurdos y diestros sujetan el móvil distinto. Es un booleano y evita que
/// media base de jugadores juegue incómoda.
///
/// Va en un RectTransform que cubra la ZONA donde se puede apoyar el pulgar (media pantalla,
/// no el dibujo del stick). El dibujo se mueve dentro.
public class VirtualJoystick : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [System.Serializable] public class Vector2Event : UnityEvent<Vector2> { }

    [Header("Referencias")]
    [Tooltip("La base del stick. Se mueve al dedo si está en modo flotante")]
    [SerializeField] private RectTransform ring;
    [Tooltip("La perilla, hija del anillo")]
    [SerializeField] private RectTransform handle;
    [Tooltip("La zona donde se puede apoyar el pulgar. Vacío = este mismo objeto")]
    [SerializeField] private RectTransform zone;

    [Header("Comportamiento")]
    [Tooltip("El stick aparece donde pones el dedo. Recomendado: así no hay que mirar para " +
             "encontrarlo, y en este juego no puedes apartar la vista")]
    [SerializeField] private bool floating = true;
    [Tooltip("Radio en píxeles para llegar al máximo. Con pantallas distintas conviene que " +
             "sea proporcional, así que se escala con la altura de la zona")]
    [SerializeField] private float radius = 120f;
    [SerializeField] private bool scaleRadiusWithScreen = true;
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

        homePosition = ring.anchoredPosition;
        Apply(Vector2.zero);
        group.alpha = hideWhenIdle ? 0f : idleAlpha;
    }

    float Radius()
    {
        if (!scaleRadiusWithScreen) return Mathf.Max(10f, radius);
        // Proporcional a la pantalla: el mismo gesto en un móvil pequeño y en una tableta
        float reference = 1080f;
        return Mathf.Max(10f, radius * (Screen.height / reference));
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (Held) return;                  // un solo dedo manda
        Held = true;
        pointerId = e.pointerId;

        if (floating && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                zone, e.position, e.pressEventCamera, out Vector2 local))
            ring.anchoredPosition = local;

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
    public void SetSide(bool left, float marginFromEdge = 0.22f)
    {
        var rt = zone;
        if (rt == null) return;
        // Media pantalla del lado elegido
        rt.anchorMin = left ? new Vector2(0f, 0f) : new Vector2(0.5f, 0f);
        rt.anchorMax = left ? new Vector2(0.5f, 1f) : new Vector2(1f, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        if (ring != null && ring != zone)
        {
            ring.anchorMin = ring.anchorMax = new Vector2(left ? marginFromEdge : 1f - marginFromEdge, 0.34f);
            ring.anchoredPosition = Vector2.zero;
            homePosition = Vector2.zero;
        }
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
