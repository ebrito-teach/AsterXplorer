using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// El botón de modo preciso, en modo joystick: aparece en el lado contrario al pulgar que
/// lleva el stick.
///
/// EL PROBLEMA QUE RESUELVE
///
/// Con el stick flotante el jugador pone el pulgar donde quiere, así que un botón clavado en
/// una esquina fija acaba cayendo mal la mitad de las veces: o lo tapa la otra mano, o hay que
/// estirar el pulgar justo cuando no puedes mirar la pantalla. Y si lo pones donde no estorbe,
/// está lejos.
///
/// En cuanto el stick se agarra, el botón sale reflejado al otro lado y a la misma altura.
/// Esté donde esté la mano que pilota, la otra lo tiene justo debajo.
///
/// DOS DETALLES QUE LO HACEN UTILIZABLE
///
/// **La zona sensible es mayor que el dibujo**, vía `raycastPadding` en negativo. En un juego
/// donde no puedes apartar la vista, pedir puntería para pulsar un botón es pedir que falles.
/// Se ve pequeño y discreto; se pulsa como una zona.
///
/// **Mientras no se ve, no bloquea raycasts.** Si los bloqueara, el primer toque de la partida
/// podría caer encima y el stick no llegaría a aparecer nunca.
///
/// Va en el objeto del botón de focus, cuelgue de donde cuelgue: la posición se calcula en
/// coordenadas del Canvas y se escribe como posición de mundo, así que un padre intermedio
/// (un ActionGroup, pongamos) no la desvía. En los esquemas de nave se apaga solo y el botón
/// vuelve exactamente a donde lo dejaste.
[RequireComponent(typeof(RectTransform))]
public class FloatingFocus : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler
{
    [Header("Referencias")]
    [Tooltip("El stick al que se refleja. Vacío = lo busca en la escena")]
    [SerializeField] private VirtualJoystick joystick;
    [Tooltip("Quien recibe el focus. Vacío = lo busca en la escena")]
    [SerializeField] private ShipInput input;
    [Tooltip("El dibujo del botón. Vacío = el Image de este objeto")]
    [SerializeField] private Image art;
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite activeSprite;

    [Header("Colocación")]
    [Tooltip("Margen mínimo contra el borde, en fracción de pantalla, para que no quede " +
             "pegado al canto donde el pulgar no llega bien")]
    [Range(0.05f, 0.4f)] [SerializeField] private float edgeMargin = 0.18f;
    [Tooltip("Cuánto más grande es la zona táctil que el dibujo. 1.8 = casi el doble. " +
             "No se ve y se nota muchísimo")]
    [Range(1f, 3f)] [SerializeField] private float touchPadding = 1.8f;

    [Header("Aparición")]
    [SerializeField] private float fadeSpeed = 12f;
    [Range(0f, 1f)] [SerializeField] private float shownAlpha = 0.8f;

    public bool Held { get; private set; }

    RectTransform self;
    RectTransform canvasRect;
    CanvasGroup group;
    int pointerId = -1;
    Vector2 homeAnchorMin, homeAnchorMax, homePosition;

    void Awake()
    {
        self = (RectTransform)transform;

        if (joystick == null) joystick = FindFirstObjectByType<VirtualJoystick>();
        if (input == null) input = FindFirstObjectByType<ShipInput>();
        if (art == null) art = GetComponent<Image>();

        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvasRect = canvas.rootCanvas.transform as RectTransform;

        // Anclas y posición originales: este componente solo manda en modo joystick, y en los
        // de nave hay que devolver el botón exactamente donde lo colocaste tú
        homeAnchorMin = self.anchorMin;
        homeAnchorMax = self.anchorMax;
        homePosition = self.anchoredPosition;

        if (art != null)
        {
            if (idleSprite == null) idleSprite = art.sprite;

            // raycastPadding en NEGATIVO agranda la zona sensible sin tocar el dibujo ni el
            // tamaño del rect. Es la forma limpia de tener un botón pequeño y fácil de dar.
            Vector2 s = self.rect.size;
            float px = s.x * (touchPadding - 1f) * 0.5f;
            float py = s.y * (touchPadding - 1f) * 0.5f;
            art.raycastPadding = new Vector4(-px, -py, -px, -py);
            art.raycastTarget = true;
        }

        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
    }

    void Update()
    {
        bool want = joystick != null && (joystick.Held || Held);
        if (want) PlaceOppositeStick();

        group.alpha = Mathf.MoveTowards(group.alpha, want ? shownAlpha : 0f,
                                        fadeSpeed * Time.unscaledDeltaTime);
        group.blocksRaycasts = group.alpha > 0.05f;

        if (!want && Held) Release();
    }

    /// Refleja la posición del stick respecto al centro de la pantalla, a la misma altura.
    ///
    /// Se escribe como posición de MUNDO, no como anchoredPosition: las anclas se miden contra
    /// el rect del padre, así que si el botón cuelga de un grupo intermedio esas fracciones no
    /// son la pantalla sino ese recuadro, y el botón acaba en cualquier parte. Pasando por el
    /// rect del Canvas da igual de quién cuelgue.
    void PlaceOppositeStick()
    {
        if (canvasRect == null) return;

        Vector2 stick = joystick.RingViewportPosition;            // 0..1 sobre la pantalla
        float x = Mathf.Clamp(1f - stick.x, edgeMargin, 1f - edgeMargin);
        float y = Mathf.Clamp(stick.y, edgeMargin, 1f - edgeMargin);

        Rect cr = canvasRect.rect;
        Vector2 local = new Vector2((x - canvasRect.pivot.x) * cr.width,
                                    (y - canvasRect.pivot.y) * cr.height);
        self.position = canvasRect.TransformPoint(local);
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (Held) return;
        Held = true;
        pointerId = e.pointerId;
        if (input != null) input.SetFocus(true);
        SetArt(true);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (e.pointerId != pointerId) return;
        Release();
    }

    void Release()
    {
        Held = false;
        pointerId = -1;
        if (input != null) input.SetFocus(false);
        SetArt(false);
    }

    void SetArt(bool active)
    {
        if (art == null || idleSprite == null || activeSprite == null) return;
        art.sprite = active ? activeSprite : idleSprite;
    }

    /// En los esquemas de nave este componente se apaga y el botón vuelve a ser normal: hay
    /// que devolverlo visible y en su sitio. Si se quedara con el alfa a 0 que usa para
    /// esconderse, el botón de preciso desaparecería en inclinación y botones.
    void OnDisable()
    {
        if (Held) Release();

        if (group != null) { group.alpha = 1f; group.blocksRaycasts = true; }
        if (self != null)
        {
            self.anchorMin = homeAnchorMin;
            self.anchorMax = homeAnchorMax;
            self.anchoredPosition = homePosition;
        }
    }
}