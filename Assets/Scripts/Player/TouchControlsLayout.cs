using UnityEngine;

/// Monta la pantalla según el esquema de control elegido, y la cambia de lado si el jugador
/// lo pide.
///
/// Sin esto habría que tener tres UIs distintas o dejar botones muertos a la vista. Cada
/// esquema enseña solo lo suyo:
///
///   Joystick     → el stick. Y ya. Un único mando en pantalla.
///   TiltShip     → empuje y preciso. El giro lo lleva el móvil, así que no hay palanca.
///   ButtonsShip  → empuje, preciso y los dos de giro.
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

    void Start()
    {
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

        Show(joystickGroup, scheme == ShipInput.Scheme.Joystick);
        Show(thrustButton, scheme != ShipInput.Scheme.Joystick);
        Show(turnButtons, scheme == ShipInput.Scheme.ButtonsShip);
        Show(focusButton, true);
        Show(legacyLever, false);

        if (joystick != null) joystick.SetSide(controlsOnLeft, edgeMargin);

        float movementX = controlsOnLeft ? edgeMargin : 1f - edgeMargin;
        float actionX = controlsOnLeft ? 1f - edgeMargin : edgeMargin;
        Place(movementGroups, movementX);
        Place(actionGroups, actionX);
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
