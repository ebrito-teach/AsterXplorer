using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(SpriteRenderer))]
public class Asteroid : MonoBehaviour
{
    /// Multiplicador de velocidad del nivel actual (lo fija LevelPopulator)
    public static float SpeedMultiplier = 1f;
    /// Desprendimientos y fragmentos vivos en este momento
    public static int DebrisAlive { get; private set; }
    /// Masa típica de un estático en este nivel (la mediana). Contra ella se mide si un
    /// estático es "masivo": sus satélites giran más rápido y su campo frena más
    public static float StaticMassRef = 1f;
    /// Cambia cada vez que el pool reutiliza este objeto. Sirve para saber si una
    /// referencia guardada sigue apuntando al mismo asteroide o ya es otro.
    public int Serial { get; private set; }
    static int serialCounter;

    /// Choque fuerte entre dos cuerpos: punto, dirección de salida de la eyecta, energía
    /// del impacto y masa del mayor. Lo escucha ImpactFeedback.
    public static event System.Action<Vector2, Vector2, float, float> HeavyImpact;
    /// Un cometa acaba de soltar piezas: dónde y con qué velocidad. Lo usa el BuffSpawner
    /// para que uno de esos fragmentos pueda ser un buff
    public static event System.Action<Vector2, Vector2> CometShed;

    /// Mundo oscuro: esta pieza empuja pero no mata. Lo pone LevelPopulator al crearla y lo
    /// heredan sus fragmentos y su estela
    public bool Harmless { get; set; }

    /// Detenido por TimeFreeze
    public bool IsFrozen => frozenUntil > 0f || WorldFrozen;
    float frozenUntil;
    bool[] tailsPlaying;
    Vector2 frozenVel;
    float frozenSpin;
    RigidbodyType2D frozenBody;
    Color frozenColor;

    public AsteroidTypeData Type { get; private set; }
    public float Size { get; private set; }
    public float Density { get; private set; }
    /// Radio aproximado del collider en unidades de mundo
    public float WorldRadius { get; private set; }
    public float Mass => rb != null ? rb.mass : 1f;
    public bool IsDislodged => dislodged;
    /// Está orbitando a otro cuerpo
    public bool IsOrbiting => orbitHost != null;
    /// Lo que orbita mata según su anfitrión (-1), o según esto: 1 mata, 0 no (cinturón de planeta)
    int orbitLethal = -1;

    /// Decide si sus lunas actuales matan, sin mirar al anfitrión (el cinturón de un planeta sí, sus lunas no)
    public void SetMoonsLethal(bool lethal)
    {
        foreach (var m in moons) if (m != null) m.orbitLethal = lethal ? 1 : 0;
    }
    /// Lo que mata: lo que se mueve por su cuenta, un estático soltado, y las lunas
    /// de un cuerpo que las tenga declaradas mortales (coloso y cometa; el estático no).
    public bool Lethal
    {
        get
        {
            if (Type == null || swallowed) return false;
            // Mundo oscuro: lo que fluye es inofensivo salvo los raros que sí matan. Las lunas
            // (satélites de un campo) se rigen por su anfitrión, como siempre
            if (Harmless && orbitHost == null) return false;
            // Lo que orbita mata según su anfitrión, sea del tamaño que sea: los satélites de
            // un campo son peligrosos aunque sean pequeños
            if (orbitHost != null)
                return orbitLethal >= 0 ? orbitLethal == 1 : orbitHost.Type != null && orbitHost.Type.moonsAreLethal;
            // Un trozo por debajo del radio mínimo empuja, pero no mata
            if (Type.lethalMinRadius > 0f && WorldRadius < Type.lethalMinRadius) return false;
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
    int lane = -1;
    float shedTimer;
    float restoreBlockedUntil;
    float pulledUntil;
    float wakeAccum;
    Vector2 lastVelocity;      // velocidad con la que entró en el paso de física
    bool followsFlow, restoreInterpolation, dislodged, countedAsDebris, isTrail, swallowed;
    bool captureRollUsed;
    bool unstoppable;          // cinemático a propósito: nada puede desviarlo
    bool hasEnteredView;       // ya se ha dejado ver al menos una vez
    float spawnTime;

    // Órbita
    Asteroid orbitHost;
    float orbitRadius, orbitAngle, orbitSpeed, orbitSpin;
    float orbitRadiusNow;      // se acerca a orbitRadius poco a poco: una luna capturada no salta
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
        CometShed = null;
        WorldFrozen = false;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CircleCollider2D>();
        sr = GetComponent<SpriteRenderer>();
        tails = GetComponentsInChildren<ParticleSystem>(true);
        baseOrder = sr != null ? sr.sortingOrder : 0;
    }

    int baseOrder;
    /// Los planetas se dibujan por debajo del resto de rocas (y su anillo con ellos): un coloso
    /// o una roca que pase por encima se ve delante, nunca cortado por el anillo
    public const int PlanetOrderOffset = -5;

    public void Init(AsteroidTypeData type, Sprite sprite, Vector2 position, float size, AsteroidPool owner)
    {
        Type = type;
        pool = owner;
        gameObject.name = type.id;
        if (sr != null) sr.sortingOrder = baseOrder + (type.planet ? PlanetOrderOffset : 0);
        Serial = ++serialCounter;
        frozenUntil = 0f;

        dislodged = false;
        Harmless = false;
        swallowed = false;
        if (rb != null) rb.simulated = true;
        if (col != null) col.enabled = true;
        isTrail = false;
        orbitHost = null;
        orbitLethal = -1;
        moons.Clear();
        restoreBlockedUntil = 0f;
        pulledUntil = 0f;
        wakeAccum = 0f;
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

    /// Sale despedido y se va frenando solo hasta quedarse quieto.
    ///
    /// La diferencia con SetMotion está en targetSpeed: allí se guarda la velocidad que se le
    /// da, así que el cuerpo la mantiene para siempre. Aquí se pone a CERO, y el restaurado
    /// —que ya existía— tira de la velocidad hacia abajo a razón de restoreRate hasta pararla.
    /// Esa deceleración es justo lo que faltaba: antes el escombro aparecía quieto en su sitio
    /// y se veía colocado a mano, en vez de arrancado.
    public void Eject(Vector2 velocity, float spin)
    {
        rb.linearVelocity = velocity;
        rb.angularVelocity = spin;
        targetSpeed = 0f;
        lastVelocity = velocity;
        followsFlow = false;
    }

    /// Cambia la vida restante. 0 o menos = no caduca nunca.
    public void SetLifetime(float seconds)
    {
        lifetimeEnd = seconds > 0f ? Time.time + seconds : -1f;
        if (seconds <= 0f && sr != null)
        {
            Color c = sr.color;
            c.a = 1f;
            sr.color = c;
        }
    }

    /// Pasa a ser rastro de cometa: deja de contar como fragmento suelto (lo limita
    /// CometTrail con su propio tope) y no caduca solo.
    public void MakeTrail()
    {
        if (countedAsDebris)
        {
            DebrisAlive = Mathf.Max(0, DebrisAlive - 1);
            countedAsDebris = false;
        }
        isTrail = true;
        SetLifetime(0f);
    }

    void Teleport(Vector2 position)
    {
        // Un cuerpo con la estela pintada no puede nacer mirando a cualquier lado: se
        // colocaría con la cola por delante hasta que FixedUpdate lo enderezara, y eso se ve
        float angle = (Type != null && Type.faceVelocity) ? rb.rotation : Random.Range(0f, 360f);
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
        // Su carril: lentos y rápidos por bandas distintas (cometas y colosos, libres)
        lane = -1;
        if (followsFlow)
        {
            int kind = FlowField.KindOf(Type);
            lane = flow.LaneAt(position, kind);
            if (lane < 0) lane = flow.PickLane(kind);
        }

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

        TrySpawnEscort();
    }

    /// Campo de satélites de un estático: nace ya rodeado de cuerpos en órbita a varias alturas.
    ///
    /// Tercera ley de Kepler: el periodo al cuadrado va con el radio al cubo, o sea, la velocidad
    /// angular cae con r^1.5. Los cercanos giran deprisa y los lejanos despacio, y un anfitrión
    /// más masivo los hace girar a todos más rápido (raíz de la masa). Todos en el mismo
    /// sentido, como en un sistema real: el material de un disco gira junto.
    public void SpawnField(AsteroidTypeData[] types, int count, Vector2 distance, float degPerSecAtRef, float refRadius)
    {
        if (pool == null || types == null || types.Length == 0 || count <= 0) return;
        float massK = Mathf.Sqrt(Mathf.Max(0.001f, Mass) / Mathf.Max(0.001f, StaticMassRef));
        float sense = Random.value < 0.5f ? -1f : 1f;
        float step = 360f / count;
        float a0 = Random.Range(0f, 360f);

        for (int i = 0; i < count; i++)
        {
            var type = types[Random.Range(0, types.Length)];
            if (type == null || type.sprites == null || type.sprites.Length == 0) continue;
            var m = pool.Spawn(type, rb.position, type.RandomSize());
            if (m == null) continue;

            float r = WorldRadius + m.WorldRadius + Random.Range(distance.x, distance.y);
            float w = degPerSecAtRef * massK * Mathf.Pow(Mathf.Max(0.1f, refRadius) / r, 1.5f);
            w = Mathf.Clamp(w, 6f, 240f) * sense;
            m.EnterOrbit(this, a0 + step * i + Random.Range(-step * 0.35f, step * 0.35f), r, w);
        }
    }

    /// Anillos, como los de Saturno (solo en los estáticos grandes): a radios fijos, muchas
    /// lunas pequeñas repartidas por igual y alguna mayor, todas en el mismo sentido. Cada
    /// anillo gira a su velocidad de Kepler: los de dentro, más rápido. Devuelve cuántas creó
    public int SpawnRings(AsteroidTypeData[] types, int rings, float firstGap, float spacing, float moonSpacing,
                          float moonScale, float bigChance, int maxPerRing, float degPerSecAtRef, float refRadius)
    {
        if (pool == null || types == null || types.Length == 0 || rings <= 0) return 0;
        float massK = Mathf.Sqrt(Mathf.Max(0.001f, Mass) / Mathf.Max(0.001f, StaticMassRef));
        float sense = Random.value < 0.5f ? -1f : 1f;
        int made = 0;
        for (int k = 0; k < rings; k++)
        {
            float r = WorldRadius + firstGap + k * spacing;
            int n = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * r / Mathf.Max(0.2f, moonSpacing)), 4, maxPerRing);
            float w = degPerSecAtRef * massK * Mathf.Pow(Mathf.Max(0.1f, refRadius) / r, 1.5f);
            w = Mathf.Clamp(w, 4f, 200f) * sense;
            float step = 360f / n, a0 = Random.Range(0f, 360f);
            for (int i = 0; i < n; i++)
            {
                var type = types[Random.Range(0, types.Length)];
                if (type == null || type.sprites == null || type.sprites.Length == 0) continue;
                float size = type.RandomSize() * moonScale * (Random.value < bigChance ? 2f : 1f);
                var m = pool.Spawn(type, rb.position, size);
                if (m == null) continue;
                m.EnterOrbit(this, a0 + step * i + Random.Range(-step * 0.12f, step * 0.12f),
                             r + Random.Range(-0.08f, 0.08f), w);
                made++;
            }
        }
        return made;
    }

    /// Cinturón alrededor de un estático: una banda ancha de rocas agrupadas, con huecos,
    /// girando en el mismo sentido; cada una a su velocidad de Kepler (rotación diferencial: la
    /// banda se estira un poco con el tiempo, como un cinturón real). Devuelve cuántas creó
    public int SpawnBelt(AsteroidTypeData[] types, float innerGap, float width, int count, int clusters,
                         float clusterSpread, float degPerSecAtRef, float refRadius)
    {
        if (pool == null || types == null || types.Length == 0 || count <= 0) return 0;
        float massK = Mathf.Sqrt(Mathf.Max(0.001f, Mass) / Mathf.Max(0.001f, StaticMassRef));
        float sense = Random.value < 0.5f ? -1f : 1f;
        clusters = Mathf.Max(1, clusters);
        var centers = new float[clusters];
        for (int c = 0; c < clusters; c++) centers[c] = Random.Range(0f, 360f);
        int made = 0;
        for (int i = 0; i < count; i++)
        {
            var type = types[Random.Range(0, types.Length)];
            if (type == null || type.sprites == null || type.sprites.Length == 0) continue;
            // La mayoría en grupos; unas pocas sueltas por toda la banda
            float ang = Random.value < 0.8f
                ? centers[Random.Range(0, clusters)] + (Random.value + Random.value - 1f) * clusterSpread
                : Random.Range(0f, 360f);
            var m = pool.Spawn(type, rb.position, type.RandomSize());
            if (m == null) continue;
            float r = WorldRadius + innerGap + m.WorldRadius + Random.Range(0f, width);
            float w = degPerSecAtRef * massK * Mathf.Pow(Mathf.Max(0.1f, refRadius) / r, 1.5f);
            w = Mathf.Clamp(w, 3f, 120f) * sense;
            m.EnterOrbit(this, ang, r, w);
            made++;
        }
        return made;
    }

    /// Un cuerpo grande puede ENTRAR al mapa ya acompañado.
    ///
    /// Antes el séquito solo se formaba chocando durante la partida, así que el coloso llegaba
    /// siempre desnudo y las lunas eran un accidente. Naciendo con ellas, el séquito pasa a ser
    /// parte de lo que el cuerpo ES — y como el número puede empezar en cero, unos llegan solos
    /// y otros con corte, que es lo que hace que verlo aparecer tenga suspense.
    void TrySpawnEscort()
    {
        if (pool == null || Type == null || !Type.acceptsMoons) return;
        if (Type.escortChance <= 0f || Random.value > Type.escortChance) return;

        var lunaTipo = Type.escortType != null ? Type.escortType : Type.wakeType;
        if (lunaTipo == null || lunaTipo.sprites == null || lunaTipo.sprites.Length == 0) return;

        // Sin esto, un tipo que se escoltara a sí mismo se llamaría en cadena sin fin: cada
        // luna nacería con su propio séquito.
        if (lunaTipo == Type) return;

        int n = Random.Range(Type.escortCount.x, Type.escortCount.y + 1);
        n = Mathf.Min(n, Mathf.Max(0, Type.maxMoons - moons.Count));
        if (n <= 0) return;

        // Repartidas en el círculo con una desviación, no clavadas a intervalos exactos:
        // una formación perfecta se lee como un adorno, no como cuerpos atrapados
        float paso = 360f / n;
        float base0 = Random.Range(0f, 360f);

        for (int i = 0; i < n; i++)
        {
            float size = lunaTipo.RandomSize();
            var luna = pool.Spawn(lunaTipo, rb.position, size);
            if (luna == null) continue;
            luna.EnterOrbit(this, base0 + paso * i + Random.Range(-paso * 0.3f, paso * 0.3f));
        }
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
        // Congelado, el tiempo no corre para él: su vida restante se queda igual
        if (WorldFrozen) { lifetimeEnd += Time.deltaTime; return; }
        float left = lifetimeEnd - Time.time;
        if (left <= 0f) { Release(); return; }
        if (left < 2f)
        {
            Color c = sr.color;
            c.a = left / 2f;
            sr.color = c;
        }
    }

    /// TimeFreeze: todo el universo se detiene. Mientras esté a true, cada roca (las que ya
    /// hay y las que pudieran nacer) se queda quieta, sin que nada la mueva, con su estela en
    /// pausa y su cuenta de vida parada. Lo pone BuffSystem
    public static bool WorldFrozen;
    /// Color que toman las rocas congeladas
    public static Color FreezeTint = new Color(0.65f, 0.85f, 1f);

    void EnterFreeze()
    {
        frozenUntil = 1f;
        frozenVel = rb.linearVelocity;
        frozenSpin = rb.angularVelocity;
        frozenBody = rb.bodyType;
        frozenColor = sr.color;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
        sr.color = new Color(frozenColor.r * FreezeTint.r, frozenColor.g * FreezeTint.g, frozenColor.b * FreezeTint.b, frozenColor.a);
        if (tails != null)
        {
            if (tailsPlaying == null || tailsPlaying.Length != tails.Length) tailsPlaying = new bool[tails.Length];
            for (int i = 0; i < tails.Length; i++)
            {
                tailsPlaying[i] = tails[i] != null && tails[i].isPlaying;
                if (tailsPlaying[i]) tails[i].Pause(true);
            }
        }
    }

    void Unfreeze()
    {
        frozenUntil = 0f;
        if (rb == null) return;
        rb.bodyType = frozenBody;
        rb.linearVelocity = frozenVel;
        rb.angularVelocity = frozenSpin;
        lastVelocity = frozenVel;
        sr.color = new Color(frozenColor.r, frozenColor.g, frozenColor.b, sr.color.a);
        if (tails != null && tailsPlaying != null)
            for (int i = 0; i < tails.Length && i < tailsPlaying.Length; i++)
                if (tailsPlaying[i] && tails[i] != null && tails[i].gameObject.activeInHierarchy) tails[i].Play(true);
    }

    /// El primer plano de la muerte la congela (solo a esta roca) mientras dura
    public bool Held { get; set; }

    void FixedUpdate()
    {
        if (swallowed) return;
        if (Held && rb != null) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; return; }
        if (WorldFrozen && Type != null && rb != null)
        {
            if (frozenUntil <= 0f) EnterFreeze();
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            return;
        }
        if (frozenUntil > 0f) Unfreeze();
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
            // Un estático soltado por el coloso TAMBIÉN se sustituye. Antes no, y por eso
            // cada paso del coloso vaciaba el nivel de forma permanente.
            bool replace = Type.recycle && Type.behavior != AsteroidBehavior.Debris;
            if (replace)
            {
                if (pool != null && pool.NotifyExited(this)) return;
                Launch(flow.RandomEntryPoint(AvoidPoint(), 2.5f, Type));
            }
            else Release();
            return;
        }

        if (Type.dislodgesStatics) DislodgeNearbyStatics();
        if (Type.pullRadius > 0f) ApplyMagneticPull();
        if (Type.wakeType != null) TickWake();
        if (Type.comet != null) TickShedding();

        Vector2 vel = rb.linearVelocity;
        float sp = vel.magnitude;
        bool beingPulled = Time.time < pulledUntil;

        if (followsFlow && !beingPulled && flow != null && sp > 0.01f)
        {
            float current = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
            float desired = flow.AngleAt(rb.position) + personalOffset + flow.LaneCorrection(lane, rb.position);
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

        // El restaurado va en su propio método porque tiene tres salidas tempranas, y
        // orientar el sprite tiene que ocurrir en todas ellas: dejarlo dentro significaba
        // que un cometa frenado o arrastrado por un imán se quedaba con la cola torcida.
        RestoreSpeed(vel, sp, beingPulled);
        FaceTravel();
    }

    void RestoreSpeed(Vector2 vel, float sp, bool beingPulled)
    {
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

    /// Alinea el sprite con su marcha, para los cuerpos que llevan la estela pintada.
    ///
    /// Va al FINAL del paso, con la velocidad ya resuelta por la corriente y el restaurado:
    /// orientarlo antes lo dejaría mirando a la velocidad del fotograma anterior, que con un
    /// cometa girando por la corriente se nota como un bandazo de la cola.
    ///
    /// También anula el giro propio cada paso. No basta con poner spinRange a cero: los
    /// choques suman angularVelocity directamente, y un solo golpe dejaría al cometa dando
    /// vueltas con la cola de bandera el resto del nivel.
    void FaceTravel()
    {
        if (Type == null || !Type.faceVelocity) return;

        rb.angularVelocity = 0f;

        Vector2 v = rb.linearVelocity;
        if (v.sqrMagnitude < 0.0004f) return;

        float want = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg + Type.spriteForwardOffset;
        rb.MoveRotation(want);
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

        // El rastro de un cometa forma parte del mapa: dura todo el nivel aunque la cámara
        // se aleje. Solo se va si algo lo empuja fuera del mundo.
        if (isTrail)
            return gb != null && !gb.Contains(rb.position, -Mathf.Max(1f, WorldRadius));

        Rect d = flow.DespawnRect;
        float m = Mathf.Max(0f, WorldRadius);
        if (rb.position.x > d.xMin - m && rb.position.x < d.xMax + m &&
            rb.position.y > d.yMin - m && rb.position.y < d.yMax + m) return false;

        // La gracia de "aún no se ha dejado ver" es SOLO para los imparables, que nacen muy
        // lejos por su tamaño. Aplicada a todos, un asteroide que nacía fuera y se alejaba
        // vivía 12 s volando hacia ninguna parte: de ahí los choques en (-17, 18.9).
        if (!hasEnteredView && unstoppable && Time.time - spawnTime < 12f) return false;
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

        Vector2 hv = orbitHost.Body.linearVelocity;

        // Barrido desigual: rápido por delante, lento por detrás.
        //
        // Una órbita uniforme reparte las lunas por igual alrededor del cuerpo, y entonces las
        // que quedan delante parecen pegadas al morro. Frenándolas en la cola se amontonan ahí
        // solas — sin teletransportar nada, solo por el tiempo que pasan en cada tramo. Es el
        // mismo principio que la segunda ley de Kepler: barrer áreas iguales obliga a ir rápido
        // cerca y lento lejos, y aquí el "lejos" es la cola.
        float barrido = orbitSpeed;
        float sesgo = orbitHost.Type.moonTailBias;
        if (sesgo > 0f && hv.sqrMagnitude > 0.01f)
        {
            float rumbo = Mathf.Atan2(hv.y, hv.x) * Mathf.Rad2Deg;
            float delante = Mathf.Cos((orbitAngle - rumbo) * Mathf.Deg2Rad);   // +1 morro, -1 cola
            barrido *= 1f + sesgo * delante;
        }

        orbitAngle += barrido * Time.fixedDeltaTime;
        orbitRadiusNow = Mathf.MoveTowards(orbitRadiusNow, orbitRadius, 2.5f * Time.fixedDeltaTime);
        Vector2 center = orbitHost.Body.position;
        float rNow = orbitRadiusNow;
        Vector2 offset = FlowField.FromAngle(orbitAngle) * rNow;

        // Si el anfitrión se mueve, el séquito se estira detrás y se cierra en embudo. Antes
        // se desplazaba el anillo entero hacia atrás, y las lunas de delante acababan pegadas
        // al casco: por eso parecía que se le adherían. Ahora lo de delante se queda en su
        // radio y solo lo de detrás se alarga y se estrecha
        float trail = orbitHost.Type.moonTrail;
        if (trail > 0f && hv.sqrMagnitude > 0.01f)
        {
            Vector2 f = hv.normalized;
            Vector2 s = new Vector2(-f.y, f.x);
            float rel = (orbitAngle - Mathf.Atan2(f.y, f.x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float c = Mathf.Cos(rel), sn = Mathf.Sin(rel);
            float back = Mathf.Max(0f, -c);                          // 0 delante y a los lados, 1 en la cola
            float along = c * rNow * (1f + back * trail * 2.5f);
            float lateral = sn * rNow * (1f - orbitHost.Type.moonFunnel * back);
            offset = f * along + s * lateral;
        }

        // Nunca más cerca del casco que su hueco mínimo
        float minR = orbitHost.WorldRadius + WorldRadius + Mathf.Max(0.05f, orbitHost.Type.moonGap) * 0.8f;
        if (offset.sqrMagnitude < minR * minR && rNow >= minR)
            offset = (offset.sqrMagnitude > 0.0001f ? offset.normalized : FlowField.FromAngle(orbitAngle)) * minR;

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

        Vector2 d = rb.position - host.Body.position;
        if (d.sqrMagnitude < 0.0001f) d = Random.insideUnitCircle.normalized;
        // Capturado tras un choque: se aleja hasta su órbita desde donde está, sin saltar
        EnterOrbit(host, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, false);
        return true;
    }

    /// Entra en la órbita de un anfitrión. Lo usan la captura por choque y el séquito de
    /// nacimiento, para que una luna capturada y una que llegó con el cuerpo se comporten
    /// exactamente igual.
    public void EnterOrbit(Asteroid host, float angleDeg, bool placeNow = true)
    {
        // El hueco extra es lo que convierte una costra en un séquito. Con solo moonGap todas
        // las lunas orbitaban pegadas a la superficie y el conjunto se leía como un solo
        // cuerpo con bultos; repartiéndolas en varias alturas se ve lo que son — cuerpos
        // sueltos que viajan con él, no adheridos a él.
        float extra = Random.Range(host.Type.moonOrbitExtra.x, host.Type.moonOrbitExtra.y);
        float radius = host.WorldRadius + WorldRadius
                     + Mathf.Max(0.05f, host.Type.moonGap) + Mathf.Max(0f, extra);
        float speed = Random.Range(host.Type.moonOrbitSpeed.x, host.Type.moonOrbitSpeed.y)
                      * (Random.value < 0.5f ? -1f : 1f);
        EnterOrbit(host, angleDeg, radius, speed, placeNow);
    }

    /// Con radio y velocidad angular (grados/s) concretos. Lo usa el campo de satélites, que
    /// los calcula con Kepler.
    public void EnterOrbit(Asteroid host, float angleDeg, float radius, float speedDeg, bool placeNow = true)
    {
        orbitHost = host;
        host.moons.Add(this);
        orbitAngle = angleDeg;
        orbitRadius = radius;
        orbitSpeed = speedDeg;
        orbitSpin = Random.Range(-90f, 90f);

        if (placeNow)
        {
            // Ya en su sitio: si naciera en el centro del anfitrión, un fotograma estaría dentro de él
            Vector2 at = host.Body.position + FlowField.FromAngle(angleDeg) * radius;
            rb.position = at;
            transform.position = new Vector3(at.x, at.y, transform.position.z);
            orbitRadiusNow = radius;
        }
        else orbitRadiusNow = Vector2.Distance(rb.position, host.Body.position);

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
        orbitLethal = -1;

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
        if (Type.planet) return false;   // un planeta no se mueve de su sitio

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

        // Lo aparta hacia SU COSTADO y lo deja frenando, no lo arrastra consigo.
        //
        // Antes esto era `rb.linearVelocity = byVel * 0.5f` con `targetSpeed = byVel.magnitude`:
        // el escombro salía en la misma dirección que el coloso y a velocidad constante, así
        // que viajaba con él indefinidamente y acababa escoltándolo por delante. Empujado al
        // lado y con targetSpeed a cero, frena solo, el coloso lo adelanta, y queda en su cola.
        // La estela se forma sola con lo que va barriendo, que es la idea.
        //
        // shoveSideways ya existía y decía exactamente esto —"que resbale por el costado en vez
        // de quedarse delante siendo arrastrado"— pero aquí no se usaba.
        Vector2 fwd = byVel.sqrMagnitude > 0.01f ? byVel.normalized : Vector2.right;
        Vector2 lado = new Vector2(-fwd.y, fwd.x);
        float haciaDonde = Vector2.Dot(rb.position - by.Body.position, lado) >= 0f ? 1f : -1f;

        float lateral = Mathf.Clamp01(by.Type.shoveSideways);
        Vector2 empuje = (lado * (haciaDonde * lateral) + fwd * (1f - lateral)).normalized
                       * (byVel.magnitude * Mathf.Max(0.1f, by.Type.shoveSpeedMultiplier) * 0.5f);

        rb.linearVelocity = empuje;
        rb.angularVelocity += Random.Range(-60f, 60f);
        targetSpeed = 0f;
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
            Vector2 toCenter = d / dist;

            // Capa de equilibrio: por dentro de Pull Shell el tirón se vuelve empuje. Sin esto
            // todo lo atraído acababa apretado contra el casco, viajando pegado a él
            float surface = dist - WorldRadius - t.WorldRadius;
            float shell = Mathf.Max(0f, Type.pullShell);
            if (shell > 0f && surface < shell)
            {
                float push = 1f - Mathf.Max(0f, surface) / shell;
                Vector2 away = -toCenter * (Type.pullForce * 1.6f * push);
                // y hacia la cola, para que el enjambre se quede detrás
                Vector2 hv0 = rb.linearVelocity;
                if (Type.pullTailDrift > 0f && hv0.sqrMagnitude > 0.01f)
                    away += -hv0.normalized * (Type.pullForce * Type.pullTailDrift * push);
                t.ReceivePull(away);
                continue;
            }

            // Un tirón puramente radial hace que todo caiga en picado contra el casco y se
            // quede ahí amontonado. En un campo gravitatorio real los cuerpos NO caen en línea
            // recta: espiralan. La componente tangencial es lo que convierte el amontonamiento
            // en un enjambre que gira alrededor.
            Vector2 accel = toCenter;
            float tg = Mathf.Clamp01(Type.pullTangential);
            if (tg > 0f)
            {
                Vector2 side = new Vector2(-toCenter.y, toCenter.x);
                Vector2 rel = t.Body.linearVelocity - rb.linearVelocity;
                // El giro continúa el sentido que el guijarro ya llevaba: nada de tirones bruscos
                float sign = rel.x * toCenter.y - rel.y * toCenter.x;
                side *= sign >= 0f ? 1f : -1f;
                accel = Vector2.Lerp(toCenter, side, tg).normalized;
            }

            Vector2 pull = accel * (Type.pullForce * k * k);
            // Lo que atrae se va quedando en la cola si el coloso viaja
            Vector2 hv = rb.linearVelocity;
            if (Type.pullTailDrift > 0f && hv.sqrMagnitude > 0.01f)
                pull += -hv.normalized * (Type.pullForce * Type.pullTailDrift * k);
            t.ReceivePull(pull);
        }
    }

    /// Aceleración, no fuerza: el arrastre no depende de lo que pese el guijarro.
    public void ReceivePull(Vector2 acceleration)
    {
        if (rb.bodyType != RigidbodyType2D.Dynamic) return;
        rb.AddForce(acceleration * rb.mass);
        pulledUntil = Time.time + 0.25f;
    }

    // ---------------------------------------------------------------- estela de escombro
    // Un cuerpo que barre un cinturón no solo aparta material: arranca el suyo y parte de lo
    // que levanta vuelve a posarse detrás de él. Así el coloso TRANSFORMA el nivel en vez de
    // vaciarlo, y deja terreno nuevo donde antes había hueco.
    void TickWake()
    {
        var w = Type.wakeType;
        if (w == null || pool == null) return;

        wakeAccum += rb.linearVelocity.magnitude * Time.fixedDeltaTime;
        if (wakeAccum < Mathf.Max(0.5f, Type.wakeEvery)) return;
        wakeAccum = 0f;
        if (Random.value > Type.wakeChance) return;

        var gb = GameBounds.Instance;
        if (gb == null) return;
        Rect cam = gb.CameraRect;
        Vector2 playerPos = AvoidPoint();

        Vector2 v = rb.linearVelocity;
        Vector2 back = v.sqrMagnitude > 0.01f ? -v.normalized : Vector2.left;
        Vector2 side = new Vector2(-back.y, back.x);
        int mask = LayerMask.GetMask("AsteroidMoving", "AsteroidStatic");

        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 p = rb.position
                      + back * (WorldRadius + Random.Range(0.5f, 2.5f))
                      + side * Random.Range(-Type.wakeSpread, Type.wakeSpread);

            if (!cam.Contains(p)) continue;                        // solo donde se vea
            if (gb.IsBlocked(p, 1f)) continue;                     // no bajo los controles
            if (Vector2.Distance(p, playerPos) < 3f) continue;     // nunca encima del jugador
            if (Physics2D.OverlapCircle(p, 1.3f, mask) != null) continue;

            float size = w.RandomSize() * Random.Range(Type.wakeSizeScale.x, Type.wakeSizeScale.y);
            var left = pool.Spawn(w, p, size);
            if (left == null) return;
            left.Harmless = Harmless;

            // Sale despedido hacia atrás y hacia fuera, y se frena solo. La dirección se
            // mezcla entre "hacia atrás" y "de lado" para que el reguero se abra en abanico
            // en vez de quedar en fila india detrás del coloso.
            Vector2 fuera = (back * Random.Range(0.4f, 1f)
                           + side * Random.Range(-0.8f, 0.8f)).normalized;
            float vel = Random.Range(Type.wakeEjectSpeed.x, Type.wakeEjectSpeed.y);

            // Escombro recién arrancado: tumbea mucho más rápido que el terreno asentado.
            // Y de paso se distingue a simple vista de un estático de toda la vida.
            float giro = Random.Range(-120f, 120f) * Mathf.Max(0.1f, Type.wakeSpinScale);
            left.Eject(fuera * vel, giro);
            return;
        }
    }

    // ---------------------------------------------------------------- cometas
    void TickShedding()
    {
        var prof = Type.comet;
        if (pool == null || !prof.HasPieces) return;

        shedTimer -= Time.fixedDeltaTime;
        if (shedTimer > 0f) return;
        float interval = prof.shedInterval * Random.Range(0.7f, 1.3f);
        shedTimer = interval;

        Vector2 vel = rb.linearVelocity;
        Vector2 fwd = vel.sqrMagnitude > 0.0001f ? vel.normalized : Vector2.right;
        Vector2 side = new Vector2(-fwd.y, fwd.x);

        CometShed?.Invoke(rb.position - fwd * (WorldRadius + 0.6f), vel * 0.15f);
        switch (prof.PickPattern())
        {
            case CometProfile.ShedPattern.Line:    ShedLine(prof, vel, fwd, side, interval); break;
            case CometProfile.ShedPattern.Scatter: ShedScatter(prof, vel, fwd, side); break;
        }
    }

    // Un tramo de rastro: piezas quietas repartidas sobre el camino que el cometa acaba de
    // recorrer. Cubren lo avanzado desde el desprendimiento anterior, así que dos tramos
    // seguidos forman una línea continua y un patrón distinto en medio deja el hueco.
    void ShedLine(CometProfile prof, Vector2 vel, Vector2 fwd, Vector2 side, float interval)
    {
        int count = Random.Range(prof.linePieces.x, prof.linePieces.y + 1);
        if (count <= 0) return;

        float covered = vel.magnitude * interval;
        float spacing = Mathf.Max(prof.lineMinSpacing, covered / count);
        float jitter = prof.lineJitter * WorldRadius;

        for (int i = 0; i < count; i++)
        {
            var pieceType = prof.PickPiece();
            if (pieceType == null) continue;

            float size = pieceType.RandomSize();
            float behind = WorldRadius + 0.3f + size * 0.6f + i * spacing;
            Vector2 pos = rb.position - fwd * behind + side * Random.Range(-jitter, jitter);

            var piece = pool.Spawn(pieceType, pos, size);
            if (piece == null) continue;
            piece.SetMotion(Vector2.zero, Random.Range(-20f, 20f));
            piece.MakeTrail();
            CometTrail.Register(piece, Mathf.Max(8, prof.maxTrailPieces), prof.trailFadeSeconds);
        }
    }

    // Lo de siempre: abanico detrás, y cada pieza decide si se queda, deriva o lo sigue.
    void ShedScatter(CometProfile prof, Vector2 vel, Vector2 fwd, Vector2 side)
    {
        if (DebrisAlive >= prof.maxDebrisAlive) return;

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
            if (prof.scatterLifetime > 0f)
                piece.SetLifetime(prof.scatterLifetime * Random.Range(0.85f, 1.15f));
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
            // Colosos y planetas no se tocan: el coloso pasa por delante sin chocar ni capturar
            if (Type != null && other.Type != null &&
                ((Type.planet && other.Type.behavior == AsteroidBehavior.Colossus) ||
                 (other.Type.planet && Type.behavior == AsteroidBehavior.Colossus))) return;
            float impact = c.relativeVelocity.magnitude;
            ReportEnergy(other, c, impact);
            AbsorbImpact(other, impact);
            TryCapture(other, impact);
            return;
        }

        if (!Lethal) return;
        var player = c.collider.GetComponentInParent<PlayerController>();
        if (player != null) player.KillBy(this, c.contactCount > 0 ? c.GetContact(0).point : (Vector2)player.transform.position);
    }

    /// Energía real del choque: E = ½·m_reducida·v². La dispara solo el más pesado de
    /// los dos, para no contarla dos veces.
    void ReportEnergy(Asteroid other, Collision2D c, float impactSpeed)
    {
        if (HeavyImpact == null || rb.mass < other.Mass) return;
        float m1 = rb.mass, m2 = Mathf.Max(0.0001f, other.Mass);
        float reduced = (m1 * m2) / (m1 + m2);
        float energy = 0.5f * reduced * impactSpeed * impactSpeed;

        Vector2 point = rb.position, dir;
        if (c.contactCount > 0)
        {
            var ct = c.GetContact(0);
            point = ct.point;
            dir = ct.normal;                       // la eyecta sale por donde entró el golpe
        }
        else dir = (other.Body.position - rb.position);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;

        HeavyImpact(point, dir.normalized, energy, m1);
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
                // Apartarse del casco, no quedarse delante. Un imparable que empuja de frente
                // vuelve a alcanzarte al frame siguiente y te arrastra pegado a él; con una
                // componente lateral el guijarro resbala por el costado y lo deja pasar.
                // Es además lo que hace un choque no frontal: desvía de lado.
                float side = Mathf.Clamp01(other.Type.shoveSideways);
                if (side > 0f)
                {
                    Vector2 n = rb.position - other.Body.position;
                    if (n.sqrMagnitude > 0.0001f)
                    {
                        n.Normalize();
                        Vector2 tangent = new Vector2(-n.y, n.x);
                        if (Vector2.Dot(v, tangent) < 0f) tangent = -tangent;
                        v = Vector2.Lerp(v, tangent * v.magnitude, side);
                    }
                }

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

    /// Está cayendo en el agujero negro: sin física, sin choques y sin matar. La animación la
    /// lleva el Goal; al terminar llama a Release.
    public bool IsSwallowed => swallowed;
    public SpriteRenderer Renderer => sr;

    public void BeginSwallow()
    {
        swallowed = true;
        lifetimeEnd = -1f;
        ReleaseMoons(rb != null ? rb.linearVelocity : Vector2.zero);
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
        }
        if (col != null) col.enabled = false;
    }

    public void Release()
    {
        if (pool != null) pool.Release(this);
        else gameObject.SetActive(false);
    }
}