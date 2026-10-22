using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Patrones de ataque de la grieta. Con este componente la grieta deja de disparar suelto y
/// dispara en patrones que se leen, cada uno avisado antes: el eslabón se carga de luz amarilla.
///
///   Abanico     un eslabón suelta 5–15 proyectiles a la vez en cono. Puede llevar dentro el
///               orbe del QTE.
///   Ráfaga      3–5 seguidos hacia donde estás.                          (desde Aimed Level)
///   Muro        una pared vertical que avanza con un solo hueco.         (desde Wall Level)
///   Filas       chorros horizontales a varias alturas, con pasillos.     (desde Rows Level)
///   Barrido     un eslabón gira disparando, como un aspersor.            (desde Sweep Level)
///   Combinados  dos patrones a la vez.                                   (desde Combo Level)
///
/// Solo disparan los eslabones que están en su fase de fuego (no los que se están enfriando,
/// que son tu ventana para la onda). Cuanto más alto el nivel, patrones más seguidos, más
/// proyectiles, huecos más justos y avisos más cortos.
///
/// Va en el mismo objeto que el FractureDirector.
[RequireComponent(typeof(FractureDirector))]
public class FracturePatterns : MonoBehaviour
{
    [Header("Desde qué nivel sale cada patrón (el abanico, siempre)")]
    [SerializeField] private int aimedLevel = 16;
    [SerializeField] private int wallLevel = 20;
    [SerializeField] private int rowsLevel = 30;
    [SerializeField] private int sweepLevel = 50;
    [SerializeField] private int comboLevel = 60;
    [Range(0f, 1f)] [SerializeField] private float comboChance = 0.35f;
    [Tooltip("Abanicos cruzados: dos eslabones a la vez, sus conos se entrecruzan")]
    [SerializeField] private int crossFanLevel = 30;
    [Tooltip("Desde aquí los abanicos pueden salir en tres andanadas (antes, siempre dos)")]
    [SerializeField] private int tripleVolleyLevel = 40;
    [Tooltip("Desde aquí la grieta puede encadenar de 2 a 6 patrones seguidos, al azar")]
    [SerializeField] private int chainLevel = 40;
    [Range(0f, 1f)] [SerializeField] private float chainChance = 0.4f;
    [SerializeField] private Vector2Int chainLength = new Vector2Int(2, 6);

    [Header("Ritmo (primer nivel con grieta → cien niveles después)")]
    [Tooltip("Segundos entre un patrón y el siguiente")]
    [SerializeField] private Vector2 interval = new Vector2(2.4f, 1f);
    [Tooltip("Segundos de aviso (el eslabón se carga) antes de disparar")]
    [SerializeField] private Vector2 telegraph = new Vector2(0.8f, 0.4f);
    [Tooltip("Velocidad de los proyectiles de patrón respecto a la de la grieta")]
    [SerializeField] private float speedFactor = 1.6f;
    [Tooltip("Tope de proyectiles en pantalla, igual en todos los móviles")]
    [SerializeField] private int maxBullets = 120;

    [Header("Abanico")]
    [SerializeField] private Vector2Int fanCount = new Vector2Int(5, 15);
    [Tooltip("Apertura del cono en grados")]
    [SerializeField] private Vector2 fanSpread = new Vector2(60f, 100f);
    [Tooltip("Probabilidad de que un abanico lleve dentro el orbe del QTE (si al QTE le toca pronto)")]
    [Range(0f, 1f)] [SerializeField] private float fanCarriesQte = 0.6f;
    [Tooltip("Segundos entre andanadas de un mismo abanico")]
    [SerializeField] private float volleyGap = 0.22f;
    [Tooltip("Cuánto cambia el ángulo de una andanada a la siguiente (grados, al azar a un lado u otro)")]
    [SerializeField] private Vector2 volleyTurn = new Vector2(12f, 28f);

    [Header("Muro")]
    [Tooltip("Ancho del hueco en diámetros del jugador: holgado → justo")]
    [SerializeField] private Vector2 wallGap = new Vector2(3.2f, 2f);
    [SerializeField] private float wallSpeedFactor = 0.8f;

    [Header("Filas")]
    [SerializeField] private Vector2Int rowCount = new Vector2Int(3, 5);
    [SerializeField] private Vector2Int rowLength = new Vector2Int(4, 7);

    [Header("Barrido")]
    [SerializeField] private float sweepAngle = 120f;
    [SerializeField] private float sweepTime = 1.2f;

    FractureDirector fd;
    FractureQTE qte;
    PlayerController player;
    float nextAt = -1f;
    readonly List<FractureSection> firing = new List<FractureSection>();

    enum Kind { Fan, Aimed, Wall, Rows, Sweep, CrossFan }

    void Awake()
    {
        fd = GetComponent<FractureDirector>();
        qte = GetComponent<FractureQTE>();
    }

    void OnEnable() { if (fd != null) { fd.SingleShots = false; fd.Prepared += ResetPatterns; } }
    void OnDisable() { if (fd != null) { fd.SingleShots = true; fd.Prepared -= ResetPatterns; } ClearCharge(); }

    void ResetPatterns()
    {
        StopAllCoroutines();
        ClearCharge();
        nextAt = -1f;
    }

    float T => fd != null ? fd.LevelProgress : 0f;

    void Update()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (fd == null || !fd.Started || player == null || player.IsDead || player.IsExpelling || player.IsAbsorbed) return;
        if (Asteroid.WorldFrozen) { if (nextAt > 0f) nextAt += Time.deltaTime; return; }
        if (nextAt < 0f) nextAt = Time.time + 1.2f;
        if (Time.time < nextAt) return;

        if (fd.ShotsAlive >= maxBullets) { nextAt = Time.time + 0.3f; return; }
        CollectFiring();
        if (firing.Count == 0) { nextAt = Time.time + 0.5f; return; }

        float dur;
        if (fd.Level >= chainLevel && Random.value < chainChance)
        {
            // Cadena: de 2 a 6 patrones seguidos, uno detrás de otro
            int n = Random.Range(chainLength.x, chainLength.y + 1);
            dur = 0f;
            StartCoroutine(Chain(n));
            dur = n * 1.1f;
        }
        else
        {
            dur = StartPattern(Pick());
            if (fd.Level >= comboLevel && Random.value < comboChance) dur = Mathf.Max(dur, StartPattern(Pick()));
        }
        float gap = Mathf.Lerp(interval.x, interval.y, T) / Mathf.Max(0.05f, FractureDirector.CalibFireRate);
        nextAt = Time.time + dur + Mathf.Max(0.3f, gap);
    }

    void CollectFiring()
    {
        firing.Clear();
        foreach (var s in fd.Sections)
            if (s != null && s.gameObject.activeSelf && !s.Closed && !s.IsOpen) firing.Add(s);
    }

    Kind Pick()
    {
        var pool = new List<Kind> { Kind.Fan, Kind.Fan };
        int lvl = fd.Level;
        if (lvl >= aimedLevel) pool.Add(Kind.Aimed);
        if (lvl >= wallLevel) pool.Add(Kind.Wall);
        if (lvl >= rowsLevel) pool.Add(Kind.Rows);
        if (lvl >= sweepLevel) pool.Add(Kind.Sweep);
        if (lvl >= crossFanLevel && firing.Count >= 2) pool.Add(Kind.CrossFan);
        return pool[Random.Range(0, pool.Count)];
    }

    // Arranca un patrón y devuelve cuánto dura (aviso incluido)
    float StartPattern(Kind k)
    {
        float tele = Mathf.Lerp(telegraph.x, telegraph.y, T);
        var s = firing[Random.Range(0, firing.Count)];
        switch (k)
        {
            case Kind.Fan: StartCoroutine(Fan(s, tele)); return tele + 0.2f;
            case Kind.Aimed: StartCoroutine(Aimed(s, tele)); return tele + 0.6f;
            case Kind.Wall: StartCoroutine(Wall(tele)); return tele + 0.4f;
            case Kind.Rows: StartCoroutine(Rows(tele)); return tele + 1.2f;
            case Kind.Sweep: StartCoroutine(Sweep(s, tele)); return tele + sweepTime;
            case Kind.CrossFan: StartCoroutine(CrossFan(tele)); return tele + 0.8f;
        }
        return tele;
    }

    // ------------------------------------------------------------------ patrones
    IEnumerator Fan(FractureSection s, float tele)
    {
        yield return Charge(new[] { s }, tele);
        if (!Alive(s)) yield break;
        // El orbe del QTE puede salir dentro del abanico
        bool qteDone = false;
        yield return Volleys(s, () =>
        {
            if (!qteDone && qte != null && Random.value < fanCarriesQte) qte.LaunchWith(s);
            qteDone = true;
        });
    }

    // Andanadas de un abanico: dos siempre, tres a veces desde Triple Volley Level. Cada una
    // gira el ángulo al azar respecto a la anterior
    IEnumerator Volleys(FractureSection s, System.Action afterFirst)
    {
        float r = RadiusFor(s);
        float sp = Speed(r);
        int n = Mathf.RoundToInt(Mathf.Lerp(fanCount.x, fanCount.y, T) * Random.Range(0.85f, 1.15f));
        float spread = Mathf.Lerp(fanSpread.x, fanSpread.y, T);
        int volleys = fd.Level >= tripleVolleyLevel && Random.value < 0.5f ? 3 : 2;
        float center = AimAngle(s, Mouth(s, 0f));
        for (int v = 0; v < volleys && Alive(s); v++)
        {
            if (v > 0)
            {
                yield return Wait(volleyGap);
                if (!Alive(s)) yield break;
                center = ClampFront(s, center + Random.Range(volleyTurn.x, volleyTurn.y) * (Random.value < 0.5f ? -1f : 1f));
            }
            Vector2 from = Mouth(s, Random.Range(-0.15f, 0.15f));
            float sp2 = sp * (v == 0 ? 1f : Random.Range(0.85f, 1.05f));
            for (int i = 0; i < n; i++)
            {
                float a = center - spread * 0.5f + spread * (n > 1 ? i / (float)(n - 1) : 0.5f);
                fd.Launch(from, Dir(a), sp2, r, null, 0f);
            }
            if (v == 0) afterFirst?.Invoke();
        }
    }

    // Dos eslabones separados disparan abanicos a la vez hacia ti: los conos se cruzan
    IEnumerator CrossFan(float tele)
    {
        var a = firing[Random.Range(0, firing.Count)];
        FractureSection b = null;
        float far = -1f;
        foreach (var s in firing)
        {
            float d = Mathf.Abs(s.transform.position.y - a.transform.position.y);
            if (s != a && d > far) { far = d; b = s; }
        }
        if (b == null) yield break;
        yield return Charge(new[] { a, b }, tele);
        StartCoroutine(Volleys(a, null));
        yield return Volleys(b, null);
    }

    IEnumerator Chain(int n)
    {
        for (int i = 0; i < n && fd.Started; i++)
        {
            CollectFiring();
            if (firing.Count == 0) yield break;
            float d = StartPattern(Pick());
            yield return Wait(Mathf.Max(0.5f, d * 0.75f));
        }
    }

    IEnumerator Aimed(FractureSection s, float tele)
    {
        yield return Charge(new[] { s }, tele);
        float r = fd.CaliberRadius.y;
        int n = Random.Range(3, 6);
        for (int i = 0; i < n && Alive(s); i++)
        {
            Vector2 from = Mouth(s, Random.Range(-0.2f, 0.2f));
            Vector2 to = (Vector2)player.transform.position - from;
            float a = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg + Random.Range(-4f, 4f);
            fd.Launch(from, Dir(ClampFront(s, a)), Speed(r) * 1.1f, r, null, 0f);
            yield return Wait(0.11f);
        }
    }

    IEnumerator Wall(float tele)
    {
        var all = new List<FractureSection>(firing);
        yield return Charge(all.ToArray(), tele);
        if (!fd.Started) yield break;

        // De arriba abajo, lo que se ve de la grieta, con un solo hueco
        if (!Span(out float y0, out float y1)) yield break;
        float r = fd.CaliberRadius.x;
        float spacing = Mathf.Max(r * 2.4f, 0.5f);
        float gapW = Mathf.Max(1.2f, fd.PlayerRadius * 2f * Mathf.Lerp(wallGap.x, wallGap.y, T));
        int third = Random.Range(0, 3);
        float gapC = Mathf.Lerp(y0, y1, (third + Random.Range(0.25f, 0.75f)) / 3f);
        Vector2 front = fd.Front;
        float sp = Speed(r) * wallSpeedFactor;
        for (float y = y0; y <= y1; y += spacing)
        {
            if (Mathf.Abs(y - gapC) < gapW * 0.5f) continue;
            Vector2 p = new Vector2(fd.WallX(y), y) + front * (fd.RiftWidth * 0.45f);
            fd.Launch(p, front, sp, r, null, 0f);
        }
    }

    IEnumerator Rows(float tele)
    {
        var all = new List<FractureSection>(firing);
        yield return Charge(all.ToArray(), tele);
        if (!Span(out float y0, out float y1)) yield break;
        int rows = Random.Range(rowCount.x, rowCount.y + 1);
        int len = Random.Range(rowLength.x, rowLength.y + 1);
        float r = Random.value < 0.5f ? fd.CaliberRadius.x : fd.CaliberRadius.y;
        float step = (y1 - y0) / rows;
        var ys = new float[rows];
        for (int i = 0; i < rows; i++) ys[i] = y0 + step * (i + Random.Range(0.3f, 0.7f));
        Vector2 front = fd.Front;
        float sp = Speed(r);
        for (int j = 0; j < len && fd.Started; j++)
        {
            foreach (float y in ys)
            {
                Vector2 p = new Vector2(fd.WallX(y), y) + front * (fd.RiftWidth * 0.45f);
                fd.Launch(p, front, sp, r, null, 0f);
            }
            yield return Wait(0.13f);
        }
    }

    IEnumerator Sweep(FractureSection s, float tele)
    {
        yield return Charge(new[] { s }, tele);
        float r = fd.CaliberRadius.x;
        Vector2 from0 = Mouth(s, 0f);
        float center = AimAngle(s, from0);
        float dirSign = Random.value < 0.5f ? 1f : -1f;
        float half = sweepAngle * 0.5f;
        for (float t = 0f; t < sweepTime && Alive(s); t += 0.07f)
        {
            float a = center + dirSign * Mathf.Lerp(-half, half, t / sweepTime);
            fd.Launch(Mouth(s, 0f), Dir(ClampFront(s, a)), Speed(r), r, null, 0f);
            yield return Wait(0.07f);
        }
    }

    // ------------------------------------------------------------------ utilidades
    IEnumerator Charge(FractureSection[] secs, float time)
    {
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            float k = t / Mathf.Max(0.01f, time);
            float v = 0.35f + 0.65f * k + 0.15f * Mathf.Sin(t * 30f) * k;
            foreach (var s in secs) if (s != null) s.SetCharge(v);
            yield return null;
        }
        foreach (var s in secs) if (s != null) s.SetCharge(0f);
    }

    void ClearCharge()
    {
        if (fd == null) return;
        foreach (var s in fd.Sections) if (s != null) s.SetCharge(0f);
    }

    static IEnumerator Wait(float t)
    {
        for (float e = 0f; e < t; e += Time.deltaTime) yield return null;
    }

    bool Alive(FractureSection s) => fd.Started && s != null && s.gameObject.activeSelf && !s.Closed;

    // Boca del eslabón: en su centro (a lo largo, -0.5..0.5) y algo por delante de la grieta
    Vector2 Mouth(FractureSection s, float along) =>
        (Vector2)s.transform.position + (Vector2)s.transform.right * (along * s.Length) + s.Front * (fd.RiftWidth * 0.4f);

    float AimAngle(FractureSection s, Vector2 from)
    {
        Vector2 to = (Vector2)player.transform.position - from;
        float a = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg + Random.Range(-10f, 10f);
        return ClampFront(s, a);
    }

    float ClampFront(FractureSection s, float a)
    {
        float front = Mathf.Atan2(s.Front.y, s.Front.x) * Mathf.Rad2Deg;
        float cone = fd.FrontConeDeg;
        return front + Mathf.Clamp(Mathf.DeltaAngle(front, a), -cone, cone);
    }

    static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));

    float Speed(float radius)
    {
        var c = fd.CaliberRadius;
        return fd.ShotSpeedNow * speedFactor * Mathf.Lerp(1f, 0.75f, Mathf.InverseLerp(c.x, c.z, radius));
    }

    // Cada eslabón tiene su calibre (los tuyos S1/S2): sobre todo pequeños, algunos medianos y grandes
    float RadiusFor(FractureSection s)
    {
        int h = Mathf.Abs(s.GetInstanceID()) % 10;
        var c = fd.CaliberRadius;
        return h < 6 ? c.x : h < 9 ? c.y : c.z;
    }

    // Lo que se ve de la grieta en vertical (para muros y filas)
    bool Span(out float y0, out float y1)
    {
        y0 = float.MaxValue; y1 = float.MinValue;
        foreach (var s in fd.Sections)
        {
            if (s == null || !s.gameObject.activeSelf || s.Closed) continue;
            float half = s.Length * 0.5f;
            y0 = Mathf.Min(y0, s.transform.position.y - half);
            y1 = Mathf.Max(y1, s.transform.position.y + half);
        }
        var cam = Camera.main;
        if (cam != null)
        {
            Vector2 h = CameraView.Half(cam);
            float cy = cam.transform.position.y;
            y0 = Mathf.Max(y0, cy - h.y - 1f);
            y1 = Mathf.Min(y1, cy + h.y + 1f);
        }
        return y1 > y0 + 1f;
    }
}
