using UnityEngine;

/// El contorno rojo de una roca que mata (ver DangerStyle). Una silueta del mismo dibujo, un
/// poco más grande y justo detrás. Lo añade Asteroid solo.
[DisallowMultipleComponent]
public class DangerOutline : MonoBehaviour
{
    Asteroid rock;
    SpriteRenderer sr, outline;

    void Awake()
    {
        rock = GetComponent<Asteroid>();
        sr = GetComponent<SpriteRenderer>();
    }

    void OnDisable() { if (outline != null) outline.enabled = false; }

    void LateUpdate()
    {
        var st = DangerStyle.Instance;
        bool show = st != null && st.Silhouette != null && rock != null && sr != null && sr.enabled &&
                    sr.sprite != null && rock.Lethal;
        if (!show) { if (outline != null) outline.enabled = false; return; }

        if (outline == null)
        {
            var go = new GameObject("Contorno");
            go.transform.SetParent(transform, false);
            outline = go.AddComponent<SpriteRenderer>();
            outline.sharedMaterial = st.Silhouette;
        }
        outline.enabled = true;
        outline.sprite = sr.sprite;
        outline.flipX = sr.flipX;
        outline.flipY = sr.flipY;
        outline.sortingLayerID = sr.sortingLayerID;
        outline.sortingOrder = sr.sortingOrder - 1;

        float r = Mathf.Max(0.05f, rock.WorldRadius);
        float k = st.Closeness(transform.position, r);
        // El contorno sale por fuera de la silueta: se escala para ganar el grosor en el radio
        float grow = 1f + st.ThicknessFor(k) / r;
        outline.transform.localPosition = Vector3.zero;
        outline.transform.localRotation = Quaternion.identity;
        outline.transform.localScale = new Vector3(grow, grow, 1f);
        var c = st.Color;
        c.a *= st.AlphaFor(k) * sr.color.a;   // se apaga con la roca al final de su vida
        outline.color = c;
    }
}
