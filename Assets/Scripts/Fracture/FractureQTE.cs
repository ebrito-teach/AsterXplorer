using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// El QTE de la fractura: un proyectil de antimateria que persigue al jugador.
///
/// CÓMO VA
///
/// Cada Homing Every segundos (al azar) la rasgadura avisa: cambia de color durante Warning
/// Time. Luego un eslabón lanza una antimateria perseguidora de tamaño al azar (pequeña,
/// mediana, grande o enorme). Sobre la cabeza del jugador aparecen dos círculos: uno fijo (el
/// punto ideal) y otro que se va cerrando según se acerca el proyectil. Hay que tocar
/// cualquier parte de la pantalla justo cuando coinciden. Mientras dura, el modo preciso no
/// funciona.
///
///   Bien:            la antimateria se deshace en pedazos de color, el jugador es invulnerable
///                    unos segundos para recolocarse y se completa su carga de onda en curso.
///   Antes de tiempo: falla. Si el proyectil le alcanza sin tocar: falla.
///   Al fallar:       muere, salvo que tenga activo LightShield (se gasta entero) o Ghost.
///                    Fallar nunca da carga.
///
/// Con Slow activo, la ventana es el doble de ancha.
///
/// De vez en cuando, en lugar de solo avisar, la rasgadura suelta además un buff hacia el
/// jugador (Buff Chance).
///
/// Va en el mismo objeto que el FractureDirector.
[RequireComponent(typeof(FractureDirector))]
public class FractureQTE : MonoBehaviour
{
    [Header("Ritmo")]
    [SerializeField] private Vector2 homingEvery = new Vector2(7f, 17f);
    [Tooltip("Segundos que la rasgadura cambia de color antes de lanzar")]
    [SerializeField] private float warningTime = 1f;

    [Header("El proyectil")]
    [Tooltip("Radios: pequeño, mediano, grande, enorme")]
    [SerializeField] private Vector4 radii = new Vector4(0.3f, 0.45f, 0.65f, 0.95f);
    [Tooltip("Velocidad del pequeño y del enorme (los grandes, más lentos)")]
    [SerializeField] private Vector2 speed = new Vector2(6.5f, 4.5f);
    [Tooltip("Grados por segundo que puede girar persiguiendo")]
    [SerializeField] private float turnRate = 110f;
    [Tooltip("Segundos máximos de persecución; luego se deshace sin más")]
    [SerializeField] private float maxChase = 9f;

    [Header("Ventana")]
    [Tooltip("Ancho de la ventana para acertar, en segundos de tiempo real")]
    [SerializeField] private float window = 0.25f;
    [Tooltip("La ventana se estrecha con el nivel: en el nivel 60 y en el 100 (no baja de ahí). " +
             "Hasta el 20, la de arriba")]
    [SerializeField] private float windowAt60 = 0.2f;
    [SerializeField] private float windowAt100 = 0.16f;
    [Tooltip("Momento ideal: segundos antes del contacto")]
    [SerializeField] private float idealBeforeContact = 0.18f;
    [Tooltip("Multiplicador de la ventana con Slow activo")]
    [SerializeField] private float slowWindowMultiplier = 2f;

    [Header("Resultado")]
    [Tooltip("Segundos de invulnerabilidad tras acertar")]
    [SerializeField] private float successIFrames = 1.6f;
    [Range(0f, 1f)] [SerializeField] private float buffChance = 0.06f;

    [Header("Aspecto")]
    [SerializeField] private Color targetColor = new Color(0.45f, 1f, 0.6f, 0.95f);
    [SerializeField] private Color closingColor = new Color(1f, 0.92f, 0.5f, 1f);
    [Tooltip("Altura sobre el centro del jugador, en sus propios tamaños")]
    [SerializeField] private float aboveHead = 1.1f;
    [SerializeField] private float targetRadius = 0.42f;
    [SerializeField] private float startRadius = 1.6f;

    /// Panel de calibración: multiplica el tiempo entre perseguidores (menos = más a menudo)
    public static float CalibInterval = 1f;

    [Tooltip("Tiempo entre perseguidores en niveles altos (cien niveles después de la primera grieta): " +
             "se mezcla con Homing Every según el nivel. Más frecuentes cuanto más alto")]
    [SerializeField] private Vector2 homingEveryHigh = new Vector2(4f, 8f);
    [Tooltip("Un patrón puede llevar dentro el orbe del QTE si al QTE le faltan menos de estos segundos")]
    [SerializeField] private float mixWindow = 3f;

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

    /// Lo llama un patrón (un abanico): si al QTE le toca pronto, su orbe sale ahora desde ese
    /// eslabón, mezclado con el resto de proyectiles. Devuelve true si salió
    public bool LaunchWith(FractureSection from)
    {
        if (shot != null || fd == null || !fd.Started || player == null || from == null || from.Closed) return false;
        if (!TeachingPlan.QteAllowed(fd.Level)) return false;
        if (nextAt < 0f || Time.time < nextAt - mixWindow) return false;
        launcher = from;
        Launch();
        return shot != null;
    }

    /// Hay un QTE en marcha
    public bool Running => shot != null;

    FractureDirector fd;
    PlayerController player;
    Antimatter shot;
    FractureSection launcher;
    float nextAt = -1f, warnUntil, chaseStart;
    bool warning;
    SpriteRenderer ringTarget, ringClosing;
    Sprite ringSprite;
    float flash;
    Color flashColor;

    void Awake()
    {
        fd = GetComponent<FractureDirector>();
        ringSprite = MakeRing(128);
        ringTarget = MakeRingRenderer("QTE_Objetivo", 801);
        ringClosing = MakeRingRenderer("QTE_Cierra", 802);
    }

    void OnEnable()
    {
        if (fd != null) fd.Prepared += ResetQTE;
    }

    void OnDisable()
    {
        if (fd != null) fd.Prepared -= ResetQTE;
        ShipInput.FocusBlocked = false;
    }

    void ResetQTE()
    {
        if (shot != null) shot.Kill();
        shot = null;
        warning = false;
        SetWarning(0f);
        nextAt = -1f;   // se programa al terminar la espera inicial de la grieta
        ShipInput.FocusBlocked = false;
        HideRings();
    }

    SpriteRenderer MakeRingRenderer(string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = ringSprite;
        sr.sortingLayerName = "FX";
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    void HideRings() { ringTarget.enabled = false; ringClosing.enabled = false; }

    void Update()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        flash = Mathf.MoveTowards(flash, 0f, Time.unscaledDeltaTime / 0.35f);

        if (fd == null || !fd.Active || !fd.Started || player == null || !TeachingPlan.QteAllowed(fd.Level))
        {
            if (shot != null) EndShot(false);
            if (warning) { warning = false; SetWarning(0f); }
            ShipInput.FocusBlocked = false;
            HideRings();
            return;
        }
        if (player.IsDead || player.IsExpelling || player.IsAbsorbed)
        {
            if (shot != null) EndShot(false);
            if (warning) { warning = false; SetWarning(0f); }
            HideRings();
            ShipInput.FocusBlocked = false;
            return;
        }
        if (nextAt < 0f) nextAt = Time.time + NextInterval();
        if (Asteroid.WorldFrozen) { nextAt += Time.deltaTime; warnUntil += Time.deltaTime; }

        // 1. Aviso y lanzamiento
        if (shot == null)
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

        // 2. Persecución y círculos
        if (!shot.Alive) { EndShot(false); return; }
        if (Time.time - chaseStart > maxChase) { fd.Burst(shot.transform.position, shot.Radius, 10); EndShot(false); return; }

        float tti = TimeToImpact();
        float win = WindowNow() * (IsBuff(BuffType.Slow) ? slowWindowMultiplier : 1f);
        UpdateRings(tti, win);

        // Ghost: lo atraviesa; sin muerte y sin carga
        if (IsBuff(BuffType.Ghost) && tti <= 0.02f) { fd.Burst(shot.transform.position, shot.Radius, 12); EndShot(false); return; }

        if (TapThisFrame())
        {
            if (Mathf.Abs(tti - idealBeforeContact) <= win * 0.5f) Success();
            else Fail();
        }
    }

    void Launch()
    {
        warning = false;
        SetWarning(0f);
        if (launcher == null || launcher.Closed) launcher = fd.RandomAliveSection();
        if (launcher == null) { nextAt = Time.time + 2f; return; }

        int size = Random.Range(0, 4);
        float r = radii[size];
        float sp = Mathf.Lerp(speed.x, speed.y, size / 3f);
        Vector2 from = (Vector2)launcher.transform.position + launcher.Front * (fd.RiftWidth * 0.4f);
        Vector2 dir = ((Vector2)player.transform.position - from).normalized;
        shot = fd.Launch(from, dir, sp, r, player.transform, turnRate);
        chaseStart = Time.time;
        ShipInput.FocusBlocked = true;

        // De vez en cuando, además, un buff que sale despedido hacia el jugador
        if (Random.value < buffChance && BuffSystem.Instance != null)
        {
            var p = BuffPickup.Create(BuffSystem.Instance.PickRandom(), from + launcher.Front * 0.6f, 0.6f, "Goal");
            p.Drift(launcher.Front * Random.Range(2.5f, 4f) + Random.insideUnitCircle * 0.8f, 0.5f);
        }
    }

    // Segundos (de tiempo real) hasta que el proyectil toque al jugador
    float TimeToImpact()
    {
        Vector2 p = player.transform.position, q = shot.transform.position;
        float gap = Vector2.Distance(p, q) - shot.Radius - fd.PlayerRadius;
        float closing = Mathf.Max(0.5f, shot.Speed);
        float ts = Time.timeScale > 0.01f ? Time.timeScale : 1f;
        return Mathf.Max(0f, gap) / closing / ts;
    }

    void UpdateRings(float tti, float win)
    {
        Vector2 head = (Vector2)player.transform.position + Vector2.up * (aboveHead * Mathf.Abs(player.transform.lossyScale.y) * 0.5f + 0.4f);
        ringTarget.enabled = ringClosing.enabled = true;
        ringTarget.transform.position = head;
        ringClosing.transform.position = head;

        // El que se cierra llega al tamaño del fijo justo en el momento ideal
        float lead = 1.4f;   // segundos antes del ideal a los que empieza a cerrarse desde el tamaño grande
        float k = Mathf.Clamp01((tti - idealBeforeContact) / lead);
        float rc = Mathf.Lerp(targetRadius, startRadius, k);
        if (tti < idealBeforeContact) rc = Mathf.Lerp(targetRadius * 0.6f, targetRadius, tti / Mathf.Max(0.01f, idealBeforeContact));
        float unit = ringSprite.bounds.size.x * 0.5f;
        ringClosing.transform.localScale = Vector3.one * (rc / unit);
        // El fijo se ensancha un poco con Slow: la ventana es mayor
        float tw = targetRadius * (1f + 0.25f * (win / window - 1f));
        ringTarget.transform.localScale = Vector3.one * (tw / unit);

        bool inWindow = Mathf.Abs(tti - idealBeforeContact) <= win * 0.5f;
        Color tc = inWindow ? Color.Lerp(targetColor, Color.white, 0.5f) : targetColor;
        ringTarget.color = Color.Lerp(tc, flashColor, flash);
        ringClosing.color = Color.Lerp(closingColor, flashColor, flash);
    }

    void Success()
    {
        var at = (Vector2)shot.transform.position;
        // La antimateria se deshace en pedazos de color reconocible
        fd.Burst(at, shot.Radius * 1.5f, 40);
        fd.CompleteCharge();
        player.GrantInvulnerability(successIFrames);
        flash = 1f; flashColor = Color.white;
        Debug.Log($"FractureQTE: ¡bien! Carga {fd.Charge:0.00}.");
        EndShot(true);
    }

    void Fail()
    {
        flash = 1f; flashColor = FractureDirector.WarningColor;
        var at = (Vector2)shot.transform.position;
        fd.Burst(at, shot.Radius, 14);
        if (IsBuff(BuffType.LightShield))
        {
            BuffSystem.Instance.ConsumeShield();
            player.GrantInvulnerability(0.6f);
            Debug.Log("FractureQTE: fallo, el escudo lo aguanta (se gasta entero).");
        }
        else if (IsBuff(BuffType.Ghost))
        {
            Debug.Log("FractureQTE: fallo, Ghost lo deja pasar.");
        }
        else
        {
            Debug.Log("FractureQTE: fallo.");
            EndShot(false);
            player.Kill();
            return;
        }
        EndShot(false);
    }

    /// El perseguidor tocó al jugador sin que él respondiera: cuenta como fallo
    public void OnHomingHit(Antimatter a)
    {
        if (a != shot) { a.Kill(); return; }
        Fail();
    }

    void EndShot(bool ok)
    {
        if (shot != null) shot.Kill();
        shot = null;
        HideRings();
        ShipInput.FocusBlocked = false;
        nextAt = Time.time + NextInterval();
    }

    void SetWarning(float k)
    {
        if (fd == null) return;
        foreach (var s in fd.Sections) if (s != null) s.SetWarning(s == launcher ? k : k * 0.45f);
    }

    static bool IsBuff(BuffType t) => BuffSystem.Instance != null && BuffSystem.Instance.IsActive(t);

    // Un toque nuevo en cualquier parte de la pantalla (o clic, o la tecla E en el editor)
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
                float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.9f) / 0.07f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    [ContextMenu("Lanzar un perseguidor ya")]
    void LaunchNow() { if (fd != null && fd.Active && shot == null) { warning = false; nextAt = Time.time + warningTime; } }
}
