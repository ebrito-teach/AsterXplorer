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
    [SerializeField] private float cruiseSpeed = 6f;
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
    [Tooltip("Multiplicador general del empujón, DESPUÉS de repartirlo por masas. Súbelo si " +
             "los golpes se sienten blandos; el reparto entre roca grande y pequeña no cambia")]
    [Range(0f, 3f)] [SerializeField] private float knockScale = 1.6f;
    [Tooltip("Cuánto rebotas. 0 = el golpe te empuja y ya; 1 = saltas como una pelota. " +
             "Bajo a propósito: en el espacio los choques son secos, no elásticos")]
    [Range(0f, 1f)] [SerializeField] private float restitution = 0.15f;
    [Tooltip("Cuánto te empuja chocar con una pared. Las paredes no tienen masa (son el " +
             "borde del mapa), así que su reparto se fija aquí. Bajo = topas y te paras, " +
             "que es lo que pediste: choque seco, sin rebote de goma")]
    [Range(0f, 1f)] [SerializeField] private float wallShare = 0.25f;

    [Header("Gravedad de la meta")]
    [Tooltip("Memoria del tirón en segundos: la velocidad que te da la gravedad se va " +
             "acumulando y se pierde con esta constante. Lejos apenas se nota; cerca del " +
             "agujero negro supera a tu propio impulso y ya no hay escapatoria")]
    [SerializeField] private float gravityMemory = 0.8f;

    [Header("Campo gravitatorio de planetas y colosos")]
    [Tooltip("Cerca de un planeta o un coloso vas más lento mientras sigas en su campo: " +
             "x = en el borde del campo, y = pegado a su superficie (0.15 = un 15 % más lento). " +
             "Los estáticos ya no tienen campo")]
    [SerializeField] private Vector2 bodySlow = new Vector2(0.15f, 0.20f);
    [Tooltip("Hasta dónde llega el campo de un coloso, en unidades desde su superficie")]
    [SerializeField] private float bodyFieldReach = 3f;
    [Tooltip("Hasta dónde llega el campo de un planeta, en unidades desde su superficie")]
    [SerializeField] private float planetFieldReach = 5f;
    [Tooltip("Tirón hacia el cuerpo, en unidades/s², pegado a su superficie (se apaga hacia el borde)")]
    [SerializeField] private float bodyPull = 1.6f;
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

    [Header("Aparición: llegada por el hiperespacio (mundo amplio)")]
    [Tooltip("Al empezar o reaparecer en el mundo amplio, entra volando desde fuera del borde " +
             "del mundo, frena y se para cerca de ese borde. Intangible e ininterrumpible")]
    [SerializeField] private bool expelOnSpawn = true;
    [Tooltip("Segundos que dura la llegada")]
    [SerializeField] private float arrivalTime = 1.4f;
    [Tooltip("Distancia que recorre desde fuera del mundo hasta su punto de aparición")]
    [SerializeField] private float arrivalDistance = 9f;
    [Tooltip("Cuánto más largo llega: sale del agujero negro anterior aún estirado y se va " +
             "recomponiendo mientras frena. 2 = tres veces su largo al entrar")]
    [Range(0f, 4f)] [SerializeField] private float recomposeStretch = 2.2f;
    [Tooltip("Rebote al terminar de recomponerse, como si el cuerpo se asentara")]
    [Range(0f, 0.3f)] [SerializeField] private float recomposeWobble = 0.1f;
    [Tooltip("Llega con la luz corrida al rojo y apagada, y recupera su color al recomponerse")]
    [SerializeField] private bool arrivalRedshift = true;
    [Tooltip("Las estrellas se estiran en rayas durante la llegada")]
    [SerializeField] private bool warpStreaks = true;

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
    Vector2 gravityVel;        // lo que te da la gravedad de la meta, acumulado
    Vector2 gravityAccel;      // lo que pidió la meta en este paso
    float facing = 90f;        // grados, hacia dónde mira el astronauta
    int facingSign = 1;        // 1 mira a la derecha, -1 a la izquierda
    Vector3 baseScale = Vector3.one;        // la escala que pusiste en la escena
    Vector3 baseVisualScale = Vector3.one;
    float baseFacing;          // el eje de avance: hacia la meta. La inclinación se mide desde aquí
    float turnRate;            // grados/s actuales de los botones de giro, con su arranque
    float thrustLevel, brakeLevel, stunUntil, invulnerableUntil;

    public float ThrustLevel => thrustLevel;
    public float BrakeLevel => brakeLevel;
    public bool IsStunned => Time.time < stunUntil;
    public bool IsInvulnerable => Time.time < invulnerableUntil;
    public bool IsDead { get; private set; }
    /// El hijo con el sprite: el agujero negro lo deforma al tragárselo
    public Transform Visual => visual;
    /// Saliendo despedido al aparecer: intangible y sin control
    public bool IsExpelling { get; private set; }
    /// Donde se detiene al terminar la expulsión. El generador deja libre esa zona
    public Vector2 SpawnPoint { get; private set; }
    /// Se acaba la expulsión y el jugador ya tiene el control
    public event System.Action OnSpawned;
    /// El agujero negro se lo está tragando: ni control ni física
    public bool IsAbsorbed { get; private set; }
    /// Mantiene pulsado el modo preciso
    public bool IsFocusing { get; private set; }
    public Rigidbody2D Body => rb;
    /// La usa el verificador de niveles para saber si un hueco da tiempo a aprovecharlo
    public float MaxSpeed => cruiseSpeed;
    /// Velocidad de crucero (la cambia el panel de calibración)
    public float CruiseSpeed { get => cruiseSpeed; set => cruiseSpeed = Mathf.Max(0.5f, value); }
    /// Velocidad real ahora mismo, empujones incluidos. La lee el velocímetro.
    public float Speed => rb != null ? rb.linearVelocity.magnitude : 0f;
    /// Cuánto te está frenando ahora el campo de los estáticos (0 = nada)
    public float FieldSlowdown { get; private set; }
    /// Golpe contra algo: velocidad del choque (para sacudida, vibración, sonido...)
    public event System.Action<float> OnImpact;
    /// El jugador ha muerto
    public event System.Action OnDied;
    /// El personaje se rompe y desaparece (explosión). Al morir, o al acabar el primer plano
    public event System.Action OnShattered;
    public bool Shattered { get; private set; }

    // ---- lo que tocan los buffs (BuffSystem) ----
    /// Multiplica la velocidad de crucero (XLR8D: 2)
    public float SpeedFactor { get; set; } = 1f;
    /// Empujón de velocidad limpia del QTE doble/triple (1 = nada). Se multiplica con SpeedFactor
    public float QteBoost { get; set; } = 1f;
    /// 0 = manejo normal, 1 = cuesta maniobrar: arranca, para y gira más torpe (XLR8D)
    public float Clumsiness { get; set; }
    /// Tope de giro de la dirección de avance, en grados por segundo (0 = sin tope). Con
    /// XLR8D cambiar de rumbo cuesta: la velocidad gira poco a poco hacia donde pides
    public float MaxTurnRate { get; set; }
    /// Se mueve en tiempo real aunque el mundo vaya a cámara lenta (Slow)
    public bool RealTime { get; set; }
    /// Transparencia del dibujo (Ghost)
    public float VisualAlpha { get; set; } = 1f;
    /// Si devuelve true, el golpe lo ha parado un escudo: ni muerte ni cuenta (LightShield)
    public System.Func<bool> HitAbsorber;

    /// Empujón fuerte que manda sobre todo lo demás (la onda expansiva de la grieta). Sin
    /// el tope de los golpes normales; se te pasa igual que ellos
    public void Shove(Vector2 velocity, float stun)
    {
        if (IsDead || IsExpelling || IsAbsorbed) return;
        knock = velocity;
        blastUntil = 0f;
        controlVel = Vector2.zero;
        stunUntil = Time.time + stun;
        thrustLevel = 0f;
    }

    /// Empujón de un solo golpe que te lleva Distance unidades en unos Seconds segundos (la onda
    /// expansiva de la grieta). Sale muy rápido y frena en seco al final
    public void Blast(Vector2 direction, float distance, float seconds, float stun)
    {
        if (IsDead || IsExpelling || IsAbsorbed || direction.sqrMagnitude < 0.0001f) return;
        blastTau = Mathf.Max(0.05f, seconds / 3f);          // a las 3 constantes ya recorrió el 95 %
        knock = direction.normalized * (distance / blastTau);
        blastUntil = Time.time + seconds;
        controlVel = Vector2.zero;
        stunUntil = Time.time + stun;
        thrustLevel = 0f;
    }
    float blastUntil, blastTau;

    /// Pared extra que no se puede cruzar (la barrera de la grieta): un punto y la normal
    /// hacia el lado permitido
    public void SetWall(bool on, Vector2 point, Vector2 normal)
    {
        hasWall = on;
        wallPoint = point;
        wallNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector2.left;
        wallXAt = null;
    }

    /// Pared que sigue una curva vertical: para cada altura, la x que no se puede pasar. La
    /// normal (horizontal) apunta al lado permitido. La usa la rasgadura
    public void SetWallCurve(System.Func<float, float> xAt, Vector2 normal)
    {
        hasWall = xAt != null;
        wallXAt = xAt;
        wallNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector2.left;
    }
    System.Func<float, float> wallXAt;
    bool hasWall;
    Vector2 wallPoint, wallNormal;
    /// Tocó la barrera (para el destello)
    public event System.Action<Vector2> HitWall;

    /// Invulnerable unos segundos más (al acabar Ghost, o tras un golpe parado por el escudo)
    public void GrantInvulnerability(float seconds) =>
        invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + seconds);

    void Awake()
    {
        // Se guardan ANTES de que nada las toque: son el tamaño que decidiste en la escena,
        // y es a ese al que vuelve Respawn()
        baseScale = transform.localScale;
        if (visual != null) baseVisualScale = visual.localScale;

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
        if (IsDead || IsExpelling) return;

        bool thrustPose = debugForceThrustPose || thrustLevel > 0.05f;
        if (animator != null)
        {
            animator.SetBool("Thrusting", thrustPose);
            var mode = RealTime ? AnimatorUpdateMode.UnscaledTime : AnimatorUpdateMode.Normal;
            if (animator.updateMode != mode) animator.updateMode = mode;
        }

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
            c.a = (IsInvulnerable ? (Mathf.Repeat(Time.time * blinkRate, 1f) < 0.5f ? 1f : 0.3f) : 1f) * VisualAlpha;
            visualRenderer.color = c;
        }
    }

    void FixedUpdate()
    {
        if (IsDead || IsExpelling || input == null) return;
        // Con Slow el mundo va más lento; el jugador no: sus pasos se miden en tiempo real
        // y su velocidad se multiplica por lo mismo que se frena el mundo
        float k = RealTime && Time.timeScale > 0.01f ? 1f / Time.timeScale : 1f;
        float dt = Time.fixedDeltaTime * k;

        IsFocusing = input.FocusHeld && !IsStunned;
        Vector2 intent = ReadIntent(dt);

        // Niveles de empuje y freno: alimentan la animación y quien quiera escucharlos
        thrustLevel = Mathf.MoveTowards(thrustLevel, intent.sqrMagnitude > 0.01f ? 1f : 0f, dt / 0.12f);
        brakeLevel = Mathf.MoveTowards(brakeLevel, IsFocusing ? 1f : 0f, dt / 0.12f);

        // --- lo que pides ---
        FieldSlowdown = Mathf.Max(StaticField(), PlanetRing.SlowAt(rb.position));
        float speed = cruiseSpeed * SpeedFactor * QteBoost * (IsFocusing ? focusSpeedFactor : 1f) * (1f - FieldSlowdown);
        Vector2 target = IsStunned ? Vector2.zero : Vector2.ClampMagnitude(intent, 1f) * speed;

        // Arrancar es rápido (precisión), parar es lento (inercia). Ahí está el híbrido.
        bool speedingUp = target.sqrMagnitude > controlVel.sqrMagnitude;
        float tau = speedingUp ? accelTime * (1f + 2.5f * Clumsiness)
                               : (IsFocusing ? focusDecelTime : decelTime) * (1f + 1.5f * Clumsiness);
        if (MaxTurnRate > 0f && controlVel.sqrMagnitude > 0.25f && target.sqrMagnitude > 0.01f)
        {
            // Rumbo limitado: la dirección gira como mucho MaxTurnRate grados por segundo y
            // la rapidez se acerca a la pedida como siempre
            float cur = Mathf.Atan2(controlVel.y, controlVel.x) * Mathf.Rad2Deg;
            float want = Mathf.Atan2(target.y, target.x) * Mathf.Rad2Deg;
            float ang = Mathf.MoveTowardsAngle(cur, want, MaxTurnRate * dt) * Mathf.Deg2Rad;
            float mag = Mathf.Lerp(controlVel.magnitude, target.magnitude, Approach(dt, tau));
            controlVel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * mag;
        }
        else controlVel = Vector2.Lerp(controlVel, target, Approach(dt, tau));

        // --- lo que te hicieron ---
        knock = Vector2.Lerp(knock, Vector2.zero, Approach(dt, Time.time < blastUntil ? blastTau : knockDecayTime));
        if (knock.sqrMagnitude < 0.0004f) knock = Vector2.zero;

        // --- lo que te hace la gravedad ---
        gravityVel += gravityAccel * dt;
        gravityAccel = Vector2.zero;
        gravityVel = Vector2.Lerp(gravityVel, Vector2.zero, Approach(dt, gravityMemory));
        if (gravityVel.sqrMagnitude < 0.0004f) gravityVel = Vector2.zero;

        rb.linearVelocity = (controlVel + knock + gravityVel) * k;
        KeepInsideWorld();
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
        float rate = tiltFollowRate * (1f - 0.5f * Clumsiness);
        if (MaxTurnRate > 0f) rate = Mathf.Min(rate, MaxTurnRate);
        facing = Mathf.MoveTowardsAngle(facing, want, rate * dt);
        turnRate = 0f;
    }

    /// Botones: aquí el giro por ritmo SÍ es lo natural — mantener es girar. Lo que faltaba
    /// era arranque, para que un toque corto fuese un ajuste fino y no un volantazo.
    void TurnByButtons(float dt)
    {
        float want = -input.Turn * turnSpeed * (1f - 0.4f * Clumsiness) * (IsFocusing ? focusTurnFactor : 1f);
        if (MaxTurnRate > 0f) want = Mathf.Clamp(want, -MaxTurnRate, MaxTurnRate);
        turnRate = Mathf.Lerp(turnRate, want, Approach(dt, turnAccelTime * (1f + 2f * Clumsiness)));
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

    void OnCollisionStay2D(Collision2D c)
    {
        if (IsDead) return;
        StopAgainstTerrain(c, c.collider.GetComponentInParent<Asteroid>());
    }

    // Contra un estático o un planeta quieto: quita la parte de la velocidad que va hacia él
    bool StopAgainstTerrain(Collision2D c, Asteroid rock)
    {
        if (rock == null || rock.Type == null || rock.Type.behavior != AsteroidBehavior.Static
            || rock.IsOrbiting || rock.IsDislodged) return false;
        Vector2 n = (Vector2)transform.position - rock.Body.position;
        if (c.contactCount > 0) { var cn = c.GetContact(0).normal; n = Vector2.Dot(cn, n) >= 0f ? cn : -cn; }
        if (n.sqrMagnitude > 0.0001f)
        {
            n.Normalize();
            controlVel -= n * Mathf.Min(0f, Vector2.Dot(controlVel, n));
            knock -= n * Mathf.Min(0f, Vector2.Dot(knock, n));
            gravityVel -= n * Mathf.Min(0f, Vector2.Dot(gravityVel, n));
        }
        return true;
    }

    [Tooltip("Velocidad mínima de un choque que no mata para que salte la chispa")]
    [SerializeField] private float bumpSparkSpeed = 1.2f;
    float nextBumpSpark;

    void OnCollisionEnter2D(Collision2D c)
    {
        if (IsDead) return;
        // Con escudo, cualquier choque con una roca cuenta como golpe parado
        var hitRock = c.collider.GetComponentInParent<Asteroid>();
        Vector2 hitAt = c.contactCount > 0 ? c.GetContact(0).point : (Vector2)transform.position;
        // El escudo solo se gasta con lo que mata (tocar un planeta o un estático no lo gasta)
        if (HitAbsorber != null && hitRock != null && hitRock.Lethal && HitAbsorber()) ImpactSparks.Shield(hitAt);
        // Choque que no mata (estáticos, planetas, rocas no letales): chispa pequeña
        if (hitRock != null && !hitRock.Lethal && c.relativeVelocity.magnitude >= bumpSparkSpeed && Time.time >= nextBumpSpark)
        {
            nextBumpSpark = Time.time + 0.15f;
            ImpactSparks.Bump(hitAt);
        }

        // Estáticos y planetas: sin rebote. Topas y te quedas pegado a su superficie (pierdes
        // solo la parte de la velocidad que iba hacia ellos), sin empujón ni aturdimiento
        if (StopAgainstTerrain(c, hitRock)) return;
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

        // TERCERA LEY. Un choque reparte el MISMO impulso a los dos cuerpos, así que el
        // cambio de velocidad que se lleva cada uno depende de la masa del otro frente a la
        // propia:
        //
        //     Δv = (1 + rebote) · m_otro / (m_mía + m_otro) · velocidad relativa
        //
        // Una gravilla (m_otro casi cero) no te mueve: su impulso se lo come tu inercia.
        // Un coloso (m_otro enorme) te arrastra con él, porque el reparto tiende a 1 y el
        // que apenas cambia de velocidad es él. Y entre medias, todo lo demás en proporción.
        //
        // Esto importa más de lo que parece: FixedUpdate escribe rb.linearVelocity cada paso,
        // así que lo que resuelve el motor de física se descarta. Este `knock` ES toda la
        // respuesta del jugador al golpe — si no mira la masa aquí, la masa no existe para él
        // por mucho que los asteroides sí la respeten entre ellos.
        float share = ImpactShare(c);
        float dv = impact * (1f + restitution) * share * knockScale;

        knock += dir * Mathf.Min(dv, maxKnock);
        knock = Vector2.ClampMagnitude(knock, maxKnock);

        if (impact >= hardImpactSpeed)
        {
            stunUntil = Time.time + stunTime;
            thrustLevel = 0f;
        }

        OnImpact?.Invoke(impact);
    }

    // El frenado del estático más influyente a tu alrededor. Crece al acercarte (suave hacia el
    // borde del campo) y con la masa del estático respecto a la media del nivel
    static readonly Collider2D[] fieldHits = new Collider2D[24];
    static ContactFilter2D fieldFilter;
    static bool fieldFilterReady;

    // Dentro del campo de un estático o un coloso: frenado estable (de 15 a 20 %) mientras no
    // salgas, y un leve tirón hacia él. Manda el cuerpo más influyente que tengas cerca
    float StaticField()
    {
        if (bodyFieldReach <= 0f && planetFieldReach <= 0f) return 0f;
        if (!fieldFilterReady)
        {
            fieldFilter = new ContactFilter2D();
            fieldFilter.SetLayerMask(LayerMask.GetMask("AsteroidStatic", "AsteroidMoving"));
            fieldFilter.useTriggers = false;
            fieldFilterReady = true;
        }
        Vector2 me = rb.position;
        int n = Physics2D.OverlapCircle(me, Mathf.Max(bodyFieldReach, planetFieldReach) + 8f, fieldFilter, fieldHits);
        float best = 0f;
        Vector2 pull = Vector2.zero;
        for (int i = 0; i < n; i++)
        {
            var a = fieldHits[i].GetComponentInParent<Asteroid>();
            if (a == null || a.Type == null || a.IsOrbiting) continue;
            bool planet = a.Type.planet && a.Type.behavior == AsteroidBehavior.Static && !a.IsDislodged;
            bool colossus = a.Type.behavior == AsteroidBehavior.Colossus;
            if (!planet && !colossus) continue;
            float reach = planet ? planetFieldReach : bodyFieldReach;
            if (reach <= 0f) continue;
            Vector2 to = a.Body.position - me;
            float d = to.magnitude - a.WorldRadius;
            if (d >= reach) continue;
            float k = 1f - Mathf.Max(0f, d) / reach;             // 0 en el borde, 1 pegado
            float edge = Mathf.Clamp01((reach - Mathf.Max(0f, d)) / 0.5f);   // entra suave
            float slow = Mathf.Lerp(bodySlow.x, bodySlow.y, k) * edge;
            if (slow > best) best = slow;
            if (to.sqrMagnitude > 0.0001f) pull += to.normalized * (bodyPull * k * k);
        }
        if (pull != Vector2.zero) AddGravity(pull);
        return Mathf.Clamp01(best);
    }

    /// Qué fracción de la velocidad del choque se te queda, según quién te golpeó.
    ///
    /// Tres casos, y los tres son el mismo reparto visto de distinta manera:
    ///   cuerpo con masa  -> m_otro / (m_mía + m_otro), el reparto newtoniano
    ///   pared            -> wallShare, un valor fijo: el borde del mapa no tiene masa y
    ///                       tratarlo como infinita te haría rebotar como una pelota
    ///   estático sin masa -> 1, es inamovible y te devuelve el choque entero
    float ImpactShare(Collision2D c)
    {
        var otherRb = c.rigidbody;
        if (otherRb != null && otherRb.bodyType == RigidbodyType2D.Dynamic)
        {
            float mMine = Mathf.Max(0.0001f, rb.mass);
            float mOther = Mathf.Max(0.0001f, otherRb.mass);
            return mOther / (mMine + mOther);
        }

        int walls = LayerMask.NameToLayer("Walls");
        if (walls >= 0 && c.collider != null && c.collider.gameObject.layer == walls)
            return wallShare;

        return 1f;
    }

    // Límite duro del mundo, independiente de las paredes: si por lo que sea el jugador queda
    // fuera (un empujón, la gravedad, una pared que no chocó), se le devuelve al borde y se le
    // quita la velocidad hacia fuera. Avisa una vez para poder encontrar la causa
    bool warnedOutside;
    void KeepInsideWorld()
    {
        if (hasWall)
        {
            if (wallXAt != null) wallPoint = new Vector2(wallXAt(rb.position.y), rb.position.y);
            float d = Vector2.Dot(rb.position - wallPoint, wallNormal);
            if (d < 0f)
            {
                rb.position -= wallNormal * d;
                Vector2 wv = rb.linearVelocity;
                float into = Vector2.Dot(wv, wallNormal);
                if (into < 0f)
                {
                    rb.linearVelocity = wv - wallNormal * into;
                    controlVel -= wallNormal * Vector2.Dot(controlVel, wallNormal);
                    knock -= wallNormal * Mathf.Min(0f, Vector2.Dot(knock, wallNormal));
                    gravityVel -= wallNormal * Mathf.Min(0f, Vector2.Dot(gravityVel, wallNormal));
                    HitWall?.Invoke(rb.position);
                }
            }
        }

        var gb = GameBounds.Instance;
        if (gb == null || !gb.IsReady) return;
        Rect a = gb.Area;
        Vector2 p = rb.position;
        Vector2 q = new Vector2(Mathf.Clamp(p.x, a.xMin, a.xMax), Mathf.Clamp(p.y, a.yMin, a.yMax));
        if (q == p) return;

        if (!warnedOutside)
        {
            warnedOutside = true;
            Debug.LogWarning($"PlayerController: el jugador salió del mundo en {p} (mundo {a}), con " +
                             $"velocidad {rb.linearVelocity} (control {controlVel}, empujón {knock}, " +
                             $"gravedad {gravityVel}). Devuelto al borde.", this);
        }
        rb.position = q;
        Vector2 v = rb.linearVelocity;
        if ((p.x < a.xMin && v.x < 0f) || (p.x > a.xMax && v.x > 0f)) { v.x = 0f; knock.x = 0f; gravityVel.x = 0f; controlVel.x = 0f; }
        if ((p.y < a.yMin && v.y < 0f) || (p.y > a.yMax && v.y > 0f)) { v.y = 0f; knock.y = 0f; gravityVel.y = 0f; controlVel.y = 0f; }
        rb.linearVelocity = v;
    }

    /// La meta tira de ti: aceleración (no fuerza) de este paso de física.
    public void AddGravity(Vector2 acceleration) => gravityAccel += acceleration;

    /// Lo que le mató (roca, antimateria; null si no se sabe) y dónde fue el impacto
    public Component LastKiller { get; private set; }
    public Vector2 DeathPoint { get; private set; }

    /// Mata al jugador (se ignora si es invulnerable o ya está muerto).
    public void Kill() => KillBy(null, transform.position);

    /// Mata al jugador sabiendo qué le golpeó y dónde (para el primer plano y la chispa)
    public void KillBy(Component killer, Vector2 at)
    {
        if (IsDead || IsInvulnerable) return;
        if (HitAbsorber != null && HitAbsorber()) { ImpactSparks.Shield(at); return; }
        IsDead = true;
        LastKiller = killer;
        DeathPoint = at;
        ImpactSparks.Death(at);

        controlVel = Vector2.zero;
        turnRate = 0f;
        knock = Vector2.zero;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.simulated = false;
        // Con primer plano, el personaje se queda a la vista (clavado donde lo tocaron) y se
        // rompe al acabar; sin él, se rompe al instante como siempre
        bool closeUp = DeathCloseUp.WillPlay;
        if (!closeUp) Shatter();

        OnDied?.Invoke();
    }

    /// Rompe al personaje muerto: lo esconde y avisa (la explosión la pone FxSpawner)
    public void Shatter()
    {
        if (Shattered) return;
        Shattered = true;
        if (visual != null) visual.gameObject.SetActive(false);
        OnShattered?.Invoke();
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
        StopAllCoroutines();
        IsExpelling = false;
        IsDead = false;
        Shattered = false;
        IsAbsorbed = false;
        IsFocusing = false;
        enabled = true;

        // La escala de la ESCENA, no 1. La absorción del agujero negro encoge al astronauta,
        // y al reaparecer hay que deshacer ese encogimiento — pero antes se devolvía a 1 fijo,
        // así que cualquier tamaño que le pusieras se perdía en el primer nivel.
        transform.localScale = baseScale;
        transform.rotation = Quaternion.identity;

        rb.simulated = true;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        controlVel = Vector2.zero;
        turnRate = 0f;
        knock = Vector2.zero;
        gravityVel = gravityAccel = Vector2.zero;
        thrustLevel = 0f;

        // Mirando a la meta, que cambia de lado cuando la corriente se invierte
        FaceGoal(GameBounds.Instance != null && GameBounds.Instance.GoalMirrored);

        brakeLevel = 0f;
        stunUntil = 0f;
        invulnerableUntil = Time.time + startInvulnerability;

        if (visual != null)
        {
            visual.gameObject.SetActive(true);
            visual.localScale = baseVisualScale;
            visual.localPosition = Vector3.zero;
        }
        if (visualRenderer != null) visualRenderer.color = Color.white;

        SpawnPoint = transform.position;
        var gb = GameBounds.Instance;
        if (expelOnSpawn && gb != null && gb.IsExpanded && arrivalTime > 0.01f)
            StartCoroutine(Expel(SpawnPoint));
        else
            OnSpawned?.Invoke();
    }

    // Llega del hiperespacio: entra desde fuera del borde del mundo más cercano, a toda
    // velocidad, y frena hasta pararse en su punto de aparición, que está cerca de ese borde.
    // Sin física (intangible) y sin control: no se puede cortar. El universo sigue a lo suyo.
    System.Collections.IEnumerator Expel(Vector2 end)
    {
        IsExpelling = true;
        rb.simulated = false;
        rb.linearVelocity = Vector2.zero;

        // Hacia dentro desde la pared más cercana: por ahí entra
        Vector2 inward = Vector2.right;
        var gb = GameBounds.Instance;
        if (gb != null)
        {
            Rect a = gb.Area;
            float dl = end.x - a.xMin, dr = a.xMax - end.x, db = end.y - a.yMin, dt = a.yMax - end.y;
            float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
            inward = m == dl ? Vector2.right : m == dr ? Vector2.left : m == db ? Vector2.up : Vector2.down;
        }
        Vector2 start = end - inward * arrivalDistance;

        // Mira hacia donde va
        if (inward.x != 0f)
        {
            facingSign = inward.x > 0f ? 1 : -1;
            if (visualRenderer != null) visualRenderer.flipX = facingSign < 0;
        }
        if (visual != null)
        {
            visual.localRotation = Quaternion.identity;
            visual.localPosition = Vector3.zero;
        }
        if (visualRenderer != null) visualRenderer.color = Color.white;
        if (animator != null) animator.SetBool("Thrusting", true);

        transform.position = start;
        transform.localScale = baseScale;
        if (warpStreaks) WarpStreaks.Begin(inward);

        float t = 0f;
        while (t < arrivalTime)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / arrivalTime);
            // Frenada: rapidísimo al llegar, se posa suave al final
            float e = 1f - (1f - u) * (1f - u) * (1f - u);
            float speed01 = (1f - u) * (1f - u);           // velocidad relativa: 1 al entrar, 0 al parar

            transform.position = Vector2.Lerp(start, end, e);

            // Llega estirado (viene de caer en un agujero negro) y se recompone mientras frena:
            // largo y fino al entrar, su forma al parar, con un pequeño rebote al asentarse
            if (visual != null)
            {
                float k = recomposeStretch * speed01;
                float len = 1f + k;
                float thin = 1f / Mathf.Sqrt(len);
                if (u > 0.7f && recomposeWobble > 0f)
                {
                    float w = (u - 0.7f) / 0.3f;
                    float bounce = Mathf.Sin(w * Mathf.PI * 2f) * recomposeWobble * (1f - w);
                    len *= 1f - bounce;
                    thin *= 1f + bounce;
                }
                bool horizontal = Mathf.Abs(inward.x) > 0.5f;
                visual.localScale = horizontal
                    ? new Vector3(baseVisualScale.x * len, baseVisualScale.y * thin, baseVisualScale.z)
                    : new Vector3(baseVisualScale.x * thin, baseVisualScale.y * len, baseVisualScale.z);
            }
            // Sale del pozo al revés de como cayó: su luz llega corrida al rojo y apagada, y
            // recupera el color a medida que se recompone
            if (visualRenderer != null && arrivalRedshift)
                visualRenderer.color = Goal.Redshift(Color.white, Mathf.Lerp(0.22f, 1f, 1f - speed01));
            if (warpStreaks) WarpStreaks.SetStrength(speed01);
            yield return null;
        }

        if (warpStreaks) WarpStreaks.End();
        if (visual != null) visual.localScale = baseVisualScale;
        if (visualRenderer != null) visualRenderer.color = Color.white;
        if (animator != null) animator.SetBool("Thrusting", false);

        transform.position = end;
        rb.position = end;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.simulated = true;
        controlVel = Vector2.zero;
        knock = Vector2.zero;

        // La invulnerabilidad de siempre (Start Invulnerability) empieza al tomar el control,
        // no al empezar la llegada: si no, casi toda se gastaría volando
        invulnerableUntil = Time.time + startInvulnerability;
        IsExpelling = false;
        OnSpawned?.Invoke();
    }
}