using System;
using System.Collections.Generic;
using UnityEngine;

/// Fuente pixel a color (AsterPixel): un atlas PNG ya pintado (grano, bisel, profundidad y
/// contorno) más un JSON con dónde está cada letra. La dibuja PixelText.
///
/// Cada letra tiene dos capas: la de atrás (profundidad y contorno) y la cara. PixelText pinta
/// primero todas las de atrás y luego todas las caras, así una letra nunca tapa la cara de
/// su vecina con su sombra.
///
/// Un asset por color: Oro, Hielo y Plata comparten el mismo JSON y cambian de atlas.
[CreateAssetMenu(menuName = "AsterXplorer/Pixel Font", fileName = "AsterPixel")]
public class PixelFont : ScriptableObject
{
    [SerializeField] private Texture2D atlas;
    [Tooltip("AsterPixel_metrics.json")]
    [SerializeField] private TextAsset metrics;

    [Serializable] public class Layer { public int x, y, w, h, ox, oy; }
    [Serializable] public class Glyph { public int code, advance; public Layer back, face; }
    [Serializable] class Info
    {
        public string name;
        public int pixel, cellHeight, capTop, capHeight, baseline, space, atlasWidth, atlasHeight;
        public Glyph[] glyphs;
    }

    Info info;
    Dictionary<int, Glyph> map;

    public Texture2D Atlas => atlas;
    public bool Ready { get { Load(); return info != null && atlas != null; } }
    public int CellHeight { get { Load(); return info != null ? info.cellHeight : 1; } }
    public int CapTop { get { Load(); return info != null ? info.capTop : 0; } }
    public int CapHeight { get { Load(); return info != null ? info.capHeight : 1; } }
    public int Space { get { Load(); return info != null ? info.space : 0; } }
    public int Pixel { get { Load(); return info != null ? info.pixel : 1; } }
    public int AtlasWidth { get { Load(); return info != null ? info.atlasWidth : 1; } }
    public int AtlasHeight { get { Load(); return info != null ? info.atlasHeight : 1; } }

    void OnValidate() { info = null; map = null; }
    void OnEnable() { info = null; map = null; }

    void Load()
    {
        if (map != null || metrics == null) return;
        info = JsonUtility.FromJson<Info>(metrics.text);
        map = new Dictionary<int, Glyph>();
        if (info != null && info.glyphs != null)
            foreach (var g in info.glyphs) map[g.code] = g;
    }

    public bool TryGet(char c, out Glyph g)
    {
        Load();
        g = null;
        return map != null && map.TryGetValue(c, out g);
    }
}
