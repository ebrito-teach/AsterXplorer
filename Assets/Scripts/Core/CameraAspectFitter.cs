using UnityEngine;

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
}