using UnityEngine;

/// Lo que convierte un choque en un GOLPE. La física ya calcula bien la transferencia de
/// energía; lo que faltaba es que el jugador la sienta. Tres recursos, en orden de potencia:
///
///   1. Hitstop: el tiempo se congela 40-80 ms en el momento del impacto. Es el truco más
///      eficaz que existe para transmitir masa, y lo usa todo el género desde Street Fighter.
///   2. Sacudida de cámara proporcional a la energía real del choque.
///   3. Zoom mínimo hacia fuera en los golpes bestiales.
///
/// Ponlo en un GameObject vacío de la escena y arrástrale la cámara.
[DefaultExecutionOrder(100)]
public class ImpactFeedback : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private Camera cam;

    [Header("Umbrales de energía (E = ½·masa reducida·v²)")]
    [Tooltip("Por debajo de esto no se nota nada: son rocas rozándose")]
    [SerializeField] private float minEnergy = 1.5f;
    [Tooltip("Energía a la que la respuesta ya es máxima. Baja este número si quieres " +
             "que hasta los choques medianos sacudan la pantalla")]
    [SerializeField] private float fullEnergy = 60f;

    [Header("Sacudida")]
    [SerializeField] private float maxShake = 0.45f;        // unidades de mundo
    [SerializeField] private float shakeDuration = 0.35f;
    [Tooltip("Velocidad del temblor. Alto = seco y nervioso, bajo = bamboleo")]
    [SerializeField] private float shakeFrequency = 26f;

    [Header("Congelación (hitstop)")]
    [Tooltip("Segundos reales que se congela el tiempo en el golpe más fuerte")]
    [SerializeField] private float maxHitstop = 0.07f;
    [Tooltip("A cuánto baja la escala de tiempo durante la congelación")]
    [Range(0.01f, 1f)] [SerializeField] private float hitstopScale = 0.08f;
    [Tooltip("Fracción de la energía máxima a partir de la cual se congela. Por debajo solo " +
             "hay sacudida: si cada choque congelara el juego, sería injugable")]
    [Range(0f, 1f)] [SerializeField] private float hitstopThreshold = 0.45f;

    [Header("Retroceso de zoom")]
    [SerializeField] private float maxZoomOut = 0.25f;      // unidades de tamaño ortográfico
    [SerializeField] private float zoomRecover = 1.5f;

    [Header("Partículas (opcional)")]
    [Tooltip("Estallido en el punto de contacto. Sirve el mismo FX_Break que ya tienes")]
    [SerializeField] private ParticleSystem impactFx;
    [SerializeField] private float fxMinEnergy = 12f;

    Vector3 camBase;
    float baseSize;
    float shakeAmount, shakeEnds;
    float seedX, seedY;
    float zoomOffset;
    float hitstopEnds;
    bool frozen;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        seedX = Random.value * 100f;
        seedY = Random.value * 100f;
    }

    bool baseReady;

    // Se toma en Start y con orden de ejecución 100, para que CameraAspectFitter ya haya
    // fijado el tamaño ortográfico de la cámara antes de que lo guardemos como referencia.
    void Start() { RefreshBase(); baseReady = true; }

    void OnEnable()
    {
        Asteroid.HeavyImpact += OnImpact;
        if (baseReady) RefreshBase();
    }

    void OnDisable()
    {
        Asteroid.HeavyImpact -= OnImpact;
        Unfreeze();
        if (cam != null)
        {
            cam.transform.localPosition = camBase;
            cam.orthographicSize = baseSize;
        }
    }

    void OnImpact(Vector2 point, float energy, float heaviestMass)
    {
        if (energy < minEnergy) return;

        // Raíz cuadrada: así los golpes medianos también se notan y no solo los extremos
        float t = Mathf.Clamp01(Mathf.Sqrt(Mathf.InverseLerp(minEnergy, fullEnergy, energy)));

        float amount = maxShake * t;
        if (amount > shakeAmount || Time.unscaledTime > shakeEnds)
        {
            shakeAmount = Mathf.Max(shakeAmount, amount);
            shakeEnds = Time.unscaledTime + shakeDuration;
            seedX = Random.value * 100f;
            seedY = Random.value * 100f;
        }

        zoomOffset = Mathf.Max(zoomOffset, maxZoomOut * t);

        if (t >= hitstopThreshold && !frozen)
        {
            frozen = true;
            hitstopEnds = Time.unscaledTime + maxHitstop * t;
            Time.timeScale = hitstopScale;
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
        }

        if (impactFx != null && energy >= fxMinEnergy)
        {
            impactFx.transform.position = point;
            impactFx.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 2.2f, t);
            impactFx.Emit(Mathf.RoundToInt(Mathf.Lerp(8f, 40f, t)));
        }
    }

    void LateUpdate()
    {
        if (frozen && Time.unscaledTime >= hitstopEnds) Unfreeze();
        if (cam == null || !baseReady) return;

        // El temblor va en tiempo real: durante el hitstop el tiempo del juego está parado
        float dt = Time.unscaledDeltaTime;

        if (Time.unscaledTime < shakeEnds && shakeAmount > 0.0001f)
        {
            float left = (shakeEnds - Time.unscaledTime) / Mathf.Max(0.01f, shakeDuration);
            float a = shakeAmount * left * left;
            float t = Time.unscaledTime * shakeFrequency;
            Vector3 offset = new Vector3((Mathf.PerlinNoise(seedX, t) - 0.5f) * 2f * a,
                                         (Mathf.PerlinNoise(seedY, t) - 0.5f) * 2f * a, 0f);
            cam.transform.localPosition = camBase + offset;
        }
        else
        {
            shakeAmount = 0f;
            cam.transform.localPosition = Vector3.MoveTowards(cam.transform.localPosition, camBase, 4f * dt);
        }

        if (zoomOffset > 0.0001f)
        {
            zoomOffset = Mathf.MoveTowards(zoomOffset, 0f, zoomRecover * dt);
            cam.orthographicSize = baseSize + zoomOffset;
        }
        else if (!Mathf.Approximately(cam.orthographicSize, baseSize))
        {
            cam.orthographicSize = baseSize;
        }
    }

    void Unfreeze()
    {
        if (!frozen) return;
        frozen = false;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
    }

    /// Si CameraAspectFitter cambia el tamaño de la cámara, avísale con esto.
    public void RefreshBase()
    {
        if (cam == null) return;
        camBase = cam.transform.localPosition;
        baseSize = cam.orthographicSize;
    }
}
