using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// El QTE de la fractura: orbes de antimateria que persiguen al jugador y se parrean tocando la
/// pantalla en el momento justo. Puede venir uno solo, dos o tres casi pegados (encadenados,
/// como en Guitar Hero): un toque preciso por orbe, en orden.
///
/// CÓMO VA
///
/// La rasgadura avisa (cambia de color) y un eslabón lanza la cadena. Sobre la cabeza del
/// jugador hay un aro fijo (el momento justo) y un aro que se cierra por cada orbe, en el orden
/// en que llegan; con puntos encima que dicen cuántos quedan. Todo sigue ahí hasta resolver el
/// último. Cada toque resuelve el orbe que va primero: dentro de su ventana, acierto; fuera
/// (antes de tiempo), fallo. Un orbe que llega sin que lo toques, también fallo.
///
///   Simple:  acierto = completa la carga en curso (hasta 2) y unos segundos invulnerable.
///   Doble:   los dos = 2 cargas + invulnerable + un empujón de velocidad limpia al terminar.
///            Un fallo = fallo.
///   Triple:  los tres = 3 cargas + invulnerable + velocidad. Un fallo = vives y 1 carga.
///            Dos fallos = fallo.
///   Fallo:   muere, salvo LightShield (se gasta entero) o Ghost (te atraviesa). Nunca da carga.
///
/// Los orbes no desaparecen por alejarse: si huyes, aceleran poco a poco. Con Slow activo, la
/// ventana es el doble. Mientras dura, el modo preciso no funciona.
///
/// Va en el mismo objeto que el FractureDirector.
[RequireComponent(typeof(FractureDirector))]
public class FractureQTE : MonoBehaviour
{
    [Header("Ritmo")]
    [SerializeField] private Vector2 homingEvery = new Vector2(7f, 17f);
    [Tooltip("Tiempo entre QTE cien niveles después de la primera grieta (se mezcla según el nivel)")]
    [SerializeField] private Vector2 homingEveryHigh = new Vector2(4f, 8f);
    [Tooltip("Segundos que la rasgadura cambia de color antes de lanzar")]
    [SerializeField] private float warningTime = 1f;
    [Tooltip("Un patrón puede llevar dentro el QTE si le faltan menos de estos segundos")]
    [SerializeField] private float mixWindow = 3f;

    [Header("Cadenas")]
    [SerializeField] private int doubleLevel = 30;
    [SerializeField] private int tripleLevel = 60;
    [Tooltip("Probabilidad de doble al entrar (nivel Double Level) y cien niveles después")]
    [SerializeField] private Vector2 doubleChance = new Vector2(0.2f, 0.5f);
    [Tooltip("Probabilidad de triple al entrar (nivel Triple Level) y cien niveles después")]
    [SerializeField] private Vector2 tripleChance = new Vector2(0.15f, 0.35f);
    [Tooltip("Segundos entre un orbe y el siguiente de la cadena (casi pegados)")]
    [SerializeField] private Vector2 chainGap = new Vector2(0.22f, 0.3f);

    [Header("El orbe")]
    [Tooltip("Radios: pequeño, mediano, grande, enorme")]
    [SerializeField] private Vector4 radii = new Vector4(0.3f, 0.45f, 0.65f, 0.95f);
    [Tooltip("Velocidad del pequeño y del enorme (los grandes, más lentos)")]
    [SerializeField] private Vector2 speed = new Vector2(6.5f, 4.5f);
    [SerializeField] private float turnRate = 110f;
    [Tooltip("Si en estos segundos no te ha alcanzado, empieza a acelerar (no desaparece)")]
    [SerializeField] private float accelAfter = 4f;
    [Tooltip("Cuánto acelera por segundo a partir de ahí (multiplicador)")]
    [SerializeField] private float accelPerSecond = 0.25f;

    [Header("Ventana")]
    [SerializeField] private float window = 0.25f;
    [SerializeField] private float windowAt60 = 0.2f;
    [SerializeField] private float windowAt100 = 0.16f;
    [Tooltip("Momento ideal: segundos antes del contacto")]
    [SerializeField] private float idealBeforeContact = 0.18f;
    [SerializeField] private float slowWindowMultiplier = 2f;

    [Header("Resultado")]
    [SerializeField] private float successIFrames = 1.6f;
    [Tooltip("Doble y triple completos: velocidad limpia al acabar la invulnerabilidad")]
    [SerializeField] private float boostFactor = 1.5f;
    [SerializeField] private float boostTime = 0.8f;
    [Range(0f, 1f)] [SerializeField] private float buffChance = 0.06f;

    [Header("Aspecto")]
    [SerializeField] private Color targetColor = new Color(0.45f, 1f, 0.6f, 0.95f);
    [Tooltip("Color del aro de cada orbe de la cadena: 1.º, 2.º y 3.º")]
    [SerializeField] private Color[] orbColors =
    {
        new Color(1f, 0.95f, 0.55f, 1f), new Color(1f, 0.65f, 0.25f, 1f), new Color(1f, 0.35f, 0.4f, 1f),
    };
    [SerializeField] private float aboveHead = 1.1f;
    [SerializeField] private float targetRadius = 0.42f;
    [SerializeField] private float startRadius = 1.6f;

    /// Panel de calibración: multiplica el tiempo entre QTE (menos = más a menudo)
    public static float CalibInterval = 1f;

    /// Hay un QTE en marcha
    public bool Running => orbs.Count > 0 || launching;

    FractureDirector fd;
    PlayerController player;
    FractureSection launcher;
    readonly List<Antimatter> orbs = new List<Antimatter>();
    readonly List<float> orbBorn = new List<float>();
    readonly List<float> orbBase = new List<float>();
    int chainSize, resolved, misses, passes;
    Coroutine launchRoutine;
    Antimatter lastResolved;
    bool launching;
    float nextAt = -1f, warnUntil;
    bool warning;
    SpriteRenderer ringTarget;
    readonly List<SpriteRenderer> closing = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> pips = new List<SpriteRenderer>();
    Sprite ringSprite, dotSprite;
    float flash;
    Color flashColor;

    void Awake()
    {
        fd = GetComponent<FractureDirector>();
        ringSprite = MakeRing(128);
        dotSprite = MakeDot(32);
        ringTarget = MakeRenderer("QTE_Objetivo", ringSprite, 801);
        for (int i = 0; i < 3; i++)
        {
            closing.Add(MakeRenderer("QTE_Cierra" + (i + 1), ringSprite, 802 + i));
            pips.Add(MakeRenderer("QTE_Punto" + (i + 1), dotSprite, 810));
        }
    }

    void OnEnable() { if (fd != null) fd.Prepared += ResetQTE; }
    void OnDisable() { if (fd != null) fd.Prepared -= ResetQTE; ShipInput.FocusBlocked = false; }

    void ResetQTE()
    {
        StopAllCoroutines();
        KillAll();
        launching = false;
        warning = false;
        SetWarning(0f);
        nextAt = -1f;
        ShipInput.FocusBlocked = false;
        HideRings();
        if (player != null) player.QteBoost = 1f;
    }

    SpriteRenderer MakeRenderer(string name, Sprite s, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.sortingLayerName = "FX"; sr.sortingOrder = order; sr.enabled = false;
        return sr;
    }

    void HideRings()
    {
        ringTarget.enabled = false;
        foreach (var c in closing) c.enabled = false;
        foreach (var p in pips) p.enabled = false;
    }

    float WindowNow()
    {
        int lvl = fd != null ? fd.Level : 1;
        if (lvl <= 20) return window;
        if (lvl <= 60) return Mathf.Lerp(window, windowAt60, (lvl - 20) / 40f);
        return Mathf.Lerp(windowAt60, windowAt100, Mathf.Clamp01((lvl - 60) / 40f));
    }

    float NextInterval()
    {
        float t = fd != null ? fd.LevelProgress : 0f;
        return Random.Range(Mathf.Lerp(homingEvery.x, homingEveryHigh.x, t), Mathf.Lerp(homingEvery.y, homingEveryHigh.y, t)) * CalibInterval;
    }

    int PickChainSize()
    {
        int lvl = fd.Level;
        if (lvl >= tripleLevel && Random.value < Mathf.Lerp(tripleChance.x, tripleChance.y, Mathf.Clamp01((lvl - tripleLevel) / 100f))) return 3;
        if (lvl >= doubleLevel && Random.value < Mathf.Lerp(doubleChance.x, doubleChance.y, Mathf.Clamp01((lvl - doubleLevel) / 100f))) return 2;
        return 1;
    }

    /// Lo llama un patrón (un abanico): si al QTE le toca pronto, sale ahora desde ese eslabón
    public bool LaunchWith(FractureSection from)
    {
        if (Running || fd == null || !fd.Started || player == null || from == null || from.Closed) return false;
        if (!TeachingPlan.QteAllowed(fd.Level)) return false;
        if (nextAt < 0f || Time.time < nextAt - mixWindow) return false;
        launcher = from;
        Launch();
        return true;
    }

    void Update()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        flash = Mathf.MoveTowards(flash, 0f, Time.unscaledDeltaTime / 0.35f);

        bool off = fd == null || !fd.Active || !fd.Started || player == null || !TeachingPlan.QteAllowed(fd.Level)
                   || player.IsDead || player.IsExpelling || player.IsAbsorbed;
        if (off)
        {
            if (Running) EndChain();
            if (warning) { warning = false; SetWarning(0f); }
            ShipInput.FocusBlocked = false;
            HideRings();
            return;
        }
        if (nextAt < 0f) nextAt = Time.time + NextInterval();
        if (Asteroid.WorldFrozen) { nextAt += Time.deltaTime; warnUntil += Time.deltaTime; for (int i = 0; i < orbBorn.Count; i++) orbBorn[i] += Time.deltaTime; }

        // 1. Aviso y lanzamiento
        if (!Running)
        {
            if (!warning && Time.time >= nextAt - warningTime)
            {
                launcher = fd.RandomAliveSection();
                if (launcher == null) { nextAt = Time.time + 2f; return; }
                warning = true;
                warnUntil = Time.time + warningTime;
            }
            if (warning)
            {
                float k = 1f - Mathf.Clamp01((warnUntil - Time.time) / Mathf.Max(0.01f, warningTime));
                SetWarning(Mathf.Sin(k * Mathf.PI * 6f) * 0.3f + k * 0.7f);
                if (Time.time >= warnUntil) Launch();
            }
            return;
        }

        // 2. Persecución: los que huyen, aceleran
        for (int i = 0; i < orbs.Count; i++)
        {
            var o = orbs[i];
            if (o == null || !o.Alive) continue;
            float age = Time.time - orbBorn[i];
            if (age > accelAfter && !Asteroid.WorldFrozen)
                o.Redirect(o.Velocity, orbBase[i] * (1f + accelPerSecond * (age - accelAfter)));
        }

        // Un orbe que desapareció por su cuenta (fuera del mundo, etc.) cuenta como resuelto sin fallo
        while (orbs.Count > 0 && (orbs[0] == null || !orbs[0].Alive)) { RemoveFirst(); resolved++; passes++; }
        if (orbs.Count == 0)
        {
            if (launching) UpdateRings(WindowNow());   // el siguiente aún sale de la grieta
            else if (resolved > 0) Finish();
            else EndChain();
            return;
        }

        float win = WindowNow() * (IsBuff(BuffType.Slow) ? slowWindowMultiplier : 1f);
        UpdateRings(win);

        // Ghost: lo atraviesa; ni muerte ni carga
        if (IsBuff(BuffType.Ghost) && TimeToImpact(orbs[0]) <= 0.02f) { fd.Burst(orbs[0].transform.position, orbs[0].Radius, 12); Resolve(false, true); return; }

        if (TapThisFrame())
        {
            float tti = TimeToImpact(orbs[0]);
            Resolve(Mathf.Abs(tti - idealBeforeContact) <= win * 0.5f, false);
        }
    }

    void Launch()
    {
        warning = false;
        SetWarning(0f);
        if (launcher == null || launcher.Closed) launcher = fd.RandomAliveSection();
        if (launcher == null) { nextAt = Time.time + 2f; return; }
        chainSize = PickChainSize();
        resolved = 0; misses = 0; passes = 0;
        ShipInput.FocusBlocked = true;
        launchRoutine = StartCoroutine(LaunchChain(launcher, chainSize));

        if (Random.value < buffChance && BuffSystem.Instance != null)
        {
            Vector2 from = (Vector2)launcher.transform.position + launcher.Front * (fd.RiftWidth * 0.4f);
            int lvl = fd.Level;
            var p = BuffPickup.Create(BuffSystem.Instance.PickRandom(t => TeachingPlan.BuffAllowed(t, lvl)), from + launcher.Front * 0.6f, 0.6f, "Goal");
            p.Drift(launcher.Front * Random.Range(2.5f, 4f) + Random.insideUnitCircle * 0.8f, 0.5f);
        }
    }

    // Los orbes salen uno detrás de otro, casi pegados, con el mismo tamaño y velocidad
    System.Collections.IEnumerator LaunchChain(FractureSection from, int n)
    {
        launching = true;
        int launched = 0;
        int size = n > 1 ? Random.Range(0, 2) : Random.Range(0, 4);   // en cadena, pequeños o medianos
        float r = radii[size];
        float sp = Mathf.Lerp(speed.x, speed.y, size / 3f);
        for (int i = 0; i < n; i++)
        {
            if (from == null || from.Closed || player == null || player.IsDead) break;
            Vector2 p0 = (Vector2)from.transform.position + from.Front * (fd.RiftWidth * 0.4f);
            Vector2 dir = ((Vector2)player.transform.position - p0).normalized;
            var o = fd.Launch(p0, dir, sp, r, player.transform, turnRate);
            if (o != null) { orbs.Add(o); orbBorn.Add(Time.time); orbBase.Add(sp); launched++; }
            if (i < n - 1)
            {
                float gap = Random.Range(chainGap.x, chainGap.y);
                for (float t = 0f; t < gap; t += Time.deltaTime) yield return null;
            }
        }
        chainSize = Mathf.Max(1, launched);   // si la grieta se cerró a mitad, la cadena es más corta
        launching = false;
        launchRoutine = null;
    }

    // Resuelve el primer orbe de la cadena (acierto o fallo) y decide si la cadena sigue
    void Resolve(bool hit, bool ghostPass)
    {
        var o = orbs[0];
        Vector2 at = o != null ? (Vector2)o.transform.position : (Vector2)player.transform.position;
        if (hit) { fd.Burst(at, (o != null ? o.Radius : 0.4f) * 1.5f, 30); flash = 1f; flashColor = Color.white; }
        else if (!ghostPass) { fd.Burst(at, o != null ? o.Radius : 0.4f, 12); misses++; flash = 1f; flashColor = FractureDirector.WarningColor; }
        else passes++;
        // Si este fallo pierde la cadena, el orbe se queda para el primer plano (lo quita Fail)
        int allowedNow = chainSize >= 3 ? 1 : 0;
        bool losing = !hit && !ghostPass && misses > allowedNow && !IsBuff(BuffType.LightShield) && !IsBuff(BuffType.Ghost);
        lastResolved = losing ? o : null;
        if (o != null && !losing) o.Kill();
        RemoveFirst();
        if (losing && o != null) orbs.Add(o);   // EndChain lo matará salvo que Fail lo reserve
        resolved++;

        // ¿Ya está perdida la cadena? simple y doble: un fallo; triple: dos fallos
        int allowed = chainSize >= 3 ? 1 : 0;
        if (misses > allowed) { Fail(); return; }
        if (resolved >= chainSize && orbs.Count == 0 && !launching) Finish();
    }

    void RemoveFirst()
    {
        if (orbs.Count == 0) return;
        orbs.RemoveAt(0); orbBorn.RemoveAt(0); orbBase.RemoveAt(0);
    }

    // Cadena terminada sin perderla
    void Finish()
    {
        int hits = resolved - misses - passes;
        if (passes > 0) { }   // Ghost lo atravesó: ni muerte ni carga
        else if (chainSize == 1)
        {
            if (hits >= 1) { fd.CompleteCharge(); player.GrantInvulnerability(successIFrames); }
        }
        else if (misses == 0)
        {
            fd.GrantCharges(chainSize);
            player.GrantInvulnerability(successIFrames);
            StartCoroutine(Boost());
        }
        else if (chainSize >= 3 && misses == 1)
        {
            fd.GrantCharges(1);
            player.GrantInvulnerability(successIFrames * 0.5f);
        }
        Debug.Log($"FractureQTE: cadena de {chainSize} — {hits} aciertos, {misses} fallos. Cargas {fd.Charge:0.00}.");
        EndChain();
    }

    System.Collections.IEnumerator Boost()
    {
        for (float t = 0f; t < successIFrames; t += Time.deltaTime) yield return null;
        if (player == null) yield break;
        player.QteBoost = boostFactor;
        for (float t = 0f; t < boostTime; t += Time.deltaTime) yield return null;
        if (player != null) player.QteBoost = 1f;
    }

    void Fail()
    {
        flash = 1f; flashColor = FractureDirector.WarningColor;
        if (IsBuff(BuffType.LightShield))
        {
            BuffSystem.Instance.ConsumeShield();
            player.GrantInvulnerability(0.6f);
            Debug.Log("FractureQTE: fallo, el escudo lo aguanta (se gasta entero).");
            EndChain();
        }
        else if (IsBuff(BuffType.Ghost))
        {
            Debug.Log("FractureQTE: fallo, Ghost lo deja pasar.");
            EndChain();
        }
        else
        {
            Debug.Log("FractureQTE: fallo.");
            // El orbe que llegaba primero se queda (lo congela el primer plano); el resto se va
            Antimatter killer = lastResolved != null && lastResolved.Alive ? lastResolved : null;
            if (killer != null) orbs.Remove(killer);
            EndChain();
            Vector2 me = player.transform.position;
            Vector2 at = killer != null ? (Vector2)killer.transform.position : me;
            player.KillBy(killer, killer != null ? at + (me - at).normalized * killer.Radius : me);
            if (killer != null && (!player.IsDead || !DeathCloseUp.WillPlay)) killer.Kill();
        }
    }

    /// Un orbe tocó al jugador sin que él respondiera: fallo de ese orbe
    public void OnHomingHit(Antimatter a)
    {
        int i = orbs.IndexOf(a);
        if (i < 0) { a.Kill(); return; }
        // Los anteriores sin resolver también cuentan como fallos (le llegaron todos)
        while (orbs.Count > 0 && orbs[0] != a) Resolve(false, false);
        if (orbs.Count > 0 && orbs[0] == a) Resolve(false, false);
    }

    void KillAll()
    {
        foreach (var o in orbs) if (o != null) o.Kill();
        orbs.Clear(); orbBorn.Clear(); orbBase.Clear();
    }

    void EndChain()
    {
        if (launchRoutine != null) { StopCoroutine(launchRoutine); launchRoutine = null; }
        launching = false;
        KillAll();
        HideRings();
        ShipInput.FocusBlocked = false;
        nextAt = Time.time + NextInterval();
    }

    float TimeToImpact(Antimatter o)
    {
        if (o == null) return 0f;
        Vector2 p = player.transform.position, q = o.transform.position;
        float gap = Vector2.Distance(p, q) - o.Radius - fd.PlayerRadius;
        float closing = Mathf.Max(0.5f, o.Speed);
        float ts = Time.timeScale > 0.01f ? Time.timeScale : 1f;
        return Mathf.Max(0f, gap) / closing / ts;
    }

    // Un aro fijo y uno que se cierra por cada orbe pendiente (el primero, delante); puntos encima
    void UpdateRings(float win)
    {
        float s = Mathf.Abs(player.transform.lossyScale.y);
        Vector2 head = (Vector2)player.transform.position + Vector2.up * (aboveHead * s * 0.5f + 0.4f);
        float unit = ringSprite.bounds.size.x * 0.5f;
        ringTarget.enabled = true;
        ringTarget.transform.position = head;
        float tw = targetRadius * (1f + 0.25f * (win / window - 1f));
        ringTarget.transform.localScale = Vector3.one * (tw / unit);
        float tti0 = orbs.Count > 0 ? TimeToImpact(orbs[0]) : 99f;
        bool inWindow = Mathf.Abs(tti0 - idealBeforeContact) <= win * 0.5f;
        Color tc = inWindow ? Color.Lerp(targetColor, Color.white, 0.5f) : targetColor;
        ringTarget.color = Color.Lerp(tc, flashColor, flash);

        int order = resolved;   // posición en la cadena del primero pendiente
        for (int i = 0; i < closing.Count; i++)
        {
            var c = closing[i];
            bool on = i < orbs.Count && orbs[i] != null && orbs[i].Alive;
            c.enabled = on;
            if (!on) continue;
            float tti = TimeToImpact(orbs[i]);
            float k = Mathf.Clamp01((tti - idealBeforeContact) / 1.4f);
            float rc = Mathf.Lerp(targetRadius, startRadius, k);
            if (tti < idealBeforeContact) rc = Mathf.Lerp(targetRadius * 0.6f, targetRadius, tti / Mathf.Max(0.01f, idealBeforeContact));
            c.transform.position = head;
            c.transform.localScale = Vector3.one * (rc / unit);
            var col = orbColors != null && orbColors.Length > 0 ? orbColors[Mathf.Min(order + i, orbColors.Length - 1)] : Color.white;
            if (i > 0) col.a *= 0.6f;   // los que vienen detrás, más tenues
            c.color = Color.Lerp(col, flashColor, flash);
        }

        // Puntos: cuántos orbes quedan de la cadena
        int left = chainSize - resolved;
        for (int i = 0; i < pips.Count; i++)
        {
            bool on = chainSize > 1 && i < left;
            pips[i].enabled = on;
            if (!on) continue;
            float x = (i - (left - 1) * 0.5f) * 0.22f * s;
            pips[i].transform.position = head + new Vector2(x, targetRadius + 0.22f * s);
            pips[i].transform.localScale = Vector3.one * (0.12f * s / dotSprite.bounds.size.x);
            pips[i].color = orbColors != null && orbColors.Length > 0 ? orbColors[Mathf.Min(resolved + i, orbColors.Length - 1)] : Color.white;
        }
    }

    void SetWarning(float k)
    {
        if (fd == null) return;
        foreach (var s in fd.Sections) if (s != null) s.SetWarning(s == launcher ? k : k * 0.45f);
    }

    static bool IsBuff(BuffType t) => BuffSystem.Instance != null && BuffSystem.Instance.IsActive(t);

    static bool TapThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var ts = Touchscreen.current;
        if (ts != null)
            foreach (var t in ts.touches)
                if (t.press.wasPressedThisFrame) return true;
        var m = Mouse.current;
        if (m != null && m.leftButton.wasPressedThisFrame) return true;
        var k = Keyboard.current;
        if (k != null && k.eKey.wasPressedThisFrame) return true;
        return false;
#else
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E)) return true;
        for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
        return false;
#endif
    }

    static Sprite MakeRing(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - Mathf.Abs(r - 0.9f) / 0.07f)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    static Sprite MakeDot(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) / 0.15f)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    [ContextMenu("Lanzar un QTE ya")]
    void LaunchNow() { if (fd != null && fd.Active && !Running) { warning = false; nextAt = Time.time + warningTime; } }
    [ContextMenu("Lanzar un QTE doble ya")]
    void LaunchDouble() { ForceChain(2); }
    [ContextMenu("Lanzar un QTE triple ya")]
    void LaunchTriple() { ForceChain(3); }

    void ForceChain(int n)
    {
        if (fd == null || !fd.Active || Running || player == null) return;
        launcher = fd.RandomAliveSection();
        if (launcher == null) return;
        warning = false; SetWarning(0f);
        chainSize = n; resolved = 0; misses = 0; passes = 0;
        ShipInput.FocusBlocked = true;
        launchRoutine = StartCoroutine(LaunchChain(launcher, n));
    }
}
