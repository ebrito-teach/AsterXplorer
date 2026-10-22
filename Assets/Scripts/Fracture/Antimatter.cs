using UnityEngine;

/// Un proyectil de antimateria. Destruye cualquier roca que toca (asteroides, cometas,
/// fragmentos, estáticos, colosos) y se destruye con ella; el perseguidor del QTE no se
/// gasta. Al jugador lo mata.
///
/// Si pasa rozando al jugador sin tocarlo (a menos de Graze Distance de su borde) le da una
/// parte de carga de onda, una sola vez por proyectil.
///
/// Lo crea y recicla FractureDirector. Capa AsteroidMoving: así choca con todo lo que importa,
/// y con Ghost el jugador también lo atraviesa.
public class Antimatter : MonoBehaviour
{
    public float Radius { get; private set; }
    public bool Homing { get; private set; }
    public bool Alive => gameObject.activeSelf;

    FractureDirector owner;
    Rigidbody2D rb;
    CircleCollider2D col;
    SpriteRenderer sr, halo;
    Vector2 velocity;
    float speed, turnRate, born, spin;
    Transform target;
    bool grazed, wasClose;
    float minGap = float.MaxValue;

    public static Antimatter Create(FractureDirector owner, Transform parent)
    {
        var go = new GameObject("Antimateria");
        go.transform.SetParent(parent, false);
        int l = LayerMask.NameToLayer("AsteroidMoving");
        if (l >= 0) go.layer = l;
        var a = go.AddComponent<Antimatter>();
        a.owner = owner;
        a.rb = go.AddComponent<Rigidbody2D>();
        a.rb.bodyType = RigidbodyType2D.Dynamic;
        a.rb.gravityScale = 0f;
        a.rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        a.rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        a.col = go.AddComponent<CircleCollider2D>();
        a.col.isTrigger = true;

        var h = new GameObject("Halo");
        h.transform.SetParent(go.transform, false);
        a.halo = h.AddComponent<SpriteRenderer>();
        var c = new GameObject("Nucleo");
        c.transform.SetParent(go.transform, false);
        a.sr = c.AddComponent<SpriteRenderer>();
        go.SetActive(false);
        return a;
    }

    public void Launch(Vector2 pos, Vector2 dir, float spd, float radius, Sprite core, Sprite glow,
                       string layer, int order, Color haloColor, Transform homingTarget, float homingTurn)
    {
        transform.position = pos;
        rb.position = pos;
        Radius = radius;
        speed = spd;
        velocity = dir.normalized * spd;
        target = homingTarget;
        Homing = homingTarget != null;
        turnRate = homingTurn;
        born = Time.time;
        spin = Random.Range(-160f, 160f);
        grazed = wasClose = false;
        minGap = float.MaxValue;
        col.radius = radius;

        sr.sprite = core;
        sr.sortingLayerName = layer;
        sr.sortingOrder = order + 1;
        sr.color = Color.white;
        sr.transform.localScale = Vector3.one * (radius * 2.3f / Mathf.Max(0.01f, core != null ? core.bounds.size.y : 1f));
        halo.sprite = glow;
        halo.sortingLayerName = layer;
        halo.sortingOrder = order;
        halo.color = haloColor;
        halo.transform.localScale = Vector3.one * (radius * 4.2f / Mathf.Max(0.01f, glow != null ? glow.bounds.size.y : 1f));
        gameObject.SetActive(true);
        rb.linearVelocity = velocity;
    }

    public void Kill()
    {
        gameObject.SetActive(false);
    }

    void FixedUpdate()
    {
        if (Asteroid.WorldFrozen) { rb.linearVelocity = Vector2.zero; return; }
        if (Homing && target != null)
        {
            Vector2 to = (Vector2)target.position - rb.position;
            if (to.sqrMagnitude > 0.0001f)
            {
                float cur = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                float want = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
                float a = Mathf.MoveTowardsAngle(cur, want, turnRate * Time.fixedDeltaTime) * Mathf.Deg2Rad;
                velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed;
            }
        }
        rb.linearVelocity = velocity;
    }

    void Update()
    {
        float t = Time.time - born;
        if (!Asteroid.WorldFrozen) sr.transform.Rotate(0f, 0f, spin * Time.deltaTime);
        halo.transform.localRotation = Quaternion.Euler(0f, 0f, -t * 40f);
        var c = halo.color; c.a = Mathf.Clamp01(0.55f + 0.25f * Mathf.Sin(t * 9f)); halo.color = c;

        // Fuera del mundo: se acaba
        var gb = GameBounds.Instance;
        if (gb != null && gb.IsReady)
        {
            Rect a = gb.Area;
            Vector2 p = transform.position;
            if (p.x < a.xMin - 3f || p.x > a.xMax + 3f || p.y < a.yMin - 3f || p.y > a.yMax + 3f) { Kill(); return; }
        }
        if (t > 40f) { Kill(); return; }

        if (!Homing) CheckGraze();
    }

    // Roce: se acerca a menos de Graze Distance del jugador y se aleja sin tocarlo
    void CheckGraze()
    {
        if (grazed || owner == null) return;
        var p = owner.PlayerBody;
        if (p == null) return;
        float gap = Vector2.Distance(p.position, rb.position) - Radius - owner.PlayerRadius;
        if (gap < minGap) minGap = gap;
        if (gap <= owner.GrazeDistance && gap > 0f) wasClose = true;
        else if (wasClose && gap > owner.GrazeDistance)
        {
            grazed = true;
            owner.OnGraze(this);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!Alive || owner == null) return;
        var pc = other.GetComponentInParent<PlayerController>();
        if (pc != null) { owner.OnHitPlayer(this, pc); return; }
        var a = other.GetComponentInParent<Asteroid>();
        if (a != null && a.isActiveAndEnabled) owner.OnShotHitRock(this, a);
    }
}
