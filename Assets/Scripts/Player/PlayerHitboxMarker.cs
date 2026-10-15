using UnityEngine;

/// El punto que marca lo que de verdad te mata.
///
/// Es el truco que sostiene el género entero: en un bullet-hell el sprite ocupa cuarenta
/// píxeles y el hitbox son cuatro, en el centro. Eso permite dibujar al personaje **grande y
/// vistoso** — tu punto 2, que se pierde y cuesta verlo — sin que ser grande le haga daño,
/// porque lo que choca es el punto.
///
/// Y hay que ENSEÑARLO. Un hitbox invisible más pequeño que el dibujo no es generoso, es
/// desconcertante: mueres sin entender por qué viviste la vez anterior. Con el punto a la
/// vista, el jugador aprende en dos partidas que lo que importa es ese centro, y la diferencia
/// entre "no sé por qué he muerto" y "la he rozado yo" desaparece.
///
/// El anillo solo sale en modo preciso. Ahí es cuando estás enhebrando y cuando de verdad
/// necesitas saber el radio exacto; el resto del tiempo sería ruido en pantalla.
///
/// Va en el objeto del jugador, junto al PlayerController. Se construye solo.
[RequireComponent(typeof(PlayerController))]
public class PlayerHitboxMarker : MonoBehaviour
{
    [Header("Sprites")]
    [SerializeField] private Sprite coreSprite;      // hitbox_core.png
    [SerializeField] private Sprite ringSprite;      // hitbox_ring.png

    [Header("Núcleo")]
    [Tooltip("Diámetro del punto en unidades de mundo. Pequeño: marca el centro, no lo tapa")]
    [SerializeField] private float coreSize = 0.16f;
    [SerializeField] private Color coreColor = new Color(1f, 0.93f, 0.72f, 0.95f);
    [Tooltip("Cuánto late. Un punto quieto se confunde con parte del dibujo; uno que respira " +
             "se lee como información")]
    [SerializeField] private float pulseAmount = 0.18f;
    [SerializeField] private float pulseSpeed = 3.2f;

    [Header("Anillo (solo en modo preciso)")]
    [SerializeField] private Color ringColor = new Color(0.78f, 0.95f, 1f, 0.8f);
    [Tooltip("Lee el radio real del Collider2D del jugador, para que el anillo no mienta. " +
             "Desmárcalo solo si quieres forzar un tamaño a mano")]
    [SerializeField] private bool ringMatchesCollider = true;
    [SerializeField] private float ringSizeFallback = 0.62f;
    [Tooltip("Lo rápido que aparece y desaparece al pulsar el modo preciso")]
    [SerializeField] private float ringFade = 12f;

    [Header("Orden de dibujo")]
    [SerializeField] private string sortingLayer = "Player";
    [Tooltip("Por encima del sprite del astronauta: el punto nunca debe quedar tapado")]
    [SerializeField] private int sortingOrder = 50;

    PlayerController player;
    SpriteRenderer core, ring;
    float ringAlpha;
    float ringDiameter;

    void Awake()
    {
        player = GetComponent<PlayerController>();

        if (coreSprite == null)
        {
            Debug.LogWarning("PlayerHitboxMarker: falta el sprite del núcleo.", this);
            enabled = false;
            return;
        }

        core = Make("HitboxCore", coreSprite, sortingOrder);
        if (ringSprite != null) ring = Make("HitboxRing", ringSprite, sortingOrder - 1);

        ringDiameter = ringSizeFallback;
        if (ringMatchesCollider)
        {
            var col = GetComponentInChildren<Collider2D>();
            if (col != null)
            {
                // El lado corto del collider es lo que de verdad limita por dónde cabes
                var e = col.bounds.extents;
                ringDiameter = Mathf.Max(0.08f, Mathf.Min(e.x, e.y) * 2f);
            }
        }
    }

    SpriteRenderer Make(string name, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerName = sortingLayer;
        sr.sortingOrder = order;
        return sr;
    }

    void LateUpdate()
    {
        if (core == null) return;

        bool visible = !player.IsDead && !player.IsAbsorbed;
        core.enabled = visible;

        if (visible)
        {
            float pulse = 1f + pulseAmount * Mathf.Sin(Time.time * pulseSpeed);
            SetSize(core, coreSize * pulse, coreSprite);

            Color c = coreColor;
            // Mientras eres invulnerable el astronauta parpadea; el punto no, pero se atenúa
            // para que se entienda que ahora mismo no te puede pasar nada
            if (player.IsInvulnerable) c.a *= 0.45f;
            core.color = c;
        }

        if (ring == null) return;

        float want = (visible && player.IsFocusing) ? 1f : 0f;
        ringAlpha = Mathf.MoveTowards(ringAlpha, want, ringFade * Time.deltaTime);
        ring.enabled = ringAlpha > 0.01f;

        if (ring.enabled)
        {
            SetSize(ring, ringDiameter, ringSprite);
            Color c = ringColor;
            c.a *= ringAlpha;
            ring.color = c;
        }
    }

    /// Fija el tamaño en unidades de mundo independientemente de la PPU con la que se haya
    /// importado el PNG. Sin esto, cambiar la PPU cambia el tamaño en pantalla sin tocar nada.
    static void SetSize(SpriteRenderer sr, float diameter, Sprite sprite)
    {
        float unit = Mathf.Max(0.0001f, sprite.bounds.size.x);
        float s = diameter / unit;
        sr.transform.localScale = new Vector3(s, s, 1f);
    }

    void OnDrawGizmosSelected()
    {
        var col = GetComponentInChildren<Collider2D>();
        if (col == null) return;
        Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.9f);
        var e = col.bounds.extents;
        Gizmos.DrawWireSphere(col.bounds.center, Mathf.Min(e.x, e.y));
    }
}
