using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private ShipInput input;
    [SerializeField] private Transform visual;   // el hijo con el sprite (Pj)
    [SerializeField] private Animator animator;  // opcional, se usa más adelante

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
    [Tooltip("Por debajo de esta velocidad de choque no cuenta como golpe")]
    [SerializeField] private float minImpactSpeed = 0.8f;
    [Tooltip("Fracción de velocidad que se pierde en cada golpe (0 a 1)")]
    [SerializeField] private float impactEnergyLoss = 0.35f;
    [Tooltip("Desde esta velocidad de choque el golpe aturde")]
    [SerializeField] private float hardImpactSpeed = 2.0f;
    [Tooltip("Segundos sin poder acelerar tras un golpe fuerte")]
    [SerializeField] private float stunTime = 0.3f;

    [Header("Inclinación del sprite")]
    [SerializeField] private float idleAngleOffset = 0f;
    [SerializeField] private float thrustAngleOffset = 0f;
    [SerializeField] private float offsetSmoothing = 10f;

    Rigidbody2D rb;
    float thrustLevel, brakeLevel, stunUntil;

    public float ThrustLevel => thrustLevel;
    public float BrakeLevel => brakeLevel;
    public bool IsStunned => Time.time < stunUntil;

    /// Se dispara en cada golpe con la velocidad del choque.
    /// Aquí se conectarán después la sacudida de cámara, la vibración, el sonido y las partículas.
    public event System.Action<float> OnImpact;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (input == null) input = FindFirstObjectByType<ShipInput>();
        if (input == null)
            Debug.LogError("PlayerController: no hay ningún ShipInput en la escena.", this);
    }

    void Update()
    {
        if (visual == null) return;
        float target = thrustLevel > 0.05f ? thrustAngleOffset : idleAngleOffset;
        float next = Mathf.LerpAngle(visual.localEulerAngles.z, target, offsetSmoothing * Time.deltaTime);
        visual.localRotation = Quaternion.Euler(0f, 0f, next);
    }

    void FixedUpdate()
    {
        if (input == null) return;
        float dt = Time.fixedDeltaTime;

        // Giro con aceleración, nunca instantáneo
        float targetTurn = -input.Steer * maxTurnSpeed;
        float rate = Mathf.Abs(input.Steer) > 0.01f ? turnAccel : turnDecel;
        rb.angularVelocity = Mathf.MoveTowards(rb.angularVelocity, targetTurn, rate * dt);

        // Rampas de empuje y freno (aturdido = sin empuje)
        bool wantsThrust = input.ThrustHeld && !IsStunned;
        thrustLevel = Mathf.MoveTowards(thrustLevel, wantsThrust ? 1f : 0f, dt / thrustRampTime);
        brakeLevel  = Mathf.MoveTowards(brakeLevel,  input.BrakeHeld ? 1f : 0f, dt / brakeRampTime);

        // Empuje hacia donde mira el astronauta
        if (thrustLevel > 0f)
            rb.AddForce(transform.up * (thrustForce * thrustLevel), ForceMode2D.Force);

        // Freno: reduce la velocidad actual sin pasarse de cero
        if (brakeLevel > 0f)
        {
            float dv = brakeDecel * brakeLevel * dt;
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, dv);
        }

        // Velocidad máxima
        rb.linearVelocity = Vector2.ClampMagnitude(rb.linearVelocity, maxSpeed);

        if (animator != null) animator.SetBool("Thrusting", thrustLevel > 0.05f);
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        float impact = c.relativeVelocity.magnitude;
        if (impact < minImpactSpeed) return;

        // Un golpe seco: pierde una parte de la velocidad que le dejó el rebote
        rb.linearVelocity *= (1f - impactEnergyLoss);

        if (impact >= hardImpactSpeed)
        {
            stunUntil = Time.time + stunTime;
            thrustLevel = 0f;
        }

        OnImpact?.Invoke(impact);
    }
}