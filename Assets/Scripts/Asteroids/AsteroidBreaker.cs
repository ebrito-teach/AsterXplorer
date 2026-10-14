using UnityEngine;

[RequireComponent(typeof(Asteroid), typeof(Rigidbody2D))]
public class AsteroidBreaker : MonoBehaviour
{
    /// posición, tamaño y variante (índice del sprite = color de la roca)
    public static event System.Action<Vector2, float, int> Broken;

    Asteroid asteroid;
    Rigidbody2D rb;
    float bornTime;
    bool broken;

    void Awake()
    {
        asteroid = GetComponent<Asteroid>();
        rb = GetComponent<Rigidbody2D>();
    }

    // Al reutilizarse desde el pool vuelve a empezar
    void OnEnable()
    {
        bornTime = Time.time;
        broken = false;
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        var type = asteroid.Type;
        if (broken || type == null) return;

        if (!type.canBreak) { Why(type, "Can Break está desmarcado en " + type.id); return; }
        if (type.fragmentType == null) { Why(type, "Fragment Type está vacío en " + type.id); return; }
        if (Time.time - bornTime < type.breakShield)
        { Why(type, $"acaba de nacer (Break Shield {type.breakShield}s)"); return; }

        float v = c.relativeVelocity.magnitude;
        if (v < type.breakMinSpeed)
        { Why(type, $"el golpe fue de {v:0.00} u/s y Break Min Speed pide {type.breakMinSpeed}"); return; }

        // Se rompe con cualquier otro asteroide, salvo con fragmentos y piezas sueltas
        var other = c.collider.GetComponentInParent<Asteroid>();
        if (other == null) { Why(type, "chocó contra algo que no es un asteroide (pared o jugador)"); return; }
        if (other.Type == null || other.Type.behavior == AsteroidBehavior.Debris)
        { Why(type, "chocó contra un fragmento, y los fragmentos no rompen"); return; }

        // Fuera de cámara no se rompe: el nivel se iría deshaciendo sin que el jugador lo vea
        if (type.breakOnlyOnScreen && !OnScreen())
        { Why(type, $"el choque fue fuera de cámara, en {rb.position}"); return; }

        if (Asteroid.DebrisAlive >= type.maxFragmentsAlive)
        { Why(type, $"ya hay {Asteroid.DebrisAlive} fragmentos vivos (tope {type.maxFragmentsAlive})"); return; }

        Break();
    }

    void Why(AsteroidTypeData type, string reason)
    {
        if (type.logBreakRejections) Debug.Log("No se rompió: " + reason, this);
    }

    bool OnScreen()
    {
        var gb = GameBounds.Instance;
        if (gb == null) return true;
        Rect r = gb.CameraRect;
        float m = asteroid.WorldRadius;
        return rb.position.x > r.xMin - m && rb.position.x < r.xMax + m &&
               rb.position.y > r.yMin - m && rb.position.y < r.yMax + m;
    }

    void Break()
    {
        broken = true;
        var type = asteroid.Type;
        var pool = asteroid.Pool;
        Vector2 pos = rb.position;
        Vector2 vel = rb.linearVelocity;
        float size = asteroid.Size;
        int variant = System.Array.IndexOf(type.sprites, asteroid.CurrentSprite);
        if (variant < 0) variant = 0;

        var fType = type.fragmentType;
        if (fType != null && pool != null && Asteroid.DebrisAlive < type.maxFragmentsAlive)
        {
            Sprite[] group = type.FragmentsFor(variant);
            int count = Random.Range(type.fragmentCount.x, type.fragmentCount.y + 1);
            float start = Random.Range(0f, 360f);

            for (int i = 0; i < count; i++)
            {
                float ang = (start + i * 360f / count + Random.Range(-25f, 25f)) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

                Sprite sp = (group != null && group.Length > 0) ? group[Random.Range(0, group.Length)] : null;
                // Los trozos ya son recortes de la roca a su escala real: mismo tamaño que el padre
                float fSize = size * Random.Range(0.85f, 1.15f);
                Vector2 p = pos + dir * (asteroid.WorldRadius * 0.45f);

                var f = pool.Spawn(fType, sp, p, fSize);
                if (f == null) continue;

                float boost = Random.Range(type.fragmentBoost.x, type.fragmentBoost.y);
                Vector2 v = (vel + dir * Random.Range(0.6f, 1.4f)) * boost;
                f.SetMotion(v, Random.Range(-180f, 180f));
            }
        }

        Broken?.Invoke(pos, size, variant);

        // Que el cinturón no se vacíe: el generador lo sustituye por otro que entre por el borde
        if (type.recycle && pool != null && pool.NotifyExited(asteroid)) return;
        asteroid.Release();
    }
}
