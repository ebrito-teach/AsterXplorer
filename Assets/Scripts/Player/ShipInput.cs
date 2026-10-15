using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// La entrada, separada del control. El PlayerController no sabe si esto viene del
/// acelerómetro, de un joystick en pantalla o del teclado, y así cambiar de esquema no le
/// afecta en nada.
///
/// TRES ESQUEMAS, Y CADA UNO PIDE LO SUYO
///
///   Joystick    — stick de dos ejes y nada más. El movimiento es directo: vas a donde
///                 apuntas. Un solo mando en pantalla.
///   TiltShip    — inclinas el móvil para girar y un botón para empujar. La palanca
///                 desaparece, que en modo nave era lo incómodo.
///   ButtonsShip — igual que el anterior pero girando con dos botones, para quien no
///                 quiera inclinar o juegue tumbado.
///
/// El modo preciso va aparte en los tres y comparte botón con el freno mientras no tenga uno
/// propio.
///
/// El cableado viejo sigue funcionando: `SetSteer` de la `HorizontalLever` entra como giro y
/// como eje X, así que puedes probar los esquemas nuevos sin desmontar la UI que ya tienes.
public class ShipInput : MonoBehaviour
{
    public enum Scheme
    {
        /// Stick de dos ejes. Movimiento directo.
        Joystick = 0,
        /// Inclinar para girar + botón de empuje.
        TiltShip = 1,
        /// Dos botones para girar + botón de empuje.
        ButtonsShip = 2,
    }

    [Header("Esquema")]
    [SerializeField] private Scheme scheme = Scheme.Joystick;
    [Tooltip("Lo lee del guardado al arrancar, si hay GameProgress en la escena. Desmárcalo " +
             "para forzar el esquema de arriba mientras pruebas")]
    [SerializeField] private bool schemeFromSettings = true;

    [Header("Inclinación")]
    [SerializeField] private TiltInput tilt;

    [Header("Modo preciso")]
    [Tooltip("El botón de freno activa también el modo preciso. Desmárcalo el día que le " +
             "pongas botón propio")]
    [SerializeField] private bool brakeAlsoFocuses = true;

    /// Dirección pedida, -1..1 en cada eje. La usa el modo direccional.
    public Vector2 Move { get; private set; }
    /// Giro pedido, -1 izquierda ... 1 derecha. La usa el modo nave.
    public float Turn { get; private set; }
    public bool ThrustHeld { get; private set; }
    public bool BrakeHeld { get; private set; }
    /// Modo preciso: menos velocidad, casi nada de deriva, hitbox resaltado
    public bool FocusHeld { get; private set; }

    /// Compatibilidad con lo que ya estaba cableado
    public float Steer => Turn;

    public Scheme CurrentScheme => scheme;
    public bool UsesTilt => scheme == Scheme.TiltShip;
    public bool UsesJoystick => scheme == Scheme.Joystick;

    Vector2 uiMove;
    float uiTurn;
    bool uiThrust, uiBrake, uiFocus;
    bool uiTurnLeft, uiTurnRight;

    // --- los llama la UI táctil ---
    /// Joystick de dos ejes
    public void SetMove(Vector2 v) => uiMove = Vector2.ClampMagnitude(v, 1f);
    /// Palanca de un eje (la de antes): entra como giro y como eje X
    public void SetSteer(float value) => uiTurn = Mathf.Clamp(value, -1f, 1f);
    public void SetThrust(bool held) => uiThrust = held;
    public void SetBrake(bool held) => uiBrake = held;
    public void SetFocus(bool held) => uiFocus = held;
    public void SetTurnLeft(bool held) => uiTurnLeft = held;
    public void SetTurnRight(bool held) => uiTurnRight = held;

    public void SetScheme(Scheme s) => scheme = s;

    void Start()
    {
        if (tilt == null) tilt = FindFirstObjectByType<TiltInput>();

        if (schemeFromSettings && GameProgress.Instance != null)
            scheme = (Scheme)GameProgress.Instance.Data.settings.control;

        // Si pide inclinación y el aparato no tiene acelerómetro, no lo dejamos sin control
        if (scheme == Scheme.TiltShip && tilt == null)
        {
            Debug.LogWarning("ShipInput: esquema de inclinación pero no hay TiltInput en la " +
                             "escena. Se usan los botones de giro.");
            scheme = Scheme.ButtonsShip;
        }
    }

    void Update()
    {
        float kbX = 0f, kbY = 0f;
        bool kbThrust = false, kbBrake = false, kbFocus = false;

#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null)
        {
            kbX = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f)
                - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            kbY = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f)
                - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
            kbThrust = k.wKey.isPressed || k.upArrowKey.isPressed;
            kbBrake = k.sKey.isPressed || k.downArrowKey.isPressed || k.spaceKey.isPressed;
            kbFocus = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        kbX = Input.GetAxisRaw("Horizontal");
        kbY = Input.GetAxisRaw("Vertical");
        kbThrust = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        kbBrake = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.Space);
        kbFocus = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif

        ThrustHeld = uiThrust || kbThrust;
        BrakeHeld = uiBrake || kbBrake;
        FocusHeld = uiFocus || kbFocus || (brakeAlsoFocuses && BrakeHeld);

        switch (scheme)
        {
            case Scheme.Joystick:
                // El stick manda; el teclado y la palanca vieja sirven de respaldo
                Vector2 kb = new Vector2(kbX, kbY);
                Move = uiMove.sqrMagnitude > 0.0001f ? uiMove
                     : kb.sqrMagnitude > 0.0001f ? Vector2.ClampMagnitude(kb, 1f)
                     : new Vector2(uiTurn, 0f);
                Turn = Move.x;
                break;

            case Scheme.TiltShip:
                // Inclinar gira. El empuje es el botón, y así en pantalla solo quedan dos
                Turn = tilt != null && tilt.Available ? tilt.Value : kbX;
                if (Mathf.Abs(Turn) < 0.001f && Mathf.Abs(uiTurn) > 0.001f) Turn = uiTurn;
                Move = new Vector2(Turn, 0f);
                break;

            default: // ButtonsShip
                float buttons = (uiTurnRight ? 1f : 0f) - (uiTurnLeft ? 1f : 0f);
                Turn = Mathf.Abs(buttons) > 0.001f ? buttons
                     : Mathf.Abs(kbX) > 0.001f ? kbX
                     : uiTurn;
                Move = new Vector2(Turn, 0f);
                break;
        }
    }
}
