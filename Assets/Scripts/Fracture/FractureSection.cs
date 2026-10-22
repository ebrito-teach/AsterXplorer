using UnityEngine;

/// Un eslabón de la rasgadura. La grieta es un solo cuerpo enorme hecho de eslabones unidos,
/// como un pirosoma: grande, mediano y pequeño solo dicen cuántas ondas necesita cada tramo.
/// La crea FractureDirector; no hace falta prefab.
///
/// VIDA PROPIA
///
/// Cada eslabón tiene su ciclo: dispara un rato y se enfría. Mientras se enfría es vulnerable:
/// su silueta se pone verde y late. Cada uno con sus tiempos, así que puede haber varios
/// vulnerables a la vez o ninguno. Sus disparos tienen calibres propios, cada calibre con su
/// propio enfriamiento (por ejemplo, pequeños a menudo y un grande de vez en cuando).
///
/// LO QUE TOCA
///
/// El jugador choca con él y no lo cruza. Una roca que entra en la rasgadura se deshace.
///
/// Al cerrarse, los eslabones vecinos se juntan: la rasgadura sigue siendo una, más corta.
public class FractureSection : MonoBehaviour
{
    public enum Size { Small = 0, Medium = 1, Large = 2 }

    [System.Serializable]
    public struct Caliber
    {
        public float radius;
        public Vector2 cooldown;
        public float next;
    }

    public Size Kind { get; private set; }
    public int WavesRequired { get; private set; }
    public int WavesTaken { get; private set; }
    public bool Closed => WavesTaken >= WavesRequired;
    /// En enfriamiento: no dispara y se le puede lanzar la onda
    public bool IsOpen { get; private set; }
    public float Length { get; private set; }
    public float Thickness { get; private set; }
    /// Su cara: hacia el lado del jugador
    public Vector2 Front { get; private set; }
    public Vector2 WeakPoint => marker != null ? (Vector2)marker.transform.position : (Vector2)transform.position;
    /// Posición a lo largo de la rasgadura (el centro de este eslabón)
    public float Along { get; set; }

    FractureDirector owner;
    SpriteRenderer dust, core, glow, marker, dustBack, dustFront;
    Vector3 backScale, frontScale;
    Color backTint, frontTint;
    Collider2D solid, eater;
    float seed, flash, closing = -1f, warn;
    Color dustTint, coreTint, glowTint, openColor;
    float markerIdleAlpha;
    Vector3 dustScale, coreScale, glowScale;
    Vector2 moveFrom, moveTo;
    float moveT = 1f;

    // Ciclo propio
    float phaseEnd;
    Caliber[] calibers;
    float fireRate = 1f;

    public static FractureSection Create(FractureDirector owner, Transform parent, Size kind, int waves, Vector2 pos,
                                         float angle, float length, float thickness, float weakOffset,
                                         FractureDirector.Look look, float markerIdleAlpha, Color openColor,
                                         string sortingLayer, int order, Vector2 front)
    {
        var go = new GameObject("Eslabon_" + kind);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        var s = go.AddComponent<FractureSection>();
        s.owner = owner;
        s.Kind = kind;
        s.WavesRequired = Mathf.Max(1, waves);
        s.Length = length;
        s.Thickness = thickness;
        s.seed = Random.Range(0f, 100f);
        s.openColor = openColor;
        s.markerIdleAlpha = markerIdleAlpha;
        s.Front = front;
        // Un elemento añadido desde el Inspector nace con los colores en negro transparente:
        // si un color no tiene alfa, se usa el de por defecto en vez de dejar la grieta invisible
        s.dustTint = FractureDirector.UsableTint(look.dustTint, new Color(1f, 1f, 1f, 0.85f));
        s.coreTint = FractureDirector.UsableTint(look.coreTint, Color.white);
        s.glowTint = FractureDirector.UsableTint(look.glowTint, new Color(0.75f, 0.45f, 1f, 0.6f));

        // Se solapan un poco con los vecinos: así la rasgadura se ve continua
        s.dust = s.Layer("Polvo", look.dust, length * 1.35f, thickness * 2.6f, sortingLayer, order, s.dustTint, out s.dustScale);
        s.glow = s.Layer("Brillo", look.core, length * 1.18f, thickness * 1.45f, sortingLayer, order + 1, s.glowTint, out s.glowScale);
        s.core = s.Layer("Desgarro", look.core, length * 1.12f, thickness, sortingLayer, order + 2, s.coreTint, out s.coreScale);

        // Profundidad (la cámara es en perspectiva): un polvo más lejano, detrás, que se mueve
        // más despacio, y unos jirones más cerca de la cámara que pasan por delante del
        // jugador, semitransparentes. Al moverse se ve como un volumen enorme, no un dibujo
        s.backTint = new Color(s.dustTint.r * 0.7f, s.dustTint.g * 0.7f, s.dustTint.b * 0.8f, s.dustTint.a * FractureDirector.BackAlpha);
        s.dustBack = s.Layer("PolvoFondo", look.dust, length * 1.7f, thickness * 3.4f, sortingLayer, order - 1, s.backTint, out s.backScale);
        s.dustBack.transform.localPosition = new Vector3(0f, 0f, FractureDirector.BackDepth);
        s.frontTint = new Color(s.dustTint.r, s.dustTint.g, s.dustTint.b, FractureDirector.FrontAlpha);
        s.dustFront = s.Layer("PolvoDelante", look.dust, length * 1.15f, thickness * 1.9f, "Player", 500 + order, s.frontTint, out s.frontScale);
        s.dustFront.transform.localPosition = new Vector3(Random.Range(-0.3f, 0.3f) * length, 0f, -FractureDirector.FrontDepth);
        s.dustFront.flipX = Random.value < 0.5f;
        s.dustFront.flipY = Random.value < 0.5f;

        var m = new GameObject("PuntoDebil");
        m.transform.SetParent(go.transform, false);
        m.transform.localPosition = new Vector3(weakOffset * length * 0.5f, 0f, 0f);
        s.marker = m.AddComponent<SpriteRenderer>();
        s.marker.sprite = FractureDirector.MarkerSprite;
        s.marker.sortingLayerName = sortingLayer;
        s.marker.sortingOrder = order + 3;
        float ms = Mathf.Clamp(thickness * 0.6f, 0.6f, 1.4f);
        m.transform.localScale = Vector3.one * (ms / Mathf.Max(0.01f, FractureDirector.MarkerSprite.bounds.size.y));
        s.marker.color = new Color(1f, 1f, 1f, markerIdleAlpha);

        // Cuerpo cinemático: se mueve (al juntarse) y late
        var body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        s.moveTo = s.moveFrom = pos;
        return s;
    }

    SpriteRenderer Layer(string name, Sprite sprite, float width, float height, string layer, int order, Color tint, out Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerName = layer;
        sr.sortingOrder = order;
        sr.color = tint;
        scale = Vector3.one;
        if (sprite != null)
            scale = new Vector3(width / Mathf.Max(0.01f, sprite.bounds.size.x), height / Mathf.Max(0.01f, sprite.bounds.size.y), 1f);
        go.transform.localScale = scale;
        return sr;
    }

    /// Sólido para el jugador (capa Walls) y "boca" que deshace las rocas (capa AsteroidMoving)
    public void MakeSolid(float solidFraction)
    {
        var a = new GameObject("Solido");
        a.transform.SetParent(transform, false);
        int walls = LayerMask.NameToLayer("Walls");
        if (walls >= 0) a.layer = walls;
        var cap = a.AddComponent<CapsuleCollider2D>();
        cap.direction = CapsuleDirection2D.Horizontal;
        cap.size = new Vector2(Length * 1.05f, Thickness * solidFraction);
        solid = cap;

        var b = new GameObject("Boca");
        b.transform.SetParent(transform, false);
        int moving = LayerMask.NameToLayer("AsteroidMoving");
        if (moving >= 0) b.layer = moving;
        var cap2 = b.AddComponent<CapsuleCollider2D>();
        cap2.direction = CapsuleDirection2D.Horizontal;
        cap2.size = new Vector2(Length * 1.05f, Thickness * 0.8f);
        cap2.isTrigger = true;
        eater = cap2;
    }

    /// Calibres de este eslabón y su ritmo
    public void SetupFiring(Caliber[] cal, float rateMul, float firstPhaseEnd)
    {
        calibers = cal;
        fireRate = Mathf.Max(0.05f, rateMul);
        for (int i = 0; i < calibers.Length; i++)
            calibers[i].next = Time.time + Random.Range(0.3f, 1f) * calibers[i].cooldown.y;
        IsOpen = false;
        phaseEnd = firstPhaseEnd;
    }

    /// Empieza su ciclo (desfasado respecto a los demás)
    public void SetupFiringStart(float firstPhaseEnd)
    {
        IsOpen = false;
        phaseEnd = firstPhaseEnd;
        if (calibers != null)
            for (int i = 0; i < calibers.Length; i++)
                calibers[i].next = Time.time + Random.Range(0.3f, 1f) * calibers[i].cooldown.y / fireRate;
    }

    /// Para probar: vulnerable ya, durante unos segundos
    public void ForceOpen(float seconds)
    {
        if (Closed) return;
        if (!IsOpen) BeginWindow();
        IsOpen = true;
        phaseEnd = Time.time + seconds;
    }

    // ---- empuje: en cada ventana vulnerable la grieta te empuja una vez, sí o sí
    int windowWaves, pushOnWave = 1;
    bool windowPushed;

    void BeginWindow()
    {
        windowWaves = 0;
        windowPushed = false;
        pushOnWave = Random.value < 0.5f ? 1 : 2;   // 50 %: a la primera onda o a la segunda
    }

    /// Tras recibir una onda: ¿toca empujar ahora? (la primera o la segunda de la ventana,
    /// según el sorteo; o la que lo cierra, si aún no empujó)
    public bool ConsumePush(bool closedNow)
    {
        if (windowPushed) return false;
        if (windowWaves >= pushOnWave || closedNow) { windowPushed = true; return true; }
        return false;
    }

    /// Lo mueve hasta otro sitio (al juntarse con sus vecinos)
    public void MoveTo(Vector2 p)
    {
        moveFrom = transform.position;
        moveTo = p;
        moveT = 0f;
    }

    public void SetWarning(float amount) => warn = Mathf.Clamp01(amount);

    /// Recibe una onda. Devuelve true si con ella se cierra
    public bool TakeWave()
    {
        if (Closed) return false;
        WavesTaken++;
        windowWaves++;
        flash = 1f;
        if (Closed) { closing = 0f; IsOpen = false; }
        return Closed;
    }

    // Ciclo propio: dispara, se enfría (vulnerable), vuelve a disparar
    public void TickCycle(Vector2 firingTime, Vector2 cooldownTime, bool frozen)
    {
        if (Closed || closing >= 0f) return;
        if (frozen)
        {
            phaseEnd += Time.deltaTime;
            if (calibers != null) for (int i = 0; i < calibers.Length; i++) calibers[i].next += Time.deltaTime;
            return;
        }
        if (Time.time >= phaseEnd)
        {
            // Sale del enfriamiento: si en esta ventana aún no empujó, empuja ahora
            if (IsOpen && !windowPushed && owner != null) { windowPushed = true; owner.OnWindowEnded(this); }
            IsOpen = !IsOpen;
            if (IsOpen) BeginWindow();
            phaseEnd = Time.time + (IsOpen ? Random.Range(cooldownTime.x, cooldownTime.y) : Random.Range(firingTime.x, firingTime.y));
            if (!IsOpen && calibers != null)
                for (int i = 0; i < calibers.Length; i++) calibers[i].next = Time.time + Random.Range(0.4f, 1.2f);
        }
        if (IsOpen || calibers == null || owner == null) return;

        for (int i = 0; i < calibers.Length; i++)
        {
            if (Time.time < calibers[i].next) continue;
            calibers[i].next = Time.time + Random.Range(calibers[i].cooldown.x, calibers[i].cooldown.y) / (fireRate * Mathf.Max(0.05f, FractureDirector.CalibFireRate));
            owner.FireFrom(this, calibers[i].radius);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (Closed || owner == null) return;
        var a = other.GetComponentInParent<Asteroid>();
        if (a != null && a.isActiveAndEnabled) owner.Disintegrate(a);
    }

    void Update()
    {
        if (moveT < 1f)
        {
            moveT = Mathf.Min(1f, moveT + Time.deltaTime / 1.1f);
            float e = moveT * moveT * (3f - 2f * moveT);
            transform.position = Vector2.Lerp(moveFrom, moveTo, e);
        }

        float t = Time.time + seed;
        // Latido doble (lub-dub): la grieta está viva
        float ph = Mathf.Repeat(t * 0.9f, 1f);
        float beat = Mathf.Exp(-Mathf.Pow((ph - 0.08f) / 0.05f, 2f)) + 0.6f * Mathf.Exp(-Mathf.Pow((ph - 0.26f) / 0.06f, 2f));

        // Cuanto más dañado, más estrecho
        float health = Closed ? 0f : 1f - (float)WavesTaken / WavesRequired;
        float width = Mathf.Lerp(0.6f, 1f, health);

        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime / 0.4f);
        float close = 0f;
        if (closing >= 0f)
        {
            closing += Time.deltaTime;
            close = Mathf.Clamp01(closing / 1.3f);
            if (solid != null) solid.enabled = false;
            if (eater != null) eater.enabled = false;
            if (close >= 1f) { gameObject.SetActive(false); return; }
        }

        float pulse = 1f + 0.03f * beat;
        float ky = pulse * width * (1f - close);
        float kx = pulse * (1f - 0.6f * close);
        if (core != null) core.transform.localScale = new Vector3(coreScale.x * kx, coreScale.y * ky, 1f);
        if (glow != null) glow.transform.localScale = new Vector3(glowScale.x * kx, glowScale.y * ky * (1f + 0.08f * beat), 1f);
        if (dust != null) dust.transform.localScale = new Vector3(dustScale.x * kx, dustScale.y * (0.85f + 0.15f * width), 1f);

        Color warnCol = FractureDirector.WarningColor;
        Color cc = Color.Lerp(coreTint, warnCol, warn * 0.6f);
        Color gc = Color.Lerp(glowTint, warnCol, warn);
        cc = Color.Lerp(cc, Color.white, flash * 0.7f);
        if (core != null) core.color = new Color(cc.r, cc.g, cc.b, coreTint.a * (1f - close));
        if (glow != null)
        {
            float ga = glowTint.a * (0.45f + 0.55f * beat + flash) * (1f - close) * (1f + warn * 0.8f);
            glow.color = new Color(gc.r, gc.g, gc.b, Mathf.Clamp01(ga));
        }
        if (dust != null)
        {
            dust.color = new Color(dustTint.r, dustTint.g, dustTint.b, dustTint.a * (1f - close * close));
            dust.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.23f) * 2f);
        }
        if (dustBack != null)
        {
            dustBack.color = new Color(backTint.r, backTint.g, backTint.b, backTint.a * (1f - close));
            dustBack.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.13f + 1f) * 3f);
            dustBack.transform.localScale = new Vector3(backScale.x * kx, backScale.y * (0.9f + 0.1f * beat), 1f);
        }
        if (dustFront != null)
        {
            dustFront.color = new Color(frontTint.r, frontTint.g, frontTint.b, frontTint.a * (1f - close) * (0.8f + 0.2f * beat));
            dustFront.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.31f + 2f) * 5f);
        }

        if (marker != null)
        {
            if (Closed) marker.enabled = false;
            else if (IsOpen)
            {
                float k = 0.55f + 0.45f * Mathf.Sin(Time.time * 7f);
                marker.color = new Color(openColor.r, openColor.g, openColor.b, Mathf.Clamp01(openColor.a * k + flash));
                // El brillo del eslabón también se tiñe: se ve desde lejos cuál está abierto
                if (glow != null) glow.color = Color.Lerp(glow.color, new Color(openColor.r, openColor.g, openColor.b, glow.color.a), 0.5f);
            }
            else marker.color = new Color(1f, 1f, 1f, markerIdleAlpha * (0.6f + 0.4f * beat));
        }
    }
}
