using UnityEngine;

[RequireComponent(typeof(Asteroid), typeof(Rigidbody2D))]
public class AsteroidBreaker : MonoBehaviour
{
    /// posición, tamaño y variante (índice del sprite = color de la roca)
    public static event System.Action<Vector2, float, int> Broken;

    /// Segundos mínimos entre dos roturas de todo el nivel, para que no se encadenen
    const float BreakSpacing = 0.45f;
    static float lastBreakTime = -99f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { lastBreakTime = -99f; }

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

        // Freno a las reacciones en cadena: dos roturas no pueden ocurrir en el mismo instante.
        // Sin esto, una rotura lanza trozos que rompen a otros y la pantalla se llena de golpe.
        if (Time.time - lastBreakTime < BreakSpacing)
        { Why(type, "otra roca acaba de romperse hace un instante"); return; }
        lastBreakTime = Time.time;

        Break(c, other);
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

    /// Rotura con CONO DE EYECCIÓN, como un cráter real:
    ///
    ///  - El material sale hacia el lado del impacto, en un cono, no en círculo. La pluma de
    ///    eyecta de DART salió justo por donde entró la nave.
    ///  - Las velocidades siguen una ley de potencias de exponente 1.22, medida en cráteres:
    ///    casi todo el material sale lento y uno o dos trozos salen disparados.
    ///  - La velocidad máxima de eyección es el 4-20 % de la del choque. La roca se parte y
    ///    sus pedazos siguen el camino de la madre; no se dispersan como confeti.
    ///  - Los tamaños decaen: un pedazo grande y los demás cada vez menores.
    void Break(Collision2D c, Asteroid other)
    {
        broken = true;
        var type = asteroid.Type;
        var pool = asteroid.Pool;
        Vector2 pos = rb.position;
        Vector2 vel = rb.linearVelocity;
        float size = asteroid.Size;
        float impactSpeed = c.relativeVelocity.magnitude;
        int variant = System.Array.IndexOf(type.sprites, asteroid.CurrentSprite);
        if (variant < 0) variant = 0;

        var fType = type.fragmentType;
        if (fType != null && pool != null && Asteroid.DebrisAlive < type.maxFragmentsAlive)
        {
            Sprite[] group = type.FragmentsFor(variant);
            int count = Random.Range(type.fragmentCount.x, type.fragmentCount.y + 1);

            // Eje del cono: del centro de la roca hacia quien la golpeó. Ahí está el cráter.
            Vector2 axis = other != null ? (other.Body.position - pos) : Random.insideUnitCircle;
            if (axis.sqrMagnitude < 0.0001f) axis = Vector2.right;
            axis.Normalize();
            float axisDeg = Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg;

            float vMax = Mathf.Max(0.8f, impactSpeed * Mathf.Max(0.01f, type.ejectaSpeedFraction));
            float vMin = vMax * 0.18f;
            float alpha = Mathf.Max(0.3f, type.ejectaSpeedExponent);
            float frac = Mathf.Clamp(type.largestFragment, 0.2f, 0.9f);

            for (int i = 0; i < count; i++)
            {
                float ang = (axisDeg + Random.Range(-type.ejectaConeAngle, type.ejectaConeAngle))
                            * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

                // Ley de potencias: v = vMin · u^(-1/alpha). La mayoría sale cerca de vMin.
                float u = Mathf.Max(0.02f, Random.value);
                float speed = Mathf.Min(vMax, vMin * Mathf.Pow(u, -1f / alpha));

                Sprite sp = (group != null && group.Length > 0) ? group[Random.Range(0, group.Length)] : null;
                float fSize = size * frac * Mathf.Pow(Mathf.Clamp(type.fragmentSizeFalloff, 0.4f, 0.95f), i)
                              * Random.Range(0.9f, 1.1f);
                Vector2 p = pos + dir * (asteroid.WorldRadius * 0.5f);

                var f = pool.Spawn(fType, sp, p, fSize);
                if (f == null) continue;
                f.Harmless = asteroid.Harmless;   // mundo oscuro: de una pieza inofensiva, trozos inofensivos

                // Heredan el movimiento de la roca madre: se separan de ella, no salen de cero
                f.SetMotion(vel + dir * speed, Random.Range(-200f, 200f));
            }
        }

        Broken?.Invoke(pos, size, variant);

        // Que el cinturón no se vacíe: el generador lo sustituye por otro que entre por el borde
        if (type.recycle && pool != null && pool.NotifyExited(asteroid)) return;
        asteroid.Release();
    }
}
