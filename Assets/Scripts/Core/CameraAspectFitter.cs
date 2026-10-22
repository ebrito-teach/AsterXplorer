/*using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraAspectFitter : MonoBehaviour
{
    [SerializeField] private float targetWidth = 6f; // unidades de ancho que quieres ver siempre

    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        Adjust();
    }

    void Adjust()
    {
        cam.orthographicSize = targetWidth / cam.aspect / 2f;
    }

#if UNITY_EDITOR
    void Update() { Adjust(); } // para ver cambios al cambiar resolución en el editor
#endif
}*/

using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraAspectFitter : MonoBehaviour
{
    public enum FitMode { Width, Height }

    [SerializeField] private FitMode fitMode = FitMode.Width;
    [Tooltip("Unidades que se ven siempre a lo ancho (modo Width)")]
    [SerializeField] private float targetWidth = 6f;
    [Tooltip("Unidades que se ven siempre a lo alto (modo Height). Vale para cámara ortográfica " +
             "y en perspectiva (ahí se calcula el campo de visión)")]
    [SerializeField] private float targetHeight = 6f;

    private Camera cam;

    /// Alejamiento de la cámara (1 = normal, 1.5 = se ve un 50 % más). Lo pone CameraFollow
    /// cerca de una grieta, para que se vea lo grande que es
    public static float Zoom = 1f;
    /// Lo más que se aleja nunca. El cielo se genera con margen para esto
    public const float MaxZoom = 1.6f;
    /// Alejamiento extra en unidades (el golpe fuerte de ImpactFeedback)
    public static float ExtraHalfHeight;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Zoom = 1f; ExtraHalfHeight = 0f; }

    void Awake()
    {
        cam = GetComponent<Camera>();
        Adjust();
    }

    void Adjust()
    {
        // Funciona igual con la cámara ortográfica y en perspectiva: lo que se fija es cuánto se
        // ve (en unidades), y en perspectiva el campo de visión se calcula para conseguirlo
        float z = Mathf.Clamp(Zoom, 0.5f, MaxZoom);
        float half = (fitMode == FitMode.Width
            ? targetWidth / cam.aspect / 2f
            : targetHeight / 2f) * z + Mathf.Max(0f, ExtraHalfHeight);
        CameraView.SetHalfHeight(cam, half);
    }

    // Cada fotograma: por el alejamiento, y en el editor por los cambios al rotar el Simulator
    void Update() { Adjust(); }
}