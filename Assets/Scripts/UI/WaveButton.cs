using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// Botón de la onda expansiva. Solo se ve en los mapas con grieta.
///
/// Va abajo, junto a los contenedores de buffs y algo mayor. Muestra las cargas (hasta 2, con
/// la parte de carga que lleves de los roces) y se enciende cuando hay una sección abierta a
/// tu alcance y tienes carga. Si se pulsa sin poder, tiembla.
///
/// Se construye solo en un objeto vacío con este componente.
public class WaveButton : MonoBehaviour, IPointerDownHandler
{
    [SerializeField] private float size = 170f;
    [Tooltip("Posición respecto al centro de abajo de la pantalla")]
    [SerializeField] private Vector2 position = new Vector2(300f, 105f);
    [SerializeField] private int canvasOrder = 101;
    [SerializeField] private Sprite icon;
    [SerializeField] private Color readyColor = new Color(0.45f, 1f, 0.55f, 1f);
    [SerializeField] private Color idleColor = new Color(0.55f, 0.6f, 0.75f, 0.7f);

    RectTransform button;
    Image fill, frame, glyph;
    Image[] pips;
    float shake, pop;
    FractureDirector fd;

    void Awake() => Build();

    void Update()
    {
        if (fd == null) fd = FractureDirector.Instance;
        bool show = fd != null && fd.Active;
        if (button.gameObject.activeSelf != show) { button.gameObject.SetActive(show); pop = 0f; }
        if (!show) return;

        float dt = Time.unscaledDeltaTime;
        pop = Mathf.MoveTowards(pop, 1f, dt / 0.3f);
        bool ready = fd.CanWave;
        float beat = ready ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
        Color c = ready ? readyColor : idleColor;
        frame.color = Color.Lerp(c, Color.white, beat * 0.35f);
        glyph.color = new Color(c.r, c.g, c.b, ready ? 1f : 0.55f);
        fill.color = new Color(0.03f, 0.06f, 0.08f, ready ? 0.75f : 0.55f);

        float sh = shake > 0f ? Mathf.Sin(Time.unscaledTime * 60f) * 10f * shake : 0f;
        shake = Mathf.MoveTowards(shake, 0f, dt / 0.3f);
        button.anchoredPosition = position + new Vector2(sh, 0f);
        button.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, pop) * (1f + 0.05f * beat);

        for (int i = 0; i < pips.Length; i++)
        {
            float f = Mathf.Clamp01(fd.Charge - i);
            pips[i].fillAmount = f;
            pips[i].color = f >= 1f ? readyColor : new Color(readyColor.r, readyColor.g, readyColor.b, 0.6f);
        }
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (fd == null || !fd.Active) return;
        if (!fd.TryWave()) shake = 1f;
    }

    void Build()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasOrder;
        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

        button = NewRect("BotonOnda", transform);
        button.anchorMin = button.anchorMax = new Vector2(0.5f, 0f);
        button.sizeDelta = Vector2.one * size;
        button.anchoredPosition = position;

        fill = NewImage("Fondo", button, Disc(128, 0f, 0.86f), 1f, true);
        frame = NewImage("Marco", button, Disc(128, 0.8f, 0.95f), 1f, false);
        glyph = NewImage("Icono", button, icon != null ? icon : Burst(128), 0.62f, false);
        glyph.preserveAspect = true;

        // Las dos cargas: dos puntos debajo del botón que se van llenando
        pips = new Image[2];
        var pipSprite = Disc(64, 0f, 0.9f);
        for (int i = 0; i < pips.Length; i++)
        {
            var back = NewImage("CargaFondo" + i, button, pipSprite, 0.16f, false);
            back.color = new Color(0f, 0f, 0f, 0.55f);
            back.rectTransform.anchoredPosition = new Vector2((i - 0.5f) * size * 0.24f, -size * 0.62f);
            var p = NewImage("Carga" + i, back.rectTransform, pipSprite, 0.16f / 0.16f * 0.85f, false);
            p.rectTransform.sizeDelta = back.rectTransform.sizeDelta * 0.85f;
            p.type = Image.Type.Filled;
            p.fillMethod = Image.FillMethod.Vertical;
            p.fillOrigin = (int)Image.OriginVertical.Bottom;
            pips[i] = p;
        }
        button.gameObject.SetActive(false);
    }

    Image NewImage(string name, RectTransform parent, Sprite sprite, float scale, bool raycast)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.one * size * scale;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = raycast;
        return img;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Sprite Disc(int n, float inner, float outer)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float px = 2f / n;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((outer - r) / px) * (inner > 0f ? Mathf.Clamp01((r - inner) / px) : 1f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    // Icono provisional: una onda (aros concéntricos) con un destello
    static Sprite Burst(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 0f;
                a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - 0.85f) / 0.07f));
                a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - 0.58f) / 0.07f) * 0.85f);
                a = Mathf.Max(a, Mathf.Clamp01((0.28f - r) / 0.06f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }
}
