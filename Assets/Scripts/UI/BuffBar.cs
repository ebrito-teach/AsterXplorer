using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// Los contenedores de los buffs, abajo en el centro.
///
/// No son botones fijos: un contenedor solo aparece cuando hay un buff guardado, y el
/// segundo cuando hay dos. Se pulsa para activarlo; mientras dura se va aclarando y vaciando,
/// y al acabar desaparece. Así nunca estorban sin motivo.
///
/// Se construye sola en un objeto vacío con este componente (lienzo propio, por encima de los
/// mandos y por debajo de la pantalla de transición).
public class BuffBar : MonoBehaviour, IPointerDownHandler
{
    [SerializeField] private float size = 130f;
    [SerializeField] private float spacing = 160f;
    [Tooltip("Distancia del borde de abajo al centro de los contenedores")]
    [SerializeField] private float bottom = 95f;
    [SerializeField] private int canvasOrder = 100;
    [Tooltip("Vacío = un aro generado por código")]
    [SerializeField] private Sprite frameSprite;
    [SerializeField] private Color frameColor = new Color(0.62f, 0.86f, 1f, 1f);
    [SerializeField] private Color fillColor = new Color(0.03f, 0.05f, 0.1f, 0.6f);

    class View
    {
        public RectTransform rt;
        public Image frame, fill, ring, icon;
        public BuffType type;
        public bool used, wasActive;
        public float shake, pop;
    }

    readonly View[] views = new View[BuffSystem.Slots];
    BuffSystem bs;
    RectTransform root;

    void Awake() => Build();

    void OnEnable()
    {
        bs = BuffSystem.Instance != null ? BuffSystem.Instance : FindFirstObjectByType<BuffSystem>();
        if (bs != null) bs.Changed += Refresh;
        Refresh();
    }

    void OnDisable() { if (bs != null) bs.Changed -= Refresh; }

    void Refresh()
    {
        if (bs == null) return;
        var c = bs.Contents;
        for (int i = 0; i < views.Length; i++)
        {
            var v = views[i];
            bool has = i < c.Count;
            if (has && (!v.used || v.type != c[i].type)) v.pop = 0f;   // entra uno nuevo: aparece
            v.used = has;
            v.rt.gameObject.SetActive(has);
            if (!has) continue;
            v.type = c[i].type;
            var info = bs.Info(v.type);
            v.icon.sprite = info != null ? info.icon : null;
            v.icon.enabled = v.icon.sprite != null;
            v.ring.color = info != null ? info.tint : Color.white;
        }
    }

    void Update()
    {
        if (bs == null) return;
        var c = bs.Contents;
        int n = Mathf.Min(c.Count, views.Length);
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < views.Length; i++)
        {
            var v = views[i];
            if (!v.used || i >= c.Count) continue;
            var s = c[i];

            // Sitio: centrados los que haya
            Vector2 want = new Vector2((i - (n - 1) * 0.5f) * spacing, bottom);
            v.rt.anchoredPosition = Vector2.Lerp(v.rt.anchoredPosition, want, 1f - Mathf.Exp(-dt * 12f));

            v.pop = Mathf.MoveTowards(v.pop, 1f, dt / 0.25f);
            float pop = 1f + 0.25f * Mathf.Sin(v.pop * Mathf.PI) * (1f - v.pop);
            float sh = v.shake > 0f ? Mathf.Sin(Time.unscaledTime * 60f) * 8f * v.shake : 0f;
            v.shake = Mathf.MoveTowards(v.shake, 0f, dt / 0.3f);
            v.rt.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, v.pop) * pop;
            v.icon.rectTransform.anchoredPosition = new Vector2(sh, 0f);

            // Activo: se aclara y el aro se vacía con el tiempo que queda
            float left = s.Remaining01;
            float a = s.active ? Mathf.Lerp(0.2f, 1f, left) : 1f;
            v.ring.fillAmount = s.active ? left : 1f;
            v.icon.color = s.active ? Color.Lerp(new Color(1f, 1f, 1f, 0.25f), Color.white, left) : Color.white;
            v.frame.color = new Color(frameColor.r, frameColor.g, frameColor.b, frameColor.a * a);
            v.fill.color = new Color(fillColor.r, fillColor.g, fillColor.b, fillColor.a * a);
            bool canUse = !s.active && bs.CanActivate(i);
            v.frame.color *= canUse || s.active ? 1f : 0.6f;
        }
    }

    public void OnPointerDown(PointerEventData e)
    {
        var hit = e.pointerPressRaycast.gameObject != null ? e.pointerPressRaycast.gameObject : e.pointerCurrentRaycast.gameObject;
        if (hit == null || bs == null) return;
        for (int i = 0; i < views.Length; i++)
        {
            if (!views[i].used || !hit.transform.IsChildOf(views[i].rt)) continue;
            if (!bs.Activate(i)) views[i].shake = 1f;
            return;
        }
    }

    // ------------------------------------------------------------------ construcción
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
        root = (RectTransform)transform;

        Sprite ring = frameSprite != null ? frameSprite : RingSprite(128, 0.80f, 0.94f);
        Sprite disc = RingSprite(128, 0f, 0.82f);
        Sprite progress = RingSprite(128, 0.86f, 0.97f);

        for (int i = 0; i < views.Length; i++)
        {
            var v = new View();
            v.rt = NewRect("Contenedor" + (i + 1), root);
            v.rt.anchorMin = v.rt.anchorMax = new Vector2(0.5f, 0f);
            v.rt.sizeDelta = new Vector2(size, size);
            v.rt.anchoredPosition = new Vector2(0f, bottom);

            v.fill = NewImage("Fondo", v.rt, disc, 1f, true);
            v.frame = NewImage("Marco", v.rt, ring, 1f, false);
            v.ring = NewImage("Tiempo", v.rt, progress, 1.06f, false);
            v.ring.type = Image.Type.Filled;
            v.ring.fillMethod = Image.FillMethod.Radial360;
            v.ring.fillOrigin = (int)Image.Origin360.Top;
            v.ring.fillClockwise = false;
            v.icon = NewImage("Icono", v.rt, null, 0.68f, false);
            v.icon.preserveAspect = true;

            v.rt.gameObject.SetActive(false);
            views[i] = v;
        }
    }

    Image NewImage(string name, RectTransform parent, Sprite sprite, float scale, bool raycast)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size * scale, size * scale);
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

    // Aro (o disco si inner = 0) blanco con bordes suaves, para teñir
    static Sprite RingSprite(int n, float inner, float outer)
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
}
