using System.Collections.Generic;
using System.Collections;
using UnityEngine;

/// El agujero negro: la meta del nivel.
///
/// No es un botón que se toca. Tira de ti desde lejos, y cuanto más te acercas menos puedes
/// escapar — igual que el de verdad. Eso lo convierte en una decisión: entrar cuando toca, no
/// cuando pasas cerca. Y como también se traga asteroides, el tramo final está vivo.
public class Goal : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private Transform disc;
    [Tooltip("DÉJALO EN 0 si tu agujero negro está dibujado en perspectiva. Girar una elipse " +
             "sobre su centro parece un plato dando vueltas; un disco de acreción real gira " +
             "DENTRO de su plano. El movimiento va en el halo, que sí es simétrico")]
    [SerializeField] private float discSpin = 0f;
    [Tooltip("Latido del disco: respira en vez de girar. Es el movimiento correcto para un " +
             "dibujo en perspectiva. 0.04 = apenas se nota pero deja de parecer una foto")]
    [SerializeField] private float discPulse = 0.04f;
    [SerializeField] private float discPulseSpeed = 0.9f;

    [Tooltip("Anillo radialmente simétrico. Al no tener arriba ni abajo, girarlo se lee como " +
             "materia arremolinándose en vez de como un dibujo dando vueltas")]
    [SerializeField] private Transform halo;
    [SerializeField] private float haloSpin = 34f;

    [Header("Variedad")]
    [Tooltip("Un disco distinto en cada nivel. Si está vacío, se queda el que tenga puesto")]
    [SerializeField] private Sprite[] discSprites;
    [Tooltip("Un halo distinto en cada nivel. Combinado con los discos da todas las parejas")]
    [SerializeField] private Sprite[] haloSprites;
    [Tooltip("El halo hereda el tinte del disco, para que la pareja no desentone")]
    [SerializeField] private bool tintHaloLikeDisc = false;

    [Tooltip("Un color por nivel, sorteado de aquí y aplicado al disco Y al halo. Los sprites " +
             "están dibujados en blanco a propósito: el blanco multiplicado por un color da ese " +
             "color exacto, así que 16 dibujos y 8 tintes dan 512 metas distintas sin gastar " +
             "una textura más. Vacío = se queda el color que tengan puesto")]
    [SerializeField] private Color[] palette = {
        new Color(1.00f, 0.72f, 0.30f),   // ámbar
        new Color(0.40f, 0.85f, 1.00f),   // cian
        new Color(0.72f, 0.45f, 1.00f),   // violeta
        new Color(0.40f, 1.00f, 0.65f),   // esmeralda
        new Color(1.00f, 0.40f, 0.45f),   // carmesí
        new Color(0.85f, 0.88f, 1.00f),   // plata
        new Color(1.00f, 0.50f, 0.20f),   // naranja
        new Color(0.45f, 0.60f, 1.00f),   // azul frío
    };
    [Tooltip("Cuánto se aclara el halo respecto al disco. El halo es gas tenue y el disco " +
             "plasma: con el mismo tinte exacto el halo se come al disco")]
    [Range(0f, 1f)] [SerializeField] private float haloDesaturate = 0.35f;

    SpriteRenderer discRenderer, haloRenderer;
    Vector3 discBaseScale = Vector3.one;

    [Header("Tamaño y sitio (mundo amplio)")]
    [Tooltip("Escala del agujero negro en cada nivel. Disco y halo crecen juntos, en " +
             "proporción. 1 = el tamaño con el que está montado en la escena")]
    [SerializeField] private Vector2 sizeRange = new Vector2(0.7f, 1.5f);
    [Tooltip("Tamaño del DIBUJO respecto al montado en la escena, sin tocar su comportamiento. " +
             "Más bajo = se ve como un objeto lejano en el cosmos, aunque su tirón, sus " +
             "horizontes y su masa sigan siendo los de un agujero negro enorme")]
    [Range(0.1f, 1f)] [SerializeField] private float drawScale = 0.45f;
    [Tooltip("El alcance del tirón y los horizontes crecen con el tamaño. Si no, un agujero " +
             "grande atraparía desde dentro de su propio dibujo")]
    [SerializeField] private bool reachScalesWithSize = true;
    [Tooltip("Margen con el borde del mundo, en unidades, además del propio tamaño del dibujo")]
    [SerializeField] private float edgeMargin = 1f;
    [Tooltip("Su centro queda justo sobre el límite del mundo: se ve la mitad de dentro y la de " +
             "fuera no. Ver solo una parte del disco y del halo lo hace sentir aún más enorme. " +
             "Medio horizonte queda dentro, así que siempre se puede llegar")]
    [SerializeField] private bool allowPartiallyHidden = true;
    [Tooltip("A lo largo del borde contrario: peso de caer en el centro, en una esquina y en " +
             "cualquier punto. En una esquina solo se ve un cuarto")]
    [SerializeField] private float centerWeight = 0.35f;
    [SerializeField] private float cornerWeight = 0.35f;
    [SerializeField] private float anyWeight = 0.3f;

    [Header("Gravitación universal (F)")]
    [Tooltip("G·M de un agujero negro de tamaño 1: la aceleración (u/s²) a 1 unidad del centro. " +
             "A distancia d tira con G·M / d², la ley de Newton")]
    [SerializeField] private float gravityGM = 12f;
    [Tooltip("La masa crece con el tamaño elevado a esto. 2 = un agujero el doble de grande " +
             "pesa cuatro veces más; 3 = ocho veces")]
    [SerializeField] private float massExponent = 2f;
    [Tooltip("Tope de la aceleración, para que pegado al horizonte no salga un número absurdo")]
    [SerializeField] private float maxPullAccel = 18f;

    [Header("Atracción sobre el jugador")]
    [Tooltip("Distancia a la que empieza a notarse el tirón")]
    [SerializeField] private float pullRadius = 4.5f;
    [Tooltip("Dentro de este radio ya no hay vuelta atrás: empieza la absorción")]
    [SerializeField] private float eventHorizon = 0.6f;

    [Header("Caída: física de agujero negro")]
    [Tooltip("Tamaño de la sombra negra del disco, en fracción de su ancho. Es el horizonte " +
             "visible: lo que cae se congela y se apaga al llegar ahí")]
    [Range(0.05f, 0.6f)] [SerializeField] private float shadowFraction = 0.27f;
    [Tooltip("Empezar a absorber como poco a esta distancia de la sombra (en radios de sombra). " +
             "Así la caída siempre se ve, aunque el dibujo del agujero sea grande")]
    [SerializeField] private bool horizonFromShadow = true;
    [SerializeField] private float horizonOverShadow = 1.8f;
    [Tooltip("Escala de tiempo de la caída del jugador, en segundos")]
    [SerializeField] private float absorbTime = 1.1f;
    [Tooltip("Escala de tiempo de la caída de un asteroide (los grandes tardan algo más)")]
    [SerializeField] private float swallowTime = 0.8f;
    [Tooltip("Estiramiento de marea al llegar a la sombra: el jugador mide 1 + esto de largo. " +
             "Crece con (sombra/distancia)³, como la fuerza de marea real")]
    [SerializeField] private float playerStretch = 3.5f;
    [SerializeField] private float asteroidStretch = 3f;
    [Tooltip("Giro en espiral inicial. Se acelera al acercarse (momento angular) y se frena " +
             "junto a la sombra (dilatación del tiempo)")]
    [SerializeField] private float spiralDegrees = 70f;
    [Tooltip("Tope de duración de la caída, en múltiplos de su escala de tiempo")]
    [SerializeField] private float maxFallFactor = 2.2f;
    [Tooltip("Caída: largo máximo del cuerpo estirado, en radios de la sombra negra. Así nunca " +
             "acaba más largo que el propio agujero")]
    [SerializeField] private float fallMaxLength = 0.9f;
    [Tooltip("Caída: cuántas más vueltas da en espiral antes de cruzar (1 = como antes)")]
    [SerializeField] private float spiralBoost = 1.6f;
    [Tooltip("Copias que se quedan atrás un instante y dibujan el hilo curvo de la caída")]
    [SerializeField] private bool ghostTrail = true;
    [SerializeField] private float ghostEvery = 0.035f;
    [SerializeField] private float ghostLife = 0.35f;

    [Header("Asteroides")]
    [Tooltip("Desvía las rocas que pasan cerca. Un pozo gravitatorio NO vacía su entorno: lo " +
             "concentra. Por eso desvía mucho y se traga poquísimo, y así la aproximación a la " +
             "meta es el tramo más cargado en vez del más despejado")]
    [SerializeField] private bool swallowAsteroids = true;
    [SerializeField] private float asteroidPullFactor = 0.9f;
    [Tooltip("Radio en el que se traga de verdad. Muy pequeño a propósito: solo lo que le cae " +
             "literalmente encima")]
    [SerializeField] private float asteroidHorizon = 0.45f;
    [Tooltip("Por encima de este radio NO se lo traga, solo lo desvía. Un coloso no desaparece " +
             "de un bocado, y si lo hiciera el director se quedaría contándolo como vivo para " +
             "siempre y no entraría ningún coloso más en ese nivel")]
    [SerializeField] private float maxSwallowRadius = 1.2f;

    /// El jugador ha sido absorbido: nivel superado.
    public event System.Action Reached;
    public bool Active { get; private set; }
    /// Alcance de su atracción, en unidades (lo usa el mundo oscuro para dejarla ver)
    public float PullRadius => pullRadius;
    /// Multiplica su tamaño al colocarse: los mundos oscuros tienen agujeros más masivos.
    /// Lo pone GameDirector antes de colocarla
    public float SizeBoost { get; set; } = 1f;

    /// Escala de este nivel (1 = la de la escena). La leerá la gravitación del paso F.
    public float SizeScale { get; private set; } = 1f;

    PlayerController player;
    Rigidbody2D playerBody;
    Vector3 rootBaseScale;
    float basePull, baseHorizon, baseAstHorizon, baseMaxSwallow;
    static readonly Collider2D[] buffer = new Collider2D[24];
    static ContactFilter2D filter;
    static bool filterReady;

    void Awake()
    {
        if (disc == null) disc = transform;
        discRenderer = disc.GetComponent<SpriteRenderer>();
        discBaseScale = disc.localScale;
        rootBaseScale = transform.localScale;
        basePull = pullRadius;
        baseHorizon = eventHorizon;
        baseAstHorizon = asteroidHorizon;
        baseMaxSwallow = maxSwallowRadius;
        if (halo != null) haloRenderer = halo.GetComponent<SpriteRenderer>();

        player = FindFirstObjectByType<PlayerController>();
        if (player != null) playerBody = player.GetComponent<Rigidbody2D>();
    }

    /// Sortea la pareja disco + halo del nivel. Con 6 discos y 6 halos salen 36 combinaciones
    /// de un puñado de dibujos: el jugador tarda mucho en ver dos metas iguales.
    void PickLook()
    {
        if (discRenderer != null && discSprites != null && discSprites.Length > 0)
            discRenderer.sprite = discSprites[Random.Range(0, discSprites.Length)];

        if (haloRenderer != null && haloSprites != null && haloSprites.Length > 0)
            haloRenderer.sprite = haloSprites[Random.Range(0, haloSprites.Length)];

        // El color va aparte de la forma a propósito: si cada dibujo trajera el suyo, tendrías
        // que generar una textura por combinación. Sorteándolo aquí, 8 discos x 8 halos x 8
        // tintes son 512 metas distintas con 16 archivos.
        if (palette != null && palette.Length > 0)
        {
            Color c = palette[Random.Range(0, palette.Length)];

            if (discRenderer != null)
            {
                float a = discRenderer.color.a;
                discRenderer.color = new Color(c.r, c.g, c.b, a);
            }
            if (haloRenderer != null)
            {
                // El halo, más lavado: mismo tinte tirando a blanco. Gas tenue alrededor de
                // plasma, no dos capas del mismo color peleándose
                Color h = Color.Lerp(c, Color.white, haloDesaturate);
                haloRenderer.color = new Color(h.r, h.g, h.b, haloRenderer.color.a);
            }
            return;   // la paleta manda sobre el tinte heredado de abajo
        }

        if (tintHaloLikeDisc && haloRenderer != null && discRenderer != null)
        {
            Color c = discRenderer.color;
            c.a = haloRenderer.color.a;
            haloRenderer.color = c;
        }
    }

    /// Mundo amplio: en el lado contrario al jugador, pegada al borde más lejano y a una
    /// altura al azar. Tamaño al azar. Con la cámara fija hace lo de siempre: la esquina.
    public void PlaceOpposite(Vector2 playerSpawn)
    {
        var gb = GameBounds.Instance;
        if (gb == null || !gb.IsReady) { Debug.LogWarning("Goal: GameBounds no está listo."); return; }
        if (!gb.IsExpanded) { Place(); return; }

        // Activo y con su dibujo de este nivel ANTES de medirlo: un objeto apagado mide cero
        gameObject.SetActive(true);
        PickLook();
        ApplySize(Random.Range(Mathf.Min(sizeRange.x, sizeRange.y), Mathf.Max(sizeRange.x, sizeRange.y)) * Mathf.Max(0.1f, SizeBoost));

        // Lo que ocupa el dibujo, ya escalado: el halo es lo más grande
        float drawn = 0.5f;
        if (haloRenderer != null && haloRenderer.sprite != null)
            drawn = Mathf.Max(drawn, haloRenderer.bounds.extents.x, haloRenderer.bounds.extents.y);
        else if (discRenderer != null && discRenderer.sprite != null)
            drawn = Mathf.Max(drawn, discRenderer.bounds.extents.x, discRenderer.bounds.extents.y);

        // Si puede quedar medio oculto, solo hace falta que su horizonte quede dentro del mundo
        // con sitio para el jugador; el resto del dibujo puede salirse de lo que se ve
        float m = allowPartiallyHidden ? EffectiveHorizon() + 0.8f : edgeMargin + drawn;
        Rect a = gb.Area;
        float mx = Mathf.Min(m, a.width * 0.45f), my = Mathf.Min(m, a.height * 0.45f);
        float x0 = a.xMin + mx, x1 = a.xMax - mx, y0 = a.yMin + my, y1 = a.yMax - my;

        // El lado contrario es el del eje en el que el jugador está más pegado a una pared:
        // si entra por la izquierda, la meta va a la derecha del todo, y al revés. La otra
        // coordenada, al azar
        float dx = Mathf.Min(playerSpawn.x - a.xMin, a.xMax - playerSpawn.x) / Mathf.Max(0.01f, a.width);
        float dy = Mathf.Min(playerSpawn.y - a.yMin, a.yMax - playerSpawn.y) / Mathf.Max(0.01f, a.height);
        // Medio oculto: el centro, justo sobre el límite del mundo en el lado contrario; la mitad
        // de fuera queda sin ver. Medio horizonte sigue dentro, así que se puede llegar a él.
        // En el otro eje, con margen para que no caiga en una esquina
        float ex0 = allowPartiallyHidden ? a.xMin : x0, ex1 = allowPartiallyHidden ? a.xMax : x1;
        float ey0 = allowPartiallyHidden ? a.yMin : y0, ey1 = allowPartiallyHidden ? a.yMax : y1;
        // Centro, esquina o cualquier punto del borde. Medio oculto, puede llegar hasta la
        // esquina misma; el siguiente nivel recuerda el tipo para que el jugador salga igual
        var slot = PlayerStart.PickSlot(centerWeight, cornerWeight, anyWeight);
        PlayerStart.LastGoalSlot = slot;
        float t = PlayerStart.SlotT(slot);
        float ay0 = allowPartiallyHidden ? a.yMin : y0, ay1 = allowPartiallyHidden ? a.yMax : y1;
        float ax0 = allowPartiallyHidden ? a.xMin : x0, ax1 = allowPartiallyHidden ? a.xMax : x1;
        Vector2 p;
        if (dx <= dy)
            p = new Vector2(playerSpawn.x < a.center.x ? ex1 : ex0, Mathf.Lerp(ay0, ay1, t));
        else
            p = new Vector2(Mathf.Lerp(ax0, ax1, t), playerSpawn.y < a.center.y ? ey1 : ey0);

        transform.position = new Vector3(p.x, p.y, transform.position.z);

        // Zona que el generador deja libre de estáticos: todo el alcance del tirón. Así la
        // meta nunca queda encerrada por terreno
        float r = Mathf.Max(pullRadius, drawn);
        gb.SetGoalZone(new Rect(p.x - r, p.y - r, 2f * r, 2f * r));
        Active = true;

        Debug.Log($"Goal: {slot}, escala {SizeScale:0.00} (dibujo x{drawScale:0.00}), en {p}, " +
                  $"a {Vector2.Distance(p, playerSpawn):0.0} u del jugador.");
    }

    static bool gbExpanded => GameBounds.Instance != null && GameBounds.Instance.IsExpanded;

    void ApplySize(float s)
    {
        SizeScale = Mathf.Max(0.05f, s);
        // El dibujo se encoge con Draw Scale; el comportamiento (más abajo) no
        float drawn = SizeScale * (gbExpanded ? drawScale : 1f);
        transform.localScale = rootBaseScale * drawn;
        // Si el disco ES este objeto, el latido escribe su escala: tiene que latir sobre la nueva
        if (disc == transform) discBaseScale = rootBaseScale * drawn;

        float k = reachScalesWithSize ? SizeScale : 1f;
        pullRadius = basePull * k;
        eventHorizon = baseHorizon * k;
        asteroidHorizon = baseAstHorizon * k;
        maxSwallowRadius = baseMaxSwallow * k;
    }

    /// Lo coloca en la esquina que GameBounds tenga reservada y lo reactiva.
    public void Place()
    {
        ApplySize(1f);
        var gb = GameBounds.Instance;
        if (gb == null || !gb.IsReady) { Debug.LogWarning("Goal: GameBounds no está listo."); return; }

        Vector2 c = gb.GoalCorner.center;
        transform.position = new Vector3(c.x, c.y, transform.position.z);
        gameObject.SetActive(true);
        PickLook();
        Active = true;
    }

    public void Deactivate() { Active = false; }

    /// Mapas de fractura: la meta no existe hasta que se cierra la grieta
    public void Hide()
    {
        Active = false;
        gameObject.SetActive(false);
    }

    /// Aparece en un punto concreto (donde estaba la grieta). Tamaño al azar como siempre
    public void PlaceAt(Vector2 p)
    {
        gameObject.SetActive(true);
        PickLook();
        ApplySize(Random.Range(Mathf.Min(sizeRange.x, sizeRange.y), Mathf.Max(sizeRange.x, sizeRange.y)) * Mathf.Max(0.1f, SizeBoost));
        transform.position = new Vector3(p.x, p.y, transform.position.z);
        PlayerStart.LastGoalSlot = null;
        Active = true;
        Debug.Log($"Goal: aparece donde estaba la grieta, en {p}, escala {SizeScale:0.00}.");
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // El halo gira: es simétrico, así que se lee como materia arremolinándose
        if (halo != null) halo.Rotate(0f, 0f, haloSpin * dt);

        if (disc == null) return;
        if (discSpin != 0f) disc.Rotate(0f, 0f, discSpin * dt);

        // El disco respira. Para un dibujo en perspectiva esto funciona y girarlo no
        if (discPulse > 0f)
            disc.localScale = discBaseScale * (1f + Mathf.Sin(Time.time * discPulseSpeed) * discPulse);
    }

    void FixedUpdate()
    {
        if (!Active) return;
        Vector2 center = transform.position;

        if (swallowAsteroids) PullAndSwallowAsteroids(center);

        if (player == null || playerBody == null) return;
        if (player.IsDead || player.IsAbsorbed) return;

        Vector2 d = center - (Vector2)player.transform.position;
        float dist = d.magnitude;
        if (dist > pullRadius) return;

        if (dist <= EffectiveHorizon()) { StartCoroutine(Absorb()); return; }

        // Newton: G·M / d². La velocidad que da se acumula en el jugador, así que lejos apenas
        // se nota y cerca supera a tu propio impulso: el punto sin retorno sale solo
        player.AddGravity(d / Mathf.Max(0.01f, dist) * Gravity(dist));
    }

    void PullAndSwallowAsteroids(Vector2 center)
    {
        if (!filterReady)
        {
            filter = new ContactFilter2D();
            filter.SetLayerMask(LayerMask.GetMask("AsteroidMoving", "AsteroidStatic"));
            filter.useTriggers = false;
            filterReady = true;
        }

        int n = Physics2D.OverlapCircle(center, pullRadius, filter, buffer);
        for (int i = 0; i < n; i++)
        {
            var a = buffer[i].GetComponentInParent<Asteroid>();
            if (a == null || a.IsFrozen) continue;   // TimeFreeze: ni el agujero lo mueve

            Vector2 d = center - a.Body.position;
            float dist = d.magnitude;

            bool devourable = !a.Unstoppable && a.WorldRadius <= maxSwallowRadius;
            float ah = horizonFromShadow ? Mathf.Max(asteroidHorizon, ShadowRadius() * 1.2f) : asteroidHorizon;
            if (devourable && dist <= ah + a.WorldRadius * 0.5f) { StartCoroutine(Spaghettify(a)); continue; }
            if (a.Body.bodyType != RigidbodyType2D.Dynamic) continue;

            a.ReceivePull(d / Mathf.Max(0.01f, dist) * (Gravity(dist) * asteroidPullFactor));
        }
    }

    /// G·M del agujero de este nivel: la masa sale de su tamaño
    public float GM => gravityGM * Mathf.Pow(SizeScale, massExponent);

    // Aceleración a distancia d. Se apaga suave en el último 20 % del alcance (Pull Radius),
    // para que no haya un borde donde el tirón empiece de golpe
    float Gravity(float d)
    {
        float a = Mathf.Min(maxPullAccel, GM / Mathf.Max(0.05f, d * d));
        float edge = Mathf.Clamp01((pullRadius - d) / Mathf.Max(0.01f, pullRadius * 0.2f));
        return a * edge;
    }

    // ------------------------------------------------------------------ caída
    // Radio de la sombra negra del dibujo: el horizonte de sucesos que se ve
    float ShadowRadius() =>
        discRenderer != null && discRenderer.sprite != null ? discRenderer.bounds.extents.x * shadowFraction : eventHorizon;

    /// Radio de la sombra negra (el centro que se ve). Lo usa la cámara para encuadrar la meta
    public float CoreRadius => ShadowRadius();

    float EffectiveHorizon() =>
        horizonFromShadow ? Mathf.Max(eventHorizon, ShadowRadius() * horizonOverShadow) : eventHorizon;

    /// Desplazamiento al rojo gravitacional. f = raíz(1 − Rs/r): 1 lejos, 0 en el horizonte.
    /// La luz pierde energía al salir del pozo: el color se corre al rojo y se apaga hasta
    /// volverse invisible antes de cruzar. La usa también la llegada del jugador, al revés.
    public static Color Redshift(Color c, float f)
    {
        f = Mathf.Clamp01(f);
        float bright = Mathf.Pow(f, 0.6f);
        var o = new Color(c.r * bright, c.g * Mathf.Pow(f, 1.5f) * bright, c.b * Mathf.Pow(f, 2.5f) * bright, c.a);
        o.a = c.a * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.35f, f));
        return o;
    }

    IEnumerator Absorb()
    {
        Active = false;
        player.BeginAbsorb();
        var vis = player.Visual;
        var sr = vis != null ? vis.GetComponent<SpriteRenderer>() : null;
        yield return FallIn(player.transform, vis, sr, absorbTime, playerStretch, () => true);

        player.transform.localScale = Vector3.zero;
        if (vis != null) vis.localRotation = Quaternion.identity;
        Reached?.Invoke();
    }

    IEnumerator Spaghettify(Asteroid a)
    {
        int serial = a.Serial;
        a.BeginSwallow();
        float t = swallowTime * Mathf.Lerp(0.8f, 1.4f, Mathf.Clamp01(a.WorldRadius / Mathf.Max(0.01f, maxSwallowRadius)));
        bool alive() => a != null && a.isActiveAndEnabled && a.Serial == serial;
        yield return FallIn(a.transform, null, a.Renderer, t, asteroidStretch, alive);
        if (alive()) a.Release();
    }

    // La caída, con las cuatro cosas que hace un agujero negro de verdad:
    //  1. Marea: la parte cercana cae más deprisa que la lejana. El estiramiento radial crece con
    //     (Rs/r)³ y el ancho se comprime para conservar el área. A primer orden la marea es una
    //     deformación lineal, así que estirar y comprimir el sprite es la física correcta; la
    //     parte delantera va adelantada al centro, y las copias que se quedan atrás dibujan el
    //     hilo curvo.
    //  2. Espiral: el giro se acelera al bajar (momento angular, ~1/r²).
    //  3. Dilatación del tiempo: visto desde fuera, todo se frena con (1 − Rs/r) al llegar al
    //     horizonte. Nunca lo cruza: se queda congelado en el borde.
    //  4. Corrimiento al rojo: mientras se congela, su luz se enrojece y se apaga hasta
    //     desaparecer. Ahí termina.
    IEnumerator FallIn(Transform t, Transform vis, SpriteRenderer sr, float timeScale, float tidal, System.Func<bool> alive)
    {
        Vector2 c = transform.position;
        Vector2 d0 = (Vector2)t.position - c;
        float r0 = Mathf.Max(0.05f, d0.magnitude);
        float rs = Mathf.Min(ShadowRadius(), r0 * 0.7f);
        float theta = Mathf.Atan2(d0.y, d0.x);
        float ts = Mathf.Max(0.05f, timeScale);
        float v0 = r0 / ts;
        float w0 = spiralDegrees * Mathf.Deg2Rad / ts * (Random.value < 0.5f ? -1f : 1f);
        Vector3 s0 = t.localScale;
        Color c0 = sr != null ? sr.color : Color.white;
        float halfLen = sr != null ? Mathf.Max(sr.bounds.extents.x, sr.bounds.extents.y) : 0.2f;
        float r = r0, e = 0f, nextGhost = 0f;
        // Tope del estirado: el cuerpo nunca más largo que Fall Max Length radios de la sombra
        float maxStretch = Mathf.Max(1f, ShadowRadius() * fallMaxLength / Mathf.Max(0.01f, 2f * halfLen));
        w0 *= spiralBoost;
        Vector2 prevPos = t.position;
        float moveAng = Mathf.Atan2(-d0.y, -d0.x) * Mathf.Rad2Deg;

        while (true)
        {
            if (!alive()) yield break;
            float dt = Time.deltaTime;
            e += dt;

            float f = Mathf.Sqrt(Mathf.Max(0f, 1f - rs / r));
            float slow = f * f;                                    // dilatación del tiempo
            r = Mathf.Max(rs * 1.0005f, r - v0 * Mathf.Sqrt(r0 / r) * slow * dt);
            theta += w0 * Mathf.Min(8f, (r0 / r) * (r0 / r)) * slow * dt;

            float x = rs / r;
            float stretch = Mathf.Min(1f + tidal * x * x * x, maxStretch);   // marea ~ 1/r³, con tope
            float tang = 1f / Mathf.Sqrt(stretch);
            Vector2 dir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
            float lead = Mathf.Min((stretch - 1f) * 0.3f * halfLen, (r - rs) * 0.5f);
            Vector2 pos = c + dir * (r - lead);
            // Se estira a lo largo de su camino (la espiral), no en línea recta hacia el centro
            Vector2 step = pos - prevPos;
            prevPos = pos;
            if (step.sqrMagnitude > 1e-8f)
                moveAng = Mathf.LerpAngle(moveAng, Mathf.Atan2(step.y, step.x) * Mathf.Rad2Deg, 0.35f);
            float ang = moveAng;

            t.position = pos;
            t.rotation = Quaternion.Euler(0f, 0f, ang);
            t.localScale = new Vector3(s0.x * stretch, s0.y * tang, s0.z);
            if (vis != null) vis.localRotation = Quaternion.Euler(0f, 0f, -ang);
            Color col = Redshift(c0, f);
            if (sr != null) sr.color = col;

            if (ghostTrail && PerformanceManager.FallGhosts && sr != null && sr.sprite != null && e >= nextGhost)
            {
                nextGhost = e + ghostEvery;
                SpawnGhost(sr, pos, ang, new Vector2(s0.x * stretch, s0.y * tang), vis != null ? -ang : 0f,
                           vis != null ? vis.localScale : Vector3.one, col);
            }

            if (f < 0.12f || e > ts * maxFallFactor) break;
            yield return null;
        }
    }

    // ------------------------------------------------------------------ hilo de copias
    class Ghost { public Transform root; public SpriteRenderer sr; public float age; public Color color; }
    readonly List<Ghost> ghosts = new List<Ghost>();
    Transform ghostParent;   // todas juntas bajo un solo objeto, para no llenar la jerarquía

    void SpawnGhost(SpriteRenderer src, Vector2 pos, float ang, Vector2 rootScale, float childRot, Vector3 childScale, Color col)
    {
        Ghost g = null;
        foreach (var x in ghosts) if (!x.root.gameObject.activeSelf) { g = x; break; }
        if (g == null)
        {
            if (ghostParent == null) ghostParent = new GameObject("FallGhosts").transform;
            var root = new GameObject("FallGhost").transform;
            root.SetParent(ghostParent, false);
            var child = new GameObject("Sprite").transform;
            child.SetParent(root, false);
            g = new Ghost { root = root, sr = child.gameObject.AddComponent<SpriteRenderer>() };
            ghosts.Add(g);
        }
        g.root.gameObject.SetActive(true);
        g.root.SetPositionAndRotation(pos, Quaternion.Euler(0f, 0f, ang));
        g.root.localScale = new Vector3(rootScale.x, rootScale.y, 1f);
        g.sr.transform.localRotation = Quaternion.Euler(0f, 0f, childRot);
        g.sr.transform.localScale = childScale;
        g.sr.sprite = src.sprite;
        g.sr.flipX = src.flipX;
        g.sr.flipY = src.flipY;
        g.sr.sortingLayerID = src.sortingLayerID;
        g.sr.sortingOrder = src.sortingOrder - 1;
        g.color = col;
        g.age = 0f;
        g.sr.color = col;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        foreach (var g in ghosts)
        {
            if (!g.root.gameObject.activeSelf) continue;
            g.age += dt;
            float k = 1f - g.age / Mathf.Max(0.01f, ghostLife);
            if (k <= 0f) { g.root.gameObject.SetActive(false); continue; }
            var col = g.color;
            col.a *= 0.45f * k * k;
            g.sr.color = col;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, pullRadius);
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, Application.isPlaying ? EffectiveHorizon() : eventHorizon);
    }
}