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

    [Header("Atracción sobre el jugador")]
    [Tooltip("Distancia a la que empieza a notarse el tirón")]
    [SerializeField] private float pullRadius = 4.5f;
    [Tooltip("Aceleración junto al horizonte (unidades/s²)")]
    [SerializeField] private float pullForce = 7f;
    [Tooltip("Dentro de este radio ya no hay vuelta atrás: empieza la absorción")]
    [SerializeField] private float eventHorizon = 0.6f;

    [Header("Absorción")]
    [SerializeField] private float absorbTime = 1.1f;
    [SerializeField] private float absorbSpin = 900f;

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
        ApplySize(Random.Range(Mathf.Min(sizeRange.x, sizeRange.y), Mathf.Max(sizeRange.x, sizeRange.y)));

        // Lo que ocupa el dibujo, ya escalado: el halo es lo más grande
        float drawn = 0.5f;
        if (haloRenderer != null && haloRenderer.sprite != null)
            drawn = Mathf.Max(drawn, haloRenderer.bounds.extents.x, haloRenderer.bounds.extents.y);
        else if (discRenderer != null && discRenderer.sprite != null)
            drawn = Mathf.Max(drawn, discRenderer.bounds.extents.x, discRenderer.bounds.extents.y);

        float m = edgeMargin + drawn;
        Rect a = gb.Area;
        float mx = Mathf.Min(m, a.width * 0.45f), my = Mathf.Min(m, a.height * 0.45f);
        float x0 = a.xMin + mx, x1 = a.xMax - mx, y0 = a.yMin + my, y1 = a.yMax - my;

        // El lado contrario es el del eje en el que el jugador está más pegado a una pared:
        // si entra por la izquierda, la meta va a la derecha del todo, y al revés. La otra
        // coordenada, al azar
        float dx = Mathf.Min(playerSpawn.x - a.xMin, a.xMax - playerSpawn.x) / Mathf.Max(0.01f, a.width);
        float dy = Mathf.Min(playerSpawn.y - a.yMin, a.yMax - playerSpawn.y) / Mathf.Max(0.01f, a.height);
        Vector2 p;
        if (dx <= dy)
            p = new Vector2(playerSpawn.x < a.center.x ? x1 : x0, Random.Range(y0, y1));
        else
            p = new Vector2(Random.Range(x0, x1), playerSpawn.y < a.center.y ? y1 : y0);

        transform.position = new Vector3(p.x, p.y, transform.position.z);

        // Zona que el generador deja libre de estáticos: todo el alcance del tirón. Así la
        // meta nunca queda encerrada por terreno
        float r = Mathf.Max(pullRadius, drawn);
        gb.SetGoalZone(new Rect(p.x - r, p.y - r, 2f * r, 2f * r));
        Active = true;

        Debug.Log($"Goal: escala {SizeScale:0.00} (dibujo x{drawScale:0.00}), en {p}, " +
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

        if (dist <= eventHorizon) { StartCoroutine(Absorb()); return; }

        // Cae con el cuadrado: lejos apenas se nota, cerca ya no puedes escapar
        float k = 1f - (dist - eventHorizon) / Mathf.Max(0.01f, pullRadius - eventHorizon);
        k = Mathf.Clamp01(k);
        playerBody.AddForce(d.normalized * (pullForce * k * k * playerBody.mass));
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
            if (a == null) continue;

            Vector2 d = center - a.Body.position;
            float dist = d.magnitude;

            bool devourable = !a.Unstoppable && a.WorldRadius <= maxSwallowRadius;
            if (devourable && dist <= asteroidHorizon + a.WorldRadius * 0.5f) { a.Release(); continue; }
            if (a.Body.bodyType != RigidbodyType2D.Dynamic) continue;

            float k = Mathf.Clamp01(1f - dist / pullRadius);
            a.ReceivePull(d / Mathf.Max(0.01f, dist) * (pullForce * asteroidPullFactor * k * k));
        }
    }

    IEnumerator Absorb()
    {
        Active = false;
        player.BeginAbsorb();

        Transform t = player.transform;
        Vector3 from = t.position;
        Vector3 to = transform.position;
        Vector3 scale0 = t.localScale;
        float e = 0f;

        while (e < absorbTime)
        {
            e += Time.deltaTime;
            float u = Mathf.Clamp01(e / absorbTime);
            // Acelera al final: la caída se precipita, no es un deslizamiento uniforme
            float a = u * u * u;
            t.position = Vector3.Lerp(from, to, a);
            t.localScale = scale0 * (1f - a);
            t.Rotate(0f, 0f, absorbSpin * Time.deltaTime);
            yield return null;
        }

        t.position = to;
        t.localScale = Vector3.zero;
        Reached?.Invoke();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, pullRadius);
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, eventHorizon);
    }
}