using UnityEngine;

/// El control del jugador.
///
/// EL CAMBIO DE FONDO
///
/// Antes la velocidad era algo que se ACUMULABA: el empuje sumaba, los choques sumaban, el
/// amortiguamiento del Rigidbody (0.25, o sea una constante de tiempo de CUATRO segundos) no
/// quitaba nada, y nadie limpiaba. Por eso un roce te mandaba a velocidad máxima en una
/// dirección que no habías elegido y te la comías media pantalla. Ese era el "empuje extraño".
///
/// Ahora la velocidad es algo que se DICTA. Cada paso de física el controlador calcula la
/// velocidad que pides y se acerca a ella con una constante de tiempo. Si sueltas, la pedida
/// es cero. Un empujón ya no puede quedarse pegado porque no hay nada que acumular.
///
/// EL HÍBRIDO
///
/// La precisión del bullet-hell no viene de la velocidad: viene de que **arrancas al
/// instante**. La sensación de masa viene de que **paras despacio**. Son dos números
/// distintos, y por eso se pueden tener las dos cosas:
///
///     accelTime 0.05  →  al 90% de tu velocidad en 0.12 s. Pulsas y ya estás yendo.
///     decelTime 0.20  →  sueltas y te deslizas ~1 unidad. Hay que anticipar.
///
/// decelTime es EL número del híbrido y se ajusta en diez segundos de prueba:
///
///     0.05  bullet-hell puro, paras en seco           (deriva 0.25)
///     0.20  el híbrido de salida                      (deriva 1.0)
///     0.50  nave espacial, flotas                     (deriva 2.5)
///
/// La deriva es a velocidad de crucero y en unidades de mundo; la pantalla mide 19.4 x 9.
///
/// LOS CHOQUES SIGUEN DOLIENDO
///
/// Dictar la velocidad tiene un riesgo: que los impactos no se sientan, porque el controlador
/// los pisaría al instante siguiente. Por eso el empujón vive en un vector APARTE que se suma
/// y decae solo. Te lanza de verdad, lo ves, y se te va en medio segundo en vez de en cuatro.
/// Durante el aturdimiento tu control vale cero y solo manda el empujón — que es justo lo que
/// tiene que pasar cuando te acaba de atropellar una roca de mil toneladas.
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    public enum ControlMode
    {
        /// La palanca gira al astronauta y el empuje le manda hacia donde mira. Mantiene tu
        /// esquema de controles y tu pose de empuje tal cual. Es por donde yo empezaría.
        ShipHybrid = 0,
        /// La palanca es el eje X y los botones el eje Y. Bullet-hell puro: te mueves hacia
        /// donde pides, sin girar. Más preciso, menos "astronauta a la deriva".
        Directional = 1,
    }

    [Header("Referencias")]
    [SerializeField] private ShipInput input;
    [SerializeField] private Transform visual;   // el hijo con el sprite (Pj)
    [SerializeField] private Animator animator;  // el Animator de Pj

    [Header("Control")]
    [SerializeField] private ControlMode controlMode = ControlMode.ShipHybrid;
    [Tooltip("El modo lo decide el esquema de entrada: joystick → direccional, inclinación o " +
             "botones → nave. Déjalo marcado o tendrás dos ajustes que pueden contradecirse")]
    [SerializeField] private bool followInputScheme = true;
    [Tooltip("Velocidad de crucero en unidades/s. La pantalla mide 19.4 x 9: a 5 u/s cruzas " +
             "en 3.9 s y subes de abajo arriba en 1.8")]
    [SerializeField] private float cruiseSpeed = 5f;
    [Tooltip("Constante de tiempo para ARRANCAR. Bajo = precisión de bullet-hell")]
    [SerializeField] private float accelTime = 0.05f;
    [Tooltip("Constante de tiempo para PARAR al soltar. Aquí vive la inercia que querías " +
             "conservar: 0.20 son unos 0.5 de deriva a velocidad de crucero")]
    [SerializeField] private float decelTime = 0.20f;

    [Header("Modo preciso (botón de freno)")]
    [Tooltip("Fracción de la velocidad mientras lo mantienes")]
    [Range(0.15f, 0.8f)] [SerializeField] private float focusSpeedFactor = 0.4f;
    [Tooltip("Al mantenerlo también se recorta la deriva: el astronauta se vuelve quirúrgico. " +
             "Eso convierte el freno en la herramienta más importante del juego")]
    [SerializeField] private float focusDecelTime = 0.07f;

    [Header("Giro con BOTONES")]
    [Tooltip("Grados por segundo mientras mantienes un botón de giro. 320 daba una vuelta " +
             "entera en 1.1 s: imposible parar el morro donde querías. Con 150 tardas 2.4 s " +
             "en dar la vuelta, que suena lento hasta que ves que nunca necesitas darla")]
    [SerializeField] private float turnSpeed = 150f;
    [Tooltip("Lo que tarda el giro en coger su velocidad. Sin esto un toque de botón pega un " +
             "tirón; con esto un toque es un empujoncito y mantener es un giro sostenido")]
    [SerializeField] private float turnAccelTime = 0.09f;
    [Tooltip("Cuánto se recorta el giro en modo preciso. Es lo que convierte el focus en " +
             "puntería de verdad y no solo en ir más despacio")]
    [Range(0.2f, 1f)] [SerializeField] private float focusTurnFactor = 0.45f;

    [Header("Giro por INCLINACIÓN")]
    [Tooltip("El ángulo del móvil ES el rumbo, no la velocidad de giro. Esto es el máximo " +
             "que se separa del eje de avance con el móvil del todo inclinado. Con 70 llegas " +
             "casi a vertical, y con el móvil plano el morro vuelve solo al frente")]
    [Range(25f, 90f)] [SerializeField] private float maxTiltFacing = 70f;
    [Tooltip("Tope de grados por segundo con que el morro persigue el ángulo que pides. " +
             "Es un LÍMITE, no un suavizado: mientras no lo alcance va a velocidad plena y " +
             "llega limpio. El temblor de la mano ya lo filtra TiltInput")]
    [SerializeField] private float tiltFollowRate = 540f;
    [Tooltip("En modo preciso el mismo gesto cubre menos ángulo: misma mano, más resolución")]
    [Range(0.2f, 1f)] [SerializeField] private float focusTiltFactor = 0.55f;

    [Tooltip("Suavizado del giro del sprite hacia la dirección de avance (modo direccional)")]
    [SerializeField] private float visualTurnSmoothing = 14f;

    [Header("Impactos")]
    [SerializeField] private float minImpactSpeed = 0.8f;
    [Tooltip("Cuánta velocidad del choque se convierte en empujón")]
    [Range(0f, 2f)] [SerializeField] private float knockScale = 0.9f;
    [Tooltip("Tope del empujón en unidades/s, para que una roca enorme no te mande a órbita")]
    [SerializeField] private float maxKnock = 7f;
    [Tooltip("Constante de tiempo con la que se te pasa el empujón. ANTES eran 4 segundos, " +
             "de ahí que los golpes te arrastraran media pantalla")]
    [SerializeField] private float knockDecayTime = 0.35f;
    [SerializeField] private float hardImpactSpeed = 2.0f;
    [Tooltip("Segundos sin control tras un golpe fuerte. Corto a propósito: perder el mando " +
             "es lo que hace que un empujón se sienta injusto")]
    [SerializeField] private float stunTime = 0.15f;

    [Header("Vida")]
    [Tooltip("Segundos de invulnerabilidad al empezar")]
    [SerializeField] private float startInvulnerability = 2f;
    [SerializeField] private float blinkRate = 4f;

    [Header("Orientación del sprite")]
    [Tooltip("Voltea el sprite según hacia dónde avanzas. Es lo que faltaba: el astronauta " +
             "miraba siempre a la derecha aunque la salida estuviera al otro lado")]
    [SerializeField] private bool flipSpriteWithDirection = true;
    [Tooltip("Velocidad horizontal mínima para cambiar de lado. Sin esto, parado o subiendo " +
             "en vertical, el sprite se voltearía sin parar con cualquier microdesvío")]
    [SerializeField] private float flipDeadzone = 0.35f;
    [Tooltip("Gira también el CUERPO hacia donde mira. En modo nave es lo natural; si tu " +
             "arte es de perfil y al girar queda raro, desmárcalo y deja solo el volteo")]
    [SerializeField] private bool rotateBodyToFacing = true;

    [Header("Pose del sprite")]
    [SerializeField] private float idleAngleOffset = 0f;
    [SerializeField] private float thrustAngleOffset = 45f;
    [SerializeField] private Vector2 thrustPositionOffset = new Vector2(-0.10f, -0.06f);
    [SerializeField] private float offsetSmoothing = 10f;
    [Tooltip("Solo para ajustar: fuerza la pose de empuje sin pulsar nada")]
    [SerializeField] private bool debugForceThrustPose = false;

    Rigidbody2D rb;
    SpriteRenderer visualRenderer;
    Vector2 controlVel;        // lo que tú pides
    Vector2 knock;             // lo que te hicieron; decae solo
    float facing = 90f;        // grados, hacia dónde mira el astronauta
    int facingSign = 1;        // 1 mira a la derecha, -1 a la izquierda
    float baseFacing;          // el eje de avance: hacia la meta. La inclinación se mide desde aquí
    float turnRate;            // grados/s actuales de los botones de giro, con su arranque
    float thrustLevel, brakeLevel, stunUntil, invulnerableUntil;

    public float ThrustLevel => thrustLevel;
    public float BrakeLevel => brakeLevel;
    public bool IsStunned => Time.time < stunUntil;
    public bool IsInvulnerable => Time.time < invulnerableUntil;
    public bool IsDead { get; private set; }
    /// El agujero negro se lo está tragando: ni control ni física
    public bool IsAbsorbed { get; private set; }
    /// Mantiene pulsado el modo preciso
    public bool IsFocusing { get; private set; }
    public Rigidbody2D Body => rb;
    /// La usa el verificador de niveles para saber si un hueco da tiempo a aprovecharlo
    public float MaxSpeed => cruiseSpeed;
    /// Golpe contra algo: velocidad del choque (para sacudida, vibración, sonido...)
    public event System.Action<float> OnImpact;
    /// El jugador ha muerto
    public event System.Action OnDied;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (input == null) input = FindFirstObjectByType<ShipInput>();
        if (input == null)
            Debug.LogError("PlayerController: no hay ningún ShipInput en la escena.", this);

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null)
            Debug.LogWarning("PlayerController: no se encontró ningún Animator.", this);

        if (visual != null) visualRenderer = visual.GetComponent<SpriteRenderer>();

        // A partir de ahora la velocidad la lleva este script. El amortiguamiento pelearía
        // contra él, así que se apaga aquí y no hay que acordarse de ponerlo en el Inspector.
        //
        // La rotación NO se congela con freezeRotation: esa restricción hace que MoveRotation
        // se ignore, y es justo con lo que orientamos al astronauta. En su lugar se pone la
        // velocidad angular a cero en cada paso, que impide que los choques lo hagan girar
        // y deja la rotación en nuestras manos.
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.freezeRotation = false;
        facing = rb.rotation + 90f;
    }

    void Start()
    {
        invulnerableUntil = Time.time + startInvulnerability;
    }

    void Update()
    {
        if (IsDead) return;

        bool thrustPose = debugForceThrustPose || thrustLevel > 0.05f;
        if (animator != null) animator.SetBool("Thrusting", thrustPose);

        // ¿Hacia qué lado mira? Lo decide por dónde avanza de verdad, con zona muerta para
        // que no parpadee al ir en vertical o estando quieto.
        if (flipSpriteWithDirection)
        {
            float h = rb.linearVelocity.x;
            if (Mathf.Abs(h) > flipDeadzone) facingSign = h > 0f ? 1 : -1;
            if (visualRenderer != null) visualRenderer.flipX = facingSign < 0;
        }

        if (visual != null)
        {
            float s = offsetSmoothing * Time.deltaTime;
            // La pose se refleja con él: si no, al mirar a la izquierda el astronauta se
            // inclinaría hacia el lado contrario al que empuja
            float mirror = (flipSpriteWithDirection && facingSign < 0) ? -1f : 1f;
            float targetAngle = (thrustPose ? thrustAngleOffset : idleAngleOffset) * mirror;
            Vector2 targetPos = thrustPose ? thrustPositionOffset : Vector2.zero;
            targetPos.x *= mirror;

            float angle = Mathf.LerpAngle(visual.localEulerAngles.z, targetAngle, s);
            visual.localRotation = Quaternion.Euler(0f, 0f, angle);
            visual.localPosition = Vector3.Lerp(visual.localPosition,
                                                new Vector3(targetPos.x, targetPos.y, 0f), s);
        }

        // Parpadeo mientras es invulnerable
        if (visualRenderer != null)
        {
            Color c = visualRenderer.color;
            c.a = IsInvulnerable ? (Mathf.Repeat(Time.time * blinkRate, 1f) < 0.5f ? 1f : 0.3f) : 1f;
            visualRenderer.color = c;
        }
    }

    void FixedUpdate()
    {
        if (IsDead || input == null) return;
        float dt = Time.fixedDeltaTime;

        IsFocusing = input.FocusHeld && !IsStunned;
        Vector2 intent = ReadIntent(dt);

        // Niveles de empuje y freno: alimentan la animación y quien quiera escucharlos
        thrustLevel = Mathf.MoveTowards(thrustLevel, intent.sqrMagnitude > 0.01f ? 1f : 0f, dt / 0.12f);
        brakeLevel = Mathf.MoveTowards(brakeLevel, IsFocusing ? 1f : 0f, dt / 0.12f);

        // --- lo que pides ---
        float speed = cruiseSpeed * (IsFocusing ? focusSpeedFactor : 1f);
        Vector2 target = IsStunned ? Vector2.zero : Vector2.ClampMagnitude(intent, 1f) * speed;

        // Arrancar es rápido (precisión), parar es lento (inercia). Ahí está el híbrido.
        bool speedingUp = target.sqrMagnitude > controlVel.sqrMagnitude;
        float tau = speedingUp ? accelTime : (IsFocusing ? focusDecelTime : decelTime);
        controlVel = Vector2.Lerp(controlVel, target, Approach(dt, tau));

        // --- lo que te hicieron ---
        knock = Vector2.Lerp(knock, Vector2.zero, Approach(dt, knockDecayTime));
        if (knock.sqrMagnitude < 0.0004f) knock = Vector2.zero;

        rb.linearVelocity = controlVel + knock;
        rb.angularVelocity = 0f;   // los choques no nos hacen girar; la rotación la llevamos aquí

        if (!rotateBodyToFacing) return;
        if (Mode == ControlMode.ShipHybrid) rb.MoveRotation(facing - 90f);
        else UpdateVisualFacing(dt);
    }

    /// El modo de control sale del esquema de entrada: con joystick el movimiento es directo,
    /// con inclinación o botones es de nave. Así no hay dos ajustes que puedan contradecirse.
    ControlMode Mode
    {
        get
        {
            if (!followInputScheme || input == null) return controlMode;
            return input.UsesJoystick ? ControlMode.Directional : ControlMode.ShipHybrid;
        }
    }

    /// Hacia dónde quiere ir el jugador, en coordenadas de mundo y con magnitud 0..1.
    Vector2 ReadIntent(float dt)
    {
        if (Mode == ControlMode.Directional)
        {
            // El joystick ya da las dos direcciones. Sin botones de dirección en pantalla.
            return input.Move;
        }

        // Modo nave: inclinar o los botones deciden el rumbo, el empuje manda hacia él
        if (input.UsesTilt) AimByTilt(dt);
        else TurnByButtons(dt);

        // El modo preciso mantiene el avance. Antes esto era solo ThrustHeld, y soltar el
        // empuje para afinar te dejaba clavado justo cuando más falta hacía moverse — el
        // jugador pedía precisión y el juego le daba un frenazo.
        if (!input.ThrustHeld && !IsFocusing) return Vector2.zero;

        float r = facing * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }

    /// Inclinación: el ángulo del móvil ES el rumbo.
    ///
    /// Antes la inclinación alimentaba la VELOCIDAD de giro, así que mantener el móvil torcido
    /// seguía girando sin parar y había que corregir a ojo adivinando cuándo soltar. Eso no se
    /// puede calibrar: estás pilotando una derivada.
    ///
    /// Midiendo el rumbo directamente, la postura de la mano y la del morro son la misma cosa:
    /// móvil plano, al frente; móvil torcido a tope, el máximo. Y vuelve solo al centro, que es
    /// lo que hace que se pueda aprender.
    void AimByTilt(float dt)
    {
        float span = maxTiltFacing * (IsFocusing ? focusTiltFactor : 1f);
        float want = baseFacing - input.Turn * span;

        // MoveTowardsAngle y NO Lerp. El lerp exponencial arranca rápido pero llega
        // arrastrándose, y esa cola asintótica es justo lo que se siente como que el morro
        // no termina de colocarse. Peor aún: encadenado detrás del suavizado de TiltInput
        // daba una respuesta de segundo orden, que arranca con pendiente CERO — mueves el
        // móvil y los primeros milisegundos no pasa nada.
        //
        // Con un tope de grados/s hay un solo filtro en toda la cadena (el de TiltInput, que
        // es el que debe estar porque filtra ruido del sensor) y el morro va a velocidad
        // plena hasta el ángulo pedido.
        facing = Mathf.MoveTowardsAngle(facing, want, tiltFollowRate * dt);
        turnRate = 0f;
    }

    /// Botones: aquí el giro por ritmo SÍ es lo natural — mantener es girar. Lo que faltaba
    /// era arranque, para que un toque corto fuese un ajuste fino y no un volantazo.
    void TurnByButtons(float dt)
    {
        float want = -input.Turn * turnSpeed * (IsFocusing ? focusTurnFactor : 1f);
        turnRate = Mathf.Lerp(turnRate, want, Approach(dt, turnAccelTime));
        facing += turnRate * dt;
    }

    /// En modo direccional el astronauta no gira por física: el cuerpo mira hacia donde avanza.
    void UpdateVisualFacing(float dt)
    {
        if (controlVel.sqrMagnitude < 0.04f) return;
        float want = Mathf.Atan2(controlVel.y, controlVel.x) * Mathf.Rad2Deg;
        facing = Mathf.LerpAngle(facing, want, Mathf.Clamp01(visualTurnSmoothing * dt));
        rb.MoveRotation(facing - 90f);
    }

    /// Orienta al astronauta hacia la meta al empezar el nivel. Cuando la corriente se
    /// invierte, la meta y la salida se intercambian — y hasta ahora el astronauta seguía
    /// mirando a la derecha como si nada.
    public void FaceGoal(bool goalIsMirrored)
    {
        facingSign = goalIsMirrored ? -1 : 1;
        facing = goalIsMirrored ? 180f : 0f;
        // El eje desde el que la inclinación mide su ángulo. Si no se actualizara, en los
        // niveles de corriente invertida el móvil plano apuntaría al lado contrario a la meta
        baseFacing = facing;
        turnRate = 0f;
        if (visualRenderer != null) visualRenderer.flipX = facingSign < 0;
        // Aquí se escribe la rotación directa, no MoveRotation: esto ocurre al reaparecer,
        // fuera del paso de física, y tiene que verse ya en el primer fotograma
        if (rotateBodyToFacing) rb.rotation = facing - 90f;
    }

    /// Fracción del camino que se recorre hacia el objetivo en este paso, con una constante
    /// de tiempo. Usa exponencial para que el resultado NO dependa de los fotogramas por
    /// segundo: a 30 fps y a 120 fps el astronauta se siente igual.
    static float Approach(float dt, float tau) =>
        tau <= 0.0001f ? 1f : 1f - Mathf.Exp(-dt / tau);

    void OnCollisionEnter2D(Collision2D c)
    {
        if (IsDead) return;
        float impact = c.relativeVelocity.magnitude;
        if (impact < minImpactSpeed) return;

        // Dirección del empujón: alejándose de lo que nos golpeó. Se usa la normal del
        // contacto y, si viene al revés, se le da la vuelta — así da igual quién sea A y
        // quién B en el motor de física.
        Vector2 away = (Vector2)transform.position - (Vector2)c.collider.transform.position;
        Vector2 dir = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.up;
        if (c.contactCount > 0)
        {
            Vector2 n = c.GetContact(0).normal;
            dir = Vector2.Dot(n, dir) >= 0f ? n : -n;
        }

        knock += dir * Mathf.Min(impact * knockScale, maxKnock);
        knock = Vector2.ClampMagnitude(knock, maxKnock);

        if (impact >= hardImpactSpeed)
        {
            stunUntil = Time.time + stunTime;
            thrustLevel = 0f;
        }

        OnImpact?.Invoke(impact);
    }

    /// Mata al jugador (se ignora si es invulnerable o ya está muerto).
    public void Kill()
    {
        if (IsDead || IsInvulnerable) return;
        IsDead = true;

        controlVel = Vector2.zero;
        turnRate = 0f;
        knock = Vector2.zero;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.simulated = false;
        if (visual != null) visual.gameObject.SetActive(false);

        OnDied?.Invoke();
    }

    /// Lo llama el agujero negro al cruzar el horizonte: suelta el control y la física,
    /// y deja que el Goal anime la caída.
    public void BeginAbsorb()
    {
        if (IsAbsorbed || IsDead) return;
        IsAbsorbed = true;
        controlVel = Vector2.zero;
        turnRate = 0f;
        knock = Vector2.zero;
        thrustLevel = 0f;
        brakeLevel = 0f;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.simulated = false;
        enabled = false;                  // deja de leer la entrada
    }

    /// Vuelve a la vida al empezar un nivel. Sustituye a recargar la escena entera: así no se
    /// pierden los pools ni hay parón de carga entre nivel y nivel.
    public void Respawn()
    {
        IsDead = false;
        IsAbsorbed = false;
        IsFocusing = false;
        enabled = true;

        transform.localScale = Vector3.one;
        transform.rotation = Quaternion.identity;

        rb.simulated = true;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        controlVel = Vector2.zero;
        turnRate = 0f;
        knock = Vector2.zero;
        thrustLevel = 0f;

        // Mirando a la meta, que cambia de lado cuando la corriente se invierte
        FaceGoal(GameBounds.Instance != null && GameBounds.Instance.GoalMirrored);

        brakeLevel = 0f;
        stunUntil = 0f;
        invulnerableUntil = Time.time + startInvulnerability;

        if (visual != null)
        {
            visual.gameObject.SetActive(true);
            visual.localScale = Vector3.one;
            visual.localPosition = Vector3.zero;
        }
        if (visualRenderer != null) visualRenderer.color = Color.white;
    }
}