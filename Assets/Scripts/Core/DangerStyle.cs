using UnityEngine;

/// Lenguaje visual de peligro: todo lo que mata lleva un contorno rojo fino. Tenue de lejos y
/// más visible cuanto más cerca del jugador. Lo que no mata (estáticos quietos, planetas,
/// anillos, lunas inofensivas, trozos pequeños, lo inofensivo del mundo oscuro) va sin marca.
/// Cambia en vivo: un estático que sale despedido gana el contorno en ese momento.
///
/// Va en un objeto vacío de la escena con el material de silueta (shader AsterX/Silhouette).
/// Cada roca lleva su DangerOutline, que se añade sola.
public class DangerStyle : MonoBehaviour
{
    public static DangerStyle Instance { get; private set; }

    [Tooltip("Material con el shader AsterX/Silhouette")]
    [SerializeField] private Material silhouette;
    [SerializeField] private Color color = new Color(1f, 0.12f, 0.1f, 1f);
    [Tooltip("Grosor del contorno en unidades del mundo")]
    [SerializeField] private float thickness = 0.05f;
    [Tooltip("A esta distancia del jugador (o menos) el contorno se ve entero")]
    [SerializeField] private float nearDistance = 2.5f;
    [Tooltip("A esta distancia (o más) se ve con la opacidad Far Alpha")]
    [SerializeField] private float farDistance = 10f;
    [Range(0f, 1f)] [SerializeField] private float farAlpha = 0.25f;
    [Range(0f, 1f)] [SerializeField] private float nearAlpha = 1f;
    [Tooltip("El grosor también crece al acercarse (1 = no)")]
    [SerializeField] private float nearThicknessBoost = 1.5f;

    public Material Silhouette => silhouette;
    public Color Color => color;
    public float Thickness => thickness;

    Transform player;

    void Awake()
    {
        Instance = this;
        if (silhouette == null) Debug.LogWarning("DangerStyle: falta el material de silueta; no habrá contornos.", this);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// 0 = lejos, 1 = encima del jugador
    public float Closeness(Vector2 at, float radius)
    {
        if (player == null)
        {
            var p = FindFirstObjectByType<PlayerController>();
            if (p == null) return 0f;
            player = p.transform;
        }
        float d = Vector2.Distance(at, player.position) - radius;
        return 1f - Mathf.Clamp01(Mathf.InverseLerp(nearDistance, farDistance, d));
    }

    public float AlphaFor(float closeness) => Mathf.Lerp(farAlpha, nearAlpha, closeness);
    public float ThicknessFor(float closeness) => thickness * Mathf.Lerp(1f, nearThicknessBoost, closeness);
}
