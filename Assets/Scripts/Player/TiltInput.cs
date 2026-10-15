using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// Girar inclinando el móvil.
///
/// El gesto es el de un volante: sujetas el móvil en horizontal y lo giras sobre el eje que
/// sale de la pantalla. Inclinas a la derecha, apuntas a la derecha. Se lee proyectando la
/// gravedad sobre el eje horizontal DE LA PANTALLA, no del aparato — por eso hace falta saber
/// en qué orientación está, o en Landscape Right saldría al revés que en Landscape Left.
///
/// LO QUE HACE QUE UN CONTROL POR INCLINACIÓN SEA USABLE
///
/// No es la fórmula, es el **neutro**. Nadie sostiene el móvil plano: se sujeta inclinado unos
/// 30-45° hacia uno. Si das por hecho que el cero es el móvil horizontal, el jugador arranca
/// con el mando pegado a un lado y el control se siente roto desde el primer segundo. Por eso
/// esto captura su postura real al empezar y la usa como centro.
///
/// Y como la postura cambia (te recuestas, cambias de mano), se recalibra sola cuando detecta
/// que el jugador lleva un rato quieto en una posición distinta — sin que tenga que ir a
/// ningún menú.
///
/// Va en cualquier objeto de la escena; el ShipInput lo encuentra solo.
public class TiltInput : MonoBehaviour
{
    [Header("Sensibilidad")]
    [Tooltip("Grados de inclinación para llegar al máximo. 25 es cómodo de muñeca; con menos " +
             "de 15 cualquier temblor te mueve, con más de 40 hay que girar el móvil entero")]
    [Range(8f, 50f)] [SerializeField] private float fullTiltDegrees = 25f;
    [Tooltip("Por debajo de esto se considera centrado. Sin zona muerta es imposible ir recto")]
    [Range(0f, 0.3f)] [SerializeField] private float deadZone = 0.09f;
    [Tooltip("Suavizado. El acelerómetro es ruidoso y sin esto el astronauta vibra")]
    [SerializeField] private float smoothing = 0.06f;
    [Tooltip("Si el control va al revés en tu móvil, marca esto. No es un parche: hay " +
             "aparatos que informan el eje invertido")]
    [SerializeField] private bool invert = false;

    [Header("Calibración")]
    [Tooltip("Toma la postura del jugador al arrancar como centro, en vez de suponer que " +
             "sujeta el móvil plano")]
    [SerializeField] private bool calibrateOnStart = true;
    [SerializeField] private float calibrationTime = 0.4f;
    [Tooltip("Se recalibra sola si el jugador se queda quieto en otra postura este tiempo. " +
             "Así no hay que ir a ningún menú al cambiar de posición en el sofá")]
    [SerializeField] private bool autoRecalibrate = true;
    [SerializeField] private float restTimeToRecalibrate = 1.6f;
    [Tooltip("Cuánto puede moverse y seguir considerándose quieto")]
    [SerializeField] private float restTolerance = 0.035f;

    [Header("Diagnóstico")]
    [Tooltip("Escribe en consola lo que está leyendo. Para cuando no sepas si el problema es " +
             "el eje, el neutro o que el móvil no tiene acelerómetro")]
    [SerializeField] private bool logReadings = false;

    /// -1 inclinado a la izquierda ... 1 a la derecha. Ya con zona muerta y suavizado.
    public float Value { get; private set; }
    /// ¿Hay acelerómetro y está dando datos?
    public bool Available { get; private set; }
    public float Neutral => neutral;

    float neutral, smoothed, raw;
    float calibrateUntil;
    float restSince, restAnchor;
    bool calibrating;

    void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        if (Accelerometer.current != null) InputSystem.EnableDevice(Accelerometer.current);
#endif
        if (calibrateOnStart) Calibrate();
    }

    /// Toma la postura actual como centro. Llámalo desde Ajustes con un botón "Centrar".
    public void Calibrate()
    {
        calibrating = true;
        calibrateUntil = Time.unscaledTime + Mathf.Max(0.05f, calibrationTime);
    }

    void Update()
    {
        Vector3 a = ReadAcceleration();
        Available = a.sqrMagnitude > 0.01f;
        if (!Available)
        {
            Value = 0f;
            return;
        }

        raw = ScreenRight(a);
        if (invert) raw = -raw;

        // Mientras calibra, el centro persigue la postura real y el mando se queda a cero
        if (calibrating)
        {
            neutral = Mathf.Lerp(neutral, raw, 0.25f);
            if (Time.unscaledTime >= calibrateUntil) { calibrating = false; smoothed = 0f; }
            Value = 0f;
            return;
        }

        // sin(grados) es la componente de la gravedad a esa inclinación
        float span = Mathf.Max(0.05f, Mathf.Sin(fullTiltDegrees * Mathf.Deg2Rad));
        float target = Mathf.Clamp((raw - neutral) / span, -1f, 1f);

        float k = smoothing <= 0.001f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / smoothing);
        smoothed = Mathf.Lerp(smoothed, target, k);

        Value = Mathf.Abs(smoothed) < deadZone
              ? 0f
              // Reescalado tras la zona muerta: así justo al salir de ella el mando empieza
              // en cero y no da un salto
              : Mathf.Sign(smoothed) * Mathf.InverseLerp(deadZone, 1f, Mathf.Abs(smoothed));

        if (autoRecalibrate) TickAutoRecalibrate();

        if (logReadings)
            Debug.Log($"TiltInput: bruto {raw:0.000}  neutro {neutral:0.000}  valor {Value:0.00}  " +
                      $"orientación {Screen.orientation}");
    }

    /// Si el jugador lleva un rato quieto en una postura que no es el centro, esa pasa a ser
    /// la nueva postura cómoda. Evita el clásico "se me ha descentrado y no sé arreglarlo".
    void TickAutoRecalibrate()
    {
        if (Mathf.Abs(raw - restAnchor) > restTolerance)
        {
            restAnchor = raw;
            restSince = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - restSince < restTimeToRecalibrate) return;
        // Arrastra el centro despacio hacia donde se ha quedado, sin tirones
        neutral = Mathf.Lerp(neutral, raw, 0.02f);
    }

    /// Componente de la gravedad a lo largo del eje horizontal de la PANTALLA.
    ///
    /// El acelerómetro informa en ejes del aparato, que no giran con la orientación. Por eso
    /// hay que convertir: en Landscape Right y en Landscape Left el mismo gesto da signos
    /// opuestos, y sin esto el control iría al revés en la mitad de los móviles.
    static float ScreenRight(Vector3 a)
    {
        switch (Screen.orientation)
        {
            case ScreenOrientation.LandscapeLeft:      return -a.y;
            case ScreenOrientation.LandscapeRight:     return a.y;
            case ScreenOrientation.PortraitUpsideDown: return -a.x;
            default:                                   return a.x;   // Portrait
        }
    }

    static Vector3 ReadAcceleration()
    {
#if ENABLE_INPUT_SYSTEM
        var acc = Accelerometer.current;
        if (acc != null) return acc.acceleration.ReadValue();
        return Vector3.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.acceleration;
#else
        return Vector3.zero;
#endif
    }
}
