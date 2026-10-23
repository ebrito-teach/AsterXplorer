using System.Collections.Generic;
using UnityEngine;

/// La chispa amarilla (FX_Chispa_1..8) en el punto del impacto:
///   Death  al morir (grande)
///   Shield cuando el LightShield aguanta un golpe (mediana, con tinte del escudo)
///   Bump   en choques que no matan (pequeña)
///
/// Va en un objeto vacío de la escena con los 8 fotogramas en Frames. Usa el tiempo del juego:
/// en el primer plano de la muerte, la chispa va a cámara lenta como todo lo demás.
public class ImpactSparks : MonoBehaviour
{
    public static ImpactSparks Instance { get; private set; }

    [Tooltip("Los 8 fotogramas de la chispa, en orden")]
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float frameTime = 0.045f;
    [Tooltip("Tamaño en unidades del mundo: muerte, escudo, choque")]
    [SerializeField] private Vector3 sizes = new Vector3(1.6f, 1.1f, 0.6f);
    [SerializeField] private Color deathColor = Color.white;
    [SerializeField] private Color shieldColor = new Color(0.75f, 0.95f, 1f, 1f);
    [SerializeField] private Color bumpColor = Color.white;
    [SerializeField] private string sortingLayer = "FX";
    [SerializeField] private int sortingOrder = 120;

    class Spark { public SpriteRenderer sr; public float age, size; }
    readonly List<Spark> sparks = new List<Spark>();

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public static void Death(Vector2 at) { if (Instance != null) Instance.Play(at, Instance.sizes.x, Instance.deathColor); }
    public static void Shield(Vector2 at) { if (Instance != null) Instance.Play(at, Instance.sizes.y, Instance.shieldColor); }
    public static void Bump(Vector2 at) { if (Instance != null) Instance.Play(at, Instance.sizes.z, Instance.bumpColor); }

    void Play(Vector2 at, float size, Color c)
    {
        if (frames == null || frames.Length == 0) return;
        Spark sp = null;
        foreach (var x in sparks) if (!x.sr.enabled) { sp = x; break; }
        if (sp == null)
        {
            var go = new GameObject("Chispa");
            go.transform.SetParent(transform, false);
            sp = new Spark { sr = go.AddComponent<SpriteRenderer>() };
            sp.sr.sortingLayerName = sortingLayer;
            sp.sr.sortingOrder = sortingOrder;
            sparks.Add(sp);
        }
        sp.age = 0f;
        sp.size = size;
        sp.sr.color = c;
        sp.sr.transform.position = at;
        sp.sr.transform.rotation = Quaternion.identity;
        sp.sr.enabled = true;
        Apply(sp);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        foreach (var sp in sparks)
        {
            if (!sp.sr.enabled) continue;
            sp.age += dt;
            Apply(sp);
        }
    }

    void Apply(Spark sp)
    {
        int f = Mathf.FloorToInt(sp.age / Mathf.Max(0.005f, frameTime));
        if (f >= frames.Length) { sp.sr.enabled = false; return; }
        var s = frames[f];
        sp.sr.sprite = s;
        if (s == null) return;
        // Todos los fotogramas comparten celda: la escala sale del primero no vacío
        float cell = Mathf.Max(0.01f, s.rect.width / s.pixelsPerUnit);
        sp.sr.transform.localScale = Vector3.one * (sp.size / cell);
    }
}
