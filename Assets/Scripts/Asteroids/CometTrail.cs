using System.Collections.Generic;

/// El rastro que dejan los cometas: las piezas quietas del patrón de línea.
///
/// Duran todo el nivel, pero no pueden crecer sin fin en un móvil. Cuando se llega al tope,
/// la pieza más antigua se desvanece para dejar sitio a la nueva: el rastro reciente siempre
/// se ve, y los muy viejos se van borrando.
///
/// Guarda el Serial de cada pieza porque el pool reutiliza objetos: si una pieza ya volvió al
/// pool y salió convertida en otro asteroide, la referencia sigue viva pero ya no es rastro.
public static class CometTrail
{
    struct Entry { public Asteroid a; public int serial; }

    static readonly Queue<Entry> queue = new Queue<Entry>();

    public static int Count => queue.Count;

    public static void Register(Asteroid piece, int cap, float fadeSeconds)
    {
        if (piece == null) return;

        Purge();
        int limit = UnityEngine.Mathf.Max(1, cap);
        if (queue.Count >= limit) Compact();
        while (queue.Count >= limit)
        {
            var old = queue.Dequeue();
            if (IsValid(old)) old.a.SetLifetime(UnityEngine.Mathf.Max(0.1f, fadeSeconds));
        }

        queue.Enqueue(new Entry { a = piece, serial = piece.Serial });
    }

    /// Al empezar un nivel: el pool ya lo devolvió todo, solo queda olvidar las referencias.
    public static void Clear() => queue.Clear();

    // Quita del principio las que ya no existen (salieron del mapa, se las comió algo...)
    static void Purge()
    {
        while (queue.Count > 0 && !IsValid(queue.Peek())) queue.Dequeue();
    }

    // Las que desaparecieron por el medio de la cola también ocupan sitio: antes de borrar
    // una pieza buena por falta de hueco, se limpian todas las que ya no existen.
    static void Compact()
    {
        int n = queue.Count;
        for (int i = 0; i < n; i++)
        {
            var e = queue.Dequeue();
            if (IsValid(e)) queue.Enqueue(e);
        }
    }

    static bool IsValid(Entry e) =>
        e.a != null && e.a.isActiveAndEnabled && e.a.Serial == e.serial;
}
