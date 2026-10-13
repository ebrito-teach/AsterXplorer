using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class AsteroidPool : MonoBehaviour
{
    [Tooltip("Prefab por defecto (el de los asteroides normales)")]
    [SerializeField] private Asteroid prefab;
    [SerializeField] private int initialCapacity = 30;
    [SerializeField] private int maxSize = 120;

    readonly Dictionary<Asteroid, ObjectPool<Asteroid>> pools = new Dictionary<Asteroid, ObjectPool<Asteroid>>();
    readonly List<Asteroid> active = new List<Asteroid>();

    public IReadOnlyList<Asteroid> Active => active;

    /// Un asteroide salió del área. Quien se suscriba decide qué entra en su lugar.
    public event System.Action<Asteroid> OnExited;

    ObjectPool<Asteroid> GetPool(Asteroid source)
    {
        if (pools.TryGetValue(source, out var existing)) return existing;

        var created = new ObjectPool<Asteroid>(
            createFunc: () => { var a = Instantiate(source, transform); a.SourcePrefab = source; return a; },
            actionOnGet: a => a.gameObject.SetActive(true),
            actionOnRelease: a => a.gameObject.SetActive(false),
            actionOnDestroy: a => Destroy(a.gameObject),
            collectionCheck: false,
            defaultCapacity: initialCapacity,
            maxSize: maxSize);
        pools[source] = created;
        return created;
    }

    /// Sprite y tamaño los elige el tipo (con variedad: bolsa de sprites y sesgo de tamaño).
    public Asteroid Spawn(AsteroidTypeData type, Vector2 position) =>
        Spawn(type, position, type.RandomSize());

    public Asteroid Spawn(AsteroidTypeData type, Vector2 position, float size)
    {
        var source = type.prefabOverride != null ? type.prefabOverride : prefab;
        if (source == null)
        {
            Debug.LogError("AsteroidPool: falta el prefab (ni el tipo ni el pool tienen uno).", this);
            return null;
        }

        var a = GetPool(source).Get();
        active.Add(a);
        a.Init(type, type.NextSprite(), position, size, this);
        return a;
    }

    /// Devuelve true si alguien se encargó de sustituirlo.
    public bool NotifyExited(Asteroid a)
    {
        if (OnExited == null) return false;
        OnExited(a);
        return true;
    }

    public void Release(Asteroid a)
    {
        if (!active.Remove(a)) return;   // evita liberar el mismo dos veces
        GetPool(a.SourcePrefab != null ? a.SourcePrefab : prefab).Release(a);
    }

    public void ReleaseAll()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var a = active[i];
            active.RemoveAt(i);
            GetPool(a.SourcePrefab != null ? a.SourcePrefab : prefab).Release(a);
        }
    }
}