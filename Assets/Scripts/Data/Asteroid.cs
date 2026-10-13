using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(SpriteRenderer))]
public class Asteroid : MonoBehaviour
{
    /// Multiplicador de velocidad del nivel actual (lo fija LevelPopulator)
    public static float SpeedMultiplier = 1f;
    /// Desprendimientos vivos en este momento
    public static int DebrisAlive { get; private set; }

    public AsteroidTypeData Type { get; private set; }
    public float Size { get; private set; }
    public float Density { get; private set; }
    /// Radio aproximado del collider en unidades de mundo
    public float WorldRadius { get; private set; }
    public float Mass => rb != null ? rb.mass : 1f;
    public bool IsDislodged => dislodged;
    /// Un estático soltado de su sitio se mueve, y lo que se mueve mata
    public bool Lethal => Type != null && (Type.killsPlayer || dislodged);
    /// Prefab del que salió (lo usa el AsteroidPool)
    public Asteroid SourcePrefab { get; set; }

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
    bool followsFlow, restoreInterpolation, dislodged, countedAsDebris;

    static readonly Collider2D[] overlapBuffer = new Collider2D[16];
    static ContactFilter2D staticFilter;
    static bool staticFilterReady;
    static Transform playerTransform;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SpeedMultiplier = 1f;
        DebrisAlive = 0;
        staticFilterReady = false;
        playerTransform = null;
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
        Size = size;
        pool = owner;
        gameObject.name = type.id;

        dislodged = false;
        lifetimeEnd = type.lifetime > 0f ? Time.time + type.lifetime * Random.Range(0.85f, 1.15f) : -1f;
        sr.color = Color.white;

        if (type.behavior == AsteroidBehavior.Debris && !countedAsDebris)
        {
            DebrisAlive++;
            countedAsDebris = true;
        }

        bool isStatic = type.behavior == AsteroidBehavior.Static;
        SetLayer(isStatic ? "AsteroidStatic" : "AsteroidMoving");

        transform.localScale = Vector3.one * size;
        sr.sprite = sprite;
        FitCollider(sprite, type.colliderFactor);
        col.sharedMaterial = type.material;
        WorldRadius = col.radius * size;

        // Peso: crece mucho con el tamaño y cada asteroide tiene su propia densidad
        Density = Random.Range(type.densityRange.x, type.densityRange.y);
        rb.mass = Mathf.Max(0.05f, Mathf.Pow(size, type.massExponent) * Density * type.massPerArea);
        rb.gravityScale = 0f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;

        sizeT = Mathf.InverseLerp(type.sizeRange.x, type.sizeRange.y, size);
        noiseSeedA = Random.value * 100f;
        noiseSeedB = Random.value * 100f;
        shedTimer = type.comet != null ? Random.Range(0.3f, Mathf.Max(0.35f, type.comet.shedInterval)) : 0f;

        if (isStatic)
        {
            // No se mueve: solo gira en su sitio
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
            // Nace quieto; quien lo desprende le da su movimiento con SetMotion
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
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Launch(position);
        }

        // La estela (si la hay) empieza limpia en la nueva posición
        foreach (var ps in tails) { ps.Clear(true); ps.Play(true); }
    }

    void SetLayer(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0) Debug.LogError("Falta la capa " + layerName + ".", this);
        else gameObject.layer = layer;
    }

    /// Movimiento inicial propio (lo usan los desprendimientos).
    public void SetMotion(Vector2 velocity, float spin)
    {
        rb.linearVelocity = velocity;
        rb.angularVelocity = spin;
        targetSpeed = velocity.magnitude;
        followsFlow = false;
    }

    // Coloca el asteroide sin que la interpolación lo "arrastre" desde su posición anterior
    void Teleport(Vector2 position)
    {
        float angle = Random.Range(0f, 360f);
        rb.interpolation = RigidbodyInterpolation2D.None;
        restoreInterpolation = true;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
        rb.position = position;
        rb.rotation = angle;
    }

    // Tira los dados del movimiento: velocidad, giro y si sigue o no la corriente
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

        // Si nace fuera de pantalla, nunca apunta hacia afuera
        var gb = GameBounds.Instance;
        if (gb != null && !gb.CameraRect.Contains(position))
        {
            Vector2 toCenter = gb.CameraRect.center - position;
            if (Vector2.Dot(dir, toCenter) < 0f) dir = -dir;
        }

        rb.linearVelocity = dir * targetSpeed;
        rb.angularVelocity = spin;
    }

    // Usa la malla del sprite (Mesh Type Tight) para conocer el tamaño real del dibujo
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
        // Las piezas sueltas no duran para siempre: se desvanecen y desaparecen
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
        if (restoreInterpolation)
        {
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            restoreInterpolation = false;
        }

        if (Type == null) return;
        if (rb.bodyType == RigidbodyType2D.Kinematic) return;   // estático que sigue en su sitio

        var flow = FlowField.Instance;

        // Salió del área
        if (flow != null && !flow.DespawnRect.Contains(rb.position))
        {
            bool replace = Type.recycle && Type.behavior != AsteroidBehavior.Debris && !dislodged;
            if (replace)
            {
                if (pool != null && pool.NotifyExited(this)) return;      // el generador lo sustituye
                Launch(flow.RandomEntryPoint(AvoidPoint(), 2.5f));         // plan B
            }
            else Release();                                                // piezas y estáticos soltados: sin reemplazo
            return;
        }

        if (Type.dislodgesStatics) DislodgeNearbyStatics();
        if (Type.comet != null) TickShedding();

        Vector2 vel = rb.linearVelocity;
        float sp = vel.magnitude;

        // Seguir la corriente: la trayectoria gira poco a poco hacia ella (así hace arcos)
        if (followsFlow && flow != null && sp > 0.01f)
        {
            float current = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
            float desired = flow.AngleAt(rb.position) + personalOffset;
            float next = Mathf.MoveTowardsAngle(current, desired, turnRate * Time.fixedDeltaTime);
            vel = FlowField.FromAngle(next) * sp;
            rb.linearVelocity = vel;
        }

        // Perturbaciones suaves: las trayectorias no son perfectas
        if (Type.wander > 0f)
        {
            float t = Time.time * 0.35f;
            Vector2 n = new Vector2(Mathf.PerlinNoise(noiseSeedA, t) - 0.5f,
                                    Mathf.PerlinNoise(noiseSeedB, t) - 0.5f) * 2f;
            rb.AddForce(n * (Type.wander * rb.mass));
        }

        // Tras un choque, la velocidad vuelve lentamente a su valor
        if (!Type.restoreSpeed) return;

        if (sp < 0.01f)
        {
            rb.linearVelocity = Random.insideUnitCircle.normalized * targetSpeed;
            return;
        }
        float newSp = Mathf.MoveTowards(sp, targetSpeed, Type.restoreRate * Time.fixedDeltaTime);
        rb.linearVelocity = vel * (newSp / sp);
    }

    // ---------------------------------------------------------------- estáticos soltados
    // Se hace ANTES del choque: así el estático ya es dinámico cuando el motor resuelve el golpe,
    // y el coloso apenas se desvía (en vez de rebotar contra algo que no se puede mover).
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
            if (other != null && other != this) other.TryDislodge(this);
        }
    }

    public bool TryDislodge(Asteroid by)
    {
        if (dislodged || Type == null || !Type.canBeDislodged) return false;
        if (rb.bodyType != RigidbodyType2D.Kinematic) return false;
        if (by.Mass < rb.mass * Type.dislodgeMassRatio) return false;

        dislodged = true;
        SetLayer("AsteroidMoving");
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;

        Vector2 byVel = by.rb.linearVelocity;
        rb.linearVelocity = byVel * 0.5f;                  // el choque hace el resto
        rb.angularVelocity += Random.Range(-60f, 60f);
        targetSpeed = byVel.magnitude;
        followsFlow = false;
        return true;
    }

    // ---------------------------------------------------------------- cometas
    void TickShedding()
    {
        var prof = Type.comet;
        if (pool == null || !prof.HasPieces) return;

        shedTimer -= Time.fixedDeltaTime;
        if (shedTimer > 0f) return;
        shedTimer = prof.shedInterval * Random.Range(0.7f, 1.3f);

        if (Random.value > prof.shedChance) return;       // no siempre se desprende
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
            float behind = WorldRadius + 0.3f + size * 0.6f;     // fuera del cuerpo del cometa
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

    void OnCollisionEnter2D(Collision2D c)
    {
        if (!Lethal) return;
        var player = c.collider.GetComponentInParent<PlayerController>();
        if (player != null) player.Kill();
    }

    void OnDisable()
    {
        if (countedAsDebris)
        {
            DebrisAlive = Mathf.Max(0, DebrisAlive - 1);
            countedAsDebris = false;
        }
    }

    public void Release()
    {
        if (pool != null) pool.Release(this);
        else gameObject.SetActive(false);
    }
}