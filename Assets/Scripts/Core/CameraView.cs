using UnityEngine;

/// Lo que ve la cámara de verdad, en unidades del plano del juego (z = 0).
///
/// Con cámara ortográfica es su tamaño. Con cámara en perspectiva depende de la distancia al
/// plano y del campo de visión: d · tan(FOV / 2). Todo el que necesite saber "cuánto se ve"
/// pregunta aquí, así funciona igual con las dos.
public static class CameraView
{
    /// Mitad de la altura visible, en unidades
    public static float HalfHeight(Camera c)
    {
        if (c == null) return 3.5f;
        if (c.orthographic) return c.orthographicSize;
        return Distance(c) * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
    }

    /// Mitad del ancho y del alto visibles
    public static Vector2 Half(Camera c)
    {
        float h = HalfHeight(c);
        return new Vector2(h * (c != null ? c.aspect : 2f), h);
    }

    /// Distancia de la cámara al plano del juego
    public static float Distance(Camera c) => c == null ? 10f : Mathf.Max(0.01f, Mathf.Abs(c.transform.position.z));

    /// Pone la cámara para ver `halfHeight` de media altura, sea ortográfica o en perspectiva
    public static void SetHalfHeight(Camera c, float halfHeight)
    {
        if (c == null) return;
        halfHeight = Mathf.Max(0.1f, halfHeight);
        if (c.orthographic) c.orthographicSize = halfHeight;
        else c.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(halfHeight / Distance(c)) * Mathf.Rad2Deg, 1f, 170f);
    }
}
