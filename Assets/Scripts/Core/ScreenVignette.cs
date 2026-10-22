using UnityEngine;
using UnityEngine.UI;

/// Oscurece suavemente los bordes de la pantalla. Cerca de una rasgadura se intensifica y late
/// con ella: se siente su presencia.
///
/// Se construye sola en un objeto vacío con este componente. Va por debajo de los mandos.
public class ScreenVignette : MonoBehaviour
{
    [Tooltip("Oscurecido de los bordes siempre (0 = nada)")]
    [Range(0f, 1f)] [SerializeField] private float baseAmount = 0.12f;
    [Tooltip("Lo que se añade cerca de una rasgadura")]
    [Range(0f, 1f)] [SerializeField] private float fractureAmount = 0.45f;
    [SerializeField] private Color color = new Color(0.03f, 0f, 0.06f, 1f);
    [SerializeField] private int canvasOrder = 40;

    Image img;

    void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasOrder;
        var go = new GameObject("Vineta", typeof(RectTransform));
        go.layer = 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        img = go.AddComponent<Image>();
        img.sprite = Make();
        img.raycastTarget = false;
    }

    void Update()
    {
        var fd = FractureDirector.Instance;
        float p = fd != null ? fd.Presence : 0f;
        float beat = fd != null ? fd.Heartbeat : 0f;
        float a = Mathf.Clamp01(baseAmount + fractureAmount * p * (0.75f + 0.25f * beat));
        img.color = new Color(color.r, color.g, color.b, a);
    }

    static Sprite Make()
    {
        const int w = 128, h = 72;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                float r = Mathf.Sqrt(u * u * 0.8f + v * v);
                float a = Mathf.Clamp01((r - 0.55f) / 0.6f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }
}
