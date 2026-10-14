using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(SpriteRenderer))]
public class Asteroid : MonoBehaviour
{
    /// Multiplicador de velocidad del nivel actual (lo fija LevelPopulator)
    public static float SpeedMultiplier = 1f;
    /// Desprendimientos y fragmentos vivos en este momento
    public static int DebrisAlive { get; private set; }

    /// Choque fuerte entre dos cuerpos: punto, energía del impacto y masa del mayor.
    /// Lo escucha ImpactFeedback para sacudir la cámara y parar el tiempo un instante.
    public static event System.Action<Vector2, float, float> HeavyImpact;

    public AsteroidTypeData Type { get; private set; }
    public float Size { get; private set; }
    public float Density { get; private set; }
    /// Radio aproximado del collider en unidades de mundo
    public float WorldRadius { get; private set; }
    public float Mass => rb != null ? rb.mass : 1f;
    public bool IsDislodged => dislodged;
    /// Está orbitando a otro cuerpo
    public bool IsOrbiting => orbitHost != null;
    /// Lo que mata: lo que se mueve por su cuenta, un estático soltado, y las lunas
    /// de un cuerpo que las tenga declaradas mortales (coloso y cometa; el estático no).
    public bool Lethal
    {
        get
        {
            if (Type == null) return false;
            // Un trozo por debajo del radio mínimo empuja, pero no mata
            if (Type.lethalMinRadius > 0f && WorldRadius < Type.lethalMinRadius) return false;
            if (orbitHost != null)
                return orbitHost.Type != null && orbitHost.Type.moonsAreLethal;
            return Type.killsPlayer || dislodged;
        }
    }
    /// Prefab del que salió (lo usa el AsteroidPool)
    public Asteroid SourcePrefab { get; set; }
    /// Pool al que pertenece (lo usa AsteroidBreaker)
    public AsteroidPool Pool => pool;
    /// Sprite que lleva puesto ahora (para saber de qué color son sus fragmentos)
    public Sprite CurrentSprite => sr != null ? sr.sprite : null;
    /// Cuerpo físico (lo usan el campo magnético y las órbitas)
    public Rigidbody2D Body => rb;
    /// Imparable: cinemático a propósito, nada lo desvía
    public bool Unstoppable => unstoppable;

    Rigidbody2D rb;
    CircleCollider2D col;
    SpriteRenderer sr;
    AsteroidPool pool;
    ParticleSystem[] tails;

    float sizeT;               // 0 = el más pequeño de su tipo, 1 = el más grande
    float targetSpeed;
    float turnRate;
    float personalOffset;
    float noiseSeedA, noiseSeedB;
    float lifetimeEnd = -1f;
    float shedTimer;
    float restoreBlockedUntil;
    float pulledUntil;
    Vector2 lastVelocity;      // velocidad con la que entró en el paso de física
    bool followsFlow, restoreInterpolation, dislodged, countedAsDebris;
    bool captureRollUsed;
    bool unstoppable;          // cinemático a propósito: nada puede desviarlo
    bool hasEnteredView;       // ya se ha dejado ver al menos una vez
    float spawnTime;

    // Órbita
    Asteroid orbitHost;
    float orbitRadius, orbitAngle, orbitSpeed, orbitSpin;
    readonly List<Asteroid> moons = new List<Asteroid>();

    static readonly Collider2D[] overlapBuffer = new Collider2D[16];
    static readonly Collider2D[] pullBuffer = new Collider2D[32];
    static ContactFilter2D staticFilter, movingFilter;
    static bool staticFilterReady, movingFilterReady;
    static Transform playerTransform;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SpeedMultiplier = 1f;
        DebrisAlive = 0;
        staticFilterReady = false;
        movingFilterReady = false;
        playerTransform = null;
        HeavyImpact = null;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CircleCollider2D>();
        sr = GetComponent<SpriteRenderer>();
        tails = GetComponentsInChildren<ParticleSystem>(true);
    }

    public void Init(AsteroidTypeData type, Sprite sprite, Vector2 position, float size, AsteroidPool owner)
    {
        Type = type;
        pool = owner;
        gameObject.name = type.id;

        dislodged = false;
        orbitHost = null;
        moons.Clear();
        restoreBlockedUntil = 0f;
        pulledUntil = 0f;
        lastVelocity = Vector2.zero;
        captureRollUsed = false;
        unstoppable = type.unstoppable && type.behavior != AsteroidBehavior.Static;
        hasEnteredView = false;
        spawnTime = Time.time;
        lifetimeEnd = type.lifetime > 0f ? Time.time + type.lifetime * Random.Range(0.85f, 1.15f) : -1f;
        sr.color = Color.white;

        if (type.behavior == AsteroidBehavior.Debris && !countedAsDebris)
        {
            DebrisAlive++;
            countedAsDebris = true;
        }

        bool isStatic = type.behavior == AsteroidBehavior.Static;
        SetLayer(isStatic ? "AsteroidStatic" : "AsteroidMoving");

        // El collider se mide a escala 1: así conocemos el radio real antes de aplicar el tamaño
        sr.sprite = sprite;
        FitCollider(sprite, type.colliderFactor);
        float unitRadius = col.radius;

        // Suelo de tamaño: nada que mate puede ser microscópico
        float minR = Mathf.Max(0f, type.minWorldRadius);
        if (unitRadius > 0.0001f && unitRadius * size < minR) size = minR / unitRadius;

        Size = size;
        transform.localScale = Vector3.one * size;
        col.sharedMaterial = type.material;
        WorldRadius = unitRadius * size;

        // Masa de un cuerpo macizo: al doble de tamaño, ocho veces la masa (exponente 3)
        Density = Random.Range(type.densityRange.x, type.densityRange.y);
        rb.mass = Mathf.Max(0.02f, Mathf.Pow(size, type.massExponent) * Density * type.massPerArea);
        rb.gravityScale = 0f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;

        sizeT = Mathf.InverseLerp(type.sizeRange.x, type.sizeRange.y, size);
        noiseSeedA = Random.value * 100f;
        noiseSeedB = Random.value * 100f;
        shedTimer = type.comet != null ? Random.Range(0.3f, Mathf.Max(0.35f, type.comet.shedInterval)) : 0f;

        if (isStatic)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
            Teleport(position);
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = Random.Range(type.spinRange.x, type.spinRange.y);
            targetSpeed = 0f;
            followsFlow = false;
        }
        else if (type.behavior == AsteroidBehavior.Debris)
        {
            // Nace quieto; quien lo desprende o lo rompe le da su movimiento con SetMotion
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Teleport(position);
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            targetSpeed = 0f;
            followsFlow = false;
        }
        else
        {
            // Un coloso pesa mil veces más que todo lo demás del cinturón. La desviación
            // que le provocaría un guijarro no es medible, así que lo correcto Y lo jugable
            // es que sea cinemático: avanza como si nada existiera, y empuja lo que toca.
            rb.bodyType = unstoppable ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Launch(position);
        }

        foreach (var ps in tails) { ps.Clear(true); ps.Play(true); }
    }

    void SetLayer(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0) Debug.LogError("Falta la capa " + layerName + ".", this);
        else gameObject.layer = layer;
    }

    /// Movimiento inicial propio (lo usan los desprendimientos y los fragmentos).
    public void SetMotion(Vector2 velocity, float spin)
    {
        rb.linearVelocity = velocity;
        rb.angularVelocity = spin;
        targetSpeed = velocity.magnitude;
        lastVelocity = velocity;
        followsFlow = false;
    }

    void Teleport(Vector2 position)
    {
        float angle = Random.Range(0f, 360f);
        rb.interpolation = RigidbodyInterpolation2D.None;
        restoreInterpolation = true;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
        rb.position = position;
        rb.rotation = angle;
    }

    void Launch(Vector2 position)
    {
        Teleport(position);

        float sizeFactor = Type.sizeAffectsSpeed ? Mathf.Lerp(1.4f, 0.6f, sizeT) : 1f;
        float spin = Random.Range(Type.spinRange.x, Type.spinRange.y) * sizeFactor;

        float speedT = Type.sizeAffectsSpeed
            ? Mathf.Clamp01(Mathf.Lerp(0.85f, 0.15f, sizeT) + Random.Range(-0.2f, 0.2f))
            : Random.value;
        float levelScale = 1f + (SpeedMultiplier - 1f) * Type.speedScaling;
        targetSpeed = Mathf.Lerp(Type.speedRange.x, Type.speedRange.y, speedT) * levelScale;

        turnRate = Type.turnRate * sizeFactor;
        personalOffset = Random.Range(-Type.flowSpread, Type.flowSpread);

        var flow = FlowField.Instance;
        followsFlow = flow != null && Random.value < Type.followFlow;

        Vector2 dir = followsFlow
            ? FlowField.FromAngle(flow.AngleAt(position) + personalOffset)
            : Random.insideUnitCircle.normalized;
        if (dir.sqrMagnitude < 0.01f) dir = Vector2.right;
        dir.Normalize();

        var gb = GameBounds.Instance;
        if (gb != null && !gb.CameraRect.Contains(position))
        {
            Vector2 toCenter = gb.CameraRect.center - position;
            if (Vector2.Dot(dir, toCenter) < 0f) dir = -dir;
        }

        rb.linearVelocity = dir * targetSpeed;
        rb.angularVelocity = spin;
        lastVelocity = rb.linearVelocity;
    }

    void FitCollider(Sprite s, float factor)
    {
        Vector2[] v = s != null ? s.vertices : null;
        col.offset = Vector2.zero;
        if (v == null || v.Length == 0) { col.radius = 0.5f; return; }

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in v)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        col.radius = ((maxX - minX) + (maxY - minY)) * 0.25f * factor;
    }

    void Update()
    {
        if (lifetimeEnd <= 0f) return;
        float left = lifetimeEnd - Time.time;
        if (left <= 0f) { Release(); return; }
        if (left < 2f)
        {
            Color c = sr.color;
            c.a = left / 2f;
            sr.color = c;
        }
    }

    void FixedUpdate()
    {
        Simulate();
        // Con qué velocidad entra al paso de física: al resolverse el choque, comparar
        // con la de salida nos dice si me empujaron a mí o si choqué yo.
        if (rb != null) lastVelocity = rb.linearVelocity;
    }

    void Simulate()
    {
        if (restoreInterpolation)
        {
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            restoreInterpolation = false;
        }

        if (Type == null) return;

        if (orbitHost != null) { TickOrbit(); return; }

        // Un estático de verdad no se simula. Un imparable (coloso) SÍ, aunque sea cinemático:
        // es cinemático precisamente para que nada pueda desviarlo.
        if (rb.bodyType == RigidbodyType2D.Kinematic && !unstoppable)
        {
            if (Type.pullRadius > 0f) ApplyMagneticPull();
            return;
        }

        var flow = FlowField.Instance;

        if (flow != null && IsOutOfPlay(flow))
        {
            bool replace = Type.recycle && Type.behavior != AsteroidBehavior.Debris && !dislodged;
            if (replace)
            {
                if (pool != null && pool.NotifyExited(this)) return;
                Launch(flow.RandomEntryPoint(AvoidPoint(), 2.5f));
            }
            else Release();
            return;
        }

        if (Type.dislodgesStatics) DislodgeNearbyStatics();
        if (Type.pullRadius > 0f) ApplyMagneticPull();
        if (Type.comet != null) TickShedding();

        Vector2 vel = rb.linearVelocity;
        float sp = vel.magnitude;
        bool beingPulled = Time.time < pulledUntil;

        if (followsFlow && !beingPulled && flow != null && sp > 0.01f)
        {
            float current = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
            float desired = flow.AngleAt(rb.position) + personalOffset;
            float next = Mathf.MoveTowardsAngle(current, desired, turnRate * Time.fixedDeltaTime);
            vel = FlowField.FromAngle(next) * sp;
            rb.linearVelocity = vel;
        }

        if (Type.wander > 0f && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            float t = Time.time * 0.35f;
            Vector2 n = new Vector2(Mathf.PerlinNoise(noiseSeedA, t) - 0.5f,
                                    Mathf.PerlinNoise(noiseSeedB, t) - 0.5f) * 2f;
            rb.AddForce(n * (Type.wander * rb.mass));
        }

        if (!Type.restoreSpeed) return;
        if (beingPulled || Time.time < restoreBlockedUntil) return;

        if (sp < 0.01f)
        {
            rb.linearVelocity = Random.insideUnitCircle.normalized * targetSpeed;
            return;
        }
        float newSp = Mathf.MoveTowards(sp, targetSpeed, Type.restoreRate * Time.fixedDeltaTime);
        rb.linearVelocity = vel * (newSp / sp);
    }

    /// ¿Se ha ido de verdad? Tres salvedades que antes no estaban:
    ///  - un cuerpo grande necesita tanto margen como su radio, o desaparece en cuanto
    ///    asoma el borde;
    ///  - nada se recicla hasta haberse dejado ver, porque un coloso nace a 12.74 y el
    ///    límite de reciclado está en 13.2: medio empujón y se iba sin llegar a entrar;
    ///  - pasados 12 s sin entrar, se recicla igual, por si quedó atascado fuera.
    bool IsOutOfPlay(FlowField flow)
    {
        var gb = GameBounds.Instance;
        if (gb != null && !hasEnteredView && gb.CameraRect.Contains(rb.position))
            hasEnteredView = true;

        Rect d = flow.DespawnRect;
        float m = Mathf.Max(0f, WorldRadius);
        if (rb.position.x > d.xMin - m && rb.position.x < d.xMax + m &&
            rb.position.y > d.yMin - m && rb.position.y < d.yMax + m) return false;

        if (!hasEnteredView && Time.time - spawnTime < 12f) return false;
        return true;
    }

    // ---------------------------------------------------------------- órbitas
    void TickOrbit()
    {
        if (orbitHost == null || !orbitHost.isActiveAndEnabled || orbitHost.Type == null)
        {
            BreakOrbit(Vector2.zero);
            return;
        }

        orbitAngle += orbitSpeed * Time.fixedDeltaTime;
        Vector2 center = orbitHost.Body.position;
        Vector2 offset = FlowField.FromAngle(orbitAngle) * orbitRadius;

        // Si el anfitrión se mueve, el cortejo se arrastra DETRÁS: eso es la estela
        Vector2 hv = orbitHost.Body.linearVelocity;
        float trail = orbitHost.Type.moonTrail;
        if (trail > 0f && hv.sqrMagnitude > 0.01f)
            offset -= hv.normalized * (orbitRadius * trail);

        rb.MovePosition(center + offset);
        rb.MoveRotation(rb.rotation + orbitSpin * Time.fixedDeltaTime);
    }

    /// Intenta quedar capturado en la órbita de un cuerpo mayor.
    bool TryCapture(Asteroid host, float impactSpeed)
    {
        if (orbitHost != null || dislodged || Type == null || !Type.canBeCaptured) return false;
        if (host == null || host.Type == null || !host.Type.acceptsMoons) return false;
        if (host.orbitHost != null || host.dislodged) return false;
        if (host.moons.Count >= host.Type.maxMoons) return false;
        if (WorldRadius > Type.captureMaxRadius) return false;
        if (host.Mass < rb.mass * 6f) return false;        // solo te atrapa algo mucho mayor

        // Un roce no es un encuentro: sin esto, un fragmento apoyado dispara decenas de
        // contactos por segundo y acaba siendo capturado siempre (el racimo de piedras).
        if (impactSpeed < Type.captureMinSpeed || impactSpeed > Type.captureMaxSpeed) return false;
        if (captureRollUsed) return false;
        captureRollUsed = true;                            // una sola tirada en toda su vida
        if (Random.value > Type.captureChance * Mathf.Max(0f, host.Type.moonCaptureMultiplier)) return false;

        orbitHost = host;
        host.moons.Add(this);

        Vector2 d = rb.position - host.Body.position;
        if (d.sqrMagnitude < 0.0001f) d = Random.insideUnitCircle.normalized;
        orbitAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        orbitRadius = host.WorldRadius + WorldRadius + Mathf.Max(0.05f, host.Type.moonGap);
        orbitSpeed = Random.Range(host.Type.moonOrbitSpeed.x, host.Type.moonOrbitSpeed.y)
                     * (Random.value < 0.5f ? -1f : 1f);
        orbitSpin = Random.Range(-90f, 90f);

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        SetLayer("AsteroidStatic");
        followsFlow = false;
        targetSpeed = 0f;

        // Deja de contar como fragmento vivo: ahora forma parte del cuerpo, no es basura
        if (countedAsDebris)
        {
            DebrisAlive = Mathf.Max(0, DebrisAlive - 1);
            countedAsDebris = false;
        }
        lifetimeEnd = -1f;
        sr.color = Color.white;
        return true;
    }

    /// Sale de la órbita por la tangente, sumando la velocidad del anfitrión. Es lo que
    /// pasa de verdad al romperse una órbita, y visualmente es lo mejor del sistema.
    void BreakOrbit(Vector2 hostVelocity)
    {
        float r = orbitRadius;
        float w = orbitSpeed;
        var host = orbitHost;

        if (host != null) host.moons.Remove(this);
        orbitHost = null;

        SetLayer("AsteroidMoving");
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        Vector2 tangent = FlowField.FromAngle(orbitAngle + 90f * Mathf.Sign(w));
        Vector2 v = hostVelocity + tangent * (Mathf.Abs(w) * Mathf.Deg2Rad * r);
        rb.linearVelocity = v;
        rb.angularVelocity = orbitSpin + Random.Range(-120f, 120f);
        targetSpeed = Mathf.Max(0.5f, v.magnitude);
        followsFlow = false;
        if (Type == null) return;
        restoreBlockedUntil = Time.time + Type.noRestoreSeconds;

        if (Type.lifetime > 0f)
            lifetimeEnd = Time.time + Type.lifetime * Random.Range(0.85f, 1.15f);
    }

    /// Suelta todas las lunas de este cuerpo (lo llama TryDislodge y la vuelta al pool).
    void ReleaseMoons(Vector2 velocity)
    {
        for (int i = moons.Count - 1; i >= 0; i--)
        {
            var m = moons[i];
            if (m == null) { moons.RemoveAt(i); continue; }
            m.BreakOrbit(velocity);
        }
        moons.Clear();
    }

    // ---------------------------------------------------------------- estáticos soltados
    void DislodgeNearbyStatics()
    {
        if (!staticFilterReady)
        {
            staticFilter = new ContactFilter2D();
            staticFilter.SetLayerMask(LayerMask.GetMask("AsteroidStatic"));
            staticFilter.useTriggers = false;
            staticFilterReady = true;
        }

        float reach = WorldRadius + rb.linearVelocity.magnitude * Time.fixedDeltaTime * 3f + 0.6f;
        int n = Physics2D.OverlapCircle(rb.position, reach, staticFilter, overlapBuffer);
        for (int i = 0; i < n; i++)
        {
            var other = overlapBuffer[i].GetComponentInParent<Asteroid>();
            if (other == null || other == this) continue;
            if (other.orbitHost == this) continue;          // sus propias lunas no se tocan
            other.TryDislodge(this);
        }
    }

    public bool TryDislodge(Asteroid by)
    {
        if (dislodged || Type == null || by == null) return false;

        // Una luna se desprende siempre que la golpee algo bastante más pesado
        if (orbitHost != null)
        {
            if (by.Mass < rb.mass * Type.dislodgeMassRatio) return false;
            ReleaseMoons(rb.linearVelocity);
            BreakOrbit(by.Body.linearVelocity * 0.6f);
            return true;
        }

        if (rb.bodyType != RigidbodyType2D.Kinematic) return false;
        if (!Type.canBeDislodged) return false;
        if (by.Mass < rb.mass * Type.dislodgeMassRatio) return false;

        Vector2 byVel = by.Body.linearVelocity;
        ReleaseMoons(byVel * 0.5f);         // lo que le orbitaba sale disparado y vuelve a matar

        dislodged = true;
        SetLayer("AsteroidMoving");
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;

        rb.linearVelocity = byVel * 0.5f;
        rb.angularVelocity += Random.Range(-60f, 60f);
        targetSpeed = byVel.magnitude;
        followsFlow = false;
        restoreBlockedUntil = Time.time + Type.noRestoreSeconds;
        return true;
    }

    // ---------------------------------------------------------------- campo magnético
    void ApplyMagneticPull()
    {
        if (!movingFilterReady)
        {
            movingFilter = new ContactFilter2D();
            movingFilter.SetLayerMask(LayerMask.GetMask("AsteroidMoving"));
            movingFilter.useTriggers = false;
            movingFilterReady = true;
        }

        float radius = Type.pullRadius;
        int n = Physics2D.OverlapCircle(rb.position, radius, movingFilter, pullBuffer);
        float massLimit = rb.mass * Type.pullMaxTargetMassFraction;

        for (int i = 0; i < n; i++)
        {
            var t = pullBuffer[i].GetComponentInParent<Asteroid>();
            if (t == null || t == this || t.Type == null) continue;
            if (t.Body.bodyType != RigidbodyType2D.Dynamic) continue;
            if (t.WorldRadius > Type.pullMaxTargetRadius) continue;
            if (t.Mass > massLimit) continue;

            Vector2 d = rb.position - t.Body.position;
            float dist = d.magnitude;
            if (dist < 0.05f) continue;

            float span = Mathf.Max(0.01f, radius - WorldRadius);
            float k = Mathf.Clamp01(1f - (dist - WorldRadius) / span);
            t.ReceivePull(d / dist * (Type.pullForce * k * k));
        }
    }

    /// Aceleración, no fuerza: el arrastre no depende de lo que pese el guijarro.
    public void ReceivePull(Vector2 acceleration)
    {
        if (rb.bodyType != RigidbodyType2D.Dynamic) return;
        rb.AddForce(acceleration * rb.mass);
        pulledUntil = Time.time + 0.25f;
    }

    // ---------------------------------------------------------------- cometas
    void TickShedding()
    {
        var prof = Type.comet;
        if (pool == null || !prof.HasPieces) return;

        shedTimer -= Time.fixedDeltaTime;
        if (shedTimer > 0f) return;
        shedTimer = prof.shedInterval * Random.Range(0.7f, 1.3f);

        if (Random.value > prof.shedChance) return;
        if (DebrisAlive >= prof.maxDebrisAlive) return;

        Vector2 vel = rb.linearVelocity;
        Vector2 fwd = vel.sqrMagnitude > 0.0001f ? vel.normalized : Vector2.right;
        Vector2 side = new Vector2(-fwd.y, fwd.x);
        int count = Random.Range(prof.piecesPerShed.x, prof.piecesPerShed.y + 1);

        for (int i = 0; i < count; i++)
        {
            var pieceType = prof.PickPiece();
            if (pieceType == null) continue;

            float size = pieceType.RandomSize();
            float behind = WorldRadius + 0.3f + size * 0.6f;
            Vector2 pos = rb.position - fwd * behind + side * Random.Range(-0.7f, 0.7f) * WorldRadius;

            var piece = pool.Spawn(pieceType, pos, size);
            if (piece == null) continue;
            piece.SetMotion(prof.RollVelocity(vel, rb.angularVelocity, fwd, side, out float spin), spin);
        }
    }

    static Vector2 AvoidPoint()
    {
        if (playerTransform == null)
        {
            var p = FindFirstObjectByType<PlayerController>();
            if (p != null) playerTransform = p.transform;
        }
        return playerTransform != null ? (Vector2)playerTransform.position : Vector2.zero;
    }

    // ---------------------------------------------------------------- choques
    void OnCollisionEnter2D(Collision2D c)
    {
        var other = c.collider.GetComponentInParent<Asteroid>();

        if (other != null)
        {
            float impact = c.relativeVelocity.magnitude;
            ReportEnergy(other, c, impact);
            AbsorbImpact(other, impact);
            TryCapture(other, impact);
            return;
        }

        if (!Lethal) return;
        var player = c.collider.GetComponentInParent<PlayerController>();
        if (player != null) player.Kill();
    }

    /// Energía real del choque: E = ½·m_reducida·v². La dispara solo el más pesado de
    /// los dos, para no contarla dos veces.
    void ReportEnergy(Asteroid other, Collision2D c, float impactSpeed)
    {
        if (HeavyImpact == null || rb.mass < other.Mass) return;
        float m1 = rb.mass, m2 = Mathf.Max(0.0001f, other.Mass);
        float reduced = (m1 * m2) / (m1 + m2);
        float energy = 0.5f * reduced * impactSpeed * impactSpeed;
        Vector2 point = c.contactCount > 0 ? c.GetContact(0).point : rb.position;
        HeavyImpact(point, energy, m1);
    }

    /// Dos casos opuestos:
    ///   Me golpearon -> salgo disparado y me QUEDO con el impulso (keepsImpulse).
    ///                   La transferencia la calcula el motor con las masas reales.
    ///   Choqué yo    -> se elimina el retroceso (nada de goma) y se deja el deslizamiento.
    void AbsorbImpact(Asteroid other, float impactSpeed)
    {
        if (Type == null || rb.bodyType != RigidbodyType2D.Dynamic) return;

        Vector2 before = lastVelocity;
        Vector2 after = rb.linearVelocity;
        float spBefore = before.magnitude;
        float spAfter = after.magnitude;

        float myMass = Mathf.Max(0.0001f, rb.mass);
        float otherMass = Mathf.Max(0.0001f, other.Mass);
        float share = otherMass / (otherMass + myMass);    // 1 = el otro pesa muchísimo más

        if (spAfter > spBefore + 0.05f)
        {
            float boost = Mathf.Lerp(1f, Mathf.Max(1f, Type.launchBoost), share);
            Vector2 v = after * boost;

            // Tope físico del empujón de un imparable. En un choque de frente contra una masa
            // infinita que viaja a v, el cuerpo ligero sale como mucho a 2v: más que eso no
            // existe. Sin este tope el motor resuelve la penetración de un cinemático enorme
            // disparando los guijarros a velocidades absurdas, y el jugador muere sí o sí.
            if (other.Unstoppable)
            {
                float cap = other.Body.linearVelocity.magnitude *
                            Mathf.Max(0.1f, other.Type.shoveSpeedMultiplier);
                float sp = v.magnitude;
                if (cap > 0.01f && sp > cap) v *= cap / sp;
            }
            else if (Type.impactSpeedCap > 0f && targetSpeed > 0.01f)
            {
                float cap = targetSpeed * Type.impactSpeedCap;
                float sp = v.magnitude;
                if (sp > cap) v *= cap / sp;
            }

            rb.linearVelocity = v;
            rb.angularVelocity += Random.Range(-1f, 1f) * 200f * share;
            // Conserva el impulso en vez de frenar: sin esto, el empujón se deshace en segundos
            targetSpeed = Mathf.Lerp(targetSpeed, Mathf.Max(targetSpeed, v.magnitude),
                                     Mathf.Clamp01(Type.keepsImpulse));
            restoreBlockedUntil = Time.time + Type.noRestoreSeconds * 0.5f;
            return;
        }

        if (spBefore > 0.01f)
        {
            Vector2 dir = before / spBefore;
            float along = Vector2.Dot(after, dir);
            if (along < 0f)
            {
                Vector2 back = dir * along;
                after = (after - back) + back * (1f - Mathf.Clamp01(Type.reboundDamping));
            }
        }

        after *= 1f - Mathf.Clamp01(Type.impactEnergyLoss * (0.4f + 0.9f * share));

        float floor = targetSpeed * Mathf.Clamp01(Type.impactSpeedFloor);
        float spNow = after.magnitude;
        if (floor > 0.01f && spNow < floor)
            after = (spNow > 0.01f ? after / spNow : Random.insideUnitCircle.normalized) * floor;

        rb.linearVelocity = after;
        rb.angularVelocity *= 1f - Mathf.Clamp01(Type.impactSpinLoss * (0.4f + 0.9f * share));
        restoreBlockedUntil = Time.time + Type.noRestoreSeconds;
    }

    void OnDisable()
    {
        if (countedAsDebris)
        {
            DebrisAlive = Mathf.Max(0, DebrisAlive - 1);
            countedAsDebris = false;
        }
        if (orbitHost != null) { orbitHost.moons.Remove(this); orbitHost = null; }
        // Devolverlas a dinámicas: si solo les quitara el anfitrión se quedarían
        // cinemáticas y congeladas para siempre en mitad del nivel.
        ReleaseMoons(Vector2.zero);
    }

    public void Release()
    {
        if (pool != null) pool.Release(this);
        else gameObject.SetActive(false);
    }
}
