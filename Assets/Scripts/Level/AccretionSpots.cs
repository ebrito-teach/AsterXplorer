using UnityEngine;

/// Grumos de materia recorriendo el disco de acreción.
///
/// Esto es lo que de verdad se ve moverse en un agujero negro: el disco no gira como un todo,
/// son nudos de materia más densa recorriendo la órbita. Las llamaradas de Sgr A* son
/// exactamente eso, grumos dando vueltas a unos minutos por órbita.
///
/// Por eso la solución no era animar el dibujo del disco, que está en perspectiva y al rotarlo
/// parece un plato: es poner unos pocos puntos recorriendo una ELIPSE que calza con esa
/// perspectiva, encogiendo al alejarse y pasando por detrás del cuerpo.
///
/// Va en el objeto Goal, junto al componente Goal.
public class AccretionSpots : MonoBehaviour
{
    [Header("Aspecto")]
    [Tooltip("Los PNG spot_01 a spot_04. Cada grumo coge uno al azar")]
    [SerializeField] private Sprite[] sprites;
    [SerializeField] private int count = 4;
    [Tooltip("Diámetro del grumo en unidades de mundo cuando pasa por delante. Es un tamaño " +
             "real: no depende de la PPU con la que importes los PNG. Como referencia, debería " +
             "rondar un quinto del Radius X")]
    [SerializeField] private Vector2 sizeRange = new Vector2(0.35f, 0.65f);
    [Tooltip("Si está vacío, hereda el color del disco. Así combina con cualquier pareja")]
    [SerializeField] private SpriteRenderer tintFrom;
    [SerializeField] private Color tint = Color.white;
    [Range(0f, 2f)] [SerializeField] private float brightness = 1f;

    [Header("Órbita")]
    [Tooltip("Semiejes de la elipse, en unidades. El vertical pequeño es lo que da la " +
             "perspectiva: tiene que calzar con la inclinación del disco dibujado")]
    [SerializeField] private Vector2 radius = new Vector2(0.95f, 0.34f);
    [Tooltip("Inclinación de la elipse, en grados, igual que la del disco")]
    [SerializeField] private float tiltDegrees = -8f;
    [Tooltip("Vueltas por minuto. Los grumos van MUCHO más rápido que el halo: están cerca " +
             "del horizonte, donde la materia orbita a una fracción de la velocidad de la luz")]
    [SerializeField] private float rpm = 14f;
    [Tooltip("Cada grumo a su propia velocidad: lo de dentro orbita más rápido que lo de fuera")]
    [SerializeField] private Vector2 speedSpread = new Vector2(0.75f, 1.35f);

    [Header("Profundidad")]
    [Tooltip("Tamaño en la mitad de atrás respecto a la de delante")]
    [Range(0.2f, 1f)] [SerializeField] private float farScale = 0.62f;
    [Tooltip("Opacidad en la mitad de atrás")]
    [Range(0f, 1f)] [SerializeField] private float farAlpha = 0.45f;
    [SerializeField] private int orderInFront = -1;
    [SerializeField] private int orderBehind = -4;
    [SerializeField] private string sortingLayer = "Asteroids";

    [Header("Parpadeo")]
    [SerializeField] private float flickerSpeed = 2.3f;
    [Range(0f, 1f)] [SerializeField] private float flickerAmount = 0.25f;

    struct Spot
    {
        public Transform t;
        public SpriteRenderer sr;
        public float angle;      // grados
        public float speed;      // multiplicador
        public float size;       // ya convertido a escala local
        public float phase;
    }

    Spot[] spots;

    void Start() { Build(); }

    void Build()
    {
        if (sprites == null || sprites.Length == 0)
        {
            Debug.LogWarning("AccretionSpots: faltan los sprites de los grumos.", this);
            enabled = false;
            return;
        }

        int n = Mathf.Max(1, count);
        spots = new Spot[n];

        for (int i = 0; i < n; i++)
        {
            var go = new GameObject($"Spot_{i + 1}");
            go.transform.SetParent(transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprites[Random.Range(0, sprites.Length)];
            sr.sortingLayerName = sortingLayer;

            // Cuánto mide el sprite a escala 1. Dividir por esto es lo que hace que sizeRange
            // sea un tamaño REAL en unidades y no dependa de la PPU con la que se importó:
            // un PNG de 256 px a PPU 512 mide media unidad, y a PPU 128 mide dos. Sin esta
            // división, cambiar la PPU cambia el tamaño en pantalla sin tocar nada más — que
            // es justo como se acaba con grumos microscópicos.
            float unit = Mathf.Max(0.0001f, sr.sprite.bounds.size.x);

            spots[i] = new Spot
            {
                t = go.transform,
                sr = sr,
                // Repartidos por la órbita, pero no en formación perfecta
                angle = (360f / n) * i + Random.Range(-25f, 25f),
                speed = Random.Range(speedSpread.x, speedSpread.y),
                size = Random.Range(sizeRange.x, sizeRange.y) / unit,
                phase = Random.value * 10f
            };
        }
    }

    void LateUpdate()
    {
        if (spots == null) return;

        float step = rpm * 6f * Time.deltaTime;          // rpm -> grados por segundo
        float tilt = tiltDegrees * Mathf.Deg2Rad;
        float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);

        Color baseColor = tintFrom != null ? tintFrom.color : tint;
        baseColor *= brightness;

        for (int i = 0; i < spots.Length; i++)
        {
            ref Spot s = ref spots[i];
            s.angle += step * s.speed;
            if (s.angle > 360f) s.angle -= 360f;

            float a = s.angle * Mathf.Deg2Rad;
            float ex = Mathf.Cos(a) * radius.x;
            float ey = Mathf.Sin(a) * radius.y;

            // La elipse va inclinada igual que el disco dibujado
            s.t.localPosition = new Vector3(ex * ct - ey * st, ex * st + ey * ct, 0f);

            // sin(a) > 0 es la mitad de atrás: más pequeño, más tenue y por detrás del cuerpo
            float depth = (Mathf.Sin(a) + 1f) * 0.5f;      // 0 = delante, 1 = detrás
            float scale = Mathf.Lerp(1f, farScale, depth) * s.size;
            s.t.localScale = new Vector3(scale, scale, 1f);
            s.sr.sortingOrder = depth > 0.5f ? orderBehind : orderInFront;

            float flicker = 1f - flickerAmount
                          + flickerAmount * (0.5f + 0.5f * Mathf.Sin(Time.time * flickerSpeed + s.phase));
            Color c = baseColor;
            c.a = Mathf.Lerp(1f, farAlpha, depth) * flicker;
            s.sr.color = c;
        }
    }

    /// Dibuja en el editor la órbita Y el tamaño de los grumos, para poder afinar las dos cosas
    /// mirando en vez de calculando. Usa TransformPoint, así que respeta la escala y el giro
    /// del objeto: lo que ves es donde van a estar de verdad.
    void OnDrawGizmosSelected()
    {
        float tilt = tiltDegrees * Mathf.Deg2Rad;
        float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);

        Vector3 At(float a)
        {
            float ex = Mathf.Cos(a) * radius.x, ey = Mathf.Sin(a) * radius.y;
            return transform.TransformPoint(new Vector3(ex * ct - ey * st, ex * st + ey * ct, 0f));
        }

        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.6f);
        Vector3 prev = At(0f);
        for (int i = 1; i <= 48; i++)
        {
            Vector3 p = At(i / 48f * Mathf.PI * 2f);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }

        // El tamaño real de los grumos, delante (grande) y detrás (encogido)
        float world = transform.lossyScale.x;
        Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(At(-Mathf.PI * 0.5f), sizeRange.y * 0.5f * world);
        Gizmos.DrawWireSphere(At(0f), sizeRange.x * 0.5f * world);
        Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(At(Mathf.PI * 0.5f), sizeRange.y * 0.5f * farScale * world);
    }
}
