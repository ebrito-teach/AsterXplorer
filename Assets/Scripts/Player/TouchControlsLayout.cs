using UnityEngine;

/// Monta la pantalla según el esquema de control elegido, y la cambia de lado si el jugador
/// lo pide.
///
/// Sin esto habría que tener tres UIs distintas o dejar botones muertos a la vista. Cada
/// esquema enseña solo lo suyo:
///
///   Joystick     → el stick. Y ya. Un único mando en pantalla.
///   Inclinacion  → empuje y preciso. El giro lo lleva el móvil, así que no hay palanca.
///   Botones      → empuje, preciso y los dos de giro.
///
/// Esa es la razón de cambiar de esquema: cada uno **quita** botones, no los añade. Lo que
/// sobra en pantalla en un juego donde tienes que mirar los asteroides no es neutral — tapa
/// y distrae.
///
/// Va en el Canvas de los controles.
public class TouchControlsLayout : MonoBehaviour
{
    [Header("Entrada")]
    [SerializeField] private ShipInput input;
    [SerializeField] private VirtualJoystick joystick;

    [Header("Grupos que se muestran u ocultan")]
    [Tooltip("La zona del joystick de dos ejes")]
    [SerializeField] private GameObject joystickGroup;
    [Tooltip("Botón de empuje. No hace falta en modo joystick: ahí el stick ya es el movimiento")]
    [SerializeField] private GameObject thrustButton;
    [Tooltip("Botón de modo preciso. Está en los tres esquemas")]
    [SerializeField] private GameObject focusButton;
    [Tooltip("Los dos botones de giro. Solo en el esquema de botones")]
    [SerializeField] private GameObject turnButtons;
    [Tooltip("La palanca de un eje de antes, si la conservas. Se oculta siempre: el joystick " +
             "la sustituye y en modo nave gira el móvil")]
    [SerializeField] private GameObject legacyLever;

    [Header("Lado")]
    [Tooltip("Los mandos de movimiento a la izquierda. Lo lee del guardado si hay GameProgress")]
    [SerializeField] private bool controlsOnLeft = true;
    [SerializeField] private bool sideFromSettings = true;
    [Tooltip("Grupos que van al LADO DEL MOVIMIENTO, junto al joystick: los botones de giro. " +
             "En el esquema de botones, girar ES el movimiento, así que va con ese pulgar")]
    [SerializeField] private RectTransform[] movementGroups;
    [Tooltip("Grupos que van al lado CONTRARIO: empuje y modo preciso. Con el pulgar del " +
             "movimiento ocupado, el otro tiene que llegar sin cruzar la pantalla")]
    [SerializeField] private RectTransform[] actionGroups;
    [Range(0.05f, 0.45f)] [SerializeField] private float edgeMargin = 0.22f;
    [Tooltip("Separación entre empuje y preciso cuando van juntos (esquema de botones), en " +
             "fracción de pantalla. Lado a lado y no apilados: el pulgar rueda de uno al otro " +
             "sin levantarse, que apilados obliga a estirarlo")]
    [Range(0.06f, 0.25f)] [SerializeField] private float actionPairGap = 0.13f;

    RectTransform canvasRect;

    void Start()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvasRect = canvas.rootCanvas.transform as RectTransform;

        if (input == null) input = FindFirstObjectByType<ShipInput>();
        if (input == null) { Debug.LogWarning("TouchControlsLayout: no hay ShipInput.", this); return; }

        if (sideFromSettings && GameProgress.Instance != null)
            controlsOnLeft = GameProgress.Instance.Data.settings.controlsOnLeft;

        Apply();
    }

    /// Lo llama Ajustes al cambiar el esquema o el lado, para que se vea sin reiniciar.
    public void Apply()
    {
        var scheme = input.CurrentScheme;

        bool stickMode = scheme == ShipInput.Scheme.Joystick;

        Show(joystickGroup, stickMode);
        Show(thrustButton, !stickMode);
        Show(turnButtons, scheme == ShipInput.Scheme.Botones);
        Show(focusButton, true);
        Show(legacyLever, false);

        if (joystick != null) joystick.SetSide(controlsOnLeft, edgeMargin);

        float movementX = controlsOnLeft ? edgeMargin : 1f - edgeMargin;
        float actionX = controlsOnLeft ? 1f - edgeMargin : edgeMargin;
        Place(movementGroups, movementX);
        Place(actionGroups, actionX);

        // Con el stick, el focus se coloca solo frente al pulgar que pilota. En los esquemas
        // de nave vuelve a un sitio fijo, que ahí sí funciona porque la otra mano está en el
        // empuje y no se mueve.
        var floating = focusButton != null ? focusButton.GetComponent<FloatingFocus>() : null;
        if (floating != null) floating.enabled = stickMode;
        if (stickMode) return;

        // Altura común para los dos, la del empuje. Apilados dentro de ActionGroup cada uno
        // tenía la suya, y al separarlos esa diferencia se ve como un botón caído.
        float? y = LocalY(thrustButton);

        if (scheme == ShipInput.Scheme.Inclinacion)
        {
            // Inclinando no hay botones de giro: el pulgar del movimiento está libre, así que
            // empuje y preciso se reparten uno por mano.
            PlaceOne(thrustButton, actionX, y);
            PlaceOne(focusButton, movementX, y);
        }
        else
        {
            // Con botones ese pulgar ya lleva los dos de giro, así que los dos van al mismo
            // lado pero EN FILA, no apilados: el empuje fuera, donde descansa el pulgar, y el
            // preciso hacia dentro, a un rodamiento de distancia.
            float inward = actionX < 0.5f ? actionPairGap : -actionPairGap;
            PlaceOne(thrustButton, actionX, y);
            PlaceOne(focusButton, actionX + inward, y);
        }
    }

    /// Altura del objeto en el espacio del Canvas, para igualar la de otro.
    float? LocalY(GameObject go)
    {
        if (go == null || canvasRect == null) return null;
        var rt = go.transform as RectTransform;
        if (rt == null) return null;
        return canvasRect.InverseTransformPoint(rt.position).y;
    }

    /// Coloca un botón suelto en la fracción horizontal x de la PANTALLA, conservando su altura.
    ///
    /// Pasa por el rect del Canvas en vez de tocar anclas porque las anclas se miden contra el
    /// rect del padre: si el botón cuelga de un grupo intermedio, esas fracciones no son la
    /// pantalla sino ese recuadro, y el botón acaba en cualquier parte. Así da igual de quién
    /// cuelgue y no hay que reorganizar la jerarquía.
    void PlaceOne(GameObject go, float x, float? forcedY = null)
    {
        if (go == null || canvasRect == null) return;
        var rt = go.transform as RectTransform;
        if (rt == null) return;

        Rect cr = canvasRect.rect;
        float ly = forcedY ?? canvasRect.InverseTransformPoint(rt.position).y;
        float lx = (x - canvasRect.pivot.x) * cr.width;
        rt.position = canvasRect.TransformPoint(new Vector2(lx, ly));
    }

    static void Place(RectTransform[] groups, float x)
    {
        if (groups == null) return;
        foreach (var rt in groups)
        {
            if (rt == null) continue;
            // Solo se mueve en horizontal: la altura de cada grupo la decides tú en la escena
            rt.anchorMin = rt.anchorMax = new Vector2(x, rt.anchorMin.y);
            rt.anchoredPosition = new Vector2(0f, rt.anchoredPosition.y);
        }
    }

    public void SetControlsOnLeft(bool left)
    {
        controlsOnLeft = left;
        if (GameProgress.Instance != null)
        {
            GameProgress.Instance.Data.settings.controlsOnLeft = left;
            GameProgress.Instance.Save();
        }
        Apply();
    }

    public void SetScheme(int schemeIndex)
    {
        var s = (ShipInput.Scheme)Mathf.Clamp(schemeIndex, 0, 2);
        input.SetScheme(s);
        if (GameProgress.Instance != null)
        {
            GameProgress.Instance.Data.settings.control = (ControlScheme)s;
            GameProgress.Instance.Save();
        }
        Apply();
    }

    static void Show(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }
}