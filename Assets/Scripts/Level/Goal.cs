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

    SpriteRenderer discRenderer, haloRenderer;
    Vector3 discBaseScale = Vector3.one;

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

    PlayerController player;
    Rigidbody2D playerBody;
    static readonly Collider2D[] buffer = new Collider2D[24];
    static ContactFilter2D filter;
    static bool filterReady;

    void Awake()
    {
        if (disc == null) disc = transform;
        discRenderer = disc.GetComponent<SpriteRenderer>();
        discBaseScale = disc.localScale;
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

        if (tintHaloLikeDisc && haloRenderer != null && discRenderer != null)
        {
            Color c = discRenderer.color;
            c.a = haloRenderer.color.a;
            haloRenderer.color = c;
        }
    }

    /// Lo coloca en la esquina que GameBounds tenga reservada y lo reactiva.
    public void Place()
    {
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
