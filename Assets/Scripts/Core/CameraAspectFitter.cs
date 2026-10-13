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
    [Tooltip("Unidades que se ven siempre a lo alto (modo Height)")]
    [SerializeField] private float targetHeight = 6f;

    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        Adjust();
    }

    void Adjust()
    {
        cam.orthographicSize = fitMode == FitMode.Width
            ? targetWidth / cam.aspect / 2f
            : targetHeight / 2f;
    }

#if UNITY_EDITOR
    void Update() { Adjust(); } // para ver los cambios al rotar el Simulator
#endif
}