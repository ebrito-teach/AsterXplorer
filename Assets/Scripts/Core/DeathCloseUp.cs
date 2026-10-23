using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// Primer plano al morir: la cámara se acerca al choque (el doble de cerca) y el mundo va a
/// cámara lenta durante Duration segundos reales. El personaje se queda a la vista, clavado
/// donde lo tocaron, con lo que le golpeó; al acabar (o al tocar para saltarlo) se rompe y se
/// reaparece.
///
/// Se activa o desactiva en Ajustes (Settings.deathCloseUp, activado por defecto). Apagado,
/// la muerte va como antes: se rompe al instante (Respawn Delay del GameDirector).
///
/// Lo pone en marcha el GameDirector al morir. Va en un objeto vacío de la escena.
public class DeathCloseUp : MonoBehaviour
{
    public static DeathCloseUp Instance { get; private set; }

    [Tooltip("Segundos reales que dura el primer plano")]
    [SerializeField] private float duration = 2f;
    [Tooltip("Zoom de la cámara: 0.5 = el doble de cerca (es el límite de la cámara)")]
    [Range(0.5f, 1f)] [SerializeField] private float zoom = 0.5f;
    [Tooltip("Velocidad del mundo durante el primer plano (0.3 = al 30 %)")]
    [Range(0.05f, 1f)] [SerializeField] private float slowMotion = 0.3f;
    [Tooltip("Los toques de los primeros segundos no lo saltan (el toque que te mató, por ejemplo)")]
    [SerializeField] private float skipAfter = 0.3f;

    /// Hay un primer plano en marcha
    public bool Playing { get; private set; }

    float baseFixedDelta;

    void Awake()
    {
        Instance = this;
        baseFixedDelta = Time.fixedDeltaTime;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnDisable()
    {
        if (Playing) Restore();
    }

    /// Al morir, ¿habrá primer plano? (componente activo y la opción encendida en Ajustes)
    public static bool WillPlay => Instance != null && Instance.isActiveAndEnabled && SettingOn;

    static bool SettingOn => GameProgress.Instance == null || GameProgress.Instance.Data == null ||
                             GameProgress.Instance.Data.settings == null || GameProgress.Instance.Data.settings.deathCloseUp;

    /// Lo llama el GameDirector al morir. false = no hay primer plano (apagado en Ajustes)
    public bool TryPlay(Vector2 at)
    {
        if (!isActiveAndEnabled || !SettingOn) return false;
        StopAllCoroutines();
        StartCoroutine(Run());
        return true;
    }

    IEnumerator Run()
    {
        Playing = true;
        CameraFollow.ZoomOverride = zoom;
        float t = 0f;
        while (t < duration)
        {
            // Si algo pausa el juego (escala 0), el primer plano espera
            if (Time.timeScale > 0f)
            {
                t += Time.unscaledDeltaTime;
                // Cada fotograma: al morir, los buffs se limpian y devuelven la escala a 1
                Time.timeScale = slowMotion;
                Time.fixedDeltaTime = baseFixedDelta * slowMotion;
                if (t > skipAfter && TapThisFrame()) break;
            }
            yield return null;
        }
        Restore();
    }

    void Restore()
    {
        // El personaje se rompe al acabar (o al saltarlo con un toque)
        var pc = FindFirstObjectByType<PlayerController>();
        if (pc != null && pc.IsDead) pc.Shatter();
        Time.timeScale = 1f;
        Time.fixedDeltaTime = baseFixedDelta;
        CameraFollow.ZoomOverride = -1f;
        Playing = false;
    }

    static bool TapThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var ts = Touchscreen.current;
        if (ts != null)
            foreach (var t in ts.touches)
                if (t.press.wasPressedThisFrame) return true;
        var m = Mouse.current;
        if (m != null && m.leftButton.wasPressedThisFrame) return true;
        var k = Keyboard.current;
        if (k != null && (k.spaceKey.wasPressedThisFrame || k.eKey.wasPressedThisFrame)) return true;
        return false;
#else
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E)) return true;
        for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
        return false;
#endif
    }
}
