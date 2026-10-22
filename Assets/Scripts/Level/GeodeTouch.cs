using UnityEngine;

/// Recibe el contacto del collider de la geoda y se lo pasa al director
public class GeodeTouch : MonoBehaviour
{
    public GeodeDirector owner;
    void OnTriggerEnter2D(Collider2D other) { if (owner != null) owner.Touched(other); }
}
