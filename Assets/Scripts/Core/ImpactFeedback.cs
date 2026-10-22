using UnityEngine;

/// Lo que convierte un choque en un GOLPE: sacudida de cámara y un retroceso mínimo de zoom,
/// los dos proporcionales a la energía real del choque y a lo cerca que haya pasado de ti.
///
/// AQUÍ HABÍA UN HITSTOP Y SE QUITÓ. CONVIENE SABER POR QUÉ.
///
/// Congelaba el tiempo unos milisegundos en cada golpe fuerte. Es el mejor truco que existe
/// para transmitir masa... en un juego de pelea, donde no estás navegando. Aquí tenía dos
/// problemas, y el segundo es el grave:
///
///   1. Te quita el mando justo cuando estás enhebrando un hueco. En un esquivador, moverse
///      ES el verbo del juego; congelarlo trabaja contra todo lo demás.
///
///   2. **Nunca tuvo nada que ver contigo.** El evento que lo disparaba, Asteroid.HeavyImpact,
///      solo se emite cuando chocan dos asteroides ENTRE SÍ — el jugador no entra en esa
///      ecuación. Tus propios golpes van por otro camino. O sea: el juego te congelaba por
///      sucesos ajenos, en cualquier punto de la pantalla, decenas de veces por nivel.
///
/// De propina, deja de tocar Time.timeScale y Time.fixedDeltaTime en caliente, que metía
/// jitter en la física y alargaba las transiciones de nivel (van en tiempo escalado).
///
/// Si alguna vez quieres recuperarlo, el sitio correcto es el aturdimiento del
/// PlayerController: tu propio golpe, que es raro y sí merece una pausa.
///
/// La sacudida y el zoom se quedan porque son ESPACIALES: te dicen dónde pasó algo sin
/// quitarte el control. Y ahora se atenúan con la distancia, así que un choque al otro lado
/// de la pantalla ya no sacude igual que uno que te pasó rozando.
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

    [Header("Distancia al jugador")]
    [Tooltip("Dentro de este radio el golpe se siente entero. Es tu burbuja: lo que pasa aquí " +
             "te concierne")]
    [SerializeField] private float fullEffectRadius = 3f;
    [Tooltip("A partir de aquí no se nota nada. Un choque entre dos rocas a doce unidades no " +
             "tiene por qué moverte la cámara")]
    [SerializeField] private float noEffectRadius = 11f;
    [Tooltip("Sacudida mínima que conservan los golpes lejanos, para que el cinturón siga " +
             "sintiéndose vivo en vez de muerto más allá del radio")]
    [Range(0f, 1f)] [SerializeField] private float distantFloor = 0.15f;
    [SerializeField] private Transform player;

    [Header("Retroceso de zoom")]
    [SerializeField] private float maxZoomOut = 0.25f;      // unidades de tamaño ortográfico
    [SerializeField] private float zoomRecover = 1.5f;

    [Header("Polvo (FX_Impact_Dust)")]
    [SerializeField] private ParticleSystem impactFx;
    [SerializeField] private float fxMinEnergy = 3f;
    [Tooltip("Partículas que emite: la primera en el golpe más flojo, la segunda en el más " +
             "fuerte. Con más de 15 el polvo tapa el campo de juego")]
    [SerializeField] private Vector2Int fxBurst = new Vector2Int(3, 12);
    [Tooltip("Escala del efecto entre el golpe más flojo y el más fuerte")]
    [SerializeField] private Vector2 fxScale = new Vector2(0.6f, 1.4f);

    [Header("Destello (FX_Impact_Flash)")]
    [SerializeField] private ParticleSystem flashFx;
    [SerializeField] private float flashMinEnergy = 2f;

    [Header("Anillo de eyección (FX_Impact_Ring)")]
    [Tooltip("Solo en los golpes gordos: si sale en todos, pierde todo el peso")]
    [SerializeField] private ParticleSystem ringFx;
    [SerializeField] private float ringMinEnergy = 14f;

    [Header("Esquirlas (FX_Impact_Sparks)")]
    [Tooltip("Se orienta sola hacia donde sale la eyecta. Su Shape debe ser Cone")]
    [SerializeField] private ParticleSystem sparksFx;
    [SerializeField] private float sparksMinEnergy = 4f;
    [SerializeField] private Vector2Int sparksBurst = new Vector2Int(3, 12);

    Vector3 camBase;
    float baseSize;
    float shakeAmount, shakeEnds;
    float seedX, seedY;
    float zoomOffset;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (player == null)
        {
            var pc = FindFirstObjectByType<PlayerController>();
            if (pc != null) player = pc.transform;
        }
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
        if (cam != null)
        {
            cam.transform.localPosition = camBase;
            CameraAspectFitter.ExtraHalfHeight = 0f;
        }
    }

    void OnImpact(Vector2 point, Vector2 dir, float energy, float heaviestMass)
    {
        if (energy < minEnergy) return;

        // Fuerza del golpe en sí. Raíz cuadrada: así los golpes medianos también se notan y
        // no solo los extremos. Esta es la que manda en las PARTÍCULAS, porque el polvo que
        // levanta un choque es el mismo lo lejos que esté: lo que cambia con la distancia es
        // cuánto te afecta a TI, no lo que ocurre allí.
        float fx = Mathf.Clamp01(Mathf.Sqrt(Mathf.InverseLerp(minEnergy, fullEnergy, energy)));

        // Lo cerca que te pasó. La cámara reacciona a lo que te concierne, no a todo lo que
        // ocurre: con el cinturón lleno, sin esto la pantalla tiembla sin parar y el temblor
        // deja de significar nada.
        float near = 1f;
        if (player != null)
        {
            float d = Vector2.Distance(point, player.position);
            near = 1f - Mathf.Clamp01((d - fullEffectRadius)
                                      / Mathf.Max(0.01f, noEffectRadius - fullEffectRadius));
            near = Mathf.Lerp(distantFloor, 1f, near);
        }
        float t = fx * near;        // la que mueve la cámara

        float amount = maxShake * t;
        if (amount > shakeAmount || Time.unscaledTime > shakeEnds)
        {
            shakeAmount = Mathf.Max(shakeAmount, amount);
            shakeEnds = Time.unscaledTime + shakeDuration;
            seedX = Random.value * 100f;
            seedY = Random.value * 100f;
        }

        zoomOffset = Mathf.Max(zoomOffset, maxZoomOut * t);

        if (impactFx != null && energy >= fxMinEnergy)
        {
            impactFx.transform.position = point;
            impactFx.transform.localScale = Vector3.one * Mathf.Lerp(fxScale.x, fxScale.y, fx);
            impactFx.Emit(Mathf.RoundToInt(Mathf.Lerp(fxBurst.x, fxBurst.y, fx)));
        }

        if (flashFx != null && energy >= flashMinEnergy)
        {
            flashFx.transform.position = point;
            flashFx.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.5f, fx);
            flashFx.Emit(1);
        }

        if (ringFx != null && energy >= ringMinEnergy)
        {
            ringFx.transform.position = point;
            ringFx.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.8f, fx);
            ringFx.Emit(1);
        }

        if (sparksFx != null && energy >= sparksMinEnergy)
        {
            sparksFx.transform.position = point;
            // El cono apunta hacia donde sale la eyecta. Con la rotación X de -90 que Unity
            // da por defecto al sistema, el cono mira a +Y del mundo; esto lo lleva a 'dir'.
            sparksFx.transform.rotation = Quaternion.FromToRotation(Vector3.up, (Vector3)dir)
                                        * Quaternion.Euler(-90f, 0f, 0f);
            sparksFx.Emit(Mathf.RoundToInt(Mathf.Lerp(sparksBurst.x, sparksBurst.y, fx)));
        }
    }

    void LateUpdate()
    {
        if (cam == null || !baseReady) return;

        // El temblor va en tiempo real, no en tiempo de juego: así una pausa o un cambio de
        // escala de tiempo no lo deja a medias ni lo alarga
        float dt = Time.unscaledDeltaTime;

        if (Time.unscaledTime < shakeEnds && shakeAmount > 0.0001f)
        {
            float left = (shakeEnds - Time.unscaledTime) / Mathf.Max(0.01f, shakeDuration);
            float a = shakeAmount * left * left;
            float fx = Time.unscaledTime * shakeFrequency;
            Vector3 offset = new Vector3((Mathf.PerlinNoise(seedX, fx) - 0.5f) * 2f * a,
                                         (Mathf.PerlinNoise(seedY, fx) - 0.5f) * 2f * a, 0f);
            cam.transform.localPosition = camBase + offset;
        }
        else
        {
            shakeAmount = 0f;
            cam.transform.localPosition = Vector3.MoveTowards(cam.transform.localPosition, camBase, 4f * dt);
        }

        // El zoom del golpe lo aplica CameraAspectFitter (sirve igual en perspectiva)
        if (zoomOffset > 0.0001f) zoomOffset = Mathf.MoveTowards(zoomOffset, 0f, zoomRecover * dt);
        CameraAspectFitter.ExtraHalfHeight = Mathf.Max(0f, zoomOffset);
    }

    /// Si CameraAspectFitter cambia el tamaño de la cámara, avísale con esto.
    public void RefreshBase()
    {
        if (cam == null) return;
        camBase = cam.transform.localPosition;
        baseSize = CameraView.HalfHeight(cam);
    }
}
