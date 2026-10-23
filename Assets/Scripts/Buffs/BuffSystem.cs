using System;
using System.Collections.Generic;
using UnityEngine;

public enum BuffType { LightShield = 0, XLR8D = 1, Slow = 2, Ghost = 3, TimeFreeze = 4 }

/// Los buffs: qué hay, qué lleva el jugador guardado y qué hacen al usarse.
///
/// INVENTARIO
///
/// Dos contenedores. Un buff recogido entra en el primero libre; con los dos llenos sale el más
/// antiguo, el otro pasa a su sitio y el nuevo entra detrás (FIFO). Al pulsar un contenedor el
/// buff se activa y sale de la caja: desde ese momento se sigue solo con el aura del jugador,
/// y su efecto termina solo, nunca lo corta recoger otro.
///
/// Al morir se pierde lo que se estaba usando y se conserva lo guardado. Lo guardado se
/// conserva al continuar la misma partida, no en una nueva ni con otro personaje.
///
/// Se pueden usar dos a la vez, salvo si uno es TimeFreeze. El mismo buff no se puede
/// activar dos veces a la vez: el segundo espera a que acabe el primero.
///
/// EFECTOS
///
///   LightShield  aguanta hasta Shield Hits golpes de cualquier tipo, como mucho su duración
///   XLR8D        velocidad × Boost Speed, y maniobrar cuesta más
///   Slow         todo lo demás va a Slow Factor de su velocidad; el jugador no
///   Ghost        atraviesa los asteroides
///   TimeFreeze   se congela todo: las rocas se quedan quietas (todas, no solo las que se
///                ven) y no entra ninguna nueva mientras dure
///
/// Va en un objeto vacío de la escena. Los tiempos son de tiempo real (no les afecta Slow).
[DefaultExecutionOrder(-20)]
public class BuffSystem : MonoBehaviour
{
    public static BuffSystem Instance { get; private set; }

    [Serializable]
    public class Entry
    {
        public BuffType type;
        public Sprite icon;
        [Tooltip("Aura que rodea al jugador mientras está activo")]
        public Sprite aura;
        public Color tint = Color.white;
        [Tooltip("Segundos que dura")]
        public float duration = 4f;
        [Tooltip("Peso al sortear cuál aparece. Más alto = más común")]
        public float weight = 15f;
    }

    [SerializeField] private Entry[] catalog =
    {
        new Entry { type = BuffType.LightShield, duration = 7f, weight = 30f, tint = new Color(0.55f, 0.85f, 1f) },
        new Entry { type = BuffType.XLR8D,       duration = 4f, weight = 30f, tint = new Color(1f, 0.75f, 0.35f) },
        new Entry { type = BuffType.Slow,        duration = 4f, weight = 15f, tint = new Color(0.7f, 0.6f, 1f) },
        new Entry { type = BuffType.Ghost,       duration = 4f, weight = 15f, tint = new Color(0.85f, 0.95f, 1f) },
        new Entry { type = BuffType.TimeFreeze,  duration = 3f, weight = 10f, tint = new Color(0.6f, 0.95f, 1f) },
    };

    [Header("Efectos")]
    [Tooltip("LightShield: golpes que aguanta")]
    [SerializeField] private int shieldHits = 3;
    [Tooltip("XLR8D: multiplicador de velocidad")]
    [SerializeField] private float boostSpeed = 2f;
    [Tooltip("XLR8D: lo que cuesta maniobrar (0 = nada, 1 = mucho): arrancar, frenar y deriva")]
    [Range(0f, 1f)] [SerializeField] private float boostClumsiness = 1f;
    [Tooltip("XLR8D: grados por segundo que puede girar el rumbo. Más bajo = más difícil. " +
             "Sin buff el giro es casi instantáneo; 90 tarda 2 s en dar media vuelta")]
    [SerializeField] private float boostTurnRate = 90f;
    [Tooltip("Slow: velocidad del resto del mundo")]
    [Range(0.2f, 0.9f)] [SerializeField] private float slowFactor = 0.5f;
    [Tooltip("Ghost: transparencia del jugador")]
    [Range(0.15f, 1f)] [SerializeField] private float ghostAlpha = 0.45f;
    [Tooltip("Ghost: invulnerable estos segundos al acabar, por si termina dentro de una roca")]
    [SerializeField] private float ghostExitGrace = 0.7f;
    [Tooltip("TimeFreeze: color que toman las rocas detenidas")]
    [SerializeField] private Color freezeTint = new Color(0.65f, 0.85f, 1f);

    [Header("Aura sobre el personaje")]
    [Tooltip("Tamaño del aura respecto al dibujo del personaje (1 = justo su contorno). Se " +
             "mide del sprite que lleve en cada momento, así vale para cualquier personaje")]
    [SerializeField] private float auraScale = 1.3f;
    [Tooltip("Opacidad del aura recién activado. Se va haciendo invisible a medida que se agota")]
    [Range(0f, 1f)] [SerializeField] private float auraAlpha = 0.9f;

    public const int Slots = 2;

    public class Slot
    {
        public BuffType type;
        public bool active;
        public float start, end;
        public int hitsLeft;
        public float Remaining01 => active ? Mathf.Clamp01((end - Time.unscaledTime) / Mathf.Max(0.01f, end - start)) : 1f;
    }

    readonly List<Slot> slots = new List<Slot>();     // guardados, del más antiguo al más nuevo
    readonly List<Slot> actives = new List<Slot>();   // en uso: ya no están en la caja
    public IReadOnlyList<Slot> Contents => slots;
    /// Cambió el contenido (para la barra)
    public event Action Changed;
    /// Se recogió un buff
    public event Action<BuffType> Collected;

    PlayerController player;
    GameDirector director;
    readonly Dictionary<BuffType, SpriteRenderer> auras = new Dictionary<BuffType, SpriteRenderer>();
    float baseFixedDelta;
    bool slowApplied, ghostApplied;
    float shieldWindowUntil;
    System.Func<bool> absorber;
    static readonly string[] GhostLayers = { "AsteroidStatic", "AsteroidMoving", "Fragment" };

    void Awake()
    {
        Instance = this;
        baseFixedDelta = Time.fixedDeltaTime;
    }

    void OnEnable()
    {
        player = FindFirstObjectByType<PlayerController>();
        director = FindFirstObjectByType<GameDirector>();
        if (absorber == null) absorber = AbsorbHit;
        if (player != null) { player.OnDied += OnPlayerDied; player.HitAbsorber = absorber; }
        if (director != null) { director.LevelCleared += OnLevelCleared; director.LevelStarted += OnLevelStarted; }
    }

    void OnDisable()
    {
        if (player != null) { player.OnDied -= OnPlayerDied; if (player.HitAbsorber == absorber) player.HitAbsorber = null; }
        if (director != null) { director.LevelCleared -= OnLevelCleared; director.LevelStarted -= OnLevelStarted; }
        ClearEffects();
    }

    // Se carga al empezar el primer nivel: para entonces GameDirector ya ha decidido si se
    // continúa la partida guardada o empieza una nueva (que vacía los buffs)
    bool loaded;
    void OnLevelStarted(int _) { if (!loaded) { loaded = true; Load(); } }

    public Entry Info(BuffType t)
    {
        foreach (var e in catalog) if (e.type == t) return e;
        return null;
    }

    /// ¿Está en uso ahora mismo?
    public bool IsActive(BuffType t)
    {
        foreach (var s in actives) if (s.type == t) return true;
        return false;
    }

    /// Gasta el LightShield entero de golpe (fallo del QTE de la grieta)
    public void ConsumeShield()
    {
        foreach (var s in actives) if (s.type == BuffType.LightShield) s.hitsLeft = 0;
        Changed?.Invoke();
    }

    /// Sorteo por rareza
    public BuffType PickRandom() => PickRandom(null);

    /// Sorteo por rareza solo entre los permitidos (los que ya se enseñaron)
    public BuffType PickRandom(System.Predicate<BuffType> allowed)
    {
        float total = 0f;
        foreach (var e in catalog) if (allowed == null || allowed(e.type)) total += Mathf.Max(0f, e.weight);
        if (total <= 0f) return catalog[0].type;
        float r = UnityEngine.Random.value * total;
        foreach (var e in catalog)
        {
            if (allowed != null && !allowed(e.type)) continue;
            r -= Mathf.Max(0f, e.weight);
            if (r <= 0f) return e.type;
        }
        return catalog[0].type;
    }

    // ------------------------------------------------------------------ inventario
    /// Recoger. Siempre cabe: con los dos llenos sale el más antiguo (FIFO)
    public bool TryAdd(BuffType t)
    {
        while (slots.Count >= Slots) slots.RemoveAt(0);   // FIFO
        slots.Add(new Slot { type = t });
        Collected?.Invoke(t);
        Save();
        Changed?.Invoke();
        return true;
    }

    public bool CanActivate(int index)
    {
        if (index < 0 || index >= slots.Count || slots[index].active) return false;
        if (player == null || player.IsDead || player.IsExpelling || player.IsAbsorbed) return false;
        if (director != null && !director.Running) return false;
        var t = slots[index].type;
        foreach (var s in actives)
        {
            if (s.type == BuffType.TimeFreeze || t == BuffType.TimeFreeze) return false;
            if (s.type == t) return false;
        }
        return true;
    }

    public bool Activate(int index)
    {
        if (!CanActivate(index)) return false;
        var s = slots[index];
        var info = Info(s.type);
        s.active = true;
        s.start = Time.unscaledTime;
        s.end = s.start + (info != null ? info.duration : 4f);
        s.hitsLeft = shieldHits;
        slots.RemoveAt(index);
        actives.Add(s);
        Begin(s);
        Save();
        Changed?.Invoke();
        return true;
    }

    void Update()
    {
        bool changed = false;
        for (int i = actives.Count - 1; i >= 0; i--)
        {
            var s = actives[i];
            if (Time.unscaledTime >= s.end || (s.type == BuffType.LightShield && s.hitsLeft <= 0))
            {
                End(s);
                actives.RemoveAt(i);
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
        UpdateAuras();
    }

    void OnPlayerDied() => DropActive();
    void OnLevelCleared(int _) => DropActive();

    // Lo que se estaba usando se pierde; lo guardado se queda
    void DropActive()
    {
        bool changed = actives.Count > 0;
        foreach (var s in actives) End(s);
        actives.Clear();
        ClearEffects();
        if (changed) { Save(); Changed?.Invoke(); }
    }

    // ------------------------------------------------------------------ efectos
    void Begin(Slot s)
    {
        switch (s.type)
        {
            case BuffType.XLR8D:
                if (player != null) { player.SpeedFactor = boostSpeed; player.Clumsiness = boostClumsiness; player.MaxTurnRate = boostTurnRate; }
                break;
            case BuffType.Slow:
                ApplySlow(true);
                break;
            case BuffType.Ghost:
                ApplyGhost(true);
                break;
            case BuffType.TimeFreeze:
                Asteroid.FreezeTint = freezeTint;
                Asteroid.WorldFrozen = true;
                break;
        }
        ShowAura(s.type, true);
    }

    void End(Slot s)
    {
        switch (s.type)
        {
            case BuffType.XLR8D:
                if (player != null) { player.SpeedFactor = 1f; player.Clumsiness = 0f; player.MaxTurnRate = 0f; }
                break;
            case BuffType.Slow:
                ApplySlow(false);
                break;
            case BuffType.Ghost:
                ApplyGhost(false);
                if (player != null && !player.IsDead) player.GrantInvulnerability(ghostExitGrace);
                break;
            case BuffType.TimeFreeze:
                Asteroid.WorldFrozen = false;
                break;
        }
        ShowAura(s.type, false);
    }

    void ClearEffects()
    {
        if (player != null) { player.SpeedFactor = 1f; player.Clumsiness = 0f; player.MaxTurnRate = 0f; }
        ApplySlow(false);
        ApplyGhost(false);
        Asteroid.WorldFrozen = false;
        foreach (var a in auras.Values) if (a != null) a.enabled = false;
    }

    void ApplySlow(bool on)
    {
        if (on == slowApplied) return;
        slowApplied = on;
        Time.timeScale = on ? slowFactor : 1f;
        Time.fixedDeltaTime = baseFixedDelta * (on ? slowFactor : 1f);
        if (player != null) player.RealTime = on;
    }

    void ApplyGhost(bool on)
    {
        if (on == ghostApplied) return;
        ghostApplied = on;
        int p = LayerMask.NameToLayer("Player");
        if (p >= 0)
            foreach (var name in GhostLayers)
            {
                int l = LayerMask.NameToLayer(name);
                if (l >= 0) Physics2D.IgnoreLayerCollision(p, l, on);
            }
        if (player != null) player.VisualAlpha = on ? ghostAlpha : 1f;
    }

    // LightShield: cualquier golpe con una roca gasta uno. Un mismo choque llega por dos
    // caminos (el del jugador y el de la roca que lo mata): la ventana corta evita contarlo dos veces
    bool AbsorbHit()
    {
        Slot shield = null;
        foreach (var s in actives) if (s.type == BuffType.LightShield && s.hitsLeft > 0) { shield = s; break; }
        if (shield == null) return false;
        if (Time.unscaledTime < shieldWindowUntil) return true;
        shieldWindowUntil = Time.unscaledTime + 0.3f;
        shield.hitsLeft--;
        if (player != null) player.GrantInvulnerability(0.3f);
        if (auras.TryGetValue(BuffType.LightShield, out var sr) && sr != null) sr.transform.localScale *= 1.25f;
        Changed?.Invoke();
        return true;
    }

    // ------------------------------------------------------------------ auras
    void ShowAura(BuffType t, bool on)
    {
        if (player == null) return;
        var info = Info(t);
        if (!auras.TryGetValue(t, out var sr) || sr == null)
        {
            if (!on || info == null || info.aura == null) return;
            var go = new GameObject("Aura_" + t);
            go.transform.SetParent(player.transform, false);
            sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = info.aura;
            sr.sortingLayerName = "Player";
            sr.sortingOrder = 5;
            auras[t] = sr;
        }
        sr.enabled = on;
    }

    // El aura se ajusta al dibujo que lleve el personaje en ese momento (sea cual sea): se
    // centra en él y mide Aura Scale veces su lado mayor. Se va apagando a medida que se agota
    void UpdateAuras()
    {
        if (player == null) return;
        SpriteRenderer body = null;
        if (player.Visual != null) body = player.Visual.GetComponentInChildren<SpriteRenderer>();
        if (body == null) body = player.GetComponentInChildren<SpriteRenderer>();
        Bounds b = body != null && body.sprite != null ? body.bounds : new Bounds(player.transform.position, Vector3.one * 1.4f);
        // Sin la rotación: el lado mayor del sprite en el mundo, no su caja girada
        Vector3 ls = body != null ? body.transform.lossyScale : Vector3.one;
        Vector2 spr = body != null && body.sprite != null
            ? new Vector2(body.sprite.bounds.size.x * Mathf.Abs(ls.x), body.sprite.bounds.size.y * Mathf.Abs(ls.y))
            : Vector2.one * 1.4f;
        float target = Mathf.Max(spr.x, spr.y) * auraScale;

        foreach (var kv in auras)
        {
            var sr = kv.Value;
            if (sr == null || !sr.enabled || sr.sprite == null) continue;
            float beat = 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 6f);
            float k = target / Mathf.Max(0.01f, sr.sprite.bounds.size.y);
            Vector3 parent = sr.transform.parent != null ? sr.transform.parent.lossyScale : Vector3.one;
            Vector3 want = new Vector3(k / Mathf.Max(0.001f, Mathf.Abs(parent.x)), k / Mathf.Max(0.001f, Mathf.Abs(parent.y)), 1f) * beat;
            sr.transform.localScale = Vector3.Lerp(sr.transform.localScale, want, 0.25f);
            sr.transform.position = new Vector3(b.center.x, b.center.y, sr.transform.position.z);
            sr.transform.rotation = Quaternion.identity;

            float left = 1f;
            foreach (var s in actives) if (s.type == kv.Key) left = s.Remaining01;
            if (kv.Key == BuffType.LightShield)
                foreach (var s in actives)
                    if (s.type == BuffType.LightShield) left = Mathf.Min(left, (float)s.hitsLeft / Mathf.Max(1, shieldHits) + 0.15f);
            sr.color = new Color(1f, 1f, 1f, auraAlpha * Mathf.Pow(Mathf.Clamp01(left), 0.8f));
        }
    }

    // ------------------------------------------------------------------ guardado
    string Character => GameProgress.Instance != null && GameProgress.Instance.Data != null
        ? GameProgress.Instance.Data.collection.equipped : "";

    void Load()
    {
        var gp = GameProgress.Instance;
        if (gp == null || gp.Data == null || gp.Data.run == null || gp.Data.run.buffs == null) return;
        slots.Clear();
        if (gp.HasRun && gp.Data.run.buffsCharacter == Character)
            foreach (int b in gp.Data.run.buffs)
                if (slots.Count < Slots && Enum.IsDefined(typeof(BuffType), b)) slots.Add(new Slot { type = (BuffType)b });
        Changed?.Invoke();
    }

    void Save()
    {
        var gp = GameProgress.Instance;
        if (gp == null || gp.Data == null || gp.Data.run == null) return;
        if (gp.Data.run.buffs == null) gp.Data.run.buffs = new List<int>();
        gp.Data.run.buffs.Clear();
        foreach (var s in slots) if (!s.active) gp.Data.run.buffs.Add((int)s.type);
        gp.Data.run.buffsCharacter = Character;
        gp.Save();
    }

    // Para probar sin tener que encontrarlos: clic derecho en el componente
    [ContextMenu("Dar LightShield")] void GiveShield() => TryAdd(BuffType.LightShield);
    [ContextMenu("Dar XLR8D")]       void GiveBoost()  => TryAdd(BuffType.XLR8D);
    [ContextMenu("Dar Slow")]        void GiveSlow()   => TryAdd(BuffType.Slow);
    [ContextMenu("Dar Ghost")]       void GiveGhost()  => TryAdd(BuffType.Ghost);
    [ContextMenu("Dar TimeFreeze")]  void GiveFreeze() => TryAdd(BuffType.TimeFreeze);
}
