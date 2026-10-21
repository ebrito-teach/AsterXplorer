using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// Pantalla entre niveles: sale al caer en el agujero negro y espera un toque.
///
/// Fondo negro con rayas de hiperespacio y, al fondo a la izquierda, el personaje que lleve el
/// jugador propulsándose (sea cual sea: se copia su dibujo animado en cada fotograma). Encima,
/// el resumen del nivel: tiempo del intento, tiempo total, muertes, variaciones del mundo y
/// cristales. Al tocar, el personaje toma impulso, se estira y sale disparado; entonces el
/// nivel siguiente arranca debajo y la pantalla se desvanece mientras empieza la llegada.
///
/// No hay que montar nada: se construye sola (lienzo, fondo, rayas y textos) en un objeto
/// vacío con este componente. Va por encima de todo, mandos y botón de pausa incluidos.
/// Textos: si se asignan las fuentes pixel (AsterPixel Coral, Tormenta, Plata) se usan esas; si no,
/// TextMeshPro con su fuente por defecto o la que se ponga en `Font`.
public class TransitionScreen : MonoBehaviour, IPointerClickHandler
{
    public struct Stats
    {
        public int level;
        public float attemptSeconds;
        public float totalSeconds;
        public int deaths;
        public int variations;
        public int crystals;
    }

    [Header("Aspecto")]
    [Tooltip("Vacío = la fuente por defecto de TextMeshPro")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Color titleColor = new Color(1f, 0.78f, 0.45f);
    [SerializeField] private Color labelColor = new Color(0.75f, 0.8f, 0.9f);
    [SerializeField] private Color valueColor = Color.white;
    [Header("Fuente pixel (si están las tres, se usan en vez de TextMeshPro)")]
    [SerializeField] private PixelFont titlePixel;
    [SerializeField] private PixelFont labelPixel;
    [SerializeField] private PixelFont valuePixel;
    [Tooltip("Altura de la mayúscula: título, líneas y aviso")]
    [SerializeField] private Vector3 pixelCapHeights = new Vector3(56f, 30f, 24f);

    [Header("Disposición")]
    [Tooltip("Hueco entre la columna de descripciones y la de valores")]
    [SerializeField] private float columnGap = 70f;
    [Tooltip("Separación entre líneas del resumen (1 = pegadas)")]
    [SerializeField] private float lineSpacing = 1.45f;

    [Tooltip("Orden del lienzo. Tiene que quedar por encima del de los mandos")]
    [SerializeField] private int canvasOrder = 500;

    [Header("Ritmo")]
    [SerializeField] private float fadeIn = 0.4f;
    [SerializeField] private float fadeOut = 0.35f;
    [Tooltip("Segundos sin aceptar toques al aparecer, para no saltarla sin querer")]
    [SerializeField] private float ignoreTapsFor = 0.6f;

    public enum StreakMode { Horizontal, Radial }

    [Header("Rayas")]
    [Tooltip("Horizontal: el espacio pasa en contra del viaje del personaje. Radial: túnel desde el centro")]
    [SerializeField] private StreakMode streakMode = StreakMode.Horizontal;
    [Tooltip("Radial: velocidad con que salen del centro")]
    [SerializeField] private float streakSpeed = 0.9f;
    [Tooltip("Horizontal: unidades por segundo")]
    [SerializeField] private float streakSpeedH = 1500f;

    [Header("Personaje")]
    [SerializeField] private bool showCharacter = true;
    [Tooltip("Posición respecto al centro de la pantalla (unidades del lienzo de 1920x1080)")]
    [SerializeField] private Vector2 characterPos = new Vector2(-560f, -40f);
    [SerializeField] private float characterHeight = 300f;
    [Tooltip("Inclinación, en grados (positivo = la cabeza hacia la izquierda)")]
    [SerializeField] private float characterTilt = 0f;
    [Tooltip("Lo que sube y baja flotando, y cuántas veces por segundo")]
    [SerializeField] private float bobAmount = 14f;
    [SerializeField] private float bobSpeed = 0.7f;
    [Tooltip("Brillo del personaje. Por debajo de 1 queda más al fondo")]
    [Range(0.3f, 1f)] [SerializeField] private float characterBrightness = 0.9f;
    [SerializeField] private Color trailColor = new Color(0.75f, 0.92f, 1f, 0.8f);
    [Header("Salida al tocar")]
    [Tooltip("Segundos que dura: toma impulso, se estira y sale por la derecha")]
    [SerializeField] private float exitTime = 0.6f;
    [Tooltip("Cuánto se estira a lo largo al salir (y se adelgaza en proporción)")]
    [SerializeField] private float exitStretch = 2.2f;

    /// Se pone a true con el toque. GameDirector espera a esto para seguir
    public bool Continued { get; private set; }

    const int MaxStreaks = 70;

    Canvas canvas;
    CanvasGroup group;
    RectTransform root, streakLayer;
    Txt title, labels, values, hint;

    // Un texto que puede ser TextMeshPro o PixelText
    class Txt
    {
        public TextMeshProUGUI tmp;
        public PixelText px;
        public string Text { set { if (px != null) px.Text = value; else tmp.text = value; } }
        public RectTransform Rect => px != null ? px.rectTransform : tmp.rectTransform;
        public void SetColor(Color c) { if (px != null) px.color = c; else tmp.color = c; }
        public float Width => px != null ? px.PreferredWidth : tmp.GetPreferredValues(tmp.text).x;
    }

    bool UsePixel => titlePixel != null && labelPixel != null && valuePixel != null;
    RectTransform charRt;
    Image charImg;
    GameObject charClone;
    SpriteRenderer charSource;
    const int TrailCount = 36;
    RectTransform[] trail;
    Image[] trailImg;
    Vector2[] trailPos, trailVel;
    float[] trailAge, trailLife;
    float trailTimer, boost = 1f;
    bool exiting;

    RectTransform[] streaks;
    Image[] streakImg;
    float[] sAngle, sDist, sSpeed, sAlpha, sThick;
    float shownAt;
    bool built, accepting;
    Coroutine fade;

    void Awake()
    {
        Build();
        group.alpha = 0f;
        canvas.enabled = false;
        Continued = true;
    }

    public void Show(Stats s)
    {
        if (!built) Build();
        Continued = false;
        accepting = false;
        shownAt = Time.unscaledTime;

        title.Text = $"NIVEL {s.level} SUPERADO";
        labels.Text = "Tiempo del intento\nTiempo total del nivel\nMuertes\nVariaciones del mundo\nCristales obtenidos";
        values.Text = $"{Clock(s.attemptSeconds)}\n{Clock(s.totalSeconds)}\n{s.deaths}\n{s.variations}\n+{s.crystals}";
        hint.Text = "Toca para continuar";
        CenterColumns();

        for (int i = 0; i < MaxStreaks; i++) Respawn(i, true);
        exiting = false;
        boost = 1f;
        SetupCharacter();
        canvas.enabled = true;
        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(Fade(group.alpha, 1f, fadeIn, false));
    }

    // El bloque descripciones + hueco + valores queda centrado en la pantalla: las
    // descripciones son más largas que los valores, y sin esto todo se iba a la izquierda
    void CenterColumns()
    {
        float lw = labels.Width, vw = values.Width;
        float labelRight = -(lw + columnGap + vw) * 0.5f + lw;
        var lr = labels.Rect; var vr = values.Rect;
        lr.anchoredPosition = new Vector2(labelRight - lr.sizeDelta.x * 0.5f, lr.anchoredPosition.y);
        vr.anchoredPosition = new Vector2(labelRight + columnGap + vr.sizeDelta.x * 0.5f, vr.anchoredPosition.y);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (!accepting || Continued || exiting) return;
        accepting = false;
        StartCoroutine(Exit());
    }

    // El personaje toma impulso, se estira y sale disparado; al salir, sigue el nivel
    IEnumerator Exit()
    {
        exiting = true;
        if (charRt != null && charImg.enabled)
        {
            Vector2 from = charRt.anchoredPosition;
            float right = root.rect.width * 0.5f + characterHeight * exitStretch + 200f;
            for (float t = 0f; t < exitTime; t += Time.unscaledDeltaTime)
            {
                float e = t / exitTime;
                // Primero se encoge un poco hacia atrás (la anticipación) y luego acelera
                float back = e < 0.25f ? Mathf.Sin(e / 0.25f * Mathf.PI * 0.5f) : 1f - (e - 0.25f) / 0.75f;
                float go = e < 0.25f ? 0f : Mathf.Pow((e - 0.25f) / 0.75f, 3f);
                float x = from.x - 40f * Mathf.Clamp01(back) + (right - from.x) * go;
                charRt.anchoredPosition = new Vector2(x, from.y);
                float st = e < 0.25f ? 1f - 0.15f * back : Mathf.Lerp(1f, exitStretch, Mathf.Sqrt(go));
                float sq = e < 0.25f ? 1f + 0.1f * back : 1f / Mathf.Sqrt(st);
                charRt.localScale = new Vector3(st, sq, 1f);
                boost = 1f + 4f * go;
                yield return null;
            }
            charImg.enabled = false;
        }
        Continued = true;
        exiting = false;
        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(Fade(group.alpha, 0f, fadeOut, true));
    }

    // Una copia del dibujo del jugador, escondida, con su Animator en la pose de empuje. Cada
    // fotograma se copia su sprite a la imagen de la pantalla: vale para cualquier personaje
    void SetupCharacter()
    {
        if (charClone != null) Destroy(charClone);
        charClone = null;
        charSource = null;
        if (charImg == null) return;
        charImg.enabled = false;
        if (!showCharacter) return;

        var pc = FindFirstObjectByType<PlayerController>();
        if (pc == null) return;
        var anim = pc.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            charClone = Instantiate(anim.gameObject);
            charClone.name = "TransitionCharacter";
            charClone.transform.SetParent(null, false);
            charClone.transform.position = new Vector3(0f, -100000f, 0f);
            var a = charClone.GetComponent<Animator>();
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var prm in a.parameters)
                if (prm.name == "Thrusting" && prm.type == AnimatorControllerParameterType.Bool) a.SetBool("Thrusting", true);
            charSource = charClone.GetComponentInChildren<SpriteRenderer>(true);
            if (charSource != null) { charSource.enabled = false; charSource.flipX = false; }
        }
        else
        {
            // Sin Animator: el dibujo quieto del jugador
            var sr = pc.Visual != null ? pc.Visual.GetComponentInChildren<SpriteRenderer>(true) : pc.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null) { charImg.sprite = sr.sprite; charImg.enabled = sr.sprite != null; }
        }

        charRt.anchoredPosition = characterPos;
        charRt.localScale = Vector3.one;
        charImg.color = new Color(characterBrightness, characterBrightness, characterBrightness, 1f);
        for (int i = 0; i < TrailCount; i++) { trailAge[i] = 1f; trailLife[i] = 1f; trailImg[i].enabled = false; }
    }

    void UpdateCharacter(float dt)
    {
        if (charImg == null) return;
        if (charSource != null && charSource.sprite != null)
        {
            if (charImg.sprite != charSource.sprite) charImg.sprite = charSource.sprite;
            // Visible mientras se muestra la pantalla; la salida lo apaga al irse
            if (!Continued) charImg.enabled = true;
        }
        if (!charImg.enabled || charImg.sprite == null) { UpdateTrail(dt, false); return; }

        var r = charImg.sprite.rect;
        charRt.sizeDelta = new Vector2(characterHeight * r.width / Mathf.Max(1f, r.height), characterHeight);

        if (!exiting)
        {
            // Flota: sube y baja y se mece un poco, como quien empuja sin parar
            float t = (Time.unscaledTime - shownAt) * bobSpeed * Mathf.PI * 2f;
            charRt.anchoredPosition = characterPos + new Vector2(Mathf.Sin(t * 0.5f) * bobAmount * 0.4f, Mathf.Sin(t) * bobAmount);
            charRt.localRotation = Quaternion.Euler(0f, 0f, characterTilt + Mathf.Sin(t * 0.8f) * 3f);
        }
        UpdateTrail(dt, true);
    }

    // Chispas del propulsor: salen por detrás del personaje y se quedan atrás
    void UpdateTrail(float dt, bool emit)
    {
        if (trail == null) return;
        if (emit)
        {
            trailTimer -= dt * boost;
            while (trailTimer <= 0f)
            {
                trailTimer += 0.035f;
                int k = -1;
                for (int i = 0; i < TrailCount; i++) if (trailAge[i] >= trailLife[i]) { k = i; break; }
                if (k < 0) break;
                Vector2 c = charRt.anchoredPosition;
                float w = charRt.sizeDelta.x * charRt.localScale.x;
                trailPos[k] = c + new Vector2(-w * 0.35f, Random.Range(-0.12f, 0.06f) * characterHeight);
                trailVel[k] = new Vector2(-Random.Range(260f, 520f) * boost, Random.Range(-30f, 30f));
                trailAge[k] = 0f;
                trailLife[k] = Random.Range(0.35f, 0.7f);
                trailImg[k].enabled = true;
            }
        }
        for (int i = 0; i < TrailCount; i++)
        {
            if (trailAge[i] >= trailLife[i]) { if (trailImg[i].enabled) trailImg[i].enabled = false; continue; }
            trailAge[i] += dt;
            trailPos[i] += trailVel[i] * dt;
            float f = 1f - trailAge[i] / trailLife[i];
            trail[i].anchoredPosition = trailPos[i];
            float size = Mathf.Lerp(4f, 18f, f);
            trail[i].sizeDelta = new Vector2(size * (1f + boost * 0.6f), size);
            trailImg[i].color = new Color(trailColor.r, trailColor.g, trailColor.b, trailColor.a * f);
        }
    }

    void OnDisable() { if (charClone != null) Destroy(charClone); }

    IEnumerator Fade(float from, float to, float time, bool hideAtEnd)
    {
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / time);
            yield return null;
        }
        group.alpha = to;
        if (hideAtEnd)
        {
            canvas.enabled = false;
            if (charClone != null) Destroy(charClone);
            charClone = null;
        }
        fade = null;
    }

    void Update()
    {
        if (!canvas.enabled) return;
        if (!Continued && !accepting && Time.unscaledTime - shownAt >= ignoreTapsFor) accepting = true;

        // Pulso suave del aviso
        float a = accepting ? 0.55f + 0.45f * Mathf.Sin((Time.unscaledTime - shownAt) * 3.5f) : 0f;
        Color hc = UsePixel ? Color.white : labelColor;
        hint.SetColor(new Color(hc.r, hc.g, hc.b, a));

        UpdateStreaks(Time.unscaledDeltaTime);
        UpdateCharacter(Time.unscaledDeltaTime);
    }

    // ------------------------------------------------------------------ rayas
    void UpdateStreaks(float dt)
    {
        Vector2 half = root.rect.size * 0.5f;
        float reach = half.magnitude;
        int shown = Mathf.Min(MaxStreaks, PerformanceManager.WarpLines);
        for (int i = 0; i < MaxStreaks; i++)
        {
            bool on = i < shown;
            if (streakImg[i].enabled != on) streakImg[i].enabled = on;
            if (!on) continue;

            var rt = streaks[i];
            if (streakMode == StreakMode.Horizontal)
            {
                // El espacio pasa de derecha a izquierda: sDist es la x, sAngle la y (de -1 a 1)
                float len = Mathf.Lerp(60f, 280f, (sSpeed[i] - 0.6f) / 0.8f) * (1f + (boost - 1f) * 0.6f);
                sDist[i] -= streakSpeedH * sSpeed[i] * boost * dt / Mathf.Max(1f, half.x);
                if (sDist[i] * half.x < -half.x - len) Respawn(i, false);
                rt.anchoredPosition = new Vector2(sDist[i] * half.x, sAngle[i] * half.y);
                rt.localRotation = Quaternion.Euler(0f, 0f, 180f);   // la cabeza brillante delante
                rt.sizeDelta = new Vector2(len, 3f * sThick[i]);
                streakImg[i].color = new Color(1f, 1f, 1f, sAlpha[i]);
                continue;
            }

            // Aceleran al alejarse del centro, como el campo de estrellas de un salto
            sDist[i] += streakSpeed * sSpeed[i] * boost * (0.15f + sDist[i]) * dt;
            if (sDist[i] > 1.15f) Respawn(i, false);

            float d = sDist[i];
            Vector2 dir = new Vector2(Mathf.Cos(sAngle[i]), Mathf.Sin(sAngle[i]));
            rt.anchoredPosition = dir * (d * reach);
            rt.localRotation = Quaternion.Euler(0f, 0f, sAngle[i] * Mathf.Rad2Deg);
            rt.sizeDelta = new Vector2(Mathf.Lerp(4f, 260f, d * d) * sSpeed[i], 3f * sThick[i]);
            float fadeInNear = Mathf.Clamp01(d / 0.25f);
            streakImg[i].color = new Color(1f, 1f, 1f, sAlpha[i] * fadeInNear);
        }
    }

    // anywhere = repartida por toda la pantalla (al aparecer); si no, entra por su borde
    void Respawn(int i, bool anywhere)
    {
        if (streakMode == StreakMode.Horizontal)
        {
            sAngle[i] = Random.Range(-1f, 1f);
            sDist[i] = anywhere ? Random.Range(-1.1f, 1.1f) : Random.Range(1.05f, 1.3f);
        }
        else
        {
            sAngle[i] = Random.Range(0f, Mathf.PI * 2f);
            sDist[i] = anywhere ? Random.Range(0.02f, 1f) : Random.Range(0.02f, 0.12f);
        }
        sSpeed[i] = Random.Range(0.6f, 1.4f);
        sAlpha[i] = Random.Range(0.35f, 1f);
        sThick[i] = Random.Range(0.6f, 1.4f);
    }

    static string Clock(float s)
    {
        s = Mathf.Max(0f, s);
        int m = Mathf.FloorToInt(s / 60f);
        float rest = s - m * 60f;
        return $"{m}:{rest:00.0}";
    }

    // ------------------------------------------------------------------ construcción
    void Build()
    {
        if (built) return;
        built = true;

        canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasOrder;
        var scaler = gameObject.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        if (gameObject.GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();

        root = (RectTransform)transform;

        // Fondo negro: recibe el toque y lo sube a este componente, que está en el padre
        var bg = NewRect("Fondo", root);
        Stretch(bg);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = Color.black;
        bgImg.raycastTarget = true;

        streakLayer = NewRect("Rayas", root);
        Stretch(streakLayer);
        var sprite = StreakSprite();
        streaks = new RectTransform[MaxStreaks];
        streakImg = new Image[MaxStreaks];
        sAngle = new float[MaxStreaks]; sDist = new float[MaxStreaks]; sSpeed = new float[MaxStreaks];
        sAlpha = new float[MaxStreaks]; sThick = new float[MaxStreaks];
        for (int i = 0; i < MaxStreaks; i++)
        {
            var rt = NewRect("Raya", streakLayer);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);           // la cabeza brillante va por fuera
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            streaks[i] = rt;
            streakImg[i] = img;
            Respawn(i, true);
        }

        // El personaje y las chispas de su propulsor, al fondo: detrás del velo y los textos
        var charLayer = NewRect("Personaje", root);
        Stretch(charLayer);
        var soft = SoftSprite();
        trail = new RectTransform[TrailCount];
        trailImg = new Image[TrailCount];
        trailPos = new Vector2[TrailCount]; trailVel = new Vector2[TrailCount];
        trailAge = new float[TrailCount]; trailLife = new float[TrailCount];
        for (int i = 0; i < TrailCount; i++)
        {
            var rt = NewRect("Chispa", charLayer);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = soft;
            img.raycastTarget = false;
            img.enabled = false;
            trail[i] = rt; trailImg[i] = img;
            trailAge[i] = trailLife[i] = 1f;
        }
        charRt = NewRect("Dibujo", charLayer);
        charRt.anchorMin = charRt.anchorMax = new Vector2(0.5f, 0.5f);
        charImg = charRt.gameObject.AddComponent<Image>();
        charImg.preserveAspect = true;
        charImg.raycastTarget = false;
        charImg.enabled = false;

        // Velo para que los textos se lean sobre las rayas
        var veil = NewRect("Velo", root);
        veil.anchorMin = veil.anchorMax = new Vector2(0.5f, 0.5f);
        veil.sizeDelta = new Vector2(1500f, 780f);
        var veilImg = veil.gameObject.AddComponent<Image>();
        veilImg.sprite = SoftSprite();
        veilImg.color = new Color(0f, 0f, 0f, 0.75f);
        veilImg.raycastTarget = false;

        // Dos columnas: descripciones alineadas a la derecha, valores a la izquierda, con
        // Column Gap entre ellas
        float half = columnGap * 0.5f;
        const float labelW = 820f, valueW = 420f, blockH = 400f;
        if (UsePixel)
        {
            title = NewPixel("Titulo", titlePixel, pixelCapHeights.x, PixelText.Align.Center, 1f);
            labels = NewPixel("Etiquetas", labelPixel, pixelCapHeights.y, PixelText.Align.Right, lineSpacing);
            values = NewPixel("Valores", valuePixel, pixelCapHeights.y, PixelText.Align.Left, lineSpacing);
            hint = NewPixel("Aviso", labelPixel, pixelCapHeights.z, PixelText.Align.Center, 1f);
        }
        else
        {
            title = NewText("Titulo", 84f, titleColor, TextAlignmentOptions.Center, 0f);
            // TextMeshPro mide el interlineado aparte: 0 = normal; se pasa de multiplicador a su escala
            float tmpSpacing = (lineSpacing - 1f) * 100f;
            labels = NewText("Etiquetas", 46f, labelColor, TextAlignmentOptions.TopRight, tmpSpacing);
            values = NewText("Valores", 46f, valueColor, TextAlignmentOptions.TopLeft, tmpSpacing);
            hint = NewText("Aviso", 40f, labelColor, TextAlignmentOptions.Center, 0f);
        }
        Place(title.Rect, new Vector2(0f, 230f), new Vector2(1600f, 120f));
        Place(labels.Rect, new Vector2(-half - labelW * 0.5f, -45f), new Vector2(labelW, blockH));
        Place(values.Rect, new Vector2(half + valueW * 0.5f, -45f), new Vector2(valueW, blockH));
        Place(hint.Rect, new Vector2(0f, -290f), new Vector2(1200f, 70f));
    }

    Txt NewPixel(string name, PixelFont f, float cap, PixelText.Align align, float spacing)
    {
        var rt = NewRect(name, root);
        var t = rt.gameObject.AddComponent<PixelText>();
        t.Font = f;
        t.CapHeight = cap;
        t.Alignment = align;
        t.LineSpacing = spacing;
        t.raycastTarget = false;
        return new Txt { px = t };
    }

    Txt NewText(string name, float size, Color color, TextAlignmentOptions align, float spacing)
    {
        var rt = NewRect(name, root);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.lineSpacing = spacing;
        return new Txt { tmp = t };
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;   // UI
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    // Raya blanca con las puntas difuminadas y la cabeza (derecha) más brillante
    static Sprite StreakSprite()
    {
        var tex = new Texture2D(32, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < 32; x++)
        {
            float u = x / 31f;
            float a = Mathf.Sin(u * Mathf.PI) * Mathf.Lerp(0.3f, 1f, u);
            for (int y = 0; y < 4; y++)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * ((y == 0 || y == 3) ? 0.35f : 1f)));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 32, 4), new Vector2(0.5f, 0.5f), 100f);
    }

    // Mancha redonda de bordes suaves
    static Sprite SoftSprite()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((1f - r) / 0.45f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }
}