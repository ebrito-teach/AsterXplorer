using UnityEngine;
using UnityEngine.UI;

/// Texto de interfaz con una PixelFont. Para títulos, cifras y avisos; los textos largos
/// (Bitácora) siguen con TextMeshPro y AsterPixel-Regular.ttf.
///
/// Admite varias líneas con \n. El color multiplica al del atlas: blanco = tal cual.
/// Los caracteres que la fuente no tiene se dejan como espacio y se avisa una vez.
[RequireComponent(typeof(CanvasRenderer))]
public class PixelText : MaskableGraphic
{
    public enum Align { Left, Center, Right }

    [SerializeField] private PixelFont font;
    [TextArea(2, 6)] [SerializeField] private string text = "AsterPixel";
    [Tooltip("Altura de una mayúscula, en unidades del Canvas")]
    [SerializeField] private float capHeight = 40f;
    [SerializeField] private Align alignment = Align.Left;
    [Tooltip("Separación extra entre letras, en unidades del Canvas")]
    [SerializeField] private float letterSpacing = 0f;
    [Tooltip("Distancia entre líneas, en alturas de línea (1 = pegadas)")]
    [SerializeField] private float lineSpacing = 1f;

    bool warned;

    public string Text
    {
        get => text;
        set { if (text == value) return; text = value; SetVerticesDirty(); }
    }

    public PixelFont Font
    {
        get => font;
        set { if (font == value) return; font = value; SetAllDirty(); }
    }

    public float CapHeight { get => capHeight; set { capHeight = value; SetVerticesDirty(); } }
    public Align Alignment { get => alignment; set { alignment = value; SetVerticesDirty(); } }
    public float LineSpacing { get => lineSpacing; set { lineSpacing = value; SetVerticesDirty(); } }
    public float LetterSpacing { get => letterSpacing; set { letterSpacing = value; SetVerticesDirty(); } }

    public override Texture mainTexture => font != null && font.Atlas != null ? font.Atlas : s_WhiteTexture;

    /// Alto que ocupa el texto con sus líneas, en unidades del Canvas
    public float TextHeight
    {
        get
        {
            if (font == null || !font.Ready) return 0f;
            float s = capHeight / font.CapHeight;
            int lines = string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;
            return lines <= 0 ? 0f : font.CellHeight * s * (1f + (lines - 1) * lineSpacing);
        }
    }

    /// Ancho de la línea más larga, en unidades del Canvas
    public float PreferredWidth
    {
        get
        {
            if (font == null || !font.Ready || string.IsNullOrEmpty(text)) return 0f;
            float s = capHeight / font.CapHeight, w = 0f;
            foreach (var line in text.Split('\n')) w = Mathf.Max(w, Width(line, s));
            return w;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (font == null || !font.Ready || string.IsNullOrEmpty(text)) return;

        float s = capHeight / font.CapHeight;
        float aw = font.AtlasWidth, ah = font.AtlasHeight;
        Rect r = GetPixelAdjustedRect();
        string[] lines = text.Split('\n');
        float lineH = font.CellHeight * s * lineSpacing;
        // La caja empieza en la parte alta de la mayúscula, no en la zona de las tildes
        float top0 = r.yMax + font.CapTop * s;
        Color32 c = color;

        for (int pass = 0; pass < 2; pass++)
        {
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                float width = Width(line, s);
                float x = alignment == Align.Left ? r.xMin
                        : alignment == Align.Right ? r.xMax - width
                        : r.center.x - width * 0.5f;
                float top = top0 - li * lineH;

                foreach (char ch in line)
                {
                    if (ch == ' ' || !font.TryGet(ch, out var g))
                    {
                        if (ch != ' ' && !warned) { warned = true; Debug.LogWarning($"PixelText: la fuente no tiene '{ch}'.", this); }
                        x += font.Space * s + letterSpacing;
                        continue;
                    }
                    var L = pass == 0 ? g.back : g.face;
                    float x0 = x + L.ox * s, y1 = top - L.oy * s;
                    float x1 = x0 + L.w * s, y0 = y1 - L.h * s;
                    var uv0 = new Vector2(L.x / aw, L.y / ah);
                    var uv1 = new Vector2((L.x + L.w) / aw, (L.y + L.h) / ah);
                    int i = vh.currentVertCount;
                    vh.AddVert(new Vector3(x0, y0), c, uv0);
                    vh.AddVert(new Vector3(x0, y1), c, new Vector2(uv0.x, uv1.y));
                    vh.AddVert(new Vector3(x1, y1), c, uv1);
                    vh.AddVert(new Vector3(x1, y0), c, new Vector2(uv1.x, uv0.y));
                    vh.AddTriangle(i, i + 1, i + 2);
                    vh.AddTriangle(i + 2, i + 3, i);
                    x += g.advance * s + letterSpacing;
                }
            }
        }
    }

    // Ancho de una línea sin el hueco que deja la última letra a su derecha
    float Width(string line, float s)
    {
        float w = 0f;
        bool any = false;
        foreach (char ch in line)
        {
            if (ch != ' ' && font.TryGet(ch, out var g)) { w += g.advance * s; any = true; }
            else w += font.Space * s;
            w += letterSpacing;
        }
        if (any) w -= font.Pixel * s + letterSpacing;
        return Mathf.Max(0f, w);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        warned = false;
        SetAllDirty();
    }
#endif
}