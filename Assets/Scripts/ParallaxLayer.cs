using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    [SerializeField] private Transform target;   // el jugador (si se deja vacío, lo busca)
    [Tooltip("0 = fija. 0.03 = muy lejana, 0.15 = cercana. No pases de 0.2 o se verán los bordes")]
    [Range(0f, 0.3f)] [SerializeField] private float factor = 0.05f;

    Vector3 startPos;
    Transform cam;

    void Start()
    {
        cam = Camera.main.transform;
        startPos = transform.position;
        if (target == null)
        {
            var p = FindFirstObjectByType<PlayerController>();
            if (p != null) target = p.transform;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;
        Vector2 rel = (Vector2)target.position - (Vector2)cam.position;
        transform.position = startPos - (Vector3)(rel * factor);
    }
}