using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private ShipInput input;
    [SerializeField] private Transform visual;   // el hijo con el sprite (Pj)
    [SerializeField] private Animator animator;  // el Animator de Pj

    [Header("Empuje")]
    [SerializeField] private float thrustForce = 2.5f;
    [SerializeField] private float thrustRampTime = 1.0f;
    [SerializeField] private float maxSpeed = 2.6f;

    [Header("Freno")]
    [SerializeField] private float brakeDecel = 1.6f;
    [SerializeField] private float brakeRampTime = 0.8f;

    [Header("Giro")]
    [SerializeField] private float maxTurnSpeed = 90f;
    [SerializeField] private float turnAccel = 240f;
    [SerializeField] private float turnDecel = 200f;

    [Header("Impactos")]
    [SerializeField] private float minImpactSpeed = 0.8f;
    [SerializeField] private float impactEnergyLoss = 0.35f;
    [SerializeField] private float hardImpactSpeed = 2.0f;
    [SerializeField] private float stunTime = 0.3f;

    [Header("Vida")]
    [Tooltip("Segundos de invulnerabilidad al empezar")]
    [SerializeField] private float startInvulnerability = 2f;
    [SerializeField] private float blinkRate = 4f;

    [Header("Pose del sprite")]
    [SerializeField] private float idleAngleOffset = 0f;
    [SerializeField] private float thrustAngleOffset = 45f;
    [SerializeField] private Vector2 thrustPositionOffset = new Vector2(-0.10f, -0.06f);
    [SerializeField] private float offsetSmoothing = 10f;
    [Tooltip("Solo para ajustar: fuerza la pose de empuje sin pulsar nada")]
    [SerializeField] private bool debugForceThrustPose = false;

    Rigidbody2D rb;
    SpriteRenderer visualRenderer;
    float thrustLevel, brakeLevel, stunUntil, invulnerableUntil;

    public float ThrustLevel => thrustLevel;
    public float BrakeLevel => brakeLevel;
    public bool IsStunned => Time.time < stunUntil;
    public bool IsInvulnerable => Time.time < invulnerableUntil;
    public bool IsDead { get; private set; }
    /// El agujero negro se lo está tragando: ni control ni física
    public bool IsAbsorbed { get; private set; }
    public Rigidbody2D Body => rb;

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

        if (visual != null)
        {
            float s = offsetSmoothing * Time.deltaTime;
            float targetAngle = thrustPose ? thrustAngleOffset : idleAngleOffset;
            Vector2 targetPos = thrustPose ? thrustPositionOffset : Vector2.zero;

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

        float targetTurn = -input.Steer * maxTurnSpeed;
        float rate = Mathf.Abs(input.Steer) > 0.01f ? turnAccel : turnDecel;
        rb.angularVelocity = Mathf.MoveTowards(rb.angularVelocity, targetTurn, rate * dt);

        bool wantsThrust = input.ThrustHeld && !IsStunned;
        thrustLevel = Mathf.MoveTowards(thrustLevel, wantsThrust ? 1f : 0f, dt / thrustRampTime);
        brakeLevel  = Mathf.MoveTowards(brakeLevel,  input.BrakeHeld ? 1f : 0f, dt / brakeRampTime);

        if (thrustLevel > 0f)
            rb.AddForce(transform.up * (thrustForce * thrustLevel), ForceMode2D.Force);

        if (brakeLevel > 0f)
        {
            float dv = brakeDecel * brakeLevel * dt;
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, dv);
        }

        rb.linearVelocity = Vector2.ClampMagnitude(rb.linearVelocity, maxSpeed);
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        if (IsDead) return;
        float impact = c.relativeVelocity.magnitude;
        if (impact < minImpactSpeed) return;

        rb.linearVelocity *= (1f - impactEnergyLoss);

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
        enabled = true;

        transform.localScale = Vector3.one;
        transform.rotation = Quaternion.identity;

        rb.simulated = true;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        thrustLevel = 0f;
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